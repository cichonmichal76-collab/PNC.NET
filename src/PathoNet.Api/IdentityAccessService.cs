using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

internal sealed class IdentityAccessService(
    IDbContextFactory<PathoNetIdentityDbContext> dbContextFactory,
    IPasswordHasher<PathoNetIdentityUserEntity> passwordHasher)
{
    private static readonly SeedPermission[] SeedPermissions =
    [
        new("dashboard-view", "dashboard.view", "Podglad dashboardu", "Portal", "Dostep do glownego panelu operatorskiego."),
        new("analysis-view", "analysis.view", "Analiza zdarzen", "Portal", "Widok predykcji, historii i analizy."),
        new("health-view", "health.view", "Zdrowie uslug", "Serwis", "Podglad statusu workerow i watchdogow."),
        new("health-manage", "health.manage", "Restart uslug", "Serwis", "Restart procesow i akcje serwisowe."),
        new("rules-view", "rules.view", "Podglad regul", "Konfiguracja", "Odczyt mapowania surowki na nazwy biznesowe."),
        new("rules-manage", "rules.manage", "Edycja regul", "Konfiguracja", "Tworzenie i zmiana regul eskalacji."),
        new("ota-view", "ota.view", "Podglad OTA", "Konfiguracja", "Odczyt kampanii i paczek software."),
        new("ota-manage", "ota.manage", "Zarzadzanie OTA", "Konfiguracja", "Planowanie i wysylka aktualizacji."),
        new("pnc-view", "pnc.view", "Podglad PNC", "Konfiguracja", "Odczyt floty PNC i portow zewnetrznych."),
        new("pnc-manage", "pnc.manage", "Edycja PNC", "Konfiguracja", "Onboarding PNC i konfiguracja polaczen."),
        new("alerts-ack", "alerts.ack", "Potwierdzanie alertow", "Portal", "Akcje operatorskie na alertach."),
        new("users-view", "users.view", "Podglad uzytkownikow", "IAM", "Widok bazy uzytkownikow, rol i uprawnien."),
        new("users-manage", "users.manage", "Edycja uzytkownikow", "IAM", "Tworzenie uzytkownikow, rol i przypisan."),
        new("mock-view", "mock.view", "Podglad mocka", "Portal", "Widok diagnostyczny lokalnego mocka."),
        new("hdmi-view", "hdmi.view", "Ekran HDMI", "Portal", "Dostep do uproszczonego ekranu klienckiego.")
    ];

    private static readonly SeedRole[] SeedRoles =
    [
        new(
            "administrator",
            "Administrator",
            "Pelna administracja systemem, uzytkownikami i konfiguracja edge.",
            true,
            SeedPermissions.Select(static permission => permission.Id).ToArray()),
        new(
            "service-manager",
            "Serwis",
            "Dostep do ustawien serwisowych, nastawczych i operacji technicznych.",
            true,
            ["dashboard-view", "analysis-view", "health-view", "health-manage", "rules-view", "rules-manage", "ota-view", "ota-manage", "pnc-view", "pnc-manage", "mock-view", "hdmi-view"]),
        new(
            "szpital",
            "Szpital",
            "Podglad lokalny i zdalny przez HDMI oraz dedykowany panel WWW dla klienta.",
            true,
            ["dashboard-view", "pnc-view", "hdmi-view"]),
        new(
            "operator",
            "Operator",
            "Dedykowany profil goscia do podstawowego monitoringu i podgladu urzadzenia.",
            true,
            ["dashboard-view", "analysis-view", "health-view", "pnc-view", "hdmi-view"]),
        new(
            "viewer",
            "Odczyt",
            "Tylko podglad panelu operatorskiego i informacji diagnostycznych.",
            true,
            ["dashboard-view", "analysis-view", "health-view", "rules-view", "ota-view", "pnc-view", "mock-view", "hdmi-view"])
    ];

    private static readonly SeedUser[] SeedUsers =
    [
        new("admin", "Admin", "admin@pathonet.local", "+48 500 000 100", "123", false, false, ["administrator"]),
        new("serwis", "Serwis", "serwis@pathonet.local", "+48 500 000 200", "123", false, false, ["service-manager"]),
        new("szpital", "Szpital", "szpital@pathonet.local", "+48 500 000 300", "123", false, false, ["szpital"]),
        new("operator", "Operator", "operator@pathonet.local", "+48 500 000 400", "123", false, false, ["operator"])
    ];

    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        EnsureInitializedAsync(cancellationToken);

    public async Task<PortalIdentityStateRecord> GetStateAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await LoadStateAsync(cancellationToken);
    }

    public async Task<IdentityAuthenticationResultRecord> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var normalizedUserName = NormalizeUserName(userName);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(item => item.UserName == normalizedUserName, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return AuthenticationFailed("Nieprawidlowy login lub haslo.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification is PasswordVerificationResult.Failed)
        {
            return AuthenticationFailed("Nieprawidlowy login lub haslo.");
        }

        var session = await BuildSessionAsync(db, user, cancellationToken);
        var principal = CreatePrincipal(user, session);

        db.AuditEntries.Add(new PathoNetIdentityAuditEntity
        {
            Actor = user.UserName,
            Action = "auth.login",
            SubjectType = "session",
            SubjectId = user.Id,
            Summary = $"Logowanie uzytkownika {user.UserName}",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        return new IdentityAuthenticationResultRecord(true, "Zalogowano pomyslnie.", principal, session);
    }

    public async Task RecordLogoutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal.Identity?.Name ?? "anonymous";
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.AuditEntries.Add(new PathoNetIdentityAuditEntity
        {
            Actor = userName,
            Action = "auth.logout",
            SubjectType = "session",
            SubjectId = userId,
            Summary = $"Wylogowanie uzytkownika {userName}",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PortalIdentitySessionRecord> GetSessionAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return AnonymousSession();
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return AnonymousSession();
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return AnonymousSession();
        }

        return await BuildSessionAsync(db, user, cancellationToken);
    }

    public async Task<IdentityMutationResultRecord> SaveUserAsync(
        BlazorAccessUserInputRecord input,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var normalizedUserName = NormalizeUserName(input.UserName);
        var trimmedFullName = input.FullName.Trim();
        var currentUserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId.Trim();

        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            return Fail("Podaj login uzytkownika.");
        }

        if (string.IsNullOrWhiteSpace(trimmedFullName))
        {
            return Fail("Podaj imie i nazwisko lub nazwe wyswietlana.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var existsWithUserName = await db.Users
            .AnyAsync(
                user => user.UserName == normalizedUserName &&
                        user.Id != currentUserId,
                cancellationToken);

        if (existsWithUserName)
        {
            return Fail("Taki login juz istnieje.");
        }

        var knownRoleIds = await db.Roles
            .Select(role => role.Id)
            .ToArrayAsync(cancellationToken);
        var knownPermissionIds = await db.Permissions
            .Select(permission => permission.Id)
            .ToArrayAsync(cancellationToken);

        var selectedRoleIds = NormalizeIds(input.RoleIds, knownRoleIds);
        var selectedPermissionIds = NormalizeIds(input.DirectPermissionIds, knownPermissionIds);

        if (selectedRoleIds.Length == 0)
        {
            return Fail("Przypisz przynajmniej jedna role.");
        }

        PathoNetIdentityUserEntity entity;
        var isNew = string.IsNullOrWhiteSpace(currentUserId);

        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(input.Password))
            {
                return Fail("Nowy uzytkownik musi miec haslo poczatkowe.");
            }

            entity = new PathoNetIdentityUserEntity
            {
                Id = await CreateUniqueIdAsync(db.Users.Select(user => user.Id), normalizedUserName, cancellationToken),
                CreatedAtUtc = now
            };

            db.Users.Add(entity);
        }
        else
        {
            entity = await db.Users.FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken)
                     ?? new PathoNetIdentityUserEntity();

            if (string.IsNullOrWhiteSpace(entity.Id))
            {
                return Fail("Nie znaleziono wskazanego uzytkownika.");
            }
        }

        var currentlyAssignedRoleIds = await db.UserRoles
            .Where(link => link.UserId == entity.Id)
            .Select(link => link.RoleId)
            .ToArrayAsync(cancellationToken);

        if (WouldRemoveLastAdministrator(
                entity,
                selectedRoleIds,
                input.IsActive,
                currentlyAssignedRoleIds,
                db,
                cancellationToken))
        {
            return Fail("System musi miec przynajmniej jednego aktywnego administratora.");
        }

        entity.UserName = normalizedUserName;
        entity.FullName = trimmedFullName;
        entity.Email = input.Email?.Trim() ?? string.Empty;
        entity.Phone = input.Phone?.Trim() ?? string.Empty;
        entity.IsActive = input.IsActive;
        entity.IsServiceAccount = input.IsServiceAccount;
        entity.UpdatedAtUtc = now;

        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            var trimmedPassword = input.Password.Trim();
            entity.PasswordHash = passwordHasher.HashPassword(entity, trimmedPassword);
            entity.LastPasswordChangedAtUtc = now;
        }

        var currentUserRoleLinks = db.UserRoles.Where(link => link.UserId == entity.Id);
        db.UserRoles.RemoveRange(currentUserRoleLinks);
        db.UserRoles.AddRange(selectedRoleIds.Select(roleId => new PathoNetIdentityUserRoleEntity
        {
            UserId = entity.Id,
            RoleId = roleId,
            GrantedAtUtc = now
        }));

        var currentUserPermissionLinks = db.UserPermissions.Where(link => link.UserId == entity.Id);
        db.UserPermissions.RemoveRange(currentUserPermissionLinks);
        db.UserPermissions.AddRange(selectedPermissionIds.Select(permissionId => new PathoNetIdentityUserPermissionEntity
        {
            UserId = entity.Id,
            PermissionId = permissionId,
            GrantedAtUtc = now
        }));

        db.AuditEntries.Add(new PathoNetIdentityAuditEntity
        {
            Actor = "blazor-access-admin",
            Action = isNew ? "user.created" : "user.updated",
            SubjectType = "user",
            SubjectId = entity.Id,
            Summary = $"{entity.FullName} ({entity.UserName}) / role {selectedRoleIds.Length} / uprawnienia {selectedPermissionIds.Length}",
            CreatedAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(isNew
            ? $"Uzytkownik {entity.UserName} zostal utworzony."
            : $"Uzytkownik {entity.UserName} zostal zaktualizowany.");
    }

    public async Task<IdentityMutationResultRecord> DeleteUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (entity is null)
        {
            return Fail("Nie znaleziono wskazanego uzytkownika.");
        }

        var roleIds = await db.UserRoles
            .Where(link => link.UserId == userId)
            .Select(link => link.RoleId)
            .ToArrayAsync(cancellationToken);

        if (entity.IsActive && roleIds.Contains("administrator", StringComparer.OrdinalIgnoreCase))
        {
            var adminIds = await db.UserRoles
                .Where(link => link.RoleId == "administrator")
                .Select(link => link.UserId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

            var otherActiveAdmins = await db.Users
                .CountAsync(
                    user => adminIds.Contains(user.Id) &&
                            user.Id != userId &&
                            user.IsActive,
                    cancellationToken);

            if (otherActiveAdmins == 0)
            {
                return Fail("Nie mozna usunac ostatniego aktywnego administratora.");
            }
        }

        db.UserRoles.RemoveRange(db.UserRoles.Where(link => link.UserId == userId));
        db.UserPermissions.RemoveRange(db.UserPermissions.Where(link => link.UserId == userId));
        db.Users.Remove(entity);
        db.AuditEntries.Add(new PathoNetIdentityAuditEntity
        {
            Actor = "blazor-access-admin",
            Action = "user.deleted",
            SubjectType = "user",
            SubjectId = entity.Id,
            Summary = $"{entity.FullName} ({entity.UserName})",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return Ok($"Uzytkownik {entity.UserName} zostal usuniety.");
    }

    public async Task<IdentityMutationResultRecord> SaveRoleAsync(
        BlazorAccessRoleInputRecord input,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var trimmedName = input.Name.Trim();
        var currentRoleId = string.IsNullOrWhiteSpace(input.RoleId) ? null : input.RoleId.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Fail("Podaj nazwe roli.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var nameInUse = await db.Roles
            .AnyAsync(
                role => role.Name == trimmedName &&
                        role.Id != currentRoleId,
                cancellationToken);

        if (nameInUse)
        {
            return Fail("Rola o tej nazwie juz istnieje.");
        }

        var knownPermissionIds = await db.Permissions
            .Select(permission => permission.Id)
            .ToArrayAsync(cancellationToken);
        var selectedPermissionIds = NormalizeIds(input.PermissionIds, knownPermissionIds);

        if (selectedPermissionIds.Length == 0)
        {
            return Fail("Rola musi miec przynajmniej jedno uprawnienie.");
        }

        PathoNetIdentityRoleEntity entity;
        var isNew = string.IsNullOrWhiteSpace(currentRoleId);

        if (isNew)
        {
            entity = new PathoNetIdentityRoleEntity
            {
                Id = await CreateUniqueIdAsync(db.Roles.Select(role => role.Id), trimmedName, cancellationToken),
                CreatedAtUtc = now,
                IsSystemRole = false
            };
            db.Roles.Add(entity);
        }
        else
        {
            entity = await db.Roles.FirstOrDefaultAsync(role => role.Id == currentRoleId, cancellationToken)
                     ?? new PathoNetIdentityRoleEntity();

            if (string.IsNullOrWhiteSpace(entity.Id))
            {
                return Fail("Nie znaleziono wskazanej roli.");
            }
        }

        entity.Name = trimmedName;
        entity.Description = input.Description?.Trim() ?? string.Empty;
        entity.UpdatedAtUtc = now;

        db.RolePermissions.RemoveRange(db.RolePermissions.Where(link => link.RoleId == entity.Id));
        db.RolePermissions.AddRange(selectedPermissionIds.Select(permissionId => new PathoNetIdentityRolePermissionEntity
        {
            RoleId = entity.Id,
            PermissionId = permissionId,
            GrantedAtUtc = now
        }));

        db.AuditEntries.Add(new PathoNetIdentityAuditEntity
        {
            Actor = "blazor-access-admin",
            Action = isNew ? "role.created" : "role.updated",
            SubjectType = "role",
            SubjectId = entity.Id,
            Summary = $"{entity.Name} / uprawnienia {selectedPermissionIds.Length}",
            CreatedAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(isNew
            ? $"Rola {entity.Name} zostala utworzona."
            : $"Rola {entity.Name} zostala zaktualizowana.");
    }

    public async Task<IdentityMutationResultRecord> DeleteRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

        if (entity is null)
        {
            return Fail("Nie znaleziono wskazanej roli.");
        }

        if (entity.IsSystemRole)
        {
            return Fail("Roli systemowej nie mozna usunac.");
        }

        var assignedUserCount = await db.UserRoles.CountAsync(link => link.RoleId == roleId, cancellationToken);
        if (assignedUserCount > 0)
        {
            return Fail("Nie mozna usunac roli przypisanej do uzytkownikow.");
        }

        db.RolePermissions.RemoveRange(db.RolePermissions.Where(link => link.RoleId == roleId));
        db.Roles.Remove(entity);
        db.AuditEntries.Add(new PathoNetIdentityAuditEntity
        {
            Actor = "blazor-access-admin",
            Action = "role.deleted",
            SubjectType = "role",
            SubjectId = entity.Id,
            Summary = entity.Name,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return Ok($"Rola {entity.Name} zostala usunieta.");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);

            if (!await db.Permissions.AnyAsync(cancellationToken))
            {
                var now = DateTimeOffset.UtcNow;

                var permissionEntities = SeedPermissions
                    .Select(permission => new PathoNetIdentityPermissionEntity
                    {
                        Id = permission.Id,
                        Code = permission.Code,
                        Name = permission.Name,
                        Category = permission.Category,
                        Description = permission.Description,
                        IsSystemPermission = true,
                        CreatedAtUtc = now
                    })
                    .ToArray();
                db.Permissions.AddRange(permissionEntities);

                var roleEntities = SeedRoles
                    .Select(role => new PathoNetIdentityRoleEntity
                    {
                        Id = role.Id,
                        Name = role.Name,
                        Description = role.Description,
                        IsSystemRole = role.IsSystemRole,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    })
                    .ToArray();
                db.Roles.AddRange(roleEntities);

                db.RolePermissions.AddRange(
                    SeedRoles.SelectMany(role => role.PermissionIds.Select(permissionId => new PathoNetIdentityRolePermissionEntity
                    {
                        RoleId = role.Id,
                        PermissionId = permissionId,
                        GrantedAtUtc = now
                    })));

                var userEntities = SeedUsers
                    .Select(user =>
                    {
                        var entity = new PathoNetIdentityUserEntity
                        {
                            Id = Slugify(user.UserName),
                            UserName = NormalizeUserName(user.UserName),
                            FullName = user.FullName,
                            Email = user.Email,
                            Phone = user.Phone,
                            IsActive = true,
                            IsServiceAccount = user.IsServiceAccount,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now,
                            LastPasswordChangedAtUtc = now
                        };
                        entity.PasswordHash = passwordHasher.HashPassword(entity, user.Password);
                        return entity;
                    })
                    .ToArray();
                db.Users.AddRange(userEntities);

                db.UserRoles.AddRange(
                    SeedUsers.SelectMany(user => user.RoleIds.Select(roleId => new PathoNetIdentityUserRoleEntity
                    {
                        UserId = Slugify(user.UserName),
                        RoleId = roleId,
                        GrantedAtUtc = now
                    })));

                db.AuditEntries.Add(new PathoNetIdentityAuditEntity
                {
                    Actor = "system-seed",
                    Action = "identity.seeded",
                    SubjectType = "identity",
                    SubjectId = "bootstrap",
                    Summary = $"Utworzono {userEntities.Length} uzytkownikow, {roleEntities.Length} role i {permissionEntities.Length} uprawnien.",
                    CreatedAtUtc = now
                });

                await db.SaveChangesAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task<PortalIdentityStateRecord> LoadStateAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var users = await db.Users
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .ToArrayAsync(cancellationToken);
        var roles = await db.Roles
            .OrderBy(role => role.Name)
            .ToArrayAsync(cancellationToken);
        var permissions = await db.Permissions
            .OrderBy(permission => permission.Category)
            .ThenBy(permission => permission.Name)
            .ToArrayAsync(cancellationToken);
        var userRoles = await db.UserRoles.ToArrayAsync(cancellationToken);
        var rolePermissions = await db.RolePermissions.ToArrayAsync(cancellationToken);
        var userPermissions = await db.UserPermissions.ToArrayAsync(cancellationToken);
        var auditLog = (await db.AuditEntries
                .ToArrayAsync(cancellationToken))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(24)
            .ToArray();

        var permissionById = permissions.ToDictionary(permission => permission.Id, StringComparer.OrdinalIgnoreCase);
        var rolePermissionMap = rolePermissions
            .GroupBy(link => link.RoleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(link => link.PermissionId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var userRoleMap = userRoles
            .GroupBy(link => link.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(link => link.RoleId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var userPermissionMap = userPermissions
            .GroupBy(link => link.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(link => link.PermissionId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var roleUserCount = userRoles
            .GroupBy(link => link.RoleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var roleRecords = roles
            .Select(role =>
            {
                var assignedPermissionIds = rolePermissionMap.GetValueOrDefault(role.Id) ?? [];
                var assignedPermissionCodes = assignedPermissionIds
                    .Select(permissionId => permissionById.TryGetValue(permissionId, out var permission) ? permission.Code : null)
                    .Where(static code => !string.IsNullOrWhiteSpace(code))
                    .Cast<string>()
                    .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new PortalIdentityRoleRecord(
                    Id: role.Id,
                    UserCount: roleUserCount.GetValueOrDefault(role.Id),
                    Name: role.Name,
                    Description: role.Description,
                    IsSystemRole: role.IsSystemRole,
                    PermissionIds: assignedPermissionIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
                    PermissionCodes: assignedPermissionCodes,
                    CreatedAtUtc: role.CreatedAtUtc,
                    UpdatedAtUtc: role.UpdatedAtUtc);
            })
            .ToArray();

        var userRecords = users
            .Select(user =>
            {
                var assignedRoleIds = userRoleMap.GetValueOrDefault(user.Id) ?? [];
                var directPermissionIds = userPermissionMap.GetValueOrDefault(user.Id) ?? [];
                var effectivePermissionCodes = assignedRoleIds
                    .SelectMany(roleId => rolePermissionMap.GetValueOrDefault(roleId) ?? [])
                    .Concat(directPermissionIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(permissionId => permissionById.TryGetValue(permissionId, out var permission) ? permission.Code : null)
                    .Where(static code => !string.IsNullOrWhiteSpace(code))
                    .Cast<string>()
                    .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new PortalIdentityUserRecord(
                    Id: user.Id,
                    UserName: user.UserName,
                    FullName: user.FullName,
                    Email: user.Email,
                    Phone: user.Phone,
                    IsActive: user.IsActive,
                    IsServiceAccount: user.IsServiceAccount,
                    RoleIds: assignedRoleIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
                    DirectPermissionIds: directPermissionIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
                    EffectivePermissionCodes: effectivePermissionCodes,
                    CreatedAtUtc: user.CreatedAtUtc,
                    UpdatedAtUtc: user.UpdatedAtUtc,
                    LastPasswordChangedAtUtc: user.LastPasswordChangedAtUtc);
            })
            .ToArray();

        var permissionRecords = permissions
            .Select(permission => new PortalIdentityPermissionRecord(
                Id: permission.Id,
                Code: permission.Code,
                Name: permission.Name,
                Category: permission.Category,
                Description: permission.Description,
                IsSystemPermission: permission.IsSystemPermission))
            .ToArray();

        var auditRecords = auditLog
            .Select(entry => new PortalIdentityAuditRecord(
                Id: entry.Id,
                Actor: entry.Actor,
                Action: entry.Action,
                SubjectType: entry.SubjectType,
                SubjectId: entry.SubjectId,
                Summary: entry.Summary,
                CreatedAtUtc: entry.CreatedAtUtc))
            .ToArray();

        return new PortalIdentityStateRecord(
            Summary: new PortalIdentitySummaryRecord(
                UserCount: userRecords.Length,
                ActiveUserCount: userRecords.Count(user => user.IsActive),
                RoleCount: roleRecords.Length,
                PermissionCount: permissionRecords.Length,
                RoleAssignmentCount: userRoles.Length,
                RolePermissionAssignmentCount: rolePermissions.Length,
                DirectPermissionAssignmentCount: userPermissions.Length),
            Users: userRecords,
            Roles: roleRecords,
            Permissions: permissionRecords,
            AuditLog: auditRecords);
    }

    private static IdentityMutationResultRecord Ok(string message) =>
        new(true, message);

    private static IdentityMutationResultRecord Fail(string message) =>
        new(false, message);

    private static IdentityAuthenticationResultRecord AuthenticationFailed(string message) =>
        new(false, message, null, AnonymousSession());

    private static string NormalizeUserName(string value) =>
        value.Trim().ToLowerInvariant();

    private static string[] NormalizeIds(IEnumerable<string>? values, IEnumerable<string> knownIds)
    {
        var knownSet = knownIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Where(knownSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string> CreateUniqueIdAsync(
        IQueryable<string> source,
        string baseValue,
        CancellationToken cancellationToken)
    {
        var existing = await source.ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slug = Slugify(baseValue);

        if (!existingSet.Contains(slug))
        {
            return slug;
        }

        var index = 2;
        while (existingSet.Contains($"{slug}-{index}"))
        {
            index++;
        }

        return $"{slug}-{index}";
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool WouldRemoveLastAdministrator(
        PathoNetIdentityUserEntity currentUser,
        IReadOnlyCollection<string> nextRoleIds,
        bool nextIsActive,
        IReadOnlyCollection<string> currentRoleIds,
        PathoNetIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsActive || !currentRoleIds.Contains("administrator", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (nextIsActive && nextRoleIds.Contains("administrator", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var otherActiveAdmins = db.Users
            .Join(
                db.UserRoles.Where(link => link.RoleId == "administrator"),
                user => user.Id,
                link => link.UserId,
                (user, _) => user)
            .Count(user => user.Id != currentUser.Id && user.IsActive);

        return otherActiveAdmins == 0;
    }

    private static ClaimsPrincipal CreatePrincipal(
        PathoNetIdentityUserEntity user,
        PortalIdentitySessionRecord session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new(PathoNetClaimTypes.DisplayName, user.FullName)
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var roleName in session.RoleNames)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
        }

        foreach (var permissionCode in session.PermissionCodes)
        {
            claims.Add(new Claim(PathoNetClaimTypes.Permission, permissionCode));
        }

        var identity = new ClaimsIdentity(claims, "PathoNetCookie");
        return new ClaimsPrincipal(identity);
    }

    private static PortalIdentitySessionRecord AnonymousSession() =>
        new(
            Authenticated: false,
            UserId: null,
            UserName: null,
            FullName: null,
            RoleNames: [],
            PermissionCodes: []);

    private static async Task<PortalIdentitySessionRecord> BuildSessionAsync(
        PathoNetIdentityDbContext db,
        PathoNetIdentityUserEntity user,
        CancellationToken cancellationToken)
    {
        var roleLinks = await db.UserRoles
            .Where(link => link.UserId == user.Id)
            .Select(link => link.RoleId)
            .ToArrayAsync(cancellationToken);
        var roles = await db.Roles
            .Where(role => roleLinks.Contains(role.Id))
            .ToArrayAsync(cancellationToken);
        var rolePermissionIds = await db.RolePermissions
            .Where(link => roleLinks.Contains(link.RoleId))
            .Select(link => link.PermissionId)
            .ToArrayAsync(cancellationToken);
        var directPermissionIds = await db.UserPermissions
            .Where(link => link.UserId == user.Id)
            .Select(link => link.PermissionId)
            .ToArrayAsync(cancellationToken);
        var permissionIds = rolePermissionIds
            .Concat(directPermissionIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissionCodes = await db.Permissions
            .Where(permission => permissionIds.Contains(permission.Id))
            .Select(permission => permission.Code)
            .ToArrayAsync(cancellationToken);

        return new PortalIdentitySessionRecord(
            Authenticated: true,
            UserId: user.Id,
            UserName: user.UserName,
            FullName: user.FullName,
            RoleNames: roles.Select(role => role.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            PermissionCodes: permissionCodes.OrderBy(static code => code, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private sealed record SeedPermission(
        string Id,
        string Code,
        string Name,
        string Category,
        string Description);

    private sealed record SeedRole(
        string Id,
        string Name,
        string Description,
        bool IsSystemRole,
        string[] PermissionIds);

    private sealed record SeedUser(
        string UserName,
        string FullName,
        string Email,
        string Phone,
        string Password,
        bool IsSystemUser,
        bool IsServiceAccount,
        string[] RoleIds);
}
