using PathoNet.Contracts;

internal sealed partial class HardwareIntegrationStateService
{
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
