using PathoNet.Contracts;

internal sealed class HardwareIntegrationStateService(
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

    private static HardwareSelfCheckStateRecord BuildSelfCheckState(
        PortalStateRecord portalState,
        string collectorStatus,
        string collectorSummary,
        HardwareIntegrationSummaryRecord summary,
        HardwareFixedModuleStatusRecord[] modules)
    {
        var mainboard = portalState.Mainboards.FirstOrDefault();
        var lte = portalState.Lte;
        var sdModule = modules.FirstOrDefault(module => string.Equals(module.ModuleId, "SD-CARD", StringComparison.OrdinalIgnoreCase));

        var items = new[]
        {
            BuildSelfCheckItem(
                key: "power",
                title: "Zasilanie i plyta glowna",
                iconLabel: "PWR",
                required: true,
                passed: mainboard is not null
                    && mainboard.SupplyVoltage > 0
                    && mainboard.TemperatureC is > 0 and < 90,
                progressPercent: null,
                progressLabel: null,
                summary: mainboard is null
                    ? "Brak telemetryki plyty glownej."
                    : $"Status {mainboard.Status}, napiecie {mainboard.SupplyVoltage:0.0} V, temperatura {mainboard.TemperatureC} C.",
                recommendation: mainboard is not null
                    && mainboard.SupplyVoltage > 0
                    && mainboard.TemperatureC is > 0 and < 90
                    ? "Zasilanie i podstawowa telemetria plyty sa poprawne na etapie pierwszego uruchomienia."
                    : "Sprawdz zasilanie, boot plyty glownej i podstawowa telemetrie urzadzenia."),
            BuildSelfCheckItem(
                key: "collector",
                title: "Collector i lokalne uslugi",
                iconLabel: "COL",
                required: true,
                passed: string.Equals(collectorStatus, "online", StringComparison.OrdinalIgnoreCase),
                progressPercent: null,
                progressLabel: null,
                summary: collectorSummary,
                recommendation: string.Equals(collectorStatus, "online", StringComparison.OrdinalIgnoreCase)
                    ? "Collector pracuje poprawnie i moze zbierac sygnaly z urzadzenia."
                    : "Napraw collector lub runtime zanim przejdziesz do konfiguracji portow."),
            BuildSelfCheckItem(
                key: "lte-modem",
                title: "Modem LTE",
                iconLabel: "LTE",
                required: true,
                passed: string.Equals(lte.Status, "online", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(lte.OperatorName),
                progressPercent: null,
                progressLabel: null,
                summary: $"{lte.OperatorName}, {lte.NetworkType}, status {lte.Status}.",
                recommendation: string.Equals(lte.Status, "online", StringComparison.OrdinalIgnoreCase)
                    ? "Modem odpowiada i jest gotowy do pracy w lokalizacji."
                    : "Zweryfikuj modem, antene i rejestracje w sieci operatora."),
            BuildSelfCheckItem(
                key: "sim",
                title: "Karta SIM",
                iconLabel: "SIM",
                required: true,
                passed: !string.IsNullOrWhiteSpace(lte.SimNumber),
                progressPercent: null,
                progressLabel: null,
                summary: string.IsNullOrWhiteSpace(lte.SimNumber)
                    ? "Brak numeru SIM w telemetryce urzadzenia."
                    : $"SIM {lte.SimNumber}.",
                recommendation: !string.IsNullOrWhiteSpace(lte.SimNumber)
                    ? "Karta SIM jest wykryta przez system."
                    : "Sprawdz osadzenie karty SIM i konfiguracje modemu."),
            BuildSelfCheckItem(
                key: "signal",
                title: "Sila sygnalu",
                iconLabel: "SIG",
                required: true,
                passed: lte.SignalPercent >= 35,
                progressPercent: lte.SignalPercent,
                progressLabel: DescribeSignalBand(lte.SignalPercent),
                summary: $"Sygnał {lte.SignalPercent}% ({lte.SignalDbm} dBm), jakosc {lte.SignalQuality}.",
                recommendation: lte.SignalPercent >= 35
                    ? "Sygnał jest wystarczajacy do przejscia dalej."
                    : "Zmien polozenie anteny lub sprawdz zasieg w obiekcie przed wydaniem PNC."),
            BuildSelfCheckItem(
                key: "storage",
                title: "Storage / karta SD",
                iconLabel: "SD",
                required: true,
                passed: sdModule?.Present == true && !string.Equals(sdModule.Status, "critical", StringComparison.OrdinalIgnoreCase),
                progressPercent: null,
                progressLabel: null,
                summary: sdModule?.Summary ?? "Brak potwierdzenia storage.",
                recommendation: sdModule?.Present == true && !string.Equals(sdModule.Status, "critical", StringComparison.OrdinalIgnoreCase)
                    ? "Storage jest gotowy na logi i staging OTA."
                    : "Zweryfikuj nosnik systemowy i miejsce na dane przed wdrozeniem.")
        };

        var requiredCount = items.Count(static item => item.Required);
        var passedRequiredCount = items.Count(static item => item.Required && item.Passed);
        var completed = requiredCount > 0 && passedRequiredCount == requiredCount;
        var summaryText = completed
            ? "PNC gotowy do wdrozenia. Wszystkie wymagane testy self-check zakonczone statusem PASS."
            : $"Self-check w toku: PASS {passedRequiredCount}/{requiredCount} wymaganych testow.";
        var recommendation = completed
            ? "Mozesz przejsc do etapu konfiguracji portow i podpietych urzadzen."
            : "Usun pozycje NEGATIVE zanim przejdziesz do kolejnego etapu wdrozenia.";

        return new HardwareSelfCheckStateRecord(
            RequiredCount: requiredCount,
            PassedRequiredCount: passedRequiredCount,
            Completed: completed,
            Summary: summaryText,
            Recommendation: recommendation,
            Items: items);
    }

    private static HardwareSelfCheckItemRecord BuildSelfCheckItem(
        string key,
        string title,
        string iconLabel,
        bool required,
        bool passed,
        int? progressPercent,
        string? progressLabel,
        string summary,
        string recommendation) =>
        new(
            Key: key,
            Title: title,
            IconLabel: iconLabel,
            StatusLabel: passed ? "PASS" : "NEGATIVE",
            Tone: passed ? "online" : "critical",
            Passed: passed,
            Required: required,
            ProgressPercent: progressPercent,
            ProgressLabel: progressLabel,
            Summary: summary,
            Recommendation: recommendation);

    private static string DescribeSignalBand(int signalPercent) =>
        signalPercent switch
        {
            >= 70 => "dobry",
            >= 35 => "sredni",
            _ => "slaby"
        };

    private static HardwarePortStatusRecord BuildHardwarePortStatus(
        HardwarePortDefinition definition,
        CollectorPortRuntimeStateDocument? runtime,
        DeviceNotification[]? notifications)
    {
        notifications ??= [];

        var latest = notifications.FirstOrDefault();
        var monitorNotifications = notifications
            .Where(static notification => notification.Source.EndsWith("-monitor", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var dataNotifications = notifications
            .Where(static notification => !notification.Source.EndsWith("-monitor", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var inferredSimulationFallback = monitorNotifications.Any(notification =>
                notification.Text.Contains("symulacji przejsciowej", StringComparison.OrdinalIgnoreCase))
            || (definition.IsSerial && dataNotifications.Any() && !monitorNotifications.Any(notification =>
                notification.Text.Contains("Wykryto fizyczny port", StringComparison.OrdinalIgnoreCase)
                || notification.Text.Contains("zostal otwarty", StringComparison.OrdinalIgnoreCase)));

        var inferredDetected = definition.InterfaceType switch
        {
            "rs232" or "rs485" => monitorNotifications.Any(notification =>
                notification.Text.Contains("Wykryto fizyczny port", StringComparison.OrdinalIgnoreCase)
                || notification.Text.Contains("zostal otwarty", StringComparison.OrdinalIgnoreCase)),
            "can" or "ethernet" => monitorNotifications.Any(notification =>
                notification.Text.Contains("Wykryto interfejs", StringComparison.OrdinalIgnoreCase)),
            "dry-contact" => monitorNotifications.Any(notification =>
                notification.Text.Contains("jest dostepne", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

        bool? inferredLinkUp = null;
        bool? inferredRxActive = null;
        bool? inferredTxActive = null;

        if (definition.IsBus)
        {
            var latestBusSignal = notifications.FirstOrDefault(notification =>
                notification.Raw.Contains("present=", StringComparison.OrdinalIgnoreCase)
                || notification.Raw.Contains("rx=", StringComparison.OrdinalIgnoreCase)
                || notification.Raw.Contains("tx=", StringComparison.OrdinalIgnoreCase));

            inferredLinkUp = TryReadBooleanFlag(latestBusSignal?.Raw, "link");
            inferredRxActive = TryReadActivityFlag(latestBusSignal?.Raw, "rx");
            inferredTxActive = TryReadActivityFlag(latestBusSignal?.Raw, "tx");
        }
        else if (definition.IsSerial)
        {
            inferredRxActive = monitorNotifications.Any(notification =>
                    notification.Text.Contains("Wykryto odbior danych", StringComparison.OrdinalIgnoreCase))
                || dataNotifications.Any();
        }

        var connectionState = NormalizeRuntimeState(runtime?.State);
        var detected = runtime?.CablePresent ?? inferredDetected;
        var cablePresent = runtime?.CablePresent ?? inferredDetected;
        var linkUp = runtime?.LinkUp ?? inferredLinkUp;
        var rxActive = runtime?.RxActive ?? inferredRxActive;
        var txActive = runtime?.TxActive ?? inferredTxActive;
        var simulationFallback = runtime?.SimulationFallback ?? inferredSimulationFallback;

        var mode = simulationFallback
            ? "symulacja przejsciowa"
            : connectionState switch
            {
                "tx" => "transmisja TX",
                "rx" => "transmisja RX",
                "link" => "link gotowy",
                "connecting" => "stabilizacja polaczenia",
                _ => "oczekiwanie na hardware"
            };

        var status = simulationFallback
            ? "attention"
            : !detected
                ? "attention"
                : connectionState is "tx" or "rx" or "link" || rxActive == true || txActive == true || linkUp == true
                    ? "online"
                    : "attention";

        var summary = runtime?.Summary
            ?? latest?.Text
            ?? $"{definition.PortId}: brak jeszcze telemetryki z collectora dla tego wejscia.";

        var lastSignalAt = FormatRuntimeTimestamp(runtime?.LastRxAtUtc ?? runtime?.LastTxAtUtc ?? runtime?.LastDetectedAtUtc)
            ?? latest?.Meta.DateMess
            ?? "brak sygnalu";

        return new HardwarePortStatusRecord(
            PortId: definition.PortId,
            Alias: definition.Alias,
            InterfaceType: definition.InterfaceType,
            ExpectedPath: definition.ExpectedPath,
            ConnectionState: connectionState,
            ParserKind: definition.ParserKind,
            FrameMode: definition.FrameMode,
            Purpose: definition.Purpose,
            Detected: detected,
            CablePresent: cablePresent,
            LinkUp: linkUp,
            RxActive: rxActive,
            TxActive: txActive,
            SimulationFallback: simulationFallback,
            Mode: mode,
            Status: status,
            StateSinceUtc: runtime?.StateSinceUtc,
            LastRxAtUtc: runtime?.LastRxAtUtc,
            LastTxAtUtc: runtime?.LastTxAtUtc,
            RxCounter: runtime?.RxCounter,
            TxCounter: runtime?.TxCounter,
            LastSignalAt: lastSignalAt,
            Summary: summary,
            Recommendation: BuildPortRecommendation(definition, connectionState, detected, rxActive, txActive, simulationFallback),
            LastRaw: runtime?.LastRaw ?? latest?.Raw,
            LastText: runtime?.LastText ?? latest?.Text);
    }

    private static HardwareFixedModuleStatusRecord BuildFixedModuleStatus(
        HardwareFixedModuleDefinition definition,
        PortalStateRecord portalState,
        DeviceNotification[]? notifications)
    {
        notifications ??= [];
        var latest = notifications.FirstOrDefault();

        return definition.ModuleId switch
        {
            "MAINBOARD" => BuildMainboardModuleStatus(portalState, latest),
            "LTE-SIM" => BuildLteModuleStatus(portalState, latest),
            "SD-CARD" => BuildSdCardModuleStatus(latest),
            _ => new HardwareFixedModuleStatusRecord(
                ModuleId: definition.ModuleId,
                DisplayName: definition.DisplayName,
                Present: latest is not null,
                Status: latest is null ? "attention" : "online",
                LastSignalAt: latest?.Meta.DateMess ?? "brak sygnalu",
                Summary: latest?.Text ?? $"{definition.DisplayName}: brak telemetryki.",
                Recommendation: "Zweryfikuj modul staly podczas pierwszego uruchomienia.")
        };
    }

    private static HardwareFixedModuleStatusRecord BuildMainboardModuleStatus(
        PortalStateRecord portalState,
        DeviceNotification? latestNotification)
    {
        var board = portalState.Mainboards.FirstOrDefault();
        if (board is null)
        {
            return new HardwareFixedModuleStatusRecord(
                ModuleId: "MAINBOARD",
                DisplayName: "Plyta glowna",
                Present: latestNotification is not null,
                Status: "attention",
                LastSignalAt: latestNotification?.Meta.DateMess ?? "brak sygnalu",
                Summary: latestNotification?.Text ?? "Brak telemetryki plyty glownej w stanie portalu.",
                Recommendation: "Po dostarczeniu urzadzenia potwierdz numer seryjny, watchdog i telemetryke CPU/RAM.");
        }

        return new HardwareFixedModuleStatusRecord(
            ModuleId: "MAINBOARD",
            DisplayName: "Plyta glowna",
            Present: true,
            Status: board.Status,
            LastSignalAt: board.LastSeen,
            Summary: $"{board.BoardRevision}, serial {board.BoardSerialNumber}, CPU {board.CpuLoadPercent}% / RAM {board.MemoryPercent}% / dysk {board.StoragePercent}%.",
            Recommendation: board.Status == "online"
                ? "Plyta glowna raportuje poprawnie. Przy pierwszym starcie sprawdz tylko temperatury i zasilanie."
                : "Zweryfikuj temperature, zasilanie i watchdog plyty glownej przed dalszym commissioningiem.");
    }

    private static HardwareFixedModuleStatusRecord BuildLteModuleStatus(
        PortalStateRecord portalState,
        DeviceNotification? latestNotification)
    {
        var lte = portalState.Lte;
        var present = !string.IsNullOrWhiteSpace(lte.SimNumber);
        return new HardwareFixedModuleStatusRecord(
            ModuleId: "LTE-SIM",
            DisplayName: "Modem LTE i karta SIM",
            Present: present,
            Status: lte.Status,
            LastSignalAt: lte.SampledAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            Summary: $"{lte.OperatorName}, {lte.NetworkType}, sygnal {lte.SignalPercent}% ({lte.SignalDbm} dBm), SIM {lte.SimNumber}.",
            Recommendation: lte.Status == "online"
                ? "Lacznosc GSM jest gotowa. Przy uruchomieniu onsite potwierdz attach do operatora i zasieg w obiekcie."
                : latestNotification?.Text ?? "Sprawdz karte SIM, antene i rejestracje do operatora GSM.");
    }

    private static HardwareFixedModuleStatusRecord BuildSdCardModuleStatus(DeviceNotification? latestNotification)
    {
        var present = latestNotification is null || !string.Equals(latestNotification.Level, "warn", StringComparison.OrdinalIgnoreCase);
        return new HardwareFixedModuleStatusRecord(
            ModuleId: "SD-CARD",
            DisplayName: "Karta SD",
            Present: present,
            Status: present ? "online" : "attention",
            LastSignalAt: latestNotification?.Meta.DateMess ?? "brak sygnalu",
            Summary: latestNotification?.Text ?? "Karta SD jest traktowana jako staly element urzadzenia i oczekuje na potwierdzenie podczas bootu.",
            Recommendation: present
                ? "Nosnik jest przygotowany. Przy pierwszym starcie sprawdz miejsce na logi i staging OTA."
                : "Zweryfikuj obecnosc karty SD i integralnosc systemu plikow przed wydaniem urzadzenia.");
    }

    private static bool? TryReadBooleanFlag(string? raw, string key)
    {
        var value = TryReadFlag(raw, key);
        if (value is null)
        {
            return null;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? true
            : value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

    private static bool? TryReadActivityFlag(string? raw, string key)
    {
        var value = TryReadFlag(raw, key);
        if (value is null)
        {
            return null;
        }

        return value.Equals("active", StringComparison.OrdinalIgnoreCase)
            ? true
            : value.Equals("idle", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

    private static string? TryReadFlag(string? raw, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = raw
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase));

        return match is null || match.Length != 2 ? null : match[1];
    }

    private static string BuildPortRecommendation(
        HardwarePortDefinition definition,
        string connectionState,
        bool detected,
        bool? rxActive,
        bool? txActive,
        bool simulationFallback)
    {
        if (simulationFallback)
        {
            return "Port pracuje jeszcze w trybie symulacji przejsciowej. Po dostarczeniu hardware potwierdz fizyczna obecnosc i wylacz fallback.";
        }

        if (!detected)
        {
            return definition.InterfaceType switch
            {
                "rs232" or "rs485" => "Port nie zostal potwierdzony przez system. Sprawdz okablowanie, nazwe /dev i parametry szeregowe.",
                "can" or "ethernet" => "Interfejs nie jest jeszcze widoczny. Zweryfikuj link, adresacje lub nazwe interfejsu w Linuxie.",
                "dry-contact" => "Wejscie stanowe nie jest widoczne. Sprawdz GPIO / mapowanie wejscia na plycie.",
                _ => "Zweryfikuj fizyczna obecnosc interfejsu i mapowanie collectora."
            };
        }

        if (definition.IsSerial && rxActive != true)
        {
            return "Port jest widoczny, ale brak odbioru danych. Ustal baudrate, parity, stop bits oraz kierunek pracy urzadzenia.";
        }

        if (definition.IsBus && rxActive != true && txActive != true)
        {
            return "Interfejs jest widoczny, ale brak ruchu RX/TX. Sprawdz partnera komunikacji, link i licznik ramek.";
        }

        if (connectionState == "connecting")
        {
            return "Port zostal wykryty i przechodzi przez stabilizacje. Zaczekaj na link albo pierwszy ruch RX/TX.";
        }

        return "Port jest gotowy do integracji. Kolejny krok to parser protokolu i mapowanie sygnalu do logiki biznesowej.";
    }

    private static string NormalizeRuntimeState(string? state) =>
        string.IsNullOrWhiteSpace(state)
            ? "disconnected"
            : state.Trim().ToLowerInvariant();

    private static string? FormatRuntimeTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
}
