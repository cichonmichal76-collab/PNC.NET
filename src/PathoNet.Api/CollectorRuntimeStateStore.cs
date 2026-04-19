using System.Text.Json;
using PathoNet.Infrastructure.Hosting;

internal sealed class CollectorRuntimeStateStore
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _runtimeFilePath;

    public CollectorRuntimeStateStore(string contentRoot)
    {
        _runtimeFilePath = PathoNetRuntimePaths.ResolveCollectorRuntimeStateFilePath(contentRoot);
    }

    public CollectorRuntimeStateSnapshotDocument? GetState()
    {
        if (!File.Exists(_runtimeFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_runtimeFilePath);
            return JsonSerializer.Deserialize<CollectorRuntimeStateSnapshotDocument>(json, _jsonOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class CollectorRuntimeStateSnapshotDocument
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public CollectorPortRuntimeStateDocument[] Ports { get; set; } = [];
}

internal sealed class CollectorPortRuntimeStateDocument
{
    public string PortId { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public string State { get; set; } = "disconnected";
    public bool CablePresent { get; set; }
    public bool? LinkUp { get; set; }
    public bool? RxActive { get; set; }
    public bool? TxActive { get; set; }
    public bool SimulationFallback { get; set; }
    public DateTimeOffset StateSinceUtc { get; set; }
    public DateTimeOffset LastTransitionAtUtc { get; set; }
    public DateTimeOffset? LastDetectedAtUtc { get; set; }
    public DateTimeOffset? LastOpenedAtUtc { get; set; }
    public DateTimeOffset? LastRxAtUtc { get; set; }
    public DateTimeOffset? LastTxAtUtc { get; set; }
    public long RxCounter { get; set; }
    public long TxCounter { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? LastRaw { get; set; }
    public string? LastText { get; set; }
}
