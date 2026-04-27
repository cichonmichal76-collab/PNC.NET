using PathoNet.Contracts;

internal sealed partial class HardwareIntegrationStateService
{
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
}
