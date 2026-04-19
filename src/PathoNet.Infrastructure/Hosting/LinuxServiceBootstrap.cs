using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting.Systemd;

namespace PathoNet.Infrastructure.Hosting;

public static class LinuxServiceBootstrap
{
    public static IDisposable RegisterCancellation(CancellationTokenSource cancellationTokenSource, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(cancellationTokenSource);

        var registrations = new List<IDisposable>();

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestShutdown(cancellationTokenSource, serviceName, "Ctrl+C");
        };

        Console.CancelKeyPress += cancelHandler;

        if (OperatingSystem.IsLinux())
        {
            registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                RequestShutdown(cancellationTokenSource, serviceName, "SIGTERM");
            }));

            registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                context.Cancel = true;
                RequestShutdown(cancellationTokenSource, serviceName, "SIGINT");
            }));
        }

        return new CompositeRegistration(() =>
        {
            Console.CancelKeyPress -= cancelHandler;
            foreach (var registration in registrations)
            {
                registration.Dispose();
            }
        });
    }

    public static string ResolveHttpUrls(string fallbackUrl) =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("PATHONET_HTTP_URLS"),
            Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
            fallbackUrl);

    public static void LogRuntimeMode(string serviceName, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (SystemdHelpers.IsSystemdService())
        {
            log($"[{serviceName}] systemd runtime detected.");
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static void RequestShutdown(
        CancellationTokenSource cancellationTokenSource,
        string serviceName,
        string reason)
    {
        if (cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        Console.WriteLine($"[{serviceName}] shutdown requested by {reason}.");
        cancellationTokenSource.Cancel();
    }

    private sealed class CompositeRegistration(Action disposeAction) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            disposeAction();
        }
    }
}
