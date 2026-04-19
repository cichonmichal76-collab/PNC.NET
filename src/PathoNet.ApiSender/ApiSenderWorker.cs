using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PathoNet.Contracts;

namespace PathoNet.ApiSender;

internal sealed class ApiSenderWorker(ApiSenderSettings settings, ILogger<ApiSenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[SENDER] {SenderName}", settings.SenderName);
        logger.LogInformation("[SENDER] subscribe: {SubscribeAddress}", settings.ZmqSubAddr);
        logger.LogInformation("[SENDER] notify: {NotifyUrl}", settings.SendApiUrl);
        logger.LogInformation("[SENDER] heartbeat: {HeartbeatUrl}", settings.HeartbeatURL);

        var queue = Channel.CreateUnbounded<HubEnvelope>();

        try
        {
            await Task.WhenAll(
                RunSubscriberLoopAsync(settings, queue.Writer, stoppingToken),
                RunDeliveryLoopAsync(settings, queue.Reader, stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("[SENDER] Shutdown complete.");
        }
    }

    private async Task RunSubscriberLoopAsync(
        ApiSenderSettings currentSettings,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        var endpoint = ParseTcpEndpoint(currentSettings.ZmqSubAddr);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var reconnectDelay = TimeSpan.FromMilliseconds(Math.Max(currentSettings.ReconnectDelayMs, 250));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
                logger.LogInformation("[SENDER][SUB] connected to hub");

                using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
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

                    HubEnvelope? envelope;
                    try
                    {
                        envelope = JsonSerializer.Deserialize<HubEnvelope>(line, jsonOptions);
                    }
                    catch (JsonException exception)
                    {
                        logger.LogWarning("[SENDER][SUB] invalid payload: {Message}", exception.Message);
                        continue;
                    }

                    if (envelope is null || !string.Equals(envelope.Topic, currentSettings.ZmqTopic, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    await writer.WriteAsync(envelope, cancellationToken);
                    logger.LogInformation("[SENDER][QUEUE] accepted {Kind}", envelope.Kind);
                }
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                logger.LogWarning("[SENDER][SUB] reconnect: {Message}", exception.Message);
            }

            await Task.Delay(reconnectDelay, cancellationToken);
        }
    }

    private async Task RunDeliveryLoopAsync(
        ApiSenderSettings currentSettings,
        ChannelReader<HubEnvelope> reader,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(currentSettings.ApiTimeoutSec, 2))
        };

        await foreach (var envelope in reader.ReadAllAsync(cancellationToken))
        {
            await DeliverWithRetryAsync(currentSettings, httpClient, envelope, cancellationToken);
        }
    }

    private async Task DeliverWithRetryAsync(
        ApiSenderSettings currentSettings,
        HttpClient httpClient,
        HubEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var delay = TimeSpan.FromMilliseconds(Math.Max(currentSettings.ReconnectDelayMs, 250));

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            var result = await TryDeliverAsync(currentSettings, httpClient, envelope, cancellationToken);
            if (result is DeliveryResult.Success or DeliveryResult.PermanentFailure)
            {
                return;
            }

            if (attempt >= Math.Max(currentSettings.ApiRetryCount, 1))
            {
                logger.LogWarning("[SENDER][RETRY] holding {Kind} for another round", envelope.Kind);
                attempt = 0;
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task<DeliveryResult> TryDeliverAsync(
        ApiSenderSettings currentSettings,
        HttpClient httpClient,
        HubEnvelope envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response;

            if (envelope.Kind == HubEnvelopeKinds.Notify && envelope.Notification is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, currentSettings.SendApiUrl)
                {
                    Content = JsonContent.Create(envelope.Notification)
                };

                if (!string.IsNullOrWhiteSpace(envelope.DeviceKey))
                {
                    request.Headers.Add("x-device-key", envelope.DeviceKey);
                }

                response = await httpClient.SendAsync(request, cancellationToken);
                logger.LogInformation("[SENDER][NOTIFY] => {StatusCode} {ReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);
            }
            else if (envelope.Kind == HubEnvelopeKinds.Heartbeat && envelope.Heartbeat is not null)
            {
                response = await httpClient.PostAsJsonAsync(currentSettings.HeartbeatURL, envelope.Heartbeat, cancellationToken);
                logger.LogInformation("[SENDER][HEARTBEAT] => {StatusCode} {ReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);
            }
            else
            {
                logger.LogWarning("[SENDER] dropped unsupported envelope kind: {Kind}", envelope.Kind);
                return DeliveryResult.PermanentFailure;
            }

            if (response.IsSuccessStatusCode)
            {
                return DeliveryResult.Success;
            }

            if ((int)response.StatusCode is 400 or 401 or 403 or 404 or 422)
            {
                return DeliveryResult.PermanentFailure;
            }

            return DeliveryResult.TemporaryFailure;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("[SENDER][API] temporary failure: {Message}", exception.Message);
            return DeliveryResult.TemporaryFailure;
        }
    }

    private static TcpEndpoint ParseTcpEndpoint(string address)
    {
        var uri = new Uri(address, UriKind.Absolute);
        return new TcpEndpoint(uri.Host, uri.Port);
    }
}

internal enum DeliveryResult
{
    Success,
    TemporaryFailure,
    PermanentFailure
}

internal sealed record ApiSenderSettings(
    string SenderName,
    string ZmqSubAddr,
    string ZmqTopic,
    string SendApiUrl,
    string HeartbeatURL,
    int ApiTimeoutSec,
    int ApiRetryCount,
    int ReconnectDelayMs);

internal sealed record TcpEndpoint(string Host, int Port);
