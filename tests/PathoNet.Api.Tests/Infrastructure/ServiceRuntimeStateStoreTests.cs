using System.Text.Json;
using PathoNet.Api.Tests.TestSupport;
using PathoNet.Infrastructure.Hosting;

namespace PathoNet.Api.Tests.Infrastructure;

public sealed class ServiceRuntimeStateStoreTests
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Lifecycle_WritesAndUpdatesRuntimeSnapshot()
    {
        using var root = new PathoNetTestRoot();
        using var scope = root.UseAsPathoNetRoot();

        var store = new ServiceRuntimeStateStore(new SystemdWatchdogOptions("PathoNet.Api"));

        await store.MarkStartedAsync(
            systemdDetected: true,
            notifierEnabled: true,
            watchdogActive: true,
            watchdogInterval: TimeSpan.FromSeconds(30),
            cancellationToken: CancellationToken.None);

        var started = ReadSnapshot(root.ServiceStateFilePath("PathoNet.Api"));
        Assert.Equal("PathoNet.Api", started!.ServiceName);
        Assert.Equal("running", started.Status);
        Assert.Equal(1, started.RestartCount);
        Assert.True(started.SystemdDetected);
        Assert.True(started.SystemdNotifierEnabled);
        Assert.True(started.WatchdogActive);
        Assert.Equal(30, started.WatchdogIntervalSeconds);
        Assert.NotNull(started.LastWatchdogHeartbeatUtc);

        await Task.Delay(20);
        await store.MarkHeartbeatAsync(CancellationToken.None);
        var heartbeat = ReadSnapshot(root.ServiceStateFilePath("PathoNet.Api"));
        Assert.Equal("running", heartbeat!.Status);
        Assert.True(heartbeat.UpdatedAtUtc >= started.UpdatedAtUtc);
        Assert.True(heartbeat.LastWatchdogHeartbeatUtc >= started.LastWatchdogHeartbeatUtc);

        await store.MarkStoppingAsync(CancellationToken.None);
        var stopped = ReadSnapshot(root.ServiceStateFilePath("PathoNet.Api"));
        Assert.Equal("stopping", stopped!.Status);
        Assert.NotNull(stopped.LastStoppedAtUtc);
        Assert.Equal(1, stopped.RestartCount);
    }

    private ServiceRuntimeSnapshot? ReadSnapshot(string filePath) =>
        JsonSerializer.Deserialize<ServiceRuntimeSnapshot>(File.ReadAllText(filePath), _jsonOptions);
}
