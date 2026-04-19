using System.Text.Json;

internal sealed class CollectorConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _configFilePath;

    public CollectorConfigStore(string contentRoot)
    {
        _configFilePath = ResolveConfigFilePath(contentRoot);
        EnsureConfigFile();
    }

    public CollectorHardwareConfigStateRecord GetState()
    {
        var document = LoadDocument();
        var ports = document.Ports
            .OrderBy(static port => port.Order)
            .ThenBy(static port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .Select(static port => new CollectorPortConfigRecord(
                PortId: port.PortId,
                Alias: port.Alias,
                InterfaceType: port.InterfaceType,
                DevicePath: port.DevicePath,
                NetworkInterfaceName: string.IsNullOrWhiteSpace(port.NetworkInterfaceName) ? null : port.NetworkInterfaceName,
                BaudRate: port.BaudRate,
                DataBits: port.DataBits,
                Parity: port.Parity,
                StopBits: port.StopBits,
                ParserKind: port.ParserKind,
                FrameMode: port.FrameMode,
                Enabled: port.Enabled,
                AllowSimulationFallback: port.AllowSimulationFallback,
                Description: port.Description))
            .ToArray();

        return new CollectorHardwareConfigStateRecord(
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ConfigFilePath: _configFilePath,
            RestartHint: "Po zapisaniu parametrow zrestartuj collectora w panelu Zdrowie uslug, aby zastosowac zmiany live.",
            Ports: ports);
    }

    public async Task<CollectorHardwareConfigStateRecord> SavePortAsync(
        CollectorPortConfigRecord input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var document = LoadDocument();
            var port = document.Ports.FirstOrDefault(candidate =>
                string.Equals(candidate.PortId, input.PortId, StringComparison.OrdinalIgnoreCase));

            if (port is null)
            {
                throw new InvalidOperationException($"Nie znaleziono portu {input.PortId} w konfiguracji collectora.");
            }

            port.Alias = NormalizeRequired(input.Alias, port.PortId);
            port.DevicePath = NormalizeRequired(input.DevicePath, port.DevicePath);
            port.NetworkInterfaceName = NormalizeOptional(input.NetworkInterfaceName);
            port.BaudRate = Math.Clamp(input.BaudRate, 1200, 921600);
            port.DataBits = Math.Clamp(input.DataBits, 5, 8);
            port.Parity = NormalizeEnum(input.Parity, "none", ["none", "odd", "even", "mark", "space"]);
            port.StopBits = NormalizeEnum(input.StopBits, "one", ["none", "one", "onepointfive", "two"]);
            port.ParserKind = NormalizeEnum(input.ParserKind, port.ParserKind, ["generic-text", "generic-bus", "passive"]);
            port.FrameMode = NormalizeEnum(input.FrameMode, port.FrameMode, ["line", "inactivity-flush"]);
            port.Enabled = input.Enabled;
            port.AllowSimulationFallback = input.AllowSimulationFallback;
            port.Description = NormalizeOptional(input.Description) ?? string.Empty;

            Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);
            await using var stream = File.Create(_configFilePath);
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }

        return GetState();
    }

    private void EnsureConfigFile()
    {
        if (File.Exists(_configFilePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);
        var document = CollectorConfigDocument.CreateDefault();
        File.WriteAllText(_configFilePath, JsonSerializer.Serialize(document, SerializerOptions));
    }

    private CollectorConfigDocument LoadDocument()
    {
        if (!File.Exists(_configFilePath))
        {
            return CollectorConfigDocument.CreateDefault();
        }

        var json = File.ReadAllText(_configFilePath);
        return JsonSerializer.Deserialize<CollectorConfigDocument>(json, SerializerOptions)
               ?? CollectorConfigDocument.CreateDefault();
    }

    private static string ResolveConfigFilePath(string contentRoot)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(contentRoot, "..", "PathoNet.Collector", "appsettings.json")),
            Path.GetFullPath(Path.Combine(contentRoot, "..", "collector", "appsettings.json"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(contentRoot, "data", "collector-appsettings.json");
    }

    private static string NormalizeRequired(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeEnum(string? value, string fallback, string[] allowedValues)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && allowedValues.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : fallback;
    }

    private sealed class CollectorConfigDocument
    {
        public string DeviceId { get; set; } = "ddda1e4a-3660-4ba2-ba6f-24835b9d7351";
        public string ClientId { get; set; } = "68469ed7c5685ba3e52e97c7";
        public string ClientName { get; set; } = "Szpital";
        public string CurrentVersion { get; set; } = "1.0.47";
        public int PathoNetId { get; set; } = 1;
        public string DeviceApiKey { get; set; } = "Tanagra18SecretKey";
        public int HeartbeatIntervalSec { get; set; } = 30;
        public int HubHeartbeatIntervalSec { get; set; } = 5;
        public int DiscoveryIntervalSec { get; set; } = 5;
        public int SerialReadTimeoutMs { get; set; } = 250;
        public int SerialInactivityFlushMs { get; set; } = 400;
        public int ConnectionDebounceMs { get; set; } = 300;
        public int ActivityWindowMs { get; set; } = 250;
        public int RuntimeStateFlushMs { get; set; } = 200;
        public int SimulationIntervalMs { get; set; } = 1500;
        public int ReconnectDelayMs { get; set; } = 1000;
        public bool EnableSimulationFallback { get; set; } = true;
        public string ZmqPushAddr { get; set; } = "tcp://127.0.0.1:5555";
        public string ZmqHeartbeatAddr { get; set; } = "tcp://127.0.0.1:5557";
        public string ZmqTopic { get; set; } = "pathoNet";
        public List<CollectorConfigPortDocument> Ports { get; set; } = [];
        public CollectorFixedModulesDocument FixedModules { get; set; } = new();

        public static CollectorConfigDocument CreateDefault() =>
            new()
            {
                Ports =
                [
                    new() { Order = 1, PortId = "RS232/1", Alias = "Device_1", InterfaceType = "rs232", DevicePath = "/dev/ttyEM0", BaudRate = 115200, DataBits = 8, Parity = "none", StopBits = "one", ParserKind = "generic-text", FrameMode = "line", Enabled = true, AllowSimulationFallback = true, Description = "Pierwsze gniazdo RS232 dla urzadzenia wysylajacego dane." },
                    new() { Order = 2, PortId = "RS232/2", Alias = "Device_2", InterfaceType = "rs232", DevicePath = "/dev/ttyEM1", BaudRate = 115200, DataBits = 8, Parity = "none", StopBits = "one", ParserKind = "generic-text", FrameMode = "line", Enabled = true, AllowSimulationFallback = true, Description = "Drugie gniazdo RS232 dla urzadzenia wysylajacego dane." },
                    new() { Order = 3, PortId = "RS232/3", Alias = "Device_3", InterfaceType = "rs232", DevicePath = "/dev/ttyEM2", BaudRate = 115200, DataBits = 8, Parity = "none", StopBits = "one", ParserKind = "generic-text", FrameMode = "line", Enabled = true, AllowSimulationFallback = true, Description = "Trzecie gniazdo RS232 dla urzadzenia wysylajacego dane." },
                    new() { Order = 4, PortId = "RS232/4", Alias = "Device_4", InterfaceType = "rs232", DevicePath = "/dev/ttyEM3", BaudRate = 115200, DataBits = 8, Parity = "none", StopBits = "one", ParserKind = "generic-text", FrameMode = "line", Enabled = true, AllowSimulationFallback = true, Description = "Czwarte gniazdo RS232 dla urzadzenia wysylajacego dane." },
                    new() { Order = 5, PortId = "RS485", Alias = "RS485 Bus", InterfaceType = "rs485", DevicePath = "/dev/ttyEM4", BaudRate = 115200, DataBits = 8, Parity = "none", StopBits = "one", ParserKind = "generic-text", FrameMode = "line", Enabled = true, AllowSimulationFallback = true, Description = "Magistrala RS485 dla czujnikow i urzadzen wielowezlowych." },
                    new() { Order = 6, PortId = "CAN", Alias = "CAN Bus", InterfaceType = "can", DevicePath = "can0", NetworkInterfaceName = "can0", ParserKind = "generic-bus", FrameMode = "inactivity-flush", Enabled = true, AllowSimulationFallback = false, Description = "Interfejs CAN do odczytu aktywnosci i ramek magistrali." },
                    new() { Order = 7, PortId = "ETH1", Alias = "ETH1", InterfaceType = "ethernet", DevicePath = "eth0", NetworkInterfaceName = "eth0", ParserKind = "generic-bus", FrameMode = "inactivity-flush", Enabled = true, AllowSimulationFallback = false, Description = "Port Ethernet serwisowy lub uplink." },
                    new() { Order = 8, PortId = "ETH2", Alias = "ETH2", InterfaceType = "ethernet", DevicePath = "eth1", NetworkInterfaceName = "eth1", ParserKind = "generic-bus", FrameMode = "inactivity-flush", Enabled = true, AllowSimulationFallback = false, Description = "Port Ethernet dla urzadzen IP." },
                    new() { Order = 9, PortId = "ETH3", Alias = "ETH3", InterfaceType = "ethernet", DevicePath = "eth2", NetworkInterfaceName = "eth2", ParserKind = "generic-bus", FrameMode = "inactivity-flush", Enabled = true, AllowSimulationFallback = false, Description = "Port Ethernet dla urzadzen IP." },
                    new() { Order = 10, PortId = "ETH4", Alias = "ETH4", InterfaceType = "ethernet", DevicePath = "eth3", NetworkInterfaceName = "eth3", ParserKind = "generic-bus", FrameMode = "inactivity-flush", Enabled = true, AllowSimulationFallback = false, Description = "Port Ethernet dla urzadzen IP." },
                    new() { Order = 11, PortId = "WE DRY", Alias = "WE DRY", InterfaceType = "dry-contact", DevicePath = "/sys/class/gpio/dry0", ParserKind = "passive", FrameMode = "inactivity-flush", Enabled = true, AllowSimulationFallback = false, Description = "Wejscie stanowe / dry contact do prostych sygnalow binarnych." }
                ]
            };
    }

    private sealed class CollectorConfigPortDocument
    {
        public int Order { get; set; }
        public string PortId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string DevicePath { get; set; } = string.Empty;
        public string? NetworkInterfaceName { get; set; }
        public int BaudRate { get; set; } = 115200;
        public int DataBits { get; set; } = 8;
        public string Parity { get; set; } = "none";
        public string StopBits { get; set; } = "one";
        public string ParserKind { get; set; } = "generic-text";
        public string FrameMode { get; set; } = "line";
        public bool Enabled { get; set; } = true;
        public bool AllowSimulationFallback { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    private sealed class CollectorFixedModulesDocument
    {
        public CollectorFixedModuleDocument Mainboard { get; set; } = new() { Enabled = true, ModuleId = "MAINBOARD", DisplayName = "Plyta glowna" };
        public CollectorFixedModuleDocument LteSim { get; set; } = new() { Enabled = true, ModuleId = "LTE-SIM", DisplayName = "Modem LTE i karta SIM" };
        public CollectorFixedModuleDocument SdCard { get; set; } = new() { Enabled = true, ModuleId = "SD-CARD", DisplayName = "Karta SD" };
    }

    private sealed class CollectorFixedModuleDocument
    {
        public bool Enabled { get; set; }
        public string ModuleId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Path { get; set; }
    }
}
