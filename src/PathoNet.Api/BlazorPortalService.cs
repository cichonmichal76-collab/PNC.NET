internal sealed partial class BlazorPortalService(
    SimulationStore simulationStore,
    ServiceHealthStore serviceHealthStore,
    HardwareIntegrationStateService hardwareIntegrationStateService,
    HardwareSignalTestService hardwareSignalTestService,
    HardwareCollectorConfigService hardwareCollectorConfigService)
{
    public async Task<BlazorServiceDashboardState> GetServiceDashboardAsync(CancellationToken cancellationToken)
    {
        var portalState = simulationStore.PortalState();
        var rulebookState = simulationStore.RulebookState();
        var otaState = await simulationStore.OtaStateAsync(cancellationToken);
        var serviceHealth = serviceHealthStore.GetState();

        return new BlazorServiceDashboardState(
            PortalState: portalState,
            RulebookState: rulebookState,
            OtaState: otaState,
            ServiceHealth: serviceHealth);
    }

    public PortalServiceHealthStateRecord GetServiceHealthState() =>
        serviceHealthStore.GetState();

    public Task<PortalServiceRestartRequestResultRecord> RequestServiceRestartAsync(
        string serviceName,
        string requestedBy,
        CancellationToken cancellationToken) =>
        serviceHealthStore.RequestRestartAsync(serviceName, requestedBy, cancellationToken);

    private static string NormalizeRuleMessageType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "alarm" => "alarm",
            "warn" => "warn",
            "error" => "error",
            "info" => "info",
            _ => "any"
        };

    private static string[] NormalizeRecipientIds(string[]? recipientIds, PortalUserRecord[] users)
        => BlazorPortalMutationHelpers.NormalizeKnownIds(
            recipientIds,
            users.Select(user => user.Id));

    private static string CreateId(string prefix, string seed)
    {
        var normalized = new string(seed
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        normalized = string.Join("-", normalized
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(normalized)
            ? $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            : $"{prefix}-{normalized}";
    }
}

internal sealed record BlazorServiceDashboardState(
    PortalStateRecord PortalState,
    PortalRulebookStateRecord RulebookState,
    PortalOtaStateRecord OtaState,
    PortalServiceHealthStateRecord ServiceHealth);

internal sealed record BlazorOtaEditorState(
    PortalFleetConfig FleetState,
    PortalOtaStateRecord OtaState);

internal sealed record BlazorMutationResult(
    bool Success,
    string Message)
{
    public static BlazorMutationResult Ok(string message) => new(true, message);
    public static BlazorMutationResult Fail(string message) => new(false, message);
}

internal sealed record BlazorRuleInputRecord(
    string? RuleId,
    string Name,
    string MatchText,
    string? MessageType,
    string? Description,
    double ThresholdHours,
    bool SendSms,
    bool SendEmail,
    bool Enabled,
    string[] RecipientIds);

internal sealed record BlazorUserInputRecord(
    string? UserId,
    string DisplayName,
    string? Role,
    string? Email,
    string? Phone);

internal sealed record BlazorOtaPackageInputRecord(
    string? PackageId,
    string Name,
    string Version,
    string? Target,
    string? FileName,
    double SizeMb,
    string? Description,
    string? ReleaseNotes,
    bool Mandatory);

internal sealed record BlazorOtaCampaignInputRecord(
    string? CampaignId,
    string Title,
    string PackageId,
    DateTime ScheduledLocal,
    string? Transport,
    string? Window,
    int RetryLimit,
    bool NotifyServiceByEmail,
    string? Notes,
    string[] TargetDeviceCodes,
    string[] RecipientIds);

internal sealed record BlazorPncInputRecord(
    string? OriginalDeviceCode,
    string? DeviceCode,
    string Name,
    string? Location,
    string? OperatorName,
    string? NetworkType,
    string? SimNumber,
    string? SimSlot,
    int SignalPercent,
    int SignalDbm,
    string? Firmware,
    string? MainboardStatus,
    int MainboardTempC,
    double SupplyVoltage,
    string? BoardRevision,
    string? BoardSerialNumber,
    int CpuLoadPercent,
    int MemoryPercent,
    int StoragePercent,
    int UptimeHours,
    bool Online,
    bool WatchdogHealthy,
    string? Notes);

internal sealed record BlazorPncConnectionInputRecord(
    string OwnerDeviceCode,
    string? OriginalConnectionId,
    string? InterfaceType,
    string? PortName,
    string DeviceName,
    string? Protocol,
    string? Status,
    string? Notes,
    int? BaudRate);

internal sealed record BlazorCollectorPortInputRecord(
    string PortId,
    string Alias,
    string InterfaceType,
    string DevicePath,
    string? NetworkInterfaceName,
    int BaudRate,
    int DataBits,
    string? Parity,
    string? StopBits,
    string? ParserKind,
    string? FrameMode,
    bool Enabled,
    bool AllowSimulationFallback,
    string? Description);
