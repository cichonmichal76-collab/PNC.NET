using System.Text.Json.Serialization;

namespace PathoNet.Collector;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum CollectorPortConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Link = 2,
    Tx = 3,
    Rx = 4
}

internal sealed class CollectorPortRuntimeStateRecord
{
    public string PortId { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public CollectorPortConnectionState State { get; set; } = CollectorPortConnectionState.Disconnected;
    public bool CablePresent { get; set; }
    public bool? LinkUp { get; set; }
    public bool? RxActive { get; set; }
    public bool? TxActive { get; set; }
    public bool SimulationFallback { get; set; }
    public DateTimeOffset StateSinceUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastTransitionAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastDetectedAtUtc { get; set; }
    public DateTimeOffset? LastOpenedAtUtc { get; set; }
    public DateTimeOffset? LastRxAtUtc { get; set; }
    public DateTimeOffset? LastTxAtUtc { get; set; }
    public long RxCounter { get; set; }
    public long TxCounter { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? LastRaw { get; set; }
    public string? LastText { get; set; }

    public CollectorPortRuntimeStateRecord Clone() =>
        new()
        {
            PortId = PortId,
            Alias = Alias,
            InterfaceType = InterfaceType,
            DevicePath = DevicePath,
            State = State,
            CablePresent = CablePresent,
            LinkUp = LinkUp,
            RxActive = RxActive,
            TxActive = TxActive,
            SimulationFallback = SimulationFallback,
            StateSinceUtc = StateSinceUtc,
            LastTransitionAtUtc = LastTransitionAtUtc,
            LastDetectedAtUtc = LastDetectedAtUtc,
            LastOpenedAtUtc = LastOpenedAtUtc,
            LastRxAtUtc = LastRxAtUtc,
            LastTxAtUtc = LastTxAtUtc,
            RxCounter = RxCounter,
            TxCounter = TxCounter,
            Summary = Summary,
            LastRaw = LastRaw,
            LastText = LastText
        };

    public static CollectorPortRuntimeStateRecord Create(CollectorPortSettings port, DateTimeOffset timestamp) =>
        new()
        {
            PortId = port.PortId,
            Alias = port.EffectiveAlias,
            InterfaceType = port.NormalizedInterfaceType,
            DevicePath = string.IsNullOrWhiteSpace(port.DevicePath)
                ? port.EffectiveNetworkInterfaceName
                : port.DevicePath,
            State = CollectorPortConnectionState.Disconnected,
            CablePresent = false,
            LinkUp = port.NormalizedInterfaceType switch
            {
                CollectorInterfaceTypes.Rs232 or CollectorInterfaceTypes.Rs485 => null,
                _ => false
            },
            RxActive = false,
            TxActive = false,
            SimulationFallback = false,
            StateSinceUtc = timestamp,
            LastTransitionAtUtc = timestamp,
            Summary = $"Port {port.PortId} oczekuje na pierwsze wykrycie."
        };
}

internal sealed class CollectorRuntimeStateSnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public CollectorPortRuntimeStateRecord[] Ports { get; set; } = [];
}
