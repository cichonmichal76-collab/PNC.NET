internal static class PortalRoutes
{
    public const string Start = "/blazor";
    public const string Login = "/blazor/login";
    public const string Devices = "/blazor/urzadzenia";
    public const string Analysis = "/blazor/analiza";
    public const string Service = "/blazor/serwis";
    public const string Administration = "/blazor/administracja";
    public const string Health = "/blazor/health";
    public const string Ota = "/blazor/ota";
    public const string Pnc = "/blazor/pnc";
    public const string Rules = "/blazor/rules";
    public const string Diagnostics = "/blazor/diagnostyka";
}

internal static class PortalLegacyRedirects
{
    public static readonly IReadOnlyDictionary<string, string> EndpointRedirects =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/service"] = PortalRoutes.Administration,
            ["/service/health"] = PortalRoutes.Health,
            ["/service/ota"] = PortalRoutes.Ota,
            ["/service/pnc"] = PortalRoutes.Pnc,
            ["/service/rules"] = PortalRoutes.Rules,
            ["/service/analysis"] = PortalRoutes.Analysis,
            ["/service/mock"] = PortalRoutes.Diagnostics,
            ["/service-legacy"] = PortalRoutes.Administration,
            ["/analysis"] = PortalRoutes.Analysis,
            ["/mock"] = PortalRoutes.Diagnostics,
            ["/client"] = PortalRoutes.Devices
        };

    public static readonly IReadOnlyDictionary<string, string> StaticFileRedirects =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/index.html"] = PortalRoutes.Administration,
            ["/client.html"] = PortalRoutes.Devices,
            ["/analysis.html"] = PortalRoutes.Analysis,
            ["/mock.html"] = PortalRoutes.Diagnostics
        };

    public static bool TryResolveStaticRedirect(PathString requestPath, out string redirectTarget)
    {
        var path = requestPath.Value;
        if (!string.IsNullOrWhiteSpace(path) && StaticFileRedirects.TryGetValue(path, out redirectTarget!))
        {
            return true;
        }

        redirectTarget = string.Empty;
        return false;
    }
}
