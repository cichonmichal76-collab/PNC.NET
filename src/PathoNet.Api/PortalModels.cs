using PathoNet.Contracts;

internal sealed record PortalStateRecord(
    PortalOverviewRecord Overview,
    PortalRoleRecord[] Roles,
    RoadmapPhaseRecord[] Roadmap,
    PortalDeviceRecord[] Devices,
    PortalAlertRecord[] Alerts,
    PortalGroupRecord[] Groups,
    ActivityBucketRecord[] Activity,
    HistoryEventRecord[] History,
    PredictionRecord[] Predictions,
    PortalPredictionAnalysisRecord PredictionAnalysis,
    PortalLteRecord Lte,
    PortalPncDeviceRecord[] PncDevices,
    PortalMainboardRecord[] Mainboards);

internal sealed record PortalServiceHealthStateRecord(
    DateTimeOffset GeneratedAtUtc,
    PortalServiceHealthSummaryRecord Summary,
    PortalServiceStatusRecord[] Services,
    PortalServiceRestartHistoryRecord[] RestartHistory);

internal sealed record PortalServiceHealthSummaryRecord(
    int TotalCount,
    int OnlineCount,
    int AttentionCount,
    int CriticalCount,
    int SystemdCount,
    int WatchdogCount,
    int TotalRestartCount,
    int RecentRestartCount,
    int FailedRestartCount,
    int PendingRestartCount);

internal sealed record PortalServiceStatusRecord(
    string Name,
    string DisplayName,
    string Status,
    string RuntimeMode,
    int RestartCount,
    bool ProcessAlive,
    int? ProcessId,
    bool SystemdDetected,
    bool NotifierEnabled,
    bool WatchdogActive,
    double? WatchdogIntervalSeconds,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? LastWatchdogHeartbeatUtc,
    DateTimeOffset? LastStoppedAtUtc,
    string HostEnvironment,
    string MachineName,
    string WorkingDirectory,
    bool SupportsRestart,
    string RestartMode,
    string Summary,
    string? StdoutPath,
    string? StderrPath);

internal sealed record PortalServiceRestartHistoryRecord(
    string Id,
    string ServiceName,
    string DisplayName,
    string Status,
    string Mode,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int? PreviousProcessId,
    int? CurrentProcessId,
    string Summary);

internal sealed record PortalServiceRestartRequestResultRecord(
    bool Accepted,
    string ServiceName,
    string Status,
    string Mode,
    DateTimeOffset RequestedAtUtc,
    string? EventId,
    string Message);

internal sealed record PortalOverviewRecord(
    string ClientName,
    string CurrentVersion,
    DateTimeOffset StartedAtUtc,
    int NotificationCount,
    int HeartbeatCount,
    int ActiveDeviceCount,
    int AlertCount,
    int ActiveGroupCount,
    int ActiveRuleCount,
    int ActiveEscalationCount,
    int PncDeviceCount,
    int PncOnlineCount,
    string LteStatus,
    string LteOperator,
    string WorstLevel,
    DateTimeOffset? LastNotificationAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc);

internal sealed record PortalRoleRecord(
    string Name,
    string Focus,
    string DefaultView,
    string[] Capabilities);

internal sealed record RoadmapPhaseRecord(
    string Phase,
    string Title,
    string Status,
    string Summary);

internal sealed record PortalDeviceRecord(
    string Alias,
    string DisplayName,
    string Port,
    string GroupName,
    string Status,
    string CurrentLevel,
    string LastMessage,
    string LastSeen,
    int TotalEvents,
    int WarnCount,
    int AlarmCount,
    int HealthScore,
    int RiskScore,
    string Trend,
    string Recommendation,
    string? RuleName,
    string? EscalationSummary,
    bool ThresholdReached);

internal sealed record PortalAlertRecord(
    string Alias,
    string DisplayName,
    string Port,
    string GroupName,
    string Level,
    string Summary,
    string OccurredAt,
    string Action,
    string? RuleName,
    string? EscalationSummary,
    bool ThresholdReached);

internal sealed record PortalGroupRecord(
    string Name,
    int DeviceCount,
    int AlertCount,
    int AverageHealth,
    string WorstLevel,
    string[] Members,
    string Summary);

internal sealed record ActivityBucketRecord(
    string Label,
    int Count,
    string WorstLevel,
    int AlarmCount,
    int WarnCount);

internal sealed record HistoryEventRecord(
    string Alias,
    string DisplayName,
    string Port,
    string Level,
    string Message,
    string OccurredAt,
    string Raw,
    string? RuleName);

internal sealed record PredictionRecord(
    string Alias,
    string DisplayName,
    string Port,
    string RiskLabel,
    int Probability,
    string Horizon,
    string Summary,
    string Recommendation);

internal sealed record PortalPredictionAnalysisRecord(
    PortalPredictionModelRecommendationRecord RecommendedModel,
    PortalPredictionTargetRecord[] TargetLabels,
    PortalPredictionFeatureGroupRecord[] FeatureGroups,
    PortalPredictionNormalizationStepRecord[] NormalizationPipeline,
    PortalPredictionDatasetRecord Dataset,
    PredictionRecord[] Predictions);

