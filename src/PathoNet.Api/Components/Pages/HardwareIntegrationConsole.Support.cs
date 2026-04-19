namespace PathoNet.Api.Components.Pages;

using PathoNet.Api;

internal static class HardwareIntegrationWizardSupport
{
    private static readonly HardwarePortLayoutDefinition[] PortLayouts =
    [
        new("ETH1", 20.68, 36.12, 7.03, 12.16),
        new("ETH2", 31.18, 36.12, 7.03, 12.16),
        new("ETH3", 41.98, 36.12, 7.03, 12.16),
        new("ETH4", 52.88, 36.12, 7.03, 12.16),
        new("RS485", 67.15, 42.05, 7.55, 7.8),
        new("CAN", 81.95, 42.05, 7.55, 7.8),
        new("WE DRY", 25.15, 61.15, 10.9, 8.45),
        new("RS232/4", 42.0, 61.95, 7.75, 7.8),
        new("RS232/3", 54.6, 61.95, 7.75, 7.8),
        new("RS232/2", 67.05, 61.95, 7.75, 7.8),
        new("RS232/1", 81.85, 61.95, 7.75, 7.8)
    ];

    internal static readonly HardwareWizardStepDefinition[] WizardSteps =
    [
        new(HardwareWizardStep.SelectPort, "Port", "Wybierz fizyczne gniazdo lub magistrale."),
        new(HardwareWizardStep.ChooseProfile, "Profil", "Dobierz gotowy profil techniczny."),
        new(HardwareWizardStep.Configure, "Nastawy", "Ustaw parametry collectora i fallback."),
        new(HardwareWizardStep.Verify, "Weryfikacja", "Sprawdz runtime, RX/TX i diagnostyke."),
        new(HardwareWizardStep.Commit, "Zapis", "Zapisz konfiguracje i zakoncz commissioning.")
    ];

    internal static readonly HardwareCommissioningProgressStepDefinition[] CommissioningProgressSteps =
    [
        new(1, "Start wdrozenia", "Uruchom urzadzenie i przejdz kontrole systemu.", HardwareDeploymentStage.Startup),
        new(2, "Personalizacja urzadzenia", "Uzupelnij dane instalacyjne i lokalizacje.", HardwareDeploymentStage.Personalization),
        new(3, "Mapa gniazd PNC", "Kliknij gniazdo, ktore chcesz skonfigurowac.", HardwareDeploymentStage.PortCommissioning, HardwareWizardStep.SelectPort),
        new(4, "Profil komunikacji", "Dobierz gotowy profil komunikacji.", HardwareDeploymentStage.PortCommissioning, HardwareWizardStep.ChooseProfile),
        new(5, "Nastawy collectora", "Ustaw parametry collectora.", HardwareDeploymentStage.PortCommissioning, HardwareWizardStep.Configure),
        new(6, "Weryfikacja", "Zweryfikuj wykrycie i diagnostyke.", HardwareDeploymentStage.PortCommissioning, HardwareWizardStep.Verify),
        new(7, "Zapis", "Zapisz konfiguracje i zakoncz commissioning.", HardwareDeploymentStage.PortCommissioning, HardwareWizardStep.Commit)
    ];

    internal static IReadOnlyList<HardwarePortProfileDefinition> GetProfilesForPort(CollectorPortConfigRecord port) =>
        port.InterfaceType switch
        {
            "rs232" =>
            [
                new("Respirator ASCII", "Respirator", "rs232", 9600, 8, "none", "one", "generic-text", "line", true, "Profil dla urzadzenia nadajacego tekstowe ramki statusowe po RS232."),
                new("Pompa szeregowa", "Pompa", "rs232", 19200, 8, "even", "one", "generic-text", "line", true, "Profil dla pomp i sterownikow wymagajacych parity even."),
                new("Analizator laboratoryjny", "Analizator", "rs232", 115200, 8, "none", "one", "generic-text", "line", true, "Profil dla szybkich ramek tekstowych z analizatora lub bramki.")
            ],
            "rs485" =>
            [
                new("Modbus RTU RS485", "RS485 Modbus", "rs485", 19200, 8, "even", "one", "generic-text", "line", false, "Profil dla magistrali Modbus RTU po RS485."),
                new("Czujniki multi-drop", "RS485 Czujniki", "rs485", 9600, 8, "none", "one", "generic-text", "line", true, "Profil dla czujnikow i prostych sterownikow na wspolnej magistrali.")
            ],
            "ethernet" =>
            [
                new("Urzadzenie IP", port.Alias, "ethernet", port.BaudRate, port.DataBits, port.Parity, port.StopBits, "generic-bus", "inactivity-flush", false, "Profil dla urzadzenia IP z monitoringiem aktywnosci interfejsu.", NetworkInterfaceNameHint: string.IsNullOrWhiteSpace(port.NetworkInterfaceName) ? port.DevicePath : port.NetworkInterfaceName),
                new("Port serwisowy", "ETH Serwis", "ethernet", port.BaudRate, port.DataBits, port.Parity, port.StopBits, "generic-bus", "inactivity-flush", false, "Profil dla portu serwisowego technika / uplinku.", NetworkInterfaceNameHint: string.IsNullOrWhiteSpace(port.NetworkInterfaceName) ? port.DevicePath : port.NetworkInterfaceName)
            ],
            "can" =>
            [
                new("CAN telemetry", "CAN Bus", "can", port.BaudRate, port.DataBits, port.Parity, port.StopBits, "generic-bus", "inactivity-flush", false, "Profil dla magistrali CAN z monitoringiem aktywnosci ramek.", NetworkInterfaceNameHint: string.IsNullOrWhiteSpace(port.NetworkInterfaceName) ? port.DevicePath : port.NetworkInterfaceName)
            ],
            "dry-contact" =>
            [
                new("Dry contact alarm", "WE DRY Alarm", "dry-contact", port.BaudRate, port.DataBits, port.Parity, port.StopBits, "passive", "inactivity-flush", false, "Profil dla prostego wejscia alarmowego / binarnego.")
            ],
            _ => []
        };

