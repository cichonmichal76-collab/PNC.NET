internal sealed partial class BlazorPortalService
{
    public Task<HardwareIntegrationStateRecord> GetHardwareIntegrationStateAsync(CancellationToken cancellationToken) =>
        hardwareIntegrationStateService.GetStateAsync(cancellationToken);

    public Task<HardwarePortSignalTestResultRecord> TestHardwarePortSignalAsync(
        string portId,
        CancellationToken cancellationToken) =>
        hardwareSignalTestService.TestPortSignalAsync(portId, cancellationToken);
}
