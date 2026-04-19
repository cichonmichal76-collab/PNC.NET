using System.Security.Claims;

internal static class PathoNetClaimTypes
{
    public const string Permission = "pathonet.permission";
    public const string RoleId = "pathonet.role_id";
    public const string DisplayName = "pathonet.display_name";
}

internal static class PathoNetPermissions
{
    public const string DashboardView = "dashboard.view";
    public const string AnalysisView = "analysis.view";
    public const string HealthView = "health.view";
    public const string HealthManage = "health.manage";
    public const string RulesView = "rules.view";
    public const string RulesManage = "rules.manage";
    public const string OtaView = "ota.view";
    public const string OtaManage = "ota.manage";
    public const string PncView = "pnc.view";
    public const string PncManage = "pnc.manage";
    public const string AlertsAck = "alerts.ack";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string MockView = "mock.view";
    public const string HdmiView = "hdmi.view";

    public static readonly string[] All =
    [
        DashboardView,
        AnalysisView,
        HealthView,
        HealthManage,
        RulesView,
        RulesManage,
        OtaView,
        OtaManage,
        PncView,
        PncManage,
        AlertsAck,
        UsersView,
        UsersManage,
        MockView,
        HdmiView
    ];
}

internal static class PathoNetPolicyNames
{
    public const string Authenticated = "pathonet.authenticated";
}

internal static class PathoNetPrincipalExtensions
{
    public static bool HasPermission(this ClaimsPrincipal principal, string permissionCode) =>
        principal.HasClaim(PathoNetClaimTypes.Permission, permissionCode);
}