    internal static string MapTone(string value) =>
        value.ToLowerInvariant() switch
        {
            "online" => "online",
            "completed" => "online",
            "attention" => "attention",
            "warn" => "attention",
            "critical" => "critical",
            "error" => "critical",
            _ => "info"
        };

    internal static string BoolChipClass(bool value) =>
        value ? "online" : "attention";

    internal static string NullableBoolChipClass(bool? value) =>
        value switch
        {
            true => "online",
            false => "attention",
            null => "info"
        };

    internal static string DisplayNullableBool(bool? value) =>
        value switch
        {
            true => "tak",
            false => "nie",
            null => "n/d"
        };

    internal static bool IsPortConnected(HardwarePortStatusRecord port) =>
        port.Detected || port.LinkUp == true || port.RxActive == true || port.TxActive == true;

    internal static int GetPortPriority(HardwarePortStatusRecord port)
    {
        var score = 0;

        if (port.Detected)
        {
            score += 100;
        }

        if (port.RxActive == true)
        {
            score += 80;
        }

        if (port.TxActive == true)
        {
            score += 60;
        }

        if (port.LinkUp == true)
        {
            score += 40;
        }

        if (!port.SimulationFallback)
        {
            score += 25;
        }

        if (port.InterfaceType is "rs232" or "rs485")
        {
            score += 10;
        }

        return score;
    }

    internal static IReadOnlyList<HardwarePortStatusRecord> GetLayoutPorts(
        IEnumerable<HardwarePortStatusRecord> connectedPorts,
        IEnumerable<HardwarePortStatusRecord> otherPorts)
    {
        var portsById = connectedPorts
            .Concat(otherPorts)
            .GroupBy(static port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        return PortLayouts
            .Where(layout => portsById.ContainsKey(layout.PortId))
            .Select(layout => portsById[layout.PortId])
            .ToArray();
    }

    internal static HardwarePortLayoutDefinition? GetPortLayout(string portId) =>
        PortLayouts.FirstOrDefault(layout => string.Equals(layout.PortId, portId, StringComparison.OrdinalIgnoreCase));

    internal static string GetVisualStateClass(HardwarePortStatusRecord port) =>
        port.ConnectionState.ToLowerInvariant() switch
        {
            "connecting" => "connecting",
            "tx" => "link tx",
            "rx" => "link rx",
            "link" => "link",
            _ when IsPortConnected(port) => "link",
            _ => "disconnected"
        };

    internal static string GetVisualStateLabel(HardwarePortStatusRecord port) =>
        port.ConnectionState.ToLowerInvariant() switch
        {
            "connecting" => "stabilizacja",
            "tx" => "nadawanie",
            "rx" => "odbior",
            "link" => "polaczenie",
            _ when IsPortConnected(port) => "polaczone",
            _ => "brak lacza"
        };
}

internal sealed record HardwareWizardStepDefinition(
    HardwareWizardStep Step,
    string Title,
    string Description);

internal sealed record HardwareCommissioningProgressStepDefinition(
    int Order,
    string Title,
    string Description,
    HardwareDeploymentStage Stage,
    HardwareWizardStep? WizardStep = null);

public sealed record HardwarePortProfileDefinition(
    string Name,
    string AliasHint,
    string InterfaceType,
    int BaudRate,
    int DataBits,
    string Parity,
    string StopBits,
    string ParserKind,
    string FrameMode,
    bool AllowSimulationFallback,
    string Description,
    string? DevicePathHint = null,
    string? NetworkInterfaceNameHint = null);

public sealed record HardwarePortLayoutDefinition(
    string PortId,
    double Left,
    double Top,
    double Width,
    double Height);

internal enum HardwareWizardStep
{
    SelectPort = 1,
    ChooseProfile = 2,
    Configure = 3,
    Verify = 4,
    Commit = 5
}

internal enum HardwareDeploymentStage
{
    Startup = 1,
    Personalization = 2,
    PortCommissioning = 3
}
