namespace PathoNet.Api.Components.Shared;

using PathoNet.Api;

public sealed class AccessUserFormModel
{
    public static AccessUserFormModel CreateEmpty() =>
        new()
        {
            UserId = null,
            UserName = string.Empty,
            FullName = string.Empty,
            Email = string.Empty,
            Phone = string.Empty,
            IsActive = true,
            IsServiceAccount = false,
            Password = string.Empty,
            RoleIds = [],
            DirectPermissionIds = []
        };

    internal static AccessUserFormModel FromUser(PortalIdentityUserRecord user) =>
        new()
        {
            UserId = user.Id,
            UserName = user.UserName,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            IsActive = user.IsActive,
            IsServiceAccount = user.IsServiceAccount,
            Password = string.Empty,
            RoleIds = user.RoleIds.ToList(),
            DirectPermissionIds = user.DirectPermissionIds.ToList()
        };

    public string? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsServiceAccount { get; set; }
    public string Password { get; set; } = string.Empty;
    public List<string> RoleIds { get; set; } = [];
    public List<string> DirectPermissionIds { get; set; } = [];
}

public sealed class AccessRoleFormModel
{
    public static AccessRoleFormModel CreateEmpty() =>
        new()
        {
            RoleId = null,
            Name = string.Empty,
            Description = string.Empty,
            PermissionIds = []
        };

    internal static AccessRoleFormModel FromRole(PortalIdentityRoleRecord role) =>
        new()
        {
            RoleId = role.Id,
            Name = role.Name,
            Description = role.Description,
            PermissionIds = role.PermissionIds.ToList()
        };

    public string? RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> PermissionIds { get; set; } = [];
}
