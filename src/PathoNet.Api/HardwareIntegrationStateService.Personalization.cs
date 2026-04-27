internal sealed partial class HardwareIntegrationStateService
{
    private static HardwarePersonalizationStateRecord BuildPersonalizationState(PortalStateRecord portalState)
    {
        var detectedMainboard = portalState.Mainboards.FirstOrDefault();
        var detectedPnc = portalState.PncDevices.FirstOrDefault();
        var locations = portalState.PncDevices
            .Select(static device => new HardwareLocationOptionRecord(
                device.Province,
                device.City,
                device.Hospital,
                device.Site))
            .Distinct()
            .OrderBy(static location => location.Province, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static location => location.City, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static location => location.Hospital, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static location => location.Site, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new HardwarePersonalizationStateRecord(
            DetectedDeviceCode: detectedPnc?.DeviceCode ?? "PNC",
            DetectedSimNumber: portalState.Lte.SimNumber,
            DetectedSoftwareVersion: detectedPnc?.Firmware ?? detectedMainboard?.Firmware ?? "n/d",
            DetectedBoardSerialNumber: detectedMainboard?.BoardSerialNumber ?? "n/d",
            Locations: locations,
            VariantOptions:
            [
                "Standard",
                "Ze switchem",
                "Z dodatkowymi portami RS",
                "Ze switchem i dodatkowymi portami RS"
            ]);
    }
}
