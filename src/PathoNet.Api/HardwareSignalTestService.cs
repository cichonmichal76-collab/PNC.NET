internal sealed class HardwareSignalTestService(HardwareIntegrationStateService stateService)
{
    public async Task<HardwarePortSignalTestResultRecord> TestPortSignalAsync(
        string portId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await stateService.GetStateAsync(cancellationToken);
        var port = state.Ports.FirstOrDefault(candidate =>
            string.Equals(candidate.PortId, portId, StringComparison.OrdinalIgnoreCase));

        if (port is null)
        {
            return new HardwarePortSignalTestResultRecord(
                PortId: portId,
                Success: false,
                Tone: "critical",
                Summary: "Nie znaleziono wybranego portu w runtime collectora.",
                Observations:
                [
                    "Port nie wystepuje w aktualnej topologii runtime."
                ],
                Recommendation: "Wroc do kroku 1 i wybierz port z listy collectora.");
        }

        var observations = new List<string>
        {
            $"Stan portu: {port.ConnectionState}",
            $"Wykrycie portu: {(port.Detected ? "tak" : "nie")}",
            $"RX aktywne: {DisplayNullableBool(port.RxActive)}",
            $"TX aktywne: {DisplayNullableBool(port.TxActive)}",
            $"Link: {DisplayNullableBool(port.LinkUp)}",
            $"Tryb pracy: {port.Mode}"
        };

        if (!string.IsNullOrWhiteSpace(port.LastSignalAt))
        {
            observations.Add($"Ostatni sygnal: {port.LastSignalAt}");
        }

        if (!string.IsNullOrWhiteSpace(port.LastText))
        {
            observations.Add($"Ostatni komunikat: {port.LastText}");
        }

        var success = port.Detected && (port.RxActive == true || port.TxActive == true || port.LinkUp == true);
        var tone = success
            ? "online"
            : port.Detected
                ? "attention"
                : "critical";
        var summary = success
            ? $"Port {port.PortId} wyglada na gotowy do wdrozenia i raportuje aktywny sygnal."
            : port.Detected
                ? $"Port {port.PortId} jest widoczny, ale collector nie potwierdzil jeszcze aktywnego sygnalu."
                : $"Port {port.PortId} nie zostal jeszcze potwierdzony przez collectora.";

        return new HardwarePortSignalTestResultRecord(
            PortId: port.PortId,
            Success: success,
            Tone: tone,
            Summary: summary,
            Observations: observations.ToArray(),
            Recommendation: port.Recommendation);
    }

    private static string DisplayNullableBool(bool? value) =>
        value switch
        {
            true => "tak",
            false => "nie",
            null => "n/d"
        };
}
