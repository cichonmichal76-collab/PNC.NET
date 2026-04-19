using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PathoNet.Contracts;

namespace PathoNet.Collector;

internal sealed class CollectorWorker(
    CollectorSettings settings,
    CollectorRuntimeStateStore runtimeStateStore,
    ILogger<CollectorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredPorts = settings.GetConfiguredPorts();
        var fixedModules = settings.GetFixedModules().Where(static module => module.Enabled).ToArray();

        logger.LogInformation("[COLLECTOR] DeviceId: {DeviceId}", settings.DeviceId);
        logger.LogInformation("[COLLECTOR] ClientName: {ClientName}", settings.ClientName);
        logger.LogInformation("[COLLECTOR] ZmqPushAddr: {PushAddress}", settings.ZmqPushAddr);
        logger.LogInformation("[COLLECTOR] ZmqHeartbeatAddr: {HeartbeatAddress}", settings.ZmqHeartbeatAddr);
        logger.LogInformation("[COLLECTOR] Configured ports: {PortCount}", configuredPorts.Count);

        foreach (var port in configuredPorts)
        {
            logger.LogInformation(
                "[COLLECTOR][PORT] {PortId} type={InterfaceType} path={Path} alias={Alias} simulationFallback={SimulationFallback}",
                port.PortId,
                port.NormalizedInterfaceType,
                port.DevicePath,
                port.EffectiveAlias,
                port.AllowSimulationFallback && settings.EnableSimulationFallback);
        }

        var envelopeQueue = Channel.CreateUnbounded<HubEnvelope>();
        await using var publisher = new HubPublisher(settings.ZmqPushAddr, settings.ReconnectDelayMs, logger);

        var portMonitorTasks = configuredPorts
            .Select(port => RunPortMonitorAsync(port, envelopeQueue.Writer, stoppingToken))
            .ToArray();

        var fixedModuleTask = RunFixedModuleLoopAsync(fixedModules, envelopeQueue.Writer, stoppingToken);
        var deviceHeartbeatTask = RunDeviceHeartbeatLoopAsync(configuredPorts.Count, envelopeQueue.Writer, stoppingToken);
        var dispatchTask = DispatchEventsAsync(publisher, envelopeQueue.Reader, stoppingToken);
        var hubHeartbeatTask = RunHubHeartbeatLoopAsync(stoppingToken);

        try
        {
            await Task.WhenAll(portMonitorTasks
                .Append(fixedModuleTask)
                .Append(deviceHeartbeatTask)
                .Append(dispatchTask)
                .Append(hubHeartbeatTask));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("[COLLECTOR] Shutdown complete.");
        }
    }

    private async Task RunPortMonitorAsync(
        CollectorPortSettings port,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        switch (port.NormalizedInterfaceType)
        {
            case CollectorInterfaceTypes.Rs232:
            case CollectorInterfaceTypes.Rs485:
                await RunSerialPortLoopAsync(port, writer, cancellationToken);
                return;
            case CollectorInterfaceTypes.Can:
            case CollectorInterfaceTypes.Ethernet:
                await RunNetworkInterfaceLoopAsync(port, writer, cancellationToken);
                return;
            case CollectorInterfaceTypes.DryContact:
                await RunPassiveInputLoopAsync(port, writer, cancellationToken);
                return;
            default:
                await EmitNotificationAsync(
                    writer,
                    HubEnvelopeKinds.Notify,
                    CollectorSignalProcessing.CreatePortStateNotification(
                        settings,
                        port,
                        "warn",
                        $"Nieznany typ interfejsu dla {port.PortId}: {port.InterfaceType}",
                        port.InterfaceType,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
                return;
        }
    }

    private async Task RunSerialPortLoopAsync(
        CollectorPortSettings port,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        var runtime = CollectorPortRuntimeStateRecord.Create(port, DateTimeOffset.UtcNow);
        await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken, forceFlush: true);
        var reconnectDelay = TimeSpan.FromMilliseconds(Math.Max(settings.ReconnectDelayMs, 250));
        var simulationDelay = TimeSpan.FromMilliseconds(Math.Max(settings.SimulationIntervalMs, 500));
        var readPause = TimeSpan.FromMilliseconds(100);
        var activityWindow = TimeSpan.FromMilliseconds(Math.Max(settings.ActivityWindowMs, 50));
        var debounceWindow = TimeSpan.FromMilliseconds(Math.Max(settings.ConnectionDebounceMs, 50));
        var simulationIndex = 0;
        var random = new Random(HashCode.Combine(port.PortId, settings.DeviceId));
        var lastPresenceState = PortPresenceState.Unknown;
        var simulationFallbackNotified = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var present = !string.IsNullOrWhiteSpace(port.DevicePath) && File.Exists(port.DevicePath);
            var presenceState = present ? PortPresenceState.Present : PortPresenceState.Missing;

            if (lastPresenceState != presenceState)
            {
                lastPresenceState = presenceState;
                runtime.CablePresent = present;
                runtime.LinkUp = null;
                runtime.LastDetectedAtUtc = present ? now : null;
                runtime.LastOpenedAtUtc = present ? runtime.LastOpenedAtUtc : null;
                runtime.SimulationFallback = false;
                runtime.RxActive = false;
                runtime.TxActive = false;
                runtime.Summary = present
                    ? $"Wykryto fizyczny port {port.PortId} pod {port.DevicePath}"
                    : $"Nie wykryto portu {port.PortId} pod {port.DevicePath}";
                ApplyState(runtime, present ? CollectorPortConnectionState.Connecting : CollectorPortConnectionState.Disconnected, now);
                await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);
                await EmitNotificationAsync(
                    writer,
                    HubEnvelopeKinds.Notify,
                    CollectorSignalProcessing.CreatePortStateNotification(
                        settings,
                        port,
                        present ? "info" : "warn",
                        present
                            ? $"Wykryto fizyczny port {port.PortId} pod {port.DevicePath}"
                            : $"Nie wykryto portu {port.PortId} pod {port.DevicePath}",
                        port.DevicePath,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            if (!present)
            {
                if (settings.EnableSimulationFallback && port.AllowSimulationFallback)
                {
                    if (!simulationFallbackNotified)
                    {
                        simulationFallbackNotified = true;
                        runtime.SimulationFallback = true;
                        runtime.Summary = $"Port {port.PortId} nie jest obecnie dostepny. Collector przechodzi w tryb symulacji przejsciowej.";
                        await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);
                        await EmitNotificationAsync(
                            writer,
                            HubEnvelopeKinds.Notify,
                            CollectorSignalProcessing.CreatePortStateNotification(
                                settings,
                                port,
                                "warn",
                                $"Port {port.PortId} nie jest obecnie dostepny. Collector przechodzi w tryb symulacji przejsciowej.",
                                port.DevicePath,
                                DateTimeOffset.UtcNow),
                            cancellationToken);
                    }

                    var simulatedFrame = CollectorSignalProcessing.BuildSimulationMessage(port.EffectiveAlias, simulationIndex, random);
                    var parsed = CollectorSignalProcessing.ParseFrame(settings, port, simulatedFrame, DateTimeOffset.UtcNow);
                    await EmitNotificationAsync(writer, HubEnvelopeKinds.Notify, parsed, cancellationToken);
                    simulationIndex++;
                }
                else if (runtime.SimulationFallback)
                {
                    runtime.SimulationFallback = false;
                    await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);
                }

                await Task.Delay(simulationDelay, cancellationToken);
                continue;
            }

            simulationFallbackNotified = false;
            runtime.SimulationFallback = false;

            try
            {
                using var serialPort = BuildSerialPort(port);
                serialPort.Open();
                var openedAtUtc = DateTimeOffset.UtcNow;
                runtime.CablePresent = true;
                runtime.LinkUp = null;
                runtime.LastOpenedAtUtc = openedAtUtc;
                runtime.LastDetectedAtUtc ??= openedAtUtc;
                runtime.Summary = $"Port {port.PortId} zostal otwarty i monitoruje komunikacje.";
                ApplyState(runtime, CollectorPortConnectionState.Connecting, openedAtUtc);
                await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);

                await EmitNotificationAsync(
                    writer,
                    HubEnvelopeKinds.Notify,
                    CollectorSignalProcessing.CreatePortStateNotification(
                        settings,
                        port,
                        "info",
                        $"Port {port.PortId} zostal otwarty i monitoruje komunikacje.",
                        port.DevicePath,
                        DateTimeOffset.UtcNow),
                    cancellationToken);

                var buffer = new StringBuilder();
                var lastChunkAtUtc = openedAtUtc;
                var rxDetected = false;

                while (!cancellationToken.IsCancellationRequested && serialPort.IsOpen)
                {
                    var iterationTimestamp = DateTimeOffset.UtcNow;
                    var chunk = serialPort.ReadExisting();
                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        buffer.Append(chunk);
                        lastChunkAtUtc = iterationTimestamp;
                        runtime.LastRxAtUtc = iterationTimestamp;
                        runtime.RxCounter += Encoding.UTF8.GetByteCount(chunk);
                        runtime.LastRaw = CollectorSignalProcessing.ConvertToHex(chunk);
                        runtime.LastText = chunk.Trim();
                        runtime.RxActive = true;
                        runtime.TxActive = IsRecent(runtime.LastTxAtUtc, iterationTimestamp, activityWindow);
                        runtime.Summary = $"Aktywny odbior danych na {port.PortId}. Collector parsuje kolejne ramki.";
                        ApplyState(runtime, CollectorPortConnectionState.Rx, iterationTimestamp);
                        await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);

                        if (!rxDetected)
                        {
                            rxDetected = true;
                            await EmitNotificationAsync(
                                writer,
                                HubEnvelopeKinds.Notify,
                                CollectorSignalProcessing.CreatePortStateNotification(
                                    settings,
                                    port,
                                    "info",
                                    $"Wykryto odbior danych na {port.PortId}. Collector rozpoczal parsowanie sygnalu.",
                                    CollectorSignalProcessing.ConvertToHex(chunk),
                                    DateTimeOffset.UtcNow),
                                cancellationToken);
                        }

                        foreach (var frame in CollectorSignalProcessing.ExtractFrames(buffer, port.FrameMode, flushBuffer: false))
                        {
                            var notification = CollectorSignalProcessing.ParseFrame(settings, port, frame, DateTimeOffset.UtcNow);
                            runtime.LastText = notification.Text;
                            runtime.LastRaw = notification.Raw;
                            await EmitNotificationAsync(writer, HubEnvelopeKinds.Notify, notification, cancellationToken);
                        }
                    }
                    else if (buffer.Length > 0 && (iterationTimestamp - lastChunkAtUtc).TotalMilliseconds >= settings.SerialInactivityFlushMs)
                    {
                        foreach (var frame in CollectorSignalProcessing.ExtractFrames(buffer, port.FrameMode, flushBuffer: true))
                        {
                            var notification = CollectorSignalProcessing.ParseFrame(settings, port, frame, DateTimeOffset.UtcNow);
                            runtime.LastText = notification.Text;
                            runtime.LastRaw = notification.Raw;
                            await EmitNotificationAsync(writer, HubEnvelopeKinds.Notify, notification, cancellationToken);
                        }
                    }

                    var recentRx = IsRecent(runtime.LastRxAtUtc, iterationTimestamp, activityWindow);
                    var recentTx = IsRecent(runtime.LastTxAtUtc, iterationTimestamp, activityWindow);
                    runtime.RxActive = recentRx;
                    runtime.TxActive = recentTx;

                    var nextState = ResolvePortState(
                        cablePresent: runtime.CablePresent,
                        linkUp: true,
                        rxActive: recentRx,
                        txActive: recentTx,
                        readyAtUtc: runtime.LastOpenedAtUtc ?? runtime.LastDetectedAtUtc,
                        now: iterationTimestamp,
                        debounceWindow: debounceWindow);

                    if (nextState != runtime.State)
                    {
                        runtime.Summary = nextState switch
                        {
                            CollectorPortConnectionState.Link => $"Port {port.PortId} jest gotowy, ale chwilowo bez ruchu.",
                            CollectorPortConnectionState.Connecting => $"Port {port.PortId} przechodzi przez stabilizacje polaczenia.",
                            _ => runtime.Summary
                        };
                    }

                    ApplyState(runtime, nextState, iterationTimestamp);
                    await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);

                    await Task.Delay(readPause, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                var failedAtUtc = DateTimeOffset.UtcNow;
                runtime.CablePresent = present;
                runtime.LinkUp = null;
                runtime.RxActive = false;
                runtime.TxActive = false;
                runtime.Summary = $"Nie udalo sie monitorowac {port.PortId}: {exception.Message}";
                ApplyState(runtime, CollectorPortConnectionState.Connecting, failedAtUtc);
                await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);
                await EmitNotificationAsync(
                    writer,
                    HubEnvelopeKinds.Notify,
                    CollectorSignalProcessing.CreatePortStateNotification(
                        settings,
                        port,
                        "warn",
                        $"Nie udalo sie monitorowac {port.PortId}: {exception.Message}",
                        port.DevicePath,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
                await Task.Delay(reconnectDelay, cancellationToken);
            }
        }
    }

    private async Task RunNetworkInterfaceLoopAsync(
        CollectorPortSettings port,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        var runtime = CollectorPortRuntimeStateRecord.Create(port, DateTimeOffset.UtcNow);
        runtime.LinkUp = false;
        await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken, forceFlush: true);
        var interval = TimeSpan.FromSeconds(Math.Max(settings.DiscoveryIntervalSec, 2));
        var activityWindow = TimeSpan.FromMilliseconds(Math.Max(settings.ActivityWindowMs, 50));
        var debounceWindow = TimeSpan.FromMilliseconds(Math.Max(settings.ConnectionDebounceMs, 50));
        NetworkInterfaceSnapshot? previous = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var current = ReadNetworkInterfaceSnapshot(port);
            runtime.CablePresent = current.Present;
            runtime.LinkUp = current.LinkUp;
            runtime.RxCounter = current.RxBytes;
            runtime.TxCounter = current.TxBytes;

            if (current.Present && runtime.LastDetectedAtUtc is null)
            {
                runtime.LastDetectedAtUtc = now;
            }
            else if (!current.Present)
            {
                runtime.LastDetectedAtUtc = null;
            }

            if (previous is null
                || previous.Present != current.Present
                || previous.LinkUp != current.LinkUp)
            {
                runtime.Summary = current.Present
                    ? $"Wykryto interfejs {port.PortId} ({current.InterfaceName}), link {(current.LinkUp ? "aktywny" : "nieaktywny")}."
                    : $"Nie wykryto interfejsu {port.PortId} ({current.InterfaceName}).";
                await EmitNotificationAsync(
                    writer,
                    HubEnvelopeKinds.Notify,
                    CollectorSignalProcessing.CreatePortStateNotification(
                        settings,
                        port,
                        current.Present && current.LinkUp ? "info" : "warn",
                        current.Present
                            ? $"Wykryto interfejs {port.PortId} ({current.InterfaceName}), link {(current.LinkUp ? "aktywny" : "nieaktywny")}."
                            : $"Nie wykryto interfejsu {port.PortId} ({current.InterfaceName}).",
                        current.RawState,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            if (previous is not null && current.Present)
            {
                var rxChanged = current.RxBytes != previous.RxBytes;
                var txChanged = current.TxBytes != previous.TxBytes;

                if (rxChanged)
                {
                    runtime.LastRxAtUtc = now;
                }

                if (txChanged)
                {
                    runtime.LastTxAtUtc = now;
                }

                if (rxChanged || txChanged)
                {
                    var summary = CollectorSignalProcessing.BuildActivitySummary(
                        port,
                        current.Present,
                        current.LinkUp,
                        rxChanged,
                        txChanged,
                        current.RxBytes,
                        current.TxBytes);

                    await EmitNotificationAsync(
                        writer,
                        HubEnvelopeKinds.Notify,
                        CollectorSignalProcessing.CreatePortStateNotification(
                            settings,
                            port,
                            "info",
                            $"Wykryto aktywnosc komunikacyjna na {port.PortId}: RX {(rxChanged ? "aktywny" : "idle")}, TX {(txChanged ? "aktywny" : "idle")}.",
                            summary,
                            DateTimeOffset.UtcNow),
                        cancellationToken);

                    runtime.LastRaw = summary;
                    runtime.LastText = $"RX {(rxChanged ? "active" : "idle")} / TX {(txChanged ? "active" : "idle")}";
                    runtime.Summary = $"Wykryto aktywnosc komunikacyjna na {port.PortId}: RX {(rxChanged ? "aktywny" : "idle")}, TX {(txChanged ? "aktywny" : "idle")}.";
                }
            }

            var rxActive = IsRecent(runtime.LastRxAtUtc, now, activityWindow);
            var txActive = IsRecent(runtime.LastTxAtUtc, now, activityWindow);
            runtime.RxActive = rxActive;
            runtime.TxActive = txActive;

            var nextState = ResolvePortState(
                cablePresent: current.Present,
                linkUp: current.LinkUp,
                rxActive: rxActive,
                txActive: txActive,
                readyAtUtc: runtime.LastDetectedAtUtc,
                now: now,
                debounceWindow: debounceWindow);

            if (!current.Present)
            {
                runtime.Summary = $"Nie wykryto interfejsu {port.PortId} ({current.InterfaceName}).";
                runtime.LastRaw = current.RawState;
                runtime.LastText = "missing";
            }
            else if (nextState == CollectorPortConnectionState.Link && string.IsNullOrWhiteSpace(runtime.Summary))
            {
                runtime.Summary = $"Interfejs {port.PortId} ma link, ale chwilowo bez ruchu RX/TX.";
            }

            ApplyState(runtime, nextState, now);
            await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);

            previous = current;
            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task RunPassiveInputLoopAsync(
        CollectorPortSettings port,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        var runtime = CollectorPortRuntimeStateRecord.Create(port, DateTimeOffset.UtcNow);
        runtime.LinkUp = null;
        await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken, forceFlush: true);
        var interval = TimeSpan.FromSeconds(Math.Max(settings.DiscoveryIntervalSec, 2));
        bool? previousPresent = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var present = !string.IsNullOrWhiteSpace(port.DevicePath) && (File.Exists(port.DevicePath) || Directory.Exists(port.DevicePath));
            if (previousPresent != present)
            {
                previousPresent = present;
                runtime.CablePresent = present;
                runtime.LastDetectedAtUtc = present ? now : null;
                runtime.RxActive = null;
                runtime.TxActive = null;
                runtime.LastRaw = port.DevicePath;
                runtime.LastText = present ? "dry-contact-present" : "dry-contact-missing";
                runtime.Summary = present
                    ? $"Wejscie {port.PortId} jest dostepne do monitoringu stanow binarnych."
                    : $"Wejscie {port.PortId} nie jest obecnie widoczne dla collectora.";
                ApplyState(runtime, present ? CollectorPortConnectionState.Link : CollectorPortConnectionState.Disconnected, now);
                await runtimeStateStore.UpsertPortStateAsync(runtime, cancellationToken);
                await EmitNotificationAsync(
                    writer,
                    HubEnvelopeKinds.Notify,
                    CollectorSignalProcessing.CreatePortStateNotification(
                        settings,
                        port,
                        present ? "info" : "warn",
                        present
                            ? $"Wejscie {port.PortId} jest dostepne do monitoringu stanow binarnych."
                            : $"Wejscie {port.PortId} nie jest obecnie widoczne dla collectora.",
                        port.DevicePath,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task RunFixedModuleLoopAsync(
        IReadOnlyList<FixedModuleDescriptor> modules,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(settings.DiscoveryIntervalSec, 5));
        var lastStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var module in modules)
            {
                var present = string.IsNullOrWhiteSpace(module.Path)
                    || File.Exists(module.Path)
                    || Directory.Exists(module.Path);

                if (!lastStates.TryGetValue(module.ModuleId, out var previousState) || previousState != present)
                {
                    lastStates[module.ModuleId] = present;
                    await EmitNotificationAsync(
                        writer,
                        HubEnvelopeKinds.Notify,
                        CollectorSignalProcessing.CreateModuleStateNotification(
                            settings,
                            module,
                            present ? "info" : "warn",
                            present
                                ? $"{module.DisplayName} jest traktowany jako staly element urzadzenia i zostal oznaczony jako gotowy."
                                : $"{module.DisplayName} jest skonfigurowany jako staly element, ale collector nie potwierdzil obecnosci po sciezce.",
                            module.Path ?? "configured-static-module",
                            DateTimeOffset.UtcNow),
                        cancellationToken);
                }
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task RunDeviceHeartbeatLoopAsync(
        int configuredPortCount,
        ChannelWriter<HubEnvelope> writer,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(settings.HeartbeatIntervalSec, 5));

        while (!cancellationToken.IsCancellationRequested)
        {
            var heartbeat = new DeviceHeartbeat(
                DeviceId: settings.DeviceId,
                ClientId: settings.ClientId,
                ClientName: settings.ClientName,
                CurrentVersion: settings.CurrentVersion,
                Timestamp: DateTimeOffset.UtcNow,
                Topic: settings.ZmqTopic,
                PortCount: configuredPortCount,
                PathoNetId: settings.PathoNetId);

            await writer.WriteAsync(HubEnvelope.ForHeartbeat(settings.ZmqTopic, heartbeat), cancellationToken);
            logger.LogInformation("[COLLECTOR][HB] queued device heartbeat");
            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task DispatchEventsAsync(
        HubPublisher publisher,
        ChannelReader<HubEnvelope> reader,
        CancellationToken cancellationToken)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        await foreach (var envelope in reader.ReadAllAsync(cancellationToken))
        {
            var json = JsonSerializer.Serialize(envelope, jsonOptions);
            await publisher.SendAsync(json, cancellationToken);
            logger.LogInformation("[COLLECTOR][HUB] published {Kind}", envelope.Kind);
        }
    }

    private async Task RunHubHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var endpoint = ParseTcpEndpoint(settings.ZmqHeartbeatAddr);
        var interval = TimeSpan.FromSeconds(Math.Max(settings.HubHeartbeatIntervalSec, 2));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);

                await using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                var ping = new HubPing("ping", "collector-runtime", DateTimeOffset.UtcNow);
                await writer.WriteLineAsync(JsonSerializer.Serialize(ping, jsonOptions));
                var replyLine = await reader.ReadLineAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(replyLine))
                {
                    var pong = JsonSerializer.Deserialize<HubPong>(replyLine, jsonOptions);
                    logger.LogInformation(
                        "[COLLECTOR][HUB-HB] {Status} notifications={Notifications} heartbeats={Heartbeats}",
                        pong?.Status ?? "unknown",
                        pong?.NotificationsForwarded ?? 0,
                        pong?.HeartbeatsForwarded ?? 0);
                }
            }
            catch (Exception exception) when (exception is SocketException or IOException or JsonException)
            {
                logger.LogWarning("[COLLECTOR][HUB-HB] unavailable: {Message}", exception.Message);
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task EmitNotificationAsync(
        ChannelWriter<HubEnvelope> writer,
        string kind,
        DeviceNotification notification,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(kind, HubEnvelopeKinds.Notify, StringComparison.Ordinal))
        {
            return;
        }

        await writer.WriteAsync(HubEnvelope.ForNotification(settings.ZmqTopic, settings.DeviceApiKey, notification), cancellationToken);
        logger.LogInformation("[COLLECTOR][{Port}] queued {Text}", notification.Port, notification.Text);
    }

    private static void ApplyState(
        CollectorPortRuntimeStateRecord runtime,
        CollectorPortConnectionState nextState,
        DateTimeOffset now)
    {
        if (runtime.State != nextState)
        {
            runtime.State = nextState;
            runtime.StateSinceUtc = now;
            runtime.LastTransitionAtUtc = now;
        }
    }

    private static bool IsRecent(DateTimeOffset? timestamp, DateTimeOffset now, TimeSpan window) =>
        timestamp is not null && now - timestamp.Value <= window;

    private static CollectorPortConnectionState ResolvePortState(
        bool cablePresent,
        bool? linkUp,
        bool? rxActive,
        bool? txActive,
        DateTimeOffset? readyAtUtc,
        DateTimeOffset now,
        TimeSpan debounceWindow)
    {
        if (!cablePresent)
        {
            return CollectorPortConnectionState.Disconnected;
        }

        if (readyAtUtc is not null && now - readyAtUtc.Value < debounceWindow)
        {
            return CollectorPortConnectionState.Connecting;
        }

        if (txActive == true)
        {
            return CollectorPortConnectionState.Tx;
        }

        if (rxActive == true)
        {
            return CollectorPortConnectionState.Rx;
        }

        if (linkUp is false)
        {
            return CollectorPortConnectionState.Connecting;
        }

        return CollectorPortConnectionState.Link;
    }

    private SerialPort BuildSerialPort(CollectorPortSettings port) =>
        new(
            port.DevicePath,
            port.BaudRate,
            CollectorSignalProcessing.ResolveParity(port.Parity),
            port.DataBits,
            CollectorSignalProcessing.ResolveStopBits(port.StopBits))
        {
            ReadTimeout = Math.Max(settings.SerialReadTimeoutMs, 50),
            WriteTimeout = Math.Max(settings.SerialReadTimeoutMs, 50),
            Encoding = Encoding.UTF8,
            NewLine = "\n"
        };

    private static NetworkInterfaceSnapshot ReadNetworkInterfaceSnapshot(CollectorPortSettings port)
    {
        var name = port.EffectiveNetworkInterfaceName;
        var root = Path.Combine("/sys/class/net", name);
        if (!Directory.Exists(root))
        {
            return new NetworkInterfaceSnapshot(name, Present: false, LinkUp: false, RxBytes: 0, TxBytes: 0, RawState: "missing");
        }

        var operState = TryReadAllText(Path.Combine(root, "operstate")) ?? "unknown";
        var rxBytes = TryReadInt64(Path.Combine(root, "statistics", "rx_bytes"));
        var txBytes = TryReadInt64(Path.Combine(root, "statistics", "tx_bytes"));
        var linkUp = operState.Equals("up", StringComparison.OrdinalIgnoreCase) || operState.Equals("unknown", StringComparison.OrdinalIgnoreCase);

        return new NetworkInterfaceSnapshot(name, Present: true, LinkUp: linkUp, RxBytes: rxBytes, TxBytes: txBytes, RawState: operState);
    }

    private static string? TryReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long TryReadInt64(string path)
    {
        var text = TryReadAllText(path);
        return long.TryParse(text, out var value) ? value : 0;
    }

    private static TcpEndpoint ParseTcpEndpoint(string address)
    {
        var uri = new Uri(address, UriKind.Absolute);
        return new TcpEndpoint(uri.Host, uri.Port);
    }
}

internal enum PortPresenceState
{
    Unknown,
    Missing,
    Present
}

internal sealed record NetworkInterfaceSnapshot(
    string InterfaceName,
    bool Present,
    bool LinkUp,
    long RxBytes,
    long TxBytes,
    string RawState);

internal sealed record TcpEndpoint(string Host, int Port);

internal sealed class HubPublisher(string address, int reconnectDelayMs, ILogger logger) : IAsyncDisposable
{
    private readonly TcpEndpoint _endpoint = ParsePublisherEndpoint(address);
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromMilliseconds(Math.Max(reconnectDelayMs, 250));
    private TcpClient? _client;
    private StreamWriter? _writer;

    public async Task SendAsync(string line, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(cancellationToken);
                await _writer!.WriteLineAsync(line);
                await _writer.FlushAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException)
            {
                logger.LogWarning("[COLLECTOR][HUB] reconnect after error: {Message}", exception.Message);
                await ResetAsync();
                await Task.Delay(_reconnectDelay, cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is { Connected: true } && _writer is not null)
        {
            return;
        }

        await ResetAsync();
        _client = new TcpClient();
        await _client.ConnectAsync(_endpoint.Host, _endpoint.Port, cancellationToken);
        _writer = new StreamWriter(_client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
    }

    private Task ResetAsync()
    {
        _writer?.Dispose();
        _client?.Dispose();
        _writer = null;
        _client = null;
        return Task.CompletedTask;
    }

    private static TcpEndpoint ParsePublisherEndpoint(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        return new TcpEndpoint(uri.Host, uri.Port);
    }
}