internal sealed record PortalPredictionModelRecommendationRecord(
    string ModelName,
    string DeploymentMode,
    string ProblemType,
    string InferenceWindow,
    string Summary,
    string[] Reasons);

internal sealed record PortalPredictionTargetRecord(
    string Code,
    string Title,
    string Horizon,
    string Priority,
    string Summary);

internal sealed record PortalPredictionFeatureGroupRecord(
    string Name,
    string Purpose,
    string[] Features);

internal sealed record PortalPredictionNormalizationStepRecord(
    string Step,
    string Input,
    string Output,
    string Summary);

internal sealed record PortalPredictionDatasetRecord(
    int SnapshotCount,
    int PendingLabelCount,
    int ResolvedLabelCount,
    DateTimeOffset? LastCapturedAtUtc,
    PortalPredictionTargetCoverageRecord[] TargetCoverage,
    PortalPredictionSampleRecord[] RecentSamples);

internal sealed record PortalPredictionTrainingManifestRecord(
    string DatasetName,
    string[] FeatureColumns,
    string[] TargetColumns,
    string[] CategoricalColumns,
    string[] NumericColumns,
    string RecommendedPrimaryTarget,
    string[] Notes);

internal sealed record PortalPredictionTargetCoverageRecord(
    string Code,
    string Title,
    string Horizon,
    int PendingCount,
    int ResolvedCount,
    int PositiveCount,
    int NegativeCount);

internal sealed record PortalPredictionSampleRecord(
    string Alias,
    string DisplayName,
    string Port,
    DateTimeOffset CapturedAtUtc,
    string CurrentLevel,
    string Status,
    int RiskScore,
    int HealthScore,
    int AlertPressure);

internal sealed record PortalLteRecord(
    string SimSlot,
    string ModemName,
    string Status,
    string OperatorName,
    string NetworkType,
    string SimNumber,
    string Iccid,
    string Imsi,
    string Imei,
    string Apn,
    string CellId,
    string IpAddress,
    int SignalPercent,
    int SignalDbm,
    string SignalQuality,
    double DownloadMbps,
    double UploadMbps,
    DateTimeOffset SampledAtUtc,
    string RegistrationStatus,
    string Plmn,
    string MccMnc,
    bool Roaming,
    string PinState,
    string Smsc,
    string Tac,
    int RsrpDbm,
    int RsrqDb,
    int SinrDb,
    string DnsPrimary,
    string DnsSecondary,
    DateTimeOffset LastAttachAtUtc,
    string Summary);

internal sealed record PortalPncDeviceRecord(
    string DeviceCode,
    string Name,
    string Location,
    string Province,
    string City,
    string Hospital,
    string Site,
    bool IsOnline,
    string Status,
    string OperatorName,
    string NetworkType,
    string SimNumber,
    int SignalPercent,
    int SignalDbm,
    string SignalQuality,
    int Rs232Connected,
    int Rs485Connected,
    int CanConnected,
    int EthernetConnected,
    int DigitalInputs,
    int DigitalOutputs,
    string Firmware,
    string LastSeen,
    string MainboardStatus,
    int MainboardTempC,
    double SupplyVoltage,
    int HealthScore,
    string[] ConnectedDeviceTypes,
    string Summary);

internal sealed record PortalMainboardRecord(
    string DeviceCode,
    string Name,
    string Location,
    string Status,
    string BoardRevision,
    string BoardSerialNumber,
    string Firmware,
    int TemperatureC,
    double SupplyVoltage,
    int CpuLoadPercent,
    int MemoryPercent,
    int StoragePercent,
    bool WatchdogHealthy,
    string WatchdogState,
    int UptimeHours,
    string OperatorName,
    string NetworkType,
    string SimSlot,
    string SimNumber,
    int SignalPercent,
    string SignalQuality,
    string LastSeen,
    int ConfiguredConnectionCount,
    int DegradedConnectionCount,
    string PortSummary,
    string Summary);

internal sealed record PortalHdmiRecord(
    string ClientName,
    string CurrentVersion,
    int NotificationCount,
    int HeartbeatCount,
    DateTimeOffset? LastNotificationAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    PortalDeviceRecord[] Tiles,
    PortalAlertRecord[] Alerts,
    string Headline);

internal sealed record PortalDiagnosticsRecord(
    DateTimeOffset StartedAtUtc,
    int NotificationCount,
    int HeartbeatCount,
    DateTimeOffset? LastNotificationAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    PortalLevelTotalsRecord LevelTotals,
    string[] ActivePorts,
    string LteOperator,
    int PncCount,
    int MainboardCount,
    int RuleCount,
    int OtaCampaignCount,
    int ActiveRuleMatches,
    int DispatchCount,
    DeviceNotification[] LastNotifications,
    DeviceHeartbeat[] LastHeartbeats);

