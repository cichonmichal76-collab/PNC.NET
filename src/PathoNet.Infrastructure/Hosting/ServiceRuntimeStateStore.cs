using System.Text.Json;

namespace PathoNet.Infrastructure.Hosting;

internal sealed class ServiceRuntimeStateStore
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;
    private readonly string _serviceName;

    public ServiceRuntimeStateStore(SystemdWatchdogOptions options)
    {
        _serviceName = options.ServiceName;
        _filePath = PathoNetRuntimePaths.ResolveServiceStateFilePath(options.ServiceName);
    }

    public Task MarkStartedAsync(
        bool systemdDetected,
        bool notifierEnabled,
        bool watchdogActive,
        TimeSpan? watchdogInterval,
        CancellationToken cancellationToken) =>
        UpdateAsync(existing =>
        {
            var restartCount = (existing?.RestartCount ?? 0) + 1;
            var now = DateTimeOffset.UtcNow;

            return new ServiceRuntimeSnapshot(
                ServiceName: _serviceName,
                Status: "running",
                RestartCount: restartCount,
                ProcessId: Environment.ProcessId,
                StartedAtUtc: now,
                UpdatedAtUtc: now,
                LastWatchdogHeartbeatUtc: watchdogActive ? now : null,
                LastStoppedAtUtc: existing?.LastStoppedAtUtc,
                SystemdDetected: systemdDetected,
                SystemdNotifierEnabled: notifierEnabled,
                WatchdogActive: watchdogActive,
                WatchdogIntervalSeconds: watchdogInterval?.TotalSeconds,
                HostEnvironment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? "Production",
                MachineName: Environment.MachineName,
                WorkingDirectory: Directory.GetCurrentDirectory());
        }, cancellationToken);

    public Task MarkHeartbeatAsync(CancellationToken cancellationToken) =>
        UpdateAsync(existing =>
        {
            var now = DateTimeOffset.UtcNow;
            return (existing ?? CreateDefault("running", now)) with
            {
                UpdatedAtUtc = now,
                LastWatchdogHeartbeatUtc = now,
                Status = "running"
            };
        }, cancellationToken);

    public Task MarkStoppingAsync(CancellationToken cancellationToken) =>
        UpdateAsync(existing =>
        {
            var now = DateTimeOffset.UtcNow;
            return (existing ?? CreateDefault("stopping", now)) with
            {
                Status = "stopping",
                UpdatedAtUtc = now,
                LastStoppedAtUtc = now
            };
        }, cancellationToken);

    private async Task UpdateAsync(
        Func<ServiceRuntimeSnapshot?, ServiceRuntimeSnapshot> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(cancellationToken);
            var next = update(current);
            var json = JsonSerializer.Serialize(next, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ServiceRuntimeSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            return JsonSerializer.Deserialize<ServiceRuntimeSnapshot>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ServiceRuntimeSnapshot CreateDefault(string status, DateTimeOffset now) =>
        new(
            ServiceName: _serviceName,
            Status: status,
            RestartCount: 0,
            ProcessId: Environment.ProcessId,
            StartedAtUtc: now,
            UpdatedAtUtc: now,
            LastWatchdogHeartbeatUtc: null,
            LastStoppedAtUtc: null,
            SystemdDetected: false,
            SystemdNotifierEnabled: false,
            WatchdogActive: false,
            WatchdogIntervalSeconds: null,
            HostEnvironment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Production",
            MachineName: Environment.MachineName,
            WorkingDirectory: Directory.GetCurrentDirectory());
}

internal sealed record ServiceRuntimeSnapshot(
    string ServiceName,
    string Status,
    int RestartCount,
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastWatchdogHeartbeatUtc,
    DateTimeOffset? LastStoppedAtUtc,
    bool SystemdDetected,
    bool SystemdNotifierEnabled,
    bool WatchdogActive,
    double? WatchdogIntervalSeconds,
    string HostEnvironment,
    string MachineName,
    string WorkingDirectory);
