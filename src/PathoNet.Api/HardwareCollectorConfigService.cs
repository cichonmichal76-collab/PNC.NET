internal sealed class HardwareCollectorConfigService(CollectorConfigStore collectorConfigStore)
{
    public CollectorHardwareConfigStateRecord GetState() =>
        collectorConfigStore.GetState();

    public async Task<BlazorMutationResult> SavePortAsync(
        BlazorCollectorPortInputRecord input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.PortId))
        {
            return BlazorMutationResult.Fail("Nie wybrano portu do zapisania.");
        }

        if (string.IsNullOrWhiteSpace(input.Alias))
        {
            return BlazorMutationResult.Fail("Podaj alias techniczny portu.");
        }

        if (string.IsNullOrWhiteSpace(input.DevicePath))
        {
            return BlazorMutationResult.Fail("Podaj sciezke urzadzenia albo nazwe interfejsu.");
        }

        var state = await collectorConfigStore.SavePortAsync(
            new CollectorPortConfigRecord(
                PortId: input.PortId.Trim(),
                Alias: input.Alias.Trim(),
                InterfaceType: string.IsNullOrWhiteSpace(input.InterfaceType) ? "rs232" : input.InterfaceType.Trim().ToLowerInvariant(),
                DevicePath: input.DevicePath.Trim(),
                NetworkInterfaceName: string.IsNullOrWhiteSpace(input.NetworkInterfaceName) ? null : input.NetworkInterfaceName.Trim(),
                BaudRate: input.BaudRate,
                DataBits: input.DataBits,
                Parity: input.Parity?.Trim() ?? "none",
                StopBits: input.StopBits?.Trim() ?? "one",
                ParserKind: input.ParserKind?.Trim() ?? "generic-text",
                FrameMode: input.FrameMode?.Trim() ?? "line",
                Enabled: input.Enabled,
                AllowSimulationFallback: input.AllowSimulationFallback,
                Description: input.Description?.Trim() ?? string.Empty),
            cancellationToken);

        var savedPort = state.Ports.First(port => string.Equals(port.PortId, input.PortId, StringComparison.OrdinalIgnoreCase));
        return BlazorMutationResult.Ok($"Konfiguracja portu {savedPort.PortId} zostala zapisana. {state.RestartHint}");
    }
}
