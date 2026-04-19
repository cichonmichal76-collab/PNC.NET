using System.Collections.Concurrent;
using PathoNet.Contracts;

internal sealed partial class SimulationStore
{
    private readonly RulebookStore _rulebookStore;
    private readonly FleetMockStore _fleetMockStore;
    private readonly OtaMockStore _otaMockStore;
    private readonly TissueProcessorPredictionService _predictionService;
    private readonly ConcurrentQueue<DeviceNotification> _notifications = new();
    private readonly ConcurrentQueue<DeviceHeartbeat> _heartbeats = new();
    private readonly ConcurrentDictionary<string, RuleActivationState> _ruleStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<EscalationDispatchViewRecord> _dispatches = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private int _notificationCount;
    private int _heartbeatCount;
    private int _infoCount;
    private int _warnCount;
    private int _alarmCount;
    private int _errorCount;
    private long _lastNotificationUnixMs;
    private long _lastHeartbeatUnixMs;

    public SimulationStore(
        RulebookStore rulebookStore,
        FleetMockStore fleetMockStore,
        OtaMockStore otaMockStore,
        TissueProcessorPredictionService predictionService)
    {
        _rulebookStore = rulebookStore;
        _fleetMockStore = fleetMockStore;
        _otaMockStore = otaMockStore;
        _predictionService = predictionService;
    }

