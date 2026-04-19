internal sealed partial class BlazorPortalService
{
    public CollectorHardwareConfigStateRecord GetCollectorConfigState() =>
        hardwareCollectorConfigService.GetState();

    public Task<BlazorMutationResult> SaveCollectorPortAsync(
        BlazorCollectorPortInputRecord input,
        CancellationToken cancellationToken) =>
        hardwareCollectorConfigService.SavePortAsync(input, cancellationToken);
}
