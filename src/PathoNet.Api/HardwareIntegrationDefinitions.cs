internal static class HardwareIntegrationDefinitions
{
    internal static readonly HardwarePortDefinition[] Ports =
    [
        new("RS232/1", "RS232/1", "rs232", "/dev/ttyEM0", "generic-text", "line", "Pierwsze gniazdo DB9 dla urzadzenia szeregowego nadajacego dane do PNC."),
        new("RS232/2", "RS232/2", "rs232", "/dev/ttyEM1", "generic-text", "line", "Drugie gniazdo DB9 dla urzadzenia szeregowego lub stanowiska pomiarowego."),
        new("RS232/3", "RS232/3", "rs232", "/dev/ttyEM2", "generic-text", "line", "Trzecie gniazdo DB9 dla urzadzenia medycznego lub bramki protokolowej."),
        new("RS232/4", "RS232/4", "rs232", "/dev/ttyEM3", "generic-text", "line", "Czwarte gniazdo DB9 dla urzadzenia szeregowgo lub toru serwisowego."),
        new("RS485", "RS485", "rs485", "/dev/ttyEM4", "generic-text", "line", "Magistrala RS485 dla czujnikow, sterownikow i urzadzen wielowezlowych."),
        new("CAN", "CAN", "can", "can0", "generic-bus", "inactivity-flush", "Magistrala CAN do odczytu aktywnosci ramek i ruchu komunikacyjnego."),
        new("ETH1", "ETH1", "ethernet", "eth0", "generic-bus", "inactivity-flush", "Port Ethernet serwisowy lub uplink do laptopa technika."),
        new("ETH2", "ETH2", "ethernet", "eth1", "generic-bus", "inactivity-flush", "Port Ethernet dla urzadzen IP lub podsieci lokalnej."),
        new("ETH3", "ETH3", "ethernet", "eth2", "generic-bus", "inactivity-flush", "Port Ethernet dla urzadzen IP lub wydzielonego segmentu sieci."),
        new("ETH4", "ETH4", "ethernet", "eth3", "generic-bus", "inactivity-flush", "Port Ethernet dla urzadzen IP, testera lub dodatkowego uplinku."),
        new("WE DRY", "WE DRY", "dry-contact", "/sys/class/gpio/dry0", "passive", "inactivity-flush", "Wejscie stanowe / dry contact dla prostych sygnalow binarnych i alarmowych.")
    ];

    internal static readonly HardwareFixedModuleDefinition[] FixedModules =
    [
        new("MAINBOARD", "Plyta glowna"),
        new("LTE-SIM", "Modem LTE i karta SIM"),
        new("SD-CARD", "Karta SD")
    ];
}

internal sealed record HardwarePortDefinition(
    string PortId,
    string Alias,
    string InterfaceType,
    string ExpectedPath,
    string ParserKind,
    string FrameMode,
    string Purpose)
{
    public bool IsSerial =>
        InterfaceType is "rs232" or "rs485";

    public bool IsBus =>
        InterfaceType is "can" or "ethernet";
}

internal sealed record HardwareFixedModuleDefinition(
    string ModuleId,
    string DisplayName);
