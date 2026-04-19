using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PathoNet.Contracts;

namespace PathoNet.Hub;

internal sealed class HubWorker(HubSettings settings, ILogger<HubWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[HUB] {HubName}", settings.HubName);
        logger.LogInformation("[HUB] ingest: {IngestAddress}", settings.IngestAddress);
        logger.LogInformation("[HUB] publish: {PublishAddress}", settings.PublishAddress);
        logger.LogInformation("[HUB] heartbeat: {HeartbeatAddress}", settings.HeartbeatAddress);

        var server = new HubServer(settings, logger);

        try
        {
            await server.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("[HUB] Shutdown complete.");
        }
    }
}

internal sealed record HubSettings(
    string HubName,
    string IngestAddress,
    string PublishAddress,
    string HeartbeatAddress,
    int StatsIntervalSeconds);

internal sealed record TcpEndpoint(string Host, int Port);

internal sealed class HubServer(HubSettings settings, ILogger logger)
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, SubscriberConnection> _subscribers = new();
    private long _notificationsForwarded;
    private long _heartbeatsForwarded;
    private long _invalidMessages;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var ingestEndpoint = ParseTcpEndpoint(settings.IngestAddress);
        var publishEndpoint = ParseTcpEndpoint(settings.PublishAddress);
        var heartbeatEndpoint = ParseTcpEndpoint(settings.HeartbeatAddress);

        var ingestListener = CreateListener(ingestEndpoint);
        var publishListener = CreateListener(publishEndpoint);
        var heartbeatListener = CreateListener(heartbeatEndpoint);

        ingestListener.Start();
        publishListener.Start();
        heartbeatListener.Start();

        logger.LogInformation("[HUB] listeners started");

        try
        {
            await Task.WhenAll(
                RunIngestListenerAsync(ingestListener, cancellationToken),
                RunPublishListenerAsync(publishListener, cancellationToken),
                RunHeartbeatListenerAsync(heartbeatListener, cancellationToken),
                RunStatsLoopAsync(cancellationToken));
        }
        finally
        {
            ingestListener.Stop();
            publishListener.Stop();
            heartbeatListener.Stop();

            foreach (var subscriberId in _subscribers.Keys)
            {
                RemoveSubscriber(subscriberId);
            }
        }
    }

    private async Task RunIngestListenerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleIngestClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleIngestClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        logger.LogInformation("[HUB][INGEST] client connected: {Remote}", remote);

        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var envelope = JsonSerializer.Deserialize<HubEnvelope>(line, _jsonOptions);
                    if (envelope is null)
                    {
                        Interlocked.Increment(ref _invalidMessages);
                        continue;
                    }

                    if (envelope.Kind == HubEnvelopeKinds.Notify)
                    {
                        Interlocked.Increment(ref _notificationsForwarded);
                    }
                    else if (envelope.Kind == HubEnvelopeKinds.Heartbeat)
                    {
                        Interlocked.Increment(ref _heartbeatsForwarded);
                    }

                    await BroadcastAsync(line);
                }
                catch (JsonException)
                {
                    Interlocked.Increment(ref _invalidMessages);
                }
            }
        }

        logger.LogInformation("[HUB][INGEST] client disconnected: {Remote}", remote);
    }

    private async Task RunPublishListenerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            var id = Guid.NewGuid();
            var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _subscribers[id] = new SubscriberConnection(id, client, writer);
            logger.LogInformation("[HUB][PUB] subscriber connected: {SubscriberId}", id);
        }
    }

    private async Task RunHeartbeatListenerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleHeartbeatClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleHeartbeatClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var pong = new HubPong(
                    Type: "pong",
                    Status: "ok",
                    Time: DateTimeOffset.UtcNow,
                    NotificationsForwarded: Interlocked.Read(ref _notificationsForwarded),
                    HeartbeatsForwarded: Interlocked.Read(ref _heartbeatsForwarded));

                var payload = JsonSerializer.Serialize(pong, _jsonOptions);
                await writer.WriteLineAsync(payload);
            }
        }
    }

    private async Task BroadcastAsync(string line)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            try
            {
                await subscriber.Writer.WriteLineAsync(line);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                logger.LogWarning("[HUB][PUB] remove subscriber {SubscriberId}: {Message}", subscriber.Id, exception.Message);
                RemoveSubscriber(subscriber.Id);
            }
        }
    }

    private async Task RunStatsLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(settings.StatsIntervalSeconds, 2));

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            logger.LogInformation(
                "[HUB][STATS] subscribers={Subscribers} notify={Notifications} heartbeat={Heartbeats} invalid={Invalid}",
                _subscribers.Count,
                Interlocked.Read(ref _notificationsForwarded),
                Interlocked.Read(ref _heartbeatsForwarded),
                Interlocked.Read(ref _invalidMessages));
        }
    }

    private void RemoveSubscriber(Guid id)
    {
        if (_subscribers.TryRemove(id, out var removed))
        {
            removed.Writer.Dispose();
            removed.Client.Dispose();
        }
    }

    private static TcpListener CreateListener(TcpEndpoint endpoint) =>
        new(IPAddress.Parse(endpoint.Host), endpoint.Port);

    private static TcpEndpoint ParseTcpEndpoint(string address)
    {
        var uri = new Uri(address, UriKind.Absolute);
        return new TcpEndpoint(uri.Host, uri.Port);
    }
}

internal sealed record SubscriberConnection(Guid Id, TcpClient Client, StreamWriter Writer);
