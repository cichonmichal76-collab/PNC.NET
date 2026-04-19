using Microsoft.EntityFrameworkCore;

internal sealed class PathoNetIdentityDbContext(DbContextOptions<PathoNetIdentityDbContext> options) : DbContext(options)
{
    public DbSet<PathoNetIdentityUserEntity> Users => Set<PathoNetIdentityUserEntity>();
    public DbSet<PathoNetIdentityRoleEntity> Roles => Set<PathoNetIdentityRoleEntity>();
    public DbSet<PathoNetIdentityPermissionEntity> Permissions => Set<PathoNetIdentityPermissionEntity>();
    public DbSet<PathoNetIdentityUserRoleEntity> UserRoles => Set<PathoNetIdentityUserRoleEntity>();
    public DbSet<PathoNetIdentityRolePermissionEntity> RolePermissions => Set<PathoNetIdentityRolePermissionEntity>();
    public DbSet<PathoNetIdentityUserPermissionEntity> UserPermissions => Set<PathoNetIdentityUserPermissionEntity>();
    public DbSet<PathoNetIdentityAuditEntity> AuditEntries => Set<PathoNetIdentityAuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PathoNetIdentityUserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(80);
            entity.Property(item => item.UserName).HasMaxLength(80);
            entity.Property(item => item.FullName).HasMaxLength(160);
            entity.Property(item => item.Email).HasMaxLength(160);
            entity.Property(item => item.Phone).HasMaxLength(64);
            entity.Property(item => item.PasswordHash).HasMaxLength(1024);
            entity.HasIndex(item => item.UserName).IsUnique();
        });

        modelBuilder.Entity<PathoNetIdentityRoleEntity>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(80);
            entity.Property(item => item.Name).HasMaxLength(120);
            entity.Property(item => item.Description).HasMaxLength(512);
            entity.HasIndex(item => item.Name).IsUnique();
        });

        modelBuilder.Entity<PathoNetIdentityPermissionEntity>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(120);
            entity.Property(item => item.Code).HasMaxLength(120);
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.Property(item => item.Category).HasMaxLength(80);
            entity.Property(item => item.Description).HasMaxLength(512);
            entity.HasIndex(item => item.Code).IsUnique();
        });

        modelBuilder.Entity<PathoNetIdentityUserRoleEntity>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(item => new { item.UserId, item.RoleId });
            entity.Property(item => item.UserId).HasMaxLength(80);
            entity.Property(item => item.RoleId).HasMaxLength(80);
        });

        modelBuilder.Entity<PathoNetIdentityRolePermissionEntity>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(item => new { item.RoleId, item.PermissionId });
            entity.Property(item => item.RoleId).HasMaxLength(80);
            entity.Property(item => item.PermissionId).HasMaxLength(120);
        });

        modelBuilder.Entity<PathoNetIdentityUserPermissionEntity>(entity =>
        {
            entity.ToTable("UserPermissions");
            entity.HasKey(item => new { item.UserId, item.PermissionId });
            entity.Property(item => item.UserId).HasMaxLength(80);
            entity.Property(item => item.PermissionId).HasMaxLength(120);
        });

        modelBuilder.Entity<PathoNetIdentityAuditEntity>(entity =>
        {
            entity.ToTable("AuditEntries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Actor).HasMaxLength(120);
            entity.Property(item => item.Action).HasMaxLength(80);
            entity.Property(item => item.SubjectType).HasMaxLength(80);
            entity.Property(item => item.SubjectId).HasMaxLength(120);
            entity.Property(item => item.Summary).HasMaxLength(512);
        });
    }
}

internal sealed class PathoNetIdentityUserEntity
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsServiceAccount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? LastPasswordChangedAtUtc { get; set; }
}

internal sealed class PathoNetIdentityRoleEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class PathoNetIdentityPermissionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemPermission { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class PathoNetIdentityUserRoleEntity
{
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public DateTimeOffset GrantedAtUtc { get; set; }
}

internal sealed class PathoNetIdentityRolePermissionEntity
{
    public string RoleId { get; set; } = string.Empty;
    public string PermissionId { get; set; } = string.Empty;
    public DateTimeOffset GrantedAtUtc { get; set; }
}

internal sealed class PathoNetIdentityUserPermissionEntity
{
    public string UserId { get; set; } = string.Empty;
    public string PermissionId { get; set; } = string.Empty;
    public DateTimeOffset GrantedAtUtc { get; set; }
}

internal sealed class PathoNetIdentityAuditEntity
{
    public long Id { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