    public void AddNotification(DeviceNotification notification)
    {
        _notifications.Enqueue(notification);
        Interlocked.Increment(ref _notificationCount);
        Interlocked.Exchange(ref _lastNotificationUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        IncrementLevelCounter(notification.Level);
        Trim(_notifications, 160);
        TrackRuleMatch(notification);

        Console.WriteLine(
            $"[API][NOTIFY] device={notification.DeviceId} port={notification.Port} level={notification.Level} text={notification.Text}");
    }

    public void AddHeartbeat(DeviceHeartbeat heartbeat)
    {
        _heartbeats.Enqueue(heartbeat);
        Interlocked.Increment(ref _heartbeatCount);
        Interlocked.Exchange(ref _lastHeartbeatUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Trim(_heartbeats, 24);
        Console.WriteLine(
            $"[API][HEARTBEAT] device={heartbeat.DeviceId} client={heartbeat.ClientName} ports={heartbeat.PortCount}");
    }

    public PortalDiagnosticsRecord Snapshot()
    {
        var notifications = OrderedNotifications();
        var heartbeats = OrderedHeartbeats();
        var rulebook = _rulebookStore.GetConfig();
        var fleet = _fleetMockStore.GetConfig();
        EnsurePendingEscalations(rulebook);

        return new PortalDiagnosticsRecord(
            StartedAtUtc: _startedAtUtc,
            NotificationCount: _notificationCount,
            HeartbeatCount: _heartbeatCount,
            LastNotificationAtUtc: UnixMsToDateTimeOffset(Interlocked.Read(ref _lastNotificationUnixMs)),
            LastHeartbeatAtUtc: UnixMsToDateTimeOffset(Interlocked.Read(ref _lastHeartbeatUnixMs)),
            LevelTotals: new PortalLevelTotalsRecord(
                Info: _infoCount,
                Warn: _warnCount,
                Alarm: _alarmCount,
                Error: _errorCount),
            ActivePorts: notifications
                .Select(static notification => notification.Port)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static port => port, StringComparer.Ordinal)
                .ToArray(),
            LteOperator: fleet.Lte.OperatorName,
            PncCount: fleet.PncDevices.Length,
            MainboardCount: fleet.PncDevices.Length,
            RuleCount: rulebook.Rules.Length,
            OtaCampaignCount: _otaMockStore.GetStateAsync(fleet, CancellationToken.None).GetAwaiter().GetResult().Summary.CampaignCount,
            ActiveRuleMatches: BuildRuleActivationViews(rulebook, DateTimeOffset.UtcNow).Length,
            DispatchCount: _dispatches.Count,
            LastNotifications: notifications.Take(25).ToArray(),
            LastHeartbeats: heartbeats.Take(10).ToArray());
    }

    public DeviceNotification[] NotificationsSnapshot() =>
        OrderedNotifications();

    public PortalStateRecord PortalState()
    {
        var now = DateTimeOffset.UtcNow;
        var rulebook = _rulebookStore.GetConfig();
        var fleet = _fleetMockStore.GetConfig();
        EnsurePendingEscalations(rulebook);

        var notifications = OrderedNotifications();
        var heartbeats = OrderedHeartbeats();
        var latestHeartbeat = heartbeats.FirstOrDefault();
        var devices = BuildDevices(notifications, rulebook, now);
        var alerts = BuildAlerts(notifications, rulebook, now);
        var groups = BuildGroups(devices);
        var history = BuildHistory(notifications, rulebook);
        var activity = BuildActivity(notifications);
        var predictionAnalysis = _predictionService.BuildAnalysis(devices, alerts, history, activity);
        var predictions = predictionAnalysis.Predictions;
        var lte = BuildLteState(fleet.Lte, now);
        var pncDevices = BuildPncDevices(fleet.PncDevices, now, latestHeartbeat);
        var mainboards = BuildMainboards(fleet.PncDevices, now, latestHeartbeat);

        var overview = new PortalOverviewRecord(
            ClientName: latestHeartbeat?.ClientName ?? "Obiekt PathoNet",
            CurrentVersion: latestHeartbeat?.CurrentVersion ?? "1.0.47",
            StartedAtUtc: _startedAtUtc,
            NotificationCount: _notificationCount,
            HeartbeatCount: _heartbeatCount,
            ActiveDeviceCount: devices.Length,
            AlertCount: alerts.Length,
            ActiveGroupCount: groups.Length,
            ActiveRuleCount: rulebook.Rules.Count(static rule => rule.Enabled),
            ActiveEscalationCount: BuildRuleActivationViews(rulebook, now).Count(static activation => activation.ThresholdReached),
            PncDeviceCount: pncDevices.Length,
            PncOnlineCount: pncDevices.Count(static device => device.Status == "online"),
            LteStatus: lte.Status,
            LteOperator: lte.OperatorName,
            WorstLevel: GetWorstLevel(devices.Select(static device => device.CurrentLevel)),
            LastNotificationAtUtc: UnixMsToDateTimeOffset(Interlocked.Read(ref _lastNotificationUnixMs)),
            LastHeartbeatAtUtc: UnixMsToDateTimeOffset(Interlocked.Read(ref _lastHeartbeatUnixMs)));

        return new PortalStateRecord(
            Overview: overview,
            Roles: BuildRoles(),
            Roadmap: BuildRoadmap(),
            Devices: devices,
            Alerts: alerts,
            Groups: groups,
            Activity: activity,
            History: history,
            Predictions: predictions,
            PredictionAnalysis: predictionAnalysis,
            Lte: lte,
            PncDevices: pncDevices,
            Mainboards: mainboards);
    }

    public PortalHdmiRecord HdmiState()
    {
        var state = PortalState();

        return new PortalHdmiRecord(
            ClientName: state.Overview.ClientName,
            CurrentVersion: state.Overview.CurrentVersion,
            NotificationCount: state.Overview.NotificationCount,
            HeartbeatCount: state.Overview.HeartbeatCount,
            LastNotificationAtUtc: state.Overview.LastNotificationAtUtc,
            LastHeartbeatAtUtc: state.Overview.LastHeartbeatAtUtc,
            Tiles: state.Devices
                .OrderBy(static device => device.Port, StringComparer.Ordinal)
                .ToArray(),
            Alerts: state.Alerts.Take(4).ToArray(),
            Headline: BuildHdmiHeadline(state.Alerts, state.Predictions));
    }

    public PortalRulebookStateRecord RulebookState()
    {
        var rulebook = _rulebookStore.GetConfig();
        var now = DateTimeOffset.UtcNow;
        EnsurePendingEscalations(rulebook);
        return BuildRulebookState(rulebook, now);
    }

    public async Task<PortalRulebookStateRecord> UpdateRulebookAsync(
        PortalRulebookConfig rulebook,
        CancellationToken cancellationToken)
    {
        var saved = await _rulebookStore.SaveAsync(rulebook, cancellationToken);
        PruneRuleStates(saved);
        var now = DateTimeOffset.UtcNow;
        EnsurePendingEscalations(saved);
        return BuildRulebookState(saved, now);
    }

    public PortalFleetConfig FleetState() => _fleetMockStore.GetConfig();

    public Task<PortalFleetConfig> UpdateFleetAsync(
        PortalFleetConfig fleet,
        CancellationToken cancellationToken) =>
        _fleetMockStore.SaveAsync(fleet, cancellationToken);

    public Task<PortalOtaStateRecord> OtaStateAsync(CancellationToken cancellationToken) =>
        _otaMockStore.GetStateAsync(_fleetMockStore.GetConfig(), cancellationToken);

    public PortalOtaConfig OtaConfig() => _otaMockStore.GetConfig();

    public Task<PortalOtaStateRecord> UpdateOtaAsync(
        PortalOtaConfig ota,
        CancellationToken cancellationToken) =>
        _otaMockStore.SaveAsync(ota, _fleetMockStore.GetConfig(), cancellationToken);

    private void IncrementLevelCounter(string level)
    {
        switch (NormalizeLevel(level))
        {
            case "alarm":
                Interlocked.Increment(ref _alarmCount);
                break;
            case "warn":
                Interlocked.Increment(ref _warnCount);
                break;
            case "error":
                Interlocked.Increment(ref _errorCount);
                break;
            default:
                Interlocked.Increment(ref _infoCount);
                break;
        }
    }

    private DeviceNotification[] OrderedNotifications() =>
        _notifications.ToArray().Reverse().ToArray();

    private DeviceHeartbeat[] OrderedHeartbeats() =>
        _heartbeats.ToArray().Reverse().ToArray();

    private static DateTimeOffset? UnixMsToDateTimeOffset(long unixMs) =>
        unixMs <= 0
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

    private static void Trim<T>(ConcurrentQueue<T> queue, int maxItems)
    {
        while (queue.Count > maxItems)
        {
            queue.TryDequeue(out _);
        }
    }
}
