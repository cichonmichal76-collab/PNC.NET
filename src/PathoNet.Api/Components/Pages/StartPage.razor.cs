namespace PathoNet.Api.Components.Pages;

using Microsoft.AspNetCore.Components;

public partial class StartPage
{
    private BlazorServiceDashboardState? _state;
    private bool _loading = true;
    private string? _error;
    private string _statusFilter = string.Empty;
    private string _provinceFilter = string.Empty;
    private string _cityFilter = string.Empty;
    private string _hospitalFilter = string.Empty;
    private string _deviceTypeFilter = string.Empty;

    protected override async Task OnInitializedAsync() =>
        await LoadAsync();

    private PortalPncDeviceRecord[] Devices =>
        _state?.PortalState.PncDevices ?? [];

    private string[] AvailableProvinces =>
        Devices.Select(device => device.Province)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[] AvailableCities =>
        Devices.Select(device => device.City)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[] AvailableHospitals =>
        Devices.Select(device => device.Hospital)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[] AvailableDeviceTypes =>
        Devices.SelectMany(device => device.ConnectedDeviceTypes)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private PortalPncDeviceRecord[] FilteredDevices =>
        Devices
            .Where(device => string.IsNullOrWhiteSpace(_statusFilter)
                || (_statusFilter == "online" && device.IsOnline)
                || (_statusFilter == "offline" && !device.IsOnline))
            .Where(device => string.IsNullOrWhiteSpace(_provinceFilter)
                || string.Equals(device.Province, _provinceFilter, StringComparison.OrdinalIgnoreCase))
            .Where(device => string.IsNullOrWhiteSpace(_cityFilter)
                || string.Equals(device.City, _cityFilter, StringComparison.OrdinalIgnoreCase))
            .Where(device => string.IsNullOrWhiteSpace(_hospitalFilter)
                || string.Equals(device.Hospital, _hospitalFilter, StringComparison.OrdinalIgnoreCase))
            .Where(device => string.IsNullOrWhiteSpace(_deviceTypeFilter)
                || device.ConnectedDeviceTypes.Contains(_deviceTypeFilter, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(static device => device.IsOnline)
            .ThenByDescending(static device => device.HealthScore)
            .ThenBy(static device => device.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private int TotalDevices => Devices.Length;
    private int OnlineCount => Devices.Count(static device => device.IsOnline);
    private int OfflineCount => Devices.Count(static device => !device.IsOnline);
    private int AttentionCount => _state?.PortalState.Alerts.Length ?? 0;
    private int InterventionCount => Devices.Count(device => !device.IsOnline || device.Status != "online" || device.HealthScore < 70);
    private int TotalHospitalsCount => Devices.Select(device => device.Hospital).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    private int TotalDeviceTypeCount => Devices.SelectMany(device => device.ConnectedDeviceTypes).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    private int TotalRs232Connections => Devices.Sum(static device => device.Rs232Connected);
    private int TotalRs485Connections => Devices.Sum(static device => device.Rs485Connected);
    private int TotalCanConnections => Devices.Sum(static device => device.CanConnected);
    private int TotalEthernetConnections => Devices.Sum(static device => device.EthernetConnected);
    private int TotalConnectedEndpoints => Devices.Sum(CountConnectedEndpoints);
    private int ConnectedNodeCount => Devices.Count(device => CountConnectedEndpoints(device) > 0);
    private int IntegrationCoveragePercent => TotalDevices == 0 ? 0 : (int)Math.Round((double)ConnectedNodeCount / TotalDevices * 100);
    private int ActiveInterfaceFamilyCount => CountActiveInterfaceFamilies(Devices);
    private int FilteredOnlineCount => FilteredDevices.Count(static device => device.IsOnline);
    private int FilteredOfflineCount => FilteredDevices.Count(static device => !device.IsOnline);
    private int FilteredHospitalsCount => FilteredDevices.Select(device => device.Hospital).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    private int FilteredDeviceTypeCount => FilteredDevices.SelectMany(device => device.ConnectedDeviceTypes).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    private int FilteredRs232Connections => FilteredDevices.Sum(static device => device.Rs232Connected);
    private int FilteredRs485Connections => FilteredDevices.Sum(static device => device.Rs485Connected);
    private int FilteredCanConnections => FilteredDevices.Sum(static device => device.CanConnected);
    private int FilteredEthernetConnections => FilteredDevices.Sum(static device => device.EthernetConnected);
    private int FilteredConnectedEndpoints => FilteredDevices.Sum(CountConnectedEndpoints);
    private int MaxProvinceCount => Math.Max(1, Devices.GroupBy(device => device.Province, StringComparer.OrdinalIgnoreCase).Select(static group => group.Count()).DefaultIfEmpty(0).Max());
    private int MaxDeviceTypeCount => Math.Max(1, Devices.SelectMany(device => device.ConnectedDeviceTypes).GroupBy(type => type, StringComparer.OrdinalIgnoreCase).Select(static group => group.Count()).DefaultIfEmpty(0).Max());

    private DashboardActivityBar[] ActivityBars =>
        (_state?.PortalState.Activity ?? [])
            .Select(bucket => new DashboardActivityBar(
                Label: bucket.Label,
                Total: bucket.Count,
                SalesHeight: Scale(bucket.Count, _state!.PortalState.Activity.Max(item => item.Count)),
                AlertHeight: Scale(bucket.AlarmCount + bucket.WarnCount, Math.Max(1, _state.PortalState.Activity.Max(item => item.AlarmCount + item.WarnCount))),
                HealthHeight: Scale(EstimateHealthBand(bucket), 100)))
            .ToArray();

    private DashboardLocationSummary[] TopLocations =>
        Devices.GroupBy(device => device.Province, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DashboardLocationSummary(
                Label: group.Key,
                Detail: $"{group.Select(device => device.Hospital).Distinct(StringComparer.OrdinalIgnoreCase).Count()} szpitali / {group.Count()} PNC",
                Count: group.Count(),
                Intensity: Scale(group.Count(), MaxProvinceCount)))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

    private DashboardTypeSummary[] DeviceTypeBreakdown =>
        Devices.SelectMany(device => device.ConnectedDeviceTypes.Select(type => new { device.DeviceCode, Type = type }))
            .GroupBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DashboardTypeSummary(
                Label: group.Key,
                Detail: $"{group.Select(item => item.DeviceCode).Distinct(StringComparer.OrdinalIgnoreCase).Count()} wezlow z tym typem",
                Count: group.Count(),
                Intensity: Scale(group.Count(), MaxDeviceTypeCount)))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

    private PortalPncDeviceRecord[] InterventionDevices =>
        FilteredDevices
            .Where(device => !device.IsOnline || device.Status != "online" || device.HealthScore < 70)
            .OrderBy(device => device.IsOnline)
            .ThenBy(static device => device.HealthScore)
            .Take(5)
            .ToArray();

    private HistoryEventRecord[] RecentEvents =>
        (_state?.PortalState.History ?? [])
            .Take(6)
            .ToArray();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;

        try
        {
            _state = await PortalService.GetServiceDashboardAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _error = $"Nie udalo sie zaladowac dashboardu: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void ResetFilters()
    {
        _statusFilter = string.Empty;
        _provinceFilter = string.Empty;
        _cityFilter = string.Empty;
        _hospitalFilter = string.Empty;
        _deviceTypeFilter = string.Empty;
    }

    private static int EstimateHealthBand(ActivityBucketRecord bucket)
    {
        var pressure = (bucket.AlarmCount * 20) + (bucket.WarnCount * 10);
        return Math.Clamp(100 - pressure, 15, 100);
    }

    private static int CountConnectedEndpoints(PortalPncDeviceRecord device) =>
        device.Rs232Connected + device.Rs485Connected + device.CanConnected + device.EthernetConnected;

    private static int CountActiveInterfaceFamilies(IEnumerable<PortalPncDeviceRecord> devices)
    {
        var hasRs232 = devices.Any(static device => device.Rs232Connected > 0);
        var hasRs485 = devices.Any(static device => device.Rs485Connected > 0);
        var hasCan = devices.Any(static device => device.CanConnected > 0);
        var hasEthernet = devices.Any(static device => device.EthernetConnected > 0);

        return (hasRs232 ? 1 : 0)
             + (hasRs485 ? 1 : 0)
             + (hasCan ? 1 : 0)
             + (hasEthernet ? 1 : 0);
    }

    private static int Scale(int value, int max)
    {
        if (max <= 0)
        {
            return 14;
        }

        return Math.Clamp((int)Math.Round((double)value / max * 100), 14, 100);
    }

    private static string MapTone(string value) =>
        value.ToLowerInvariant() switch
        {
            "online" => "online",
            "attention" => "attention",
            "critical" => "critical",
            "alarm" => "critical",
            "warn" => "attention",
            _ => "info"
        };

    private sealed record DashboardActivityBar(
        string Label,
        int Total,
        int SalesHeight,
        int AlertHeight,
        int HealthHeight);

    private sealed record DashboardLocationSummary(
        string Label,
        string Detail,
        int Count,
        int Intensity);

    private sealed record DashboardTypeSummary(
        string Label,
        string Detail,
        int Count,
        int Intensity);
}
