using PathoNet.Contracts;

internal sealed partial class HardwareIntegrationStateService(
    SimulationStore simulationStore,
    ServiceHealthStore serviceHealthStore,
    CollectorRuntimeStateStore collectorRuntimeStateStore)
{
    public Task<HardwareIntegrationStateRecord> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var portalState = simulationStore.PortalState();
        var diagnostics = simulationStore.Snapshot();
        var serviceHealth = serviceHealthStore.GetState();
        var notifications = simulationStore.NotificationsSnapshot();
        var runtimeState = collectorRuntimeStateStore.GetState();

        return Task.FromResult(BuildState(portalState, diagnostics, serviceHealth, notifications, runtimeState));
    }

    private static HardwareIntegrationStateRecord BuildState(
        PortalStateRecord portalState,
        PortalDiagnosticsRecord diagnostics,
        PortalServiceHealthStateRecord serviceHealth,
        DeviceNotification[] notifications,
        CollectorRuntimeStateSnapshotDocument? runtimeState)
    {
        var notificationsByPort = notifications
            .GroupBy(static notification => notification.Port, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var runtimeByPort = runtimeState?.Ports
            .GroupBy(static port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CollectorPortRuntimeStateDocument>(StringComparer.OrdinalIgnoreCase);

        var ports = HardwareIntegrationDefinitions.Ports
            .Select(definition => BuildHardwarePortStatus(
                definition,
                runtimeByPort.GetValueOrDefault(definition.PortId),
                notificationsByPort.GetValueOrDefault(definition.PortId)))
            .ToArray();

        var modules = HardwareIntegrationDefinitions.FixedModules
            .Select(definition => BuildFixedModuleStatus(
                definition,
                portalState,
                notificationsByPort.GetValueOrDefault(definition.ModuleId)))
            .ToArray();

        var collector = serviceHealth.Services.FirstOrDefault(service =>
            service.Name.Contains("collector", StringComparison.OrdinalIgnoreCase)
            || service.DisplayName.Contains("collector", StringComparison.OrdinalIgnoreCase));

        var summary = new HardwareIntegrationSummaryRecord(
            ExpectedPortCount: ports.Length,
            DetectedPortCount: ports.Count(static port => port.Detected),
            RxActiveCount: ports.Count(static port => port.RxActive == true),
            TxActiveCount: ports.Count(static port => port.TxActive == true),
            SimulationFallbackCount: ports.Count(static port => port.SimulationFallback),
            FixedModuleCount: modules.Length,
            FixedModuleReadyCount: modules.Count(static module => module.Present && module.Status != "critical"));

        var collectorStatus = collector?.Status ?? "unknown";
        var collectorSummary = collector is null
            ? "Nie znaleziono runtime state collectora. Sprawdz czy usluga jest uruchomiona i czy zapisuje heartbeat runtime."
            : $"{collector.DisplayName}: {collector.Status}, PID {(collector.ProcessId?.ToString() ?? "-")}, tryb {collector.RuntimeMode}, restartow {collector.RestartCount}.";

        var selfCheck = BuildSelfCheckState(portalState, collectorStatus, collectorSummary, summary, modules);

        return new HardwareIntegrationStateRecord(
            GeneratedAtUtc: diagnostics.LastNotificationAtUtc ?? DateTimeOffset.UtcNow,
            CollectorStatus: collectorStatus,
            CollectorSummary: collectorSummary,
            Summary: summary,
            SelfCheck: selfCheck,
            Personalization: BuildPersonalizationState(portalState),
            Ports: ports,
            Modules: modules,
            Checklist:
            [
                "Podlacz laptop serwisowy do ETH1 albo wejdz lokalnie przez panel PNC na porcie 5000.",
                "Zweryfikuj, czy collector jest online i czy nie pracuje tylko w trybie symulacji przejsciowej.",
                "Sprawdz fizyczne wykrycie portu oraz to, czy pojawil sie realny RX dla podlaczonego przewodu.",
                "Jesli port jest widoczny, ale brak ruchu, doprecyzuj baudrate, parity, stop bits i rodzaj okablowania.",
                "Dla CAN i Ethernet sprawdz link oraz liczniki RX/TX przed przejsciem do parsera protokolu.",
                "Po dostarczeniu prawdziwego hardware wylacz fallback symulacyjny dla portow, ktore maja pracowac live."
            ]);
    }
}
