using System.Text.Json;
using PathoNet.Infrastructure.Hosting;

namespace PathoNet.Collector;

internal sealed class CollectorRuntimeStateStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _deviceId;
    private readonly string _filePath;
    private readonly TimeSpan _flushInterval;
    private readonly Dictionary<string, CollectorPortRuntimeStateRecord> _ports = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastFlushAtUtc = DateTimeOffset.MinValue;

    public CollectorRuntimeStateStore(CollectorSettings settings, string contentRoot)
    {
        _deviceId = settings.DeviceId;
        _filePath = PathoNetRuntimePaths.ResolveCollectorRuntimeStateFilePath(contentRoot);
        _flushInterval = TimeSpan.FromMilliseconds(Math.Max(settings.RuntimeStateFlushMs, 50));
    }

    public async Task UpsertPortStateAsync(
        CollectorPortRuntimeStateRecord state,
        CancellationToken cancellationToken,
        bool forceFlush = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _ports[state.PortId] = state.Clone();

            var now = DateTimeOffset.UtcNow;
            if (!forceFlush && now - _lastFlushAtUtc < _flushInterval)
            {
                return;
            }

            await WriteSnapshotAsync(now, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await WriteSnapshotAsync(DateTimeOffset.UtcNow, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteSnapshotAsync(DateTimeOffset generatedAtUtc, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var snapshot = new CollectorRuntimeStateSnapshot
        {
            GeneratedAtUtc = generatedAtUtc,
            DeviceId = _deviceId,
            Ports = _ports.Values
                .OrderBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
                .Select(port => port.Clone())
                .ToArray()
        };

        var tempFilePath = _filePath + ".tmp";
        await File.WriteAllTextAsync(
            tempFilePath,
            JsonSerializer.Serialize(snapshot, _jsonOptions),
            cancellationToken);

        File.Move(tempFilePath, _filePath, overwrite: true);
        _lastFlushAtUtc = generatedAtUtc;
    }
}