internal sealed record HardwareIntegrationStateRecord(
    DateTimeOffset GeneratedAtUtc,
    string CollectorStatus,
    string CollectorSummary,
    HardwareIntegrationSummaryRecord Summary,
    HardwareSelfCheckStateRecord SelfCheck,
    HardwarePersonalizationStateRecord Personalization,
    HardwarePortStatusRecord[] Ports,
    HardwareFixedModuleStatusRecord[] Modules,
    string[] Checklist);

internal sealed record HardwarePersonalizationStateRecord(
    string DetectedDeviceCode,
    string DetectedSimNumber,
    string DetectedSoftwareVersion,
    string DetectedBoardSerialNumber,
    HardwareLocationOptionRecord[] Locations,
    string[] VariantOptions);

internal sealed record HardwareLocationOptionRecord(
    string Province,
    string City,
    string Hospital,
    string Site);

internal sealed record HardwareIntegrationSummaryRecord(
    int ExpectedPortCount,
    int DetectedPortCount,
    int RxActiveCount,
    int TxActiveCount,
    int SimulationFallbackCount,
    int FixedModuleCount,
    int FixedModuleReadyCount);

internal sealed record HardwareSelfCheckStateRecord(
    int RequiredCount,
    int PassedRequiredCount,
    bool Completed,
    string Summary,
    string Recommendation,
    HardwareSelfCheckItemRecord[] Items);

internal sealed record HardwareSelfCheckItemRecord(
    string Key,
    string Title,
    string IconLabel,
    string StatusLabel,
    string Tone,
    bool Passed,
    bool Required,
    int? ProgressPercent,
    string? ProgressLabel,
    string Summary,
    string Recommendation);

public sealed record HardwarePortStatusRecord(
    string PortId,
    string Alias,
    string InterfaceType,
    string ExpectedPath,
    string ConnectionState,
    string ParserKind,
    string FrameMode,
    string Purpose,
    bool Detected,
    bool CablePresent,
    bool? LinkUp,
    bool? RxActive,
    bool? TxActive,
    bool SimulationFallback,
    string Mode,
    string Status,
    DateTimeOffset? StateSinceUtc,
    DateTimeOffset? LastRxAtUtc,
    DateTimeOffset? LastTxAtUtc,
    long? RxCounter,
    long? TxCounter,
    string LastSignalAt,
    string Summary,
    string Recommendation,
    string? LastRaw,
    string? LastText);

public sealed record HardwarePortSignalTestResultRecord(
    string PortId,
    bool Success,
    string Tone,
    string Summary,
    string[] Observations,
    string Recommendation);

internal sealed record HardwareFixedModuleStatusRecord(
    string ModuleId,
    string DisplayName,
    bool Present,
    string Status,
    string LastSignalAt,
    string Summary,
    string Recommendation);

public sealed record CollectorHardwareConfigStateRecord(
    DateTimeOffset LoadedAtUtc,
    string ConfigFilePath,
    string RestartHint,
    CollectorPortConfigRecord[] Ports);

public sealed record CollectorPortConfigRecord(
    string PortId,
    string Alias,
    string InterfaceType,
    string DevicePath,
    string? NetworkInterfaceName,
    int BaudRate,
    int DataBits,
    string Parity,
    string StopBits,
    string ParserKind,
    string FrameMode,
    bool Enabled,
    bool AllowSimulationFallback,
    string Description);

internal sealed record PortalLevelTotalsRecord(
    int Info,
    int Warn,
    int Alarm,
    int Error);

internal sealed record PortalRulebookConfig(
    PortalUserRecord[] Users,
    PortalMessageRuleRecord[] Rules);

public sealed record PortalUserRecord(
    string Id,
    string DisplayName,
    string Role,
    string Email,
    string Phone);

internal sealed record PortalMessageRuleRecord(
    string Id,
    string Name,
    string MatchText,
    string MessageType,
    string Description,
    double ThresholdHours,
    bool SendSms,
    bool SendEmail,
    string[] RecipientIds,
    bool Enabled);

internal sealed record PortalRulebookStateRecord(
    PortalRulebookSummaryRecord Summary,
    PortalUserRecord[] Users,
    PortalMessageRuleRecord[] Rules,
    RuleActivationViewRecord[] ActiveMatches,
    EscalationDispatchViewRecord[] Dispatches);

internal sealed record PortalRulebookSummaryRecord(
    int UserCount,
    int RuleCount,
    int EnabledRuleCount,
    int ActiveMatchCount,
    int EscalatedMatchCount,
    int DispatchCount);

internal sealed record RuleActivationViewRecord(
    string RuleId,
    string RuleName,
    string MatchText,
    string MessageType,
    string Port,
    string Alias,
    string LastMessage,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset DueAtUtc,
    double ThresholdHours,
    double ElapsedHours,
    bool ThresholdReached,
    bool Dispatched,
    string[] Channels,
    string[] Recipients);

internal sealed record EscalationDispatchViewRecord(
    string Id,
    string RuleId,
    string RuleName,
    string Port,
    string Alias,
    string Channel,
    string RecipientName,
    string RecipientAddress,
    DateTimeOffset TriggeredAtUtc,
    string Message);
