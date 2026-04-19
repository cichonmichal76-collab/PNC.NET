using System.Security.Claims;

internal sealed record PortalIdentityStateRecord(
    PortalIdentitySummaryRecord Summary,
    PortalIdentityUserRecord[] Users,
    PortalIdentityRoleRecord[] Roles,
    PortalIdentityPermissionRecord[] Permissions,
    PortalIdentityAuditRecord[] AuditLog);

internal sealed record PortalIdentitySummaryRecord(
    int UserCount,
    int ActiveUserCount,
    int RoleCount,
    int PermissionCount,
    int RoleAssignmentCount,
    int RolePermissionAssignmentCount,
    int DirectPermissionAssignmentCount);

internal sealed record PortalIdentityUserRecord(
    string Id,
    string UserName,
    string FullName,
    string Email,
    string Phone,
    bool IsActive,
    bool IsServiceAccount,
    string[] RoleIds,
    string[] DirectPermissionIds,
    string[] EffectivePermissionCodes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastPasswordChangedAtUtc);

public sealed record PortalIdentityRoleRecord(
    string Id,
    string Name,
    string Description,
    bool IsSystemRole,
    string[] PermissionIds,
    string[] PermissionCodes,
    int UserCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PortalIdentityPermissionRecord(
    string Id,
    string Code,
    string Name,
    string Category,
    string Description,
    bool IsSystemPermission);

internal sealed record PortalIdentityAuditRecord(
    long Id,
    string Actor,
    string Action,
    string SubjectType,
    string SubjectId,
    string Summary,
    DateTimeOffset CreatedAtUtc);

internal sealed record IdentityMutationResultRecord(
    bool Success,
    string Message);

internal sealed record PortalIdentitySessionRecord(
    bool Authenticated,
    string? UserId,
    string? UserName,
    string? FullName,
    string[] RoleNames,
    string[] PermissionCodes);

internal sealed record IdentityAuthenticationResultRecord(
    bool Success,
    string Message,
    ClaimsPrincipal? Principal,
    PortalIdentitySessionRecord Session);

internal sealed record IdentityLoginRequestRecord(
    string UserName,
    string Password);

internal sealed record BlazorAccessUserInputRecord(
    string? UserId,
    string UserName,
    string FullName,
    string? Email,
    string? Phone,
    bool IsActive,
    bool IsServiceAccount,
    string? Password,
    string[] RoleIds,
    string[] DirectPermissionIds);

internal sealed record BlazorAccessRoleInputRecord(
    string? RoleId,
    string Name,
    string? Description,
    string[] PermissionIds);
