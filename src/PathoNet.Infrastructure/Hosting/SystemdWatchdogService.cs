using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;

namespace PathoNet.Infrastructure.Hosting;

public static class PathoNetSystemdServiceCollectionExtensions
{
    public static IServiceCollection AddPathoNetSystemdSupport(this IServiceCollection services, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddSystemd();
        services.TryAddSingleton<ISystemdNotifier, SystemdNotifier>();
        services.AddSingleton(new SystemdWatchdogOptions(serviceName));
        services.AddSingleton<ServiceRuntimeStateStore>();
        services.AddHostedService<SystemdWatchdogService>();

        return services;
    }
}

internal sealed record SystemdWatchdogOptions(string ServiceName);

internal sealed class SystemdWatchdogService(
    ISystemdNotifier notifier,
    SystemdWatchdogOptions options,
    ServiceRuntimeStateStore stateStore,
    ILogger<SystemdWatchdogService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var systemdDetected = SystemdHelpers.IsSystemdService();
        var notifierEnabled = notifier.IsEnabled;
        var watchdogActive = TryResolveWatchdogInterval(out var watchdogInterval, out var reason);

        await stateStore.MarkStartedAsync(
            systemdDetected,
            notifierEnabled,
            watchdogActive,
            watchdogActive ? watchdogInterval : null,
            stoppingToken);

        try
        {
            if (!notifierEnabled)
            {
                logger.LogDebug("[{Service}] systemd notifier is disabled.", options.ServiceName);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }

            if (!watchdogActive)
            {
                logger.LogDebug("[{Service}] systemd watchdog is inactive: {Reason}", options.ServiceName, reason);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }

            var heartbeatInterval = TimeSpan.FromTicks(Math.Max(watchdogInterval.Ticks / 2, TimeSpan.FromSeconds(1).Ticks));
            logger.LogInformation(
                "[{Service}] systemd watchdog active. Ping every {HeartbeatSeconds:0.##}s (WatchdogSec={WatchdogSeconds:0.##}s).",
                options.ServiceName,
                heartbeatInterval.TotalSeconds,
                watchdogInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(heartbeatInterval, stoppingToken);

                notifier.Notify(new ServiceState(
                    $"WATCHDOG=1{Environment.NewLine}STATUS={options.ServiceName} watchdog heartbeat {DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}"));
                await stateStore.MarkHeartbeatAsync(stoppingToken);
                logger.LogDebug("[{Service}] systemd watchdog heartbeat sent.", options.ServiceName);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await stateStore.MarkStoppingAsync(CancellationToken.None);
        }
    }

    private static bool TryResolveWatchdogInterval(out TimeSpan watchdogInterval, out string reason)
    {
        watchdogInterval = TimeSpan.Zero;
        reason = string.Empty;

        var watchdogUsec = Environment.GetEnvironmentVariable("WATCHDOG_USEC");
        if (string.IsNullOrWhiteSpace(watchdogUsec))
        {
            reason = "WATCHDOG_USEC not set";
            return false;
        }

        if (!long.TryParse(watchdogUsec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds) || microseconds <= 0)
        {
            reason = "WATCHDOG_USEC invalid";
            return false;
        }

        var watchdogPid = Environment.GetEnvironmentVariable("WATCHDOG_PID");
        if (!string.IsNullOrWhiteSpace(watchdogPid)
            && int.TryParse(watchdogPid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedPid)
            && expectedPid != Process.GetCurrentProcess().Id)
        {
            reason = $"WATCHDOG_PID targets {expectedPid}";
            return false;
        }

        watchdogInterval = TimeSpan.FromTicks(checked(microseconds * 10));
        return true;
    }
}
