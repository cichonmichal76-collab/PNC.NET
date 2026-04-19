using PathoNet.Contracts;

internal sealed partial class SimulationStore
{
    private PortalDeviceRecord[] BuildDevices(
        DeviceNotification[] notifications,
        PortalRulebookConfig rulebook,
        DateTimeOffset now)
    {
        return notifications
            .GroupBy(static notification => notification.Port, StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group.First();
                var matchedRule = FindMatchingRule(latest, rulebook.Rules);
                var activation = matchedRule is null ? null : TryGetRuleState(matchedRule.Id, latest.Port);
                var orderedLevels = group.Take(3).Select(static item => NormalizeLevel(item.Level)).ToArray();
                var warnCount = group.Count(static item => NormalizeLevel(item.Level) == "warn");
                var alarmCount = group.Count(static item => NormalizeLevel(item.Level) == "alarm");
                var errorCount = group.Count(static item => NormalizeLevel(item.Level) == "error");
                var currentLevel = NormalizeLevel(latest.Level);
                var status = StatusFromLevel(currentLevel);
                var riskScore = CalculateRiskScore(currentLevel, warnCount, alarmCount, errorCount);
                var healthScore = 100 - riskScore;

                return new PortalDeviceRecord(
                    Alias: latest.Alias,
                    DisplayName: matchedRule?.Name ?? latest.Alias,
                    Port: latest.Port,
                    GroupName: ResolveGroupName(latest.Port),
                    Status: status,
                    CurrentLevel: currentLevel,
                    LastMessage: latest.Text,
                    LastSeen: latest.Meta.DateMess,
                    TotalEvents: group.Count(),
                    WarnCount: warnCount,
                    AlarmCount: alarmCount + errorCount,
                    HealthScore: healthScore,
                    RiskScore: riskScore,
                    Trend: ComputeTrend(orderedLevels),
                    Recommendation: RecommendationForLevel(currentLevel, matchedRule),
                    RuleName: matchedRule?.Name,
                    EscalationSummary: BuildEscalationSummary(matchedRule, activation, rulebook, now),
                    ThresholdReached: IsThresholdReached(matchedRule, activation, now));
            })
            .OrderBy(static device => device.Port, StringComparer.Ordinal)
            .ToArray();
    }

    private PortalAlertRecord[] BuildAlerts(
        DeviceNotification[] notifications,
        PortalRulebookConfig rulebook,
        DateTimeOffset now)
    {
        return notifications
            .Where(static notification => NormalizeLevel(notification.Level) is "warn" or "alarm" or "error")
            .Take(12)
            .Select(notification =>
            {
                var level = NormalizeLevel(notification.Level);
                var matchedRule = FindMatchingRule(notification, rulebook.Rules);
                var activation = matchedRule is null ? null : TryGetRuleState(matchedRule.Id, notification.Port);
                return new PortalAlertRecord(
                    Alias: notification.Alias,
                    DisplayName: matchedRule?.Name ?? notification.Alias,
                    Port: notification.Port,
                    GroupName: ResolveGroupName(notification.Port),
                    Level: level,
                    Summary: notification.Text,
                    OccurredAt: notification.Meta.DateMess,
                    Action: ActionForAlert(level, matchedRule, activation, now),
                    RuleName: matchedRule?.Name,
                    EscalationSummary: BuildEscalationSummary(matchedRule, activation, rulebook, now),
                    ThresholdReached: IsThresholdReached(matchedRule, activation, now));
            })
            .ToArray();
    }

    private static PortalGroupRecord[] BuildGroups(PortalDeviceRecord[] devices)
    {
        return devices
            .GroupBy(static device => device.GroupName, StringComparer.Ordinal)
            .Select(group =>
            {
                var members = group.ToArray();
                var worstLevel = GetWorstLevel(members.Select(static member => member.CurrentLevel));

                return new PortalGroupRecord(
                    Name: group.Key,
                    DeviceCount: members.Length,
                    AlertCount: members.Sum(static member => member.AlarmCount + member.WarnCount),
                    AverageHealth: (int)Math.Round(members.Average(static member => member.HealthScore)),
                    WorstLevel: worstLevel,
                    Members: members.Select(static member => $"{member.DisplayName} ({member.Port})").ToArray(),
                    Summary: GroupSummary(group.Key, worstLevel, members.Length));
            })
            .OrderBy(static group => group.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private HistoryEventRecord[] BuildHistory(DeviceNotification[] notifications, PortalRulebookConfig rulebook)
    {
        return notifications
            .Take(18)
            .Select(notification =>
            {
                var matchedRule = FindMatchingRule(notification, rulebook.Rules);
                return new HistoryEventRecord(
                    Alias: notification.Alias,
                    DisplayName: matchedRule?.Name ?? notification.Alias,
                    Port: notification.Port,
                    Level: NormalizeLevel(notification.Level),
                    Message: notification.Text,
                    OccurredAt: notification.Meta.DateMess,
                    Raw: notification.Raw,
                    RuleName: matchedRule?.Name);
            })
            .ToArray();
    }

    private static ActivityBucketRecord[] BuildActivity(DeviceNotification[] notifications)
    {
        return notifications
            .Take(24)
            .Reverse()
            .Chunk(3)
            .Select((chunk, index) =>
            {
                var levels = chunk.Select(static item => NormalizeLevel(item.Level));
                return new ActivityBucketRecord(
                    Label: $"B{index + 1}",
                    Count: chunk.Length,
                    WorstLevel: GetWorstLevel(levels),
                    AlarmCount: chunk.Count(static item => NormalizeLevel(item.Level) is "alarm" or "error"),
                    WarnCount: chunk.Count(static item => NormalizeLevel(item.Level) == "warn"));
            })
            .ToArray();
    }

    private static PortalLteRecord BuildLteState(LteModemConfigRecord config, DateTimeOffset now)
    {
        var pulse = ((now.Second / 2) % 7) - 3;
        var signalPercent = Math.Clamp(config.BaseSignalPercent + (pulse * 2), 18, 98);
        var signalDbm = config.BaseSignalDbm + (pulse * 2);
        var rsrpDbm = config.BaseRsrpDbm + pulse;
        var rsrqDb = config.BaseRsrqDb + (pulse / 2);
        var sinrDb = Math.Clamp(config.BaseSinrDb + pulse, -20, 40);
        var status = signalPercent switch
        {
            >= 65 => "online",
            >= 40 => "attention",
            _ => "critical"
        };

        return new PortalLteRecord(
            SimSlot: config.SimSlot,
            ModemName: config.ModemName,
            Status: status,
            OperatorName: config.OperatorName,
            NetworkType: config.NetworkType,
            SimNumber: config.SimNumber,
            Iccid: config.Iccid,
            Imsi: config.Imsi,
            Imei: config.Imei,
            Apn: config.Apn,
            CellId: config.CellId,
            IpAddress: config.IpAddress,
            SignalPercent: signalPercent,
            SignalDbm: signalDbm,
            SignalQuality: SignalQualityLabel(signalPercent),
            DownloadMbps: Math.Round(Math.Max(4, config.BaseDownloadMbps + (pulse * 0.7)), 1),
            UploadMbps: Math.Round(Math.Max(1, config.BaseUploadMbps + (pulse * 0.3)), 1),
            SampledAtUtc: now,
            RegistrationStatus: config.RegistrationStatus,
            Plmn: config.Plmn,
            MccMnc: config.MccMnc,
            Roaming: config.Roaming,
            PinState: config.PinState,
            Smsc: config.Smsc,
            Tac: config.Tac,
            RsrpDbm: rsrpDbm,
            RsrqDb: rsrqDb,
            SinrDb: sinrDb,
            DnsPrimary: config.DnsPrimary,
            DnsSecondary: config.DnsSecondary,
            LastAttachAtUtc: config.LastAttachAtUtc,
            Summary: $"{config.OperatorName}, {config.NetworkType}, slot {config.SimSlot}. {config.Notes}");
    }

    private static PortalPncDeviceRecord[] BuildPncDevices(
        PncDeviceConfigRecord[] configs,
        DateTimeOffset now,
        DeviceHeartbeat? latestHeartbeat)
    {
        return configs
            .Select((config, index) =>
            {
                var pulse = ((now.Minute + (index * 3)) % 9) - 4;
                var signalPercent = Math.Clamp(config.BaseSignalPercent + (pulse * 2), 16, 97);
                var signalDbm = config.BaseSignalDbm + (pulse * 2);
                var status = !config.Online
                    ? "critical"
                    : signalPercent switch
                    {
                        >= 60 => "online",
                        >= 38 => "attention",
                        _ => "critical"
                    };
                var (province, city, hospital, site) = SplitStructuredLocation(config.Location);
                var connectedDeviceTypes = ResolveConnectedDeviceTypes(config.Connections);
                var healthScore = CalculatePncHealthScore(config, status, signalPercent);

                var lastSeenUtc = now.AddMinutes(-(index * 4) - (status == "attention" ? 2 : 0));
                var summary = config.DeviceCode == "PNC-001" && latestHeartbeat is not null
                    ? $"Wezel nadrzedny dla klienta {latestHeartbeat.ClientName}. {config.Notes}"
                    : config.Notes;

                return new PortalPncDeviceRecord(
                    DeviceCode: config.DeviceCode,
                    Name: config.Name,
                    Location: config.Location,
                    Province: province,
                    City: city,
                    Hospital: hospital,
                    Site: site,
                    IsOnline: config.Online,
                    Status: status,
                    OperatorName: config.OperatorName,
                    NetworkType: config.NetworkType,
                    SimNumber: config.SimNumber,
                    SignalPercent: signalPercent,
                    SignalDbm: signalDbm,
                    SignalQuality: SignalQualityLabel(signalPercent),
                    Rs232Connected: config.Rs232Connected,
                    Rs485Connected: config.Rs485Connected,
                    CanConnected: config.CanConnected,
                    EthernetConnected: config.EthernetConnected,
                    DigitalInputs: config.DigitalInputs,
                    DigitalOutputs: config.DigitalOutputs,
                    Firmware: config.Firmware,
                    LastSeen: lastSeenUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                    MainboardStatus: config.MainboardStatus,
                    MainboardTempC: config.MainboardTempC + pulse,
                    SupplyVoltage: Math.Round(config.SupplyVoltage + (pulse * 0.04), 2),
                    HealthScore: healthScore,
                    ConnectedDeviceTypes: connectedDeviceTypes,
                    Summary: summary);
            })
            .OrderBy(static device => device.DeviceCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static PortalMainboardRecord[] BuildMainboards(
        PncDeviceConfigRecord[] configs,
        DateTimeOffset now,
        DeviceHeartbeat? latestHeartbeat)
    {
        return configs
            .Select((config, index) =>
            {
                var pulse = ((now.Second + (index * 4)) % 9) - 4;
                var cpuLoadPercent = Math.Clamp(config.BaseCpuLoadPercent + (pulse * 3), 6, 96);
                var memoryPercent = Math.Clamp(config.BaseMemoryPercent + (pulse * 2), 8, 97);
                var storagePercent = Math.Clamp(config.BaseStoragePercent + Math.Max(pulse, 0), 10, 98);
                var temperatureC = Math.Clamp(config.MainboardTempC + pulse, 22, 92);
                var supplyVoltage = Math.Round(config.SupplyVoltage + (pulse * 0.03), 2);
                var signalPercent = Math.Clamp(config.BaseSignalPercent + (pulse * 2), 12, 97);
                var connectionCount = config.Connections?.Length ?? 0;
                var degradedConnections = (config.Connections ?? [])
                    .Count(static connection => connection.Status != "online");
                var status = !config.Online
                    ? "critical"
                    : temperatureC switch
                    {
                        >= 58 => "critical",
                        >= 48 => "attention",
                        _ => cpuLoadPercent >= 82 || memoryPercent >= 80 || degradedConnections > 0 || config.MainboardStatus.Contains("obserw", StringComparison.OrdinalIgnoreCase)
                            ? "attention"
                            : "online"
                    };

                var summary = config.DeviceCode == "PNC-001" && latestHeartbeat is not null
                    ? $"Plyta nadrzedna dla obiektu {latestHeartbeat.ClientName}. CPU {cpuLoadPercent}% i RAM {memoryPercent}%."
                    : $"CPU {cpuLoadPercent}%, RAM {memoryPercent}%, magazyn {storagePercent}% i {connectionCount} aktywnych mapowan portow.";

                return new PortalMainboardRecord(
                    DeviceCode: config.DeviceCode,
                    Name: config.Name,
                    Location: config.Location,
                    Status: status,
                    BoardRevision: config.BoardRevision,
                    BoardSerialNumber: config.BoardSerialNumber,
                    Firmware: config.Firmware,
                    TemperatureC: temperatureC,
                    SupplyVoltage: supplyVoltage,
                    CpuLoadPercent: cpuLoadPercent,
                    MemoryPercent: memoryPercent,
                    StoragePercent: storagePercent,
                    WatchdogHealthy: config.WatchdogHealthy,
                    WatchdogState: config.WatchdogHealthy ? "aktywny" : "blad",
                    UptimeHours: config.UptimeHours + Math.Max(index, 1),
                    OperatorName: config.OperatorName,
                    NetworkType: config.NetworkType,
                    SimSlot: config.SimSlot,
                    SimNumber: config.SimNumber,
                    SignalPercent: signalPercent,
                    SignalQuality: SignalQualityLabel(signalPercent),
                    LastSeen: now.AddMinutes(-(index * 5)).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                    ConfiguredConnectionCount: connectionCount,
                    DegradedConnectionCount: degradedConnections,
                    PortSummary: $"RS-232 {config.Rs232Connected} / RS-485 {config.Rs485Connected} / CAN {config.CanConnected} / ETH {config.EthernetConnected} / DI {config.DigitalInputs} / DO {config.DigitalOutputs}",
                    Summary: summary);
            })
            .OrderBy(static board => board.DeviceCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static (string Province, string City, string Hospital, string Site) SplitStructuredLocation(string location)
    {
        var parts = location
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return (
            Province: parts.ElementAtOrDefault(0) ?? "Nieznane",
            City: parts.ElementAtOrDefault(1) ?? "Nieznane",
            Hospital: parts.ElementAtOrDefault(2) ?? "Obiekt PathoNet",
            Site: parts.ElementAtOrDefault(3) ?? location);
    }

    private static string[] ResolveConnectedDeviceTypes(PncExternalConnectionConfigRecord[] connections)
    {
        return connections
            .Select(ResolveConnectedDeviceType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveConnectedDeviceType(PncExternalConnectionConfigRecord connection)
    {
        var deviceName = connection.DeviceName.ToLowerInvariant();

        if (deviceName.Contains("respirator", StringComparison.Ordinal))
        {
            return "Respirator";
        }

        if (deviceName.Contains("pompa", StringComparison.Ordinal))
        {
            return "Pompa";
        }

        if (deviceName.Contains("defibrylator", StringComparison.Ordinal))
        {
            return "Defibrylator";
        }

        if (deviceName.Contains("analizator", StringComparison.Ordinal) || deviceName.Contains("waga", StringComparison.Ordinal))
        {
            return "Analizator";
        }

        if (deviceName.Contains("epredia", StringComparison.Ordinal)
            || deviceName.Contains("leica", StringComparison.Ordinal)
            || deviceName.Contains("tissue", StringComparison.Ordinal)
            || deviceName.Contains("histopatolog", StringComparison.Ordinal))
        {
            return "Procesor tkankowy";
        }

        if (deviceName.Contains("monitor", StringComparison.Ordinal))
        {
            return "Monitor";
        }

        if (deviceName.Contains("terminal", StringComparison.Ordinal) || deviceName.Contains("drukarka", StringComparison.Ordinal))
        {
            return "Stanowisko obslugi";
        }

        if (deviceName.Contains("ups", StringComparison.Ordinal) || deviceName.Contains("baterii", StringComparison.Ordinal) || deviceName.Contains("zasilania", StringComparison.Ordinal))
        {
            return "Zasilanie";
        }

        if (deviceName.Contains("switch", StringComparison.Ordinal) || deviceName.Contains("access point", StringComparison.Ordinal) || deviceName.Contains("serwer", StringComparison.Ordinal))
        {
            return "Siec";
        }

        if (deviceName.Contains("czujnik", StringComparison.Ordinal) || deviceName.Contains("przycisk", StringComparison.Ordinal))
        {
            return "Czujnik";
        }

        if (deviceName.Contains("sterownik", StringComparison.Ordinal) || deviceName.Contains("modul", StringComparison.Ordinal))
        {
            return "Sterownik";
        }

        if (connection.InterfaceType is "digital-input" or "digital-output")
        {
            return "IO";
        }

        return "Inne";
    }

    private static int CalculatePncHealthScore(PncDeviceConfigRecord config, string status, int signalPercent)
    {
        var score = config.Online ? 100 : 35;
        score -= Math.Max(0, 60 - signalPercent);
        score -= Math.Max(0, config.MainboardTempC - 42);
        score -= config.WatchdogHealthy ? 0 : 18;
        score -= status switch
        {
            "critical" => 24,
            "attention" => 12,
            _ => 0
        };
        score -= (config.Connections ?? []).Count(static connection => connection.Status != "online") * 6;
        return Math.Clamp(score, 5, 100);
    }
}
