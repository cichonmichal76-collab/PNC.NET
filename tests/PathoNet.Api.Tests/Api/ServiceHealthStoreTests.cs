using System.Diagnostics;
using PathoNet.Api.Tests.TestSupport;

namespace PathoNet.Api.Tests.Api;

public sealed class ServiceHealthStoreTests
{
    [Fact]
    public void GetState_SummarizesMixedRuntimeModesAndRestartHistory()
    {
        using var root = new PathoNetTestRoot();
        root.WriteRestartScript();

        var now = DateTimeOffset.UtcNow;
        var currentPid = Process.GetCurrentProcess().Id;

        root.WritePidEntries(
            new
            {
                Name = "PathoNet.Api",
                Pid = currentPid,
                Executable = "dotnet",
                Arguments = "",
                Stdout = "api.out.log",
                Stderr = "api.err.log"
            },
            new
            {
                Name = "PathoNet.Hub",
                Pid = currentPid,
                Executable = "dotnet",
                Arguments = "",
                Stdout = "hub.out.log",
                Stderr = "hub.err.log"
            },
            new
            {
                Name = "PathoNet.ApiSender",
                Pid = currentPid,
                Executable = "dotnet",
                Arguments = "",
                Stdout = "sender.out.log",
                Stderr = "sender.err.log"
            });

        root.WriteJsonFile(
            root.ServiceStateFilePath("PathoNet.Api"),
            new
            {
                ServiceName = "PathoNet.Api",
                Status = "running",
                RestartCount = 2,
                ProcessId = currentPid,
                StartedAtUtc = now.AddMinutes(-5),
                UpdatedAtUtc = now,
                LastWatchdogHeartbeatUtc = now,
                LastStoppedAtUtc = (DateTimeOffset?)null,
                SystemdDetected = true,
                SystemdNotifierEnabled = true,
                WatchdogActive = true,
                WatchdogIntervalSeconds = 30.0,
                HostEnvironment = "Development",
                MachineName = "TEST-BOX",
                WorkingDirectory = root.RootPath
            });

        root.WriteJsonFile(
            root.ServiceStateFilePath("PathoNet.Hub"),
            new
            {
                ServiceName = "PathoNet.Hub",
                Status = "running",
                RestartCount = 1,
                ProcessId = currentPid,
                StartedAtUtc = now.AddMinutes(-10),
                UpdatedAtUtc = now,
                LastWatchdogHeartbeatUtc = (DateTimeOffset?)null,
                LastStoppedAtUtc = (DateTimeOffset?)null,
                SystemdDetected = true,
                SystemdNotifierEnabled = false,
                WatchdogActive = false,
                WatchdogIntervalSeconds = (double?)null,
                HostEnvironment = "Development",
                MachineName = "TEST-BOX",
                WorkingDirectory = root.RootPath
            });

        root.WriteJsonFile(
            Path.Combine(root.SharedStateDirectory, "restart-history.json"),
            new object[]
            {
                new
                {
                    Id = "evt-scheduled",
                    ServiceName = "PathoNet.Hub",
                    Status = "scheduled",
                    Mode = "local-script",
                    RequestedBy = "tester",
                    RequestedAtUtc = now.AddMinutes(-2),
                    CompletedAtUtc = (DateTimeOffset?)null,
                    PreviousProcessId = currentPid,
                    CurrentProcessId = (int?)null,
                    Summary = "Zaplanowano restart."
                },
                new
                {
                    Id = "evt-failed",
                    ServiceName = "PathoNet.Collector",
                    Status = "failed",
                    Mode = "local-script",
                    RequestedBy = "tester",
                    RequestedAtUtc = now.AddMinutes(-3),
                    CompletedAtUtc = now.AddMinutes(-2),
                    PreviousProcessId = (int?)null,
                    CurrentProcessId = (int?)null,
                    Summary = "Restart nie udal sie."
                }
            });

        var store = new ServiceHealthStore(root.RootPath);

        var state = store.GetState();

        Assert.Equal(4, state.Summary.TotalCount);
        Assert.Equal(1, state.Summary.OnlineCount);
        Assert.Equal(2, state.Summary.AttentionCount);
        Assert.Equal(1, state.Summary.CriticalCount);
        Assert.Equal(2, state.Summary.SystemdCount);
        Assert.Equal(1, state.Summary.WatchdogCount);
        Assert.Equal(3, state.Summary.TotalRestartCount);
        Assert.Equal(2, state.Summary.RecentRestartCount);
        Assert.Equal(1, state.Summary.FailedRestartCount);
        Assert.Equal(1, state.Summary.PendingRestartCount);

        var api = Assert.Single(state.Services.Where(service => service.Name == "PathoNet.Api"));
        Assert.Equal("online", api.Status);
        Assert.Equal("systemd + watchdog 30 s", api.RuntimeMode);
        Assert.False(api.SupportsRestart);

        var sender = Assert.Single(state.Services.Where(service => service.Name == "PathoNet.ApiSender"));
        Assert.Equal("attention", sender.Status);
        Assert.True(sender.SupportsRestart);
        Assert.Equal("panel lokalny", sender.RestartMode);

        var collector = Assert.Single(state.Services.Where(service => service.Name == "PathoNet.Collector"));
        Assert.Equal("critical", collector.Status);

        Assert.Equal("evt-scheduled", state.RestartHistory[0].Id);
        Assert.Equal("evt-failed", state.RestartHistory[1].Id);
    }

    [Fact]
    public async Task RequestRestartAsync_RejectsSystemdManagedService()
    {
        using var root = new PathoNetTestRoot();
        root.WriteRestartScript();

        var now = DateTimeOffset.UtcNow;
        var currentPid = Process.GetCurrentProcess().Id;

        root.WritePidEntries(
            new
            {
                Name = "PathoNet.Api",
                Pid = currentPid,
                Executable = "dotnet",
                Arguments = "",
                Stdout = "api.out.log",
                Stderr = "api.err.log"
            });

        root.WriteJsonFile(
            root.ServiceStateFilePath("PathoNet.Api"),
            new
            {
                ServiceName = "PathoNet.Api",
                Status = "running",
                RestartCount = 1,
                ProcessId = currentPid,
                StartedAtUtc = now.AddMinutes(-1),
                UpdatedAtUtc = now,
                LastWatchdogHeartbeatUtc = now,
                LastStoppedAtUtc = (DateTimeOffset?)null,
                SystemdDetected = true,
                SystemdNotifierEnabled = true,
                WatchdogActive = true,
                WatchdogIntervalSeconds = 30.0,
                HostEnvironment = "Development",
                MachineName = "TEST-BOX",
                WorkingDirectory = root.RootPath
            });

        var store = new ServiceHealthStore(root.RootPath);

        var result = await store.RequestRestartAsync("PathoNet.Api", "tester", CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("rejected", result.Status);
        Assert.Contains("systemd", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
