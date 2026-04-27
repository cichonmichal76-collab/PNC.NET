internal sealed partial class HardwareIntegrationStateService
{
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
}
