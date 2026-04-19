namespace PathoNet.Collector;

internal sealed class CollectorSettings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString();

    public string ClientId { get; set; } = "pathonet-client";

    public string ClientName { get; set; } = "PathoNet";

    public string CurrentVersion { get; set; } = "1.0.0";

    public int PathoNetId { get; set; } = 1;

    public string DeviceApiKey { get; set; } = "PathoNetSecret";

    public int HeartbeatIntervalSec { get; set; } = 30;

    public int HubHeartbeatIntervalSec { get; set; } = 5;

    public string ZmqPushAddr { get; set; } = "tcp://127.0.0.1:5555";

    public string ZmqHeartbeatAddr { get; set; } = "tcp://127.0.0.1:5557";

    public string ZmqTopic { get; set; } = "pathoNet";

    public int ReconnectDelayMs { get; set; } = 1000;

    public int DiscoveryIntervalSec { get; set; } = 5;

    public int SerialReadTimeoutMs { get; set; } = 250;

    public int SerialInactivityFlushMs { get; set; } = 400;

    public int ConnectionDebounceMs { get; set; } = 300;

    public int ActivityWindowMs { get; set; } = 250;

    public int RuntimeStateFlushMs { get; set; } = 200;

    public int SimulationIntervalMs { get; set; } = 1500;

    public bool EnableSimulationFallback { get; set; } = true;

    public int RS232BaudRate { get; set; } = 115200;

    public List<string> RS232Ports { get; set; } = [];

    public Dictionary<string, string> RS232PortAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<CollectorPortSettings> Ports { get; set; } = [];

    public CollectorFixedModuleSettings FixedModules { get; set; } = new();

    public IReadOnlyList<CollectorPortSettings> GetConfiguredPorts()
    {
        if (Ports.Count > 0)
        {
            return Ports
                .Where(static port => port.Enabled)
                .OrderBy(static port => port.Order)
                .ThenBy(static port => port.PortId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (RS232Ports.Count == 0)
        {
            return [];
        }

        return RS232Ports
            .Select((path, index) => new CollectorPortSettings
            {
                Order = index,
                PortId = $"RS232/{index + 1}",
                Alias = RS232PortAliases.TryGetValue(path, out var alias) ? alias : $"Device_{index + 1}",
                InterfaceType = CollectorInterfaceTypes.Rs232,
                DevicePath = path,
                BaudRate = RS232BaudRate,
                ParserKind = CollectorParserKinds.GenericText,
                FrameMode = CollectorFrameModes.Line,
                Enabled = true,
                AllowSimulationFallback = EnableSimulationFallback
            })
            .ToArray();
    }

    public IReadOnlyList<FixedModuleDescriptor> GetFixedModules() =>
    [
        FixedModules.Mainboard,
        FixedModules.LteSim,
        FixedModules.SdCard
    ];
}

internal sealed class CollectorPortSettings
{
    public int Order { get; set; }

    public string PortId { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public string InterfaceType { get; set; } = CollectorInterfaceTypes.Rs232;

    public string DevicePath { get; set; } = string.Empty;

    public string? NetworkInterfaceName { get; set; }

    public int BaudRate { get; set; } = 115200;

    public int DataBits { get; set; } = 8;

    public string Parity { get; set; } = "none";

    public string StopBits { get; set; } = "one";

    public string ParserKind { get; set; } = CollectorParserKinds.GenericText;

    public string FrameMode { get; set; } = CollectorFrameModes.Line;

    public bool Enabled { get; set; } = true;

    public bool AllowSimulationFallback { get; set; } = false;

    public string Description { get; set; } = string.Empty;

    public string NormalizedInterfaceType =>
        string.IsNullOrWhiteSpace(InterfaceType)
            ? CollectorInterfaceTypes.Rs232
            : InterfaceType.Trim().ToLowerInvariant();

    public string EffectiveAlias =>
        string.IsNullOrWhiteSpace(Alias)
            ? PortId
            : Alias.Trim();

    public string EffectiveNetworkInterfaceName =>
        string.IsNullOrWhiteSpace(NetworkInterfaceName)
            ? DevicePath.Trim()
            : NetworkInterfaceName.Trim();
}

internal sealed class CollectorFixedModuleSettings
{
    public FixedModuleDescriptor Mainboard { get; set; } = new()
    {
        Enabled = true,
        ModuleId = "MAINBOARD",
        DisplayName = "Plyta glowna"
    };

    public FixedModuleDescriptor LteSim { get; set; } = new()
    {
        Enabled = true,
        ModuleId = "LTE-SIM",
        DisplayName = "Modem LTE i karta SIM"
    };

    public FixedModuleDescriptor SdCard { get; set; } = new()
    {
        Enabled = true,
        ModuleId = "SD-CARD",
        DisplayName = "Karta SD"
    };
}

internal sealed class FixedModuleDescriptor
{
    public bool Enabled { get; set; }

    public string ModuleId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Path { get; set; }
}

internal static class CollectorInterfaceTypes
{
    public const string Rs232 = "rs232";
    public const string Rs485 = "rs485";
    public const string Can = "can";
    public const string Ethernet = "ethernet";
    public const string DryContact = "dry-contact";
}

internal static class CollectorParserKinds
{
    public const string GenericText = "generic-text";
    public const string GenericBus = "generic-bus";
    public const string Passive = "passive";
}

internal static class CollectorFrameModes
{
    public const string Line = "line";
    public const string InactivityFlush = "inactivity-flush";
}
