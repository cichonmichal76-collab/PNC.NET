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

    public PortalRulebookStateRecord GetRulebookState() =>
        simulationStore.RulebookState();

    public async Task<BlazorMutationResult> SaveRuleAsync(
        BlazorRuleInputRecord input,
        CancellationToken cancellationToken)
    {
        var state = simulationStore.RulebookState();
        var trimmedName = input.Name.Trim();
        var trimmedMatchText = input.MatchText.Trim();
        var currentRuleId = string.IsNullOrWhiteSpace(input.RuleId) ? null : input.RuleId.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return BlazorMutationResult.Fail("Podaj nazwe biznesowa reguly.");
        }

        if (string.IsNullOrWhiteSpace(trimmedMatchText))
        {
            return BlazorMutationResult.Fail("Podaj wzorzec surowego komunikatu.");
        }

        var nextRule = new PortalMessageRuleRecord(
            Id: currentRuleId ?? string.Empty,
            Name: trimmedName,
            MatchText: trimmedMatchText,
            MessageType: NormalizeRuleMessageType(input.MessageType),
            Description: input.Description?.Trim() ?? string.Empty,
            ThresholdHours: Math.Round(Math.Clamp(input.ThresholdHours, 0, 720), 2),
            SendSms: input.SendSms,
            SendEmail: input.SendEmail,
            RecipientIds: NormalizeRecipientIds(input.RecipientIds, state.Users),
            Enabled: input.Enabled);

        var rules = BlazorPortalMutationHelpers.UpsertById(
            state.Rules,
            nextRule,
            rule => rule.Id,
            insertAtFront: true);

        await simulationStore.UpdateRulebookAsync(
            new PortalRulebookConfig(state.Users, rules),
            cancellationToken);

        return BlazorMutationResult.Ok($"Regula {nextRule.Name} zostala zapisana.");
    }

    public async Task<BlazorMutationResult> DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        var state = simulationStore.RulebookState();
        var removedRule = state.Rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        var rules = BlazorPortalMutationHelpers.RemoveById(
            state.Rules,
            ruleId,
            rule => rule.Id);

        await simulationStore.UpdateRulebookAsync(
            new PortalRulebookConfig(state.Users, rules),
            cancellationToken);

        return BlazorMutationResult.Ok(removedRule is null
            ? "Regula zostala usunieta."
            : $"Regula {removedRule.Name} zostala usunieta.");
    }

    public async Task<BlazorMutationResult> SaveUserAsync(
        BlazorUserInputRecord input,
        CancellationToken cancellationToken)
    {
        var state = simulationStore.RulebookState();
        var trimmedDisplayName = input.DisplayName.Trim();
        var currentUserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId.Trim();

        if (string.IsNullOrWhiteSpace(trimmedDisplayName))
        {
            return BlazorMutationResult.Fail("Podaj nazwe uzytkownika systemu.");
        }

        var nextUser = new PortalUserRecord(
            Id: currentUserId ?? string.Empty,
            DisplayName: trimmedDisplayName,
            Role: string.IsNullOrWhiteSpace(input.Role) ? "Operator" : input.Role.Trim(),
            Email: input.Email?.Trim() ?? string.Empty,
            Phone: input.Phone?.Trim() ?? string.Empty);

        var users = BlazorPortalMutationHelpers.UpsertById(
            state.Users,
            nextUser,
            user => user.Id,
            insertAtFront: true);

        await simulationStore.UpdateRulebookAsync(
            new PortalRulebookConfig(users, state.Rules),
            cancellationToken);

        return BlazorMutationResult.Ok($"Uzytkownik {nextUser.DisplayName} zostal zapisany.");
    }

    public async Task<BlazorMutationResult> DeleteUserAsync(string userId, CancellationToken cancellationToken)
    {
        var state = simulationStore.RulebookState();
        var removedUser = state.Users.FirstOrDefault(user => string.Equals(user.Id, userId, StringComparison.OrdinalIgnoreCase));
        var users = BlazorPortalMutationHelpers.RemoveById(
            state.Users,
            userId,
            user => user.Id);
        var rules = state.Rules
            .Select(rule => rule with
            {
                RecipientIds = rule.RecipientIds
                    .Where(recipientId => !string.Equals(recipientId, userId, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            })
            .ToArray();

        await simulationStore.UpdateRulebookAsync(
            new PortalRulebookConfig(users, rules),
            cancellationToken);

        return BlazorMutationResult.Ok(removedUser is null
            ? "Uzytkownik zostal usuniety."
            : $"Uzytkownik {removedUser.DisplayName} zostal usuniety.");
    }

    public async Task<BlazorOtaEditorState> GetOtaEditorStateAsync(CancellationToken cancellationToken)
    {
        var fleetState = simulationStore.FleetState();
        var otaState = await simulationStore.OtaStateAsync(cancellationToken);

        return new BlazorOtaEditorState(fleetState, otaState);
    }

    public async Task<BlazorMutationResult> SavePackageAsync(
        BlazorOtaPackageInputRecord input,
        CancellationToken cancellationToken)
    {
        var config = simulationStore.OtaConfig();
        var trimmedName = input.Name.Trim();
        var trimmedVersion = input.Version.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName) || string.IsNullOrWhiteSpace(trimmedVersion))
        {
            return BlazorMutationResult.Fail("Podaj nazwe i wersje pakietu OTA.");
        }

        var generatedId = CreateId("pkg", $"{trimmedName}-{trimmedVersion}");
        var nextPackage = new PortalOtaPackageRecord(
            Id: string.IsNullOrWhiteSpace(input.PackageId) ? generatedId : input.PackageId.Trim(),
            Name: trimmedName,
            Version: trimmedVersion,
            Target: string.IsNullOrWhiteSpace(input.Target) ? "PNC OS" : input.Target.Trim(),
            FileName: string.IsNullOrWhiteSpace(input.FileName) ? $"{generatedId}.bin" : input.FileName.Trim(),
            SizeMb: Math.Round(Math.Clamp(input.SizeMb, 1, 4096), 1),
            Description: input.Description?.Trim() ?? string.Empty,
            ReleaseNotes: input.ReleaseNotes?.Trim() ?? string.Empty,
            Mandatory: input.Mandatory);

        var packages = BlazorPortalMutationHelpers.UpsertById(
            config.Packages,
            nextPackage,
            package => package.Id,
            insertAtFront: true);

        await simulationStore.UpdateOtaAsync(
            config with { Packages = packages },
            cancellationToken);

        return BlazorMutationResult.Ok($"Pakiet {nextPackage.Name} {nextPackage.Version} zostal zapisany.");
    }

    public async Task<BlazorMutationResult> DeletePackageAsync(string packageId, CancellationToken cancellationToken)
    {
        var config = simulationStore.OtaConfig();
        if (config.Campaigns.Any(campaign => string.Equals(campaign.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
        {
            return BlazorMutationResult.Fail("Nie mozna usunac pakietu przypisanego do kampanii OTA.");
        }

        var packages = BlazorPortalMutationHelpers.RemoveById(
            config.Packages,
            packageId,
            package => package.Id);

        await simulationStore.UpdateOtaAsync(
            config with { Packages = packages },
            cancellationToken);

        return BlazorMutationResult.Ok("Pakiet OTA zostal usuniety.");
    }

    public async Task<BlazorMutationResult> SaveCampaignAsync(
        BlazorOtaCampaignInputRecord input,
        CancellationToken cancellationToken)
    {
        var config = simulationStore.OtaConfig();
        var trimmedTitle = input.Title.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return BlazorMutationResult.Fail("Podaj nazwe kampanii OTA.");
        }

        if (string.IsNullOrWhiteSpace(input.PackageId))
        {
            return BlazorMutationResult.Fail("Wybierz pakiet OTA.");
        }

        var selectedTargets = (input.TargetDeviceCodes ?? [])
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Select(static code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedTargets.Length == 0)
        {
            return BlazorMutationResult.Fail("Wybierz co najmniej jeden wezel PNC dla kampanii.");
        }

        var scheduledUtc = new DateTimeOffset(DateTime.SpecifyKind(input.ScheduledLocal, DateTimeKind.Local)).ToUniversalTime();
        var existing = config.Campaigns.FirstOrDefault(campaign => string.Equals(campaign.Id, input.CampaignId, StringComparison.OrdinalIgnoreCase));
        var nextCampaign = new PortalOtaCampaignRecord(
            Id: string.IsNullOrWhiteSpace(input.CampaignId)
                ? CreateId("campaign", $"{trimmedTitle}-{input.PackageId}")
                : input.CampaignId.Trim(),
            Title: trimmedTitle,
            PackageId: input.PackageId.Trim(),
            TargetDeviceCodes: selectedTargets,
            ScheduledForUtc: scheduledUtc,
            Transport: string.IsNullOrWhiteSpace(input.Transport) ? "LTE" : input.Transport.Trim(),
            Window: string.IsNullOrWhiteSpace(input.Window) ? "okno serwisowe 00:00-04:00" : input.Window.Trim(),
            RetryLimit: Math.Clamp(input.RetryLimit, 0, 10),
            NotifyServiceByEmail: input.NotifyServiceByEmail,
            RecipientIds: (input.RecipientIds ?? [])
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Status: existing?.Status == "completed" || existing?.Status == "partial" || existing?.Status == "failed"
                ? existing.Status
                : "scheduled",
            Notes: input.Notes?.Trim() ?? string.Empty,
            CreatedAtUtc: existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            StartedAtUtc: existing?.StartedAtUtc,
            CompletedAtUtc: existing?.CompletedAtUtc);

        var campaigns = BlazorPortalMutationHelpers.UpsertById(
            config.Campaigns,
            nextCampaign,
            campaign => campaign.Id,
            insertAtFront: true);

        await simulationStore.UpdateOtaAsync(
            config with { Campaigns = campaigns },
            cancellationToken);

        return BlazorMutationResult.Ok($"Kampania {nextCampaign.Title} zostala zapisana.");
    }

    public async Task<BlazorMutationResult> DeleteCampaignAsync(string campaignId, CancellationToken cancellationToken)
    {
        var config = simulationStore.OtaConfig();
        var campaigns = BlazorPortalMutationHelpers.RemoveById(
            config.Campaigns,
            campaignId,
            campaign => campaign.Id);
        var logs = BlazorPortalMutationHelpers.RemoveById(
            config.Logs,
            campaignId,
            log => log.CampaignId);
        var emailLogs = BlazorPortalMutationHelpers.RemoveById(
            config.EmailLogs,
            campaignId,
            email => email.CampaignId);

        await simulationStore.UpdateOtaAsync(
            config with
            {
                Campaigns = campaigns,
                Logs = logs,
                EmailLogs = emailLogs
            },
            cancellationToken);

        return BlazorMutationResult.Ok("Kampania OTA zostala usunieta.");
    }

    public PortalFleetConfig GetFleetState() =>
        simulationStore.FleetState();

    public async Task<BlazorMutationResult> SavePncAsync(
        BlazorPncInputRecord input,
        CancellationToken cancellationToken)
    {
        var fleet = simulationStore.FleetState();
        var trimmedName = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return BlazorMutationResult.Fail("Podaj nazwe wezla PNC.");
        }

        var normalizedCode = string.IsNullOrWhiteSpace(input.DeviceCode)
            ? CreateDeviceCode(trimmedName)
            : input.DeviceCode.Trim().ToUpperInvariant();
        var normalizedOriginalCode = string.IsNullOrWhiteSpace(input.OriginalDeviceCode)
            ? null
            : input.OriginalDeviceCode.Trim().ToUpperInvariant();

        var existing = fleet.PncDevices.FirstOrDefault(device =>
            string.Equals(device.DeviceCode, normalizedOriginalCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.DeviceCode, normalizedCode, StringComparison.OrdinalIgnoreCase));

        var nextDevice = BuildDevice(
            existing,
            normalizedCode,
            trimmedName,
            input.Location,
            input.OperatorName,
            input.NetworkType,
            input.SimNumber,
            input.SimSlot,
            input.SignalPercent,
            input.SignalDbm,
            input.Firmware,
            input.MainboardStatus,
            input.MainboardTempC,
            input.SupplyVoltage,
            input.BoardRevision,
            input.BoardSerialNumber,
            input.CpuLoadPercent,
            input.MemoryPercent,
            input.StoragePercent,
            input.UptimeHours,
            input.Online,
            input.WatchdogHealthy,
            input.Notes);

        var devices = fleet.PncDevices
            .Where(device =>
                !string.Equals(device.DeviceCode, normalizedOriginalCode, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(device.DeviceCode, normalizedCode, StringComparison.OrdinalIgnoreCase))
            .Append(nextDevice)
            .OrderBy(device => device.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await simulationStore.UpdateFleetAsync(
            new PortalFleetConfig(fleet.Lte, devices),
            cancellationToken);

        return BlazorMutationResult.Ok($"PNC {nextDevice.DeviceCode} zostal zapisany.");
    }

    public async Task<BlazorMutationResult> DeletePncAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var fleet = simulationStore.FleetState();
        var removed = fleet.PncDevices.FirstOrDefault(device => string.Equals(device.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase));
        var devices = BlazorPortalMutationHelpers.RemoveById(
            fleet.PncDevices,
            deviceCode,
            device => device.DeviceCode,
            items => items.OrderBy(device => device.DeviceCode, StringComparer.OrdinalIgnoreCase));

        await simulationStore.UpdateFleetAsync(
            new PortalFleetConfig(fleet.Lte, devices),
            cancellationToken);

        return BlazorMutationResult.Ok(removed is null
            ? "Wezel PNC zostal usuniety."
            : $"PNC {removed.DeviceCode} zostal usuniety.");
    }

    public async Task<BlazorMutationResult> SaveConnectionAsync(
        BlazorPncConnectionInputRecord input,
        CancellationToken cancellationToken)
    {
        var fleet = simulationStore.FleetState();
        var owner = fleet.PncDevices.FirstOrDefault(device => string.Equals(device.DeviceCode, input.OwnerDeviceCode, StringComparison.OrdinalIgnoreCase));
        if (owner is null)
        {
            return BlazorMutationResult.Fail("Najpierw wybierz poprawny wezel PNC.");
        }

        var trimmedDeviceName = input.DeviceName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDeviceName))
        {
            return BlazorMutationResult.Fail("Podaj nazwe urzadzenia podlaczonego do portu.");
        }

        var normalizedInterface = NormalizeInterfaceType(input.InterfaceType);
        var normalizedPortName = string.IsNullOrWhiteSpace(input.PortName)
            ? DefaultPortName(normalizedInterface, owner.Connections.Length)
            : input.PortName.Trim();
        var normalizedOriginalConnectionId = string.IsNullOrWhiteSpace(input.OriginalConnectionId)
            ? null
            : input.OriginalConnectionId.Trim().ToLowerInvariant();
        var nextConnectionId = string.IsNullOrWhiteSpace(normalizedOriginalConnectionId)
            ? CreateUniqueConnectionId(owner, normalizedInterface, normalizedPortName, trimmedDeviceName)
            : normalizedOriginalConnectionId;

        var nextConnection = new PncExternalConnectionConfigRecord(
            Id: nextConnectionId,
            InterfaceType: normalizedInterface,
            PortName: normalizedPortName,
            DeviceName: trimmedDeviceName,
            Protocol: string.IsNullOrWhiteSpace(input.Protocol) ? DefaultProtocol(normalizedInterface) : input.Protocol.Trim(),
            Status: NormalizeConnectionStatus(input.Status),
            Notes: input.Notes?.Trim() ?? string.Empty,
            BaudRate: normalizedInterface is "rs232" or "rs485"
                ? Math.Max(input.BaudRate ?? 9600, 1200)
                : null);

        var devices = fleet.PncDevices
            .Select(device =>
            {
                if (!string.Equals(device.DeviceCode, owner.DeviceCode, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }

                var connections = device.Connections
                    .Where(connection => !string.Equals(connection.Id, normalizedOriginalConnectionId, StringComparison.OrdinalIgnoreCase) &&
                                         !string.Equals(connection.Id, nextConnection.Id, StringComparison.OrdinalIgnoreCase))
                    .Append(nextConnection);

                return BlazorPortalMutationHelpers.WithConnections(device, connections);
            })
            .ToArray();

        await simulationStore.UpdateFleetAsync(
            new PortalFleetConfig(fleet.Lte, devices),
            cancellationToken);

        return BlazorMutationResult.Ok($"Polaczenie {nextConnection.PortName} zostalo zapisane.");
    }

    public async Task<BlazorMutationResult> DeleteConnectionAsync(
        string ownerDeviceCode,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var fleet = simulationStore.FleetState();
        var devices = fleet.PncDevices
            .Select(device =>
            {
                if (!string.Equals(device.DeviceCode, ownerDeviceCode, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }

                var connections = device.Connections
                    .Where(connection => !string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase));

                return BlazorPortalMutationHelpers.WithConnections(device, connections);
            })
            .ToArray();

        await simulationStore.UpdateFleetAsync(
            new PortalFleetConfig(fleet.Lte, devices),
            cancellationToken);

        return BlazorMutationResult.Ok("Polaczenie zostalo usuniete.");
    }

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

    private static PncDeviceConfigRecord BuildDevice(
        PncDeviceConfigRecord? existing,
        string normalizedCode,
        string trimmedName,
        string? location,
        string? operatorName,
        string? networkType,
        string? simNumber,
        string? simSlot,
        int signalPercent,
        int signalDbm,
        string? firmware,
        string? mainboardStatus,
        int mainboardTempC,
        double supplyVoltage,
        string? boardRevision,
        string? boardSerialNumber,
        int cpuLoadPercent,
        int memoryPercent,
        int storagePercent,
        int uptimeHours,
        bool online,
        bool watchdogHealthy,
        string? notes)
    {
        var connections = existing?.Connections ?? [];

        return new PncDeviceConfigRecord(
            DeviceCode: normalizedCode,
            Name: trimmedName,
            Location: string.IsNullOrWhiteSpace(location) ? "Nieokreslona lokalizacja" : location.Trim(),
            OperatorName: string.IsNullOrWhiteSpace(operatorName) ? "Orange PL" : operatorName.Trim(),
            NetworkType: string.IsNullOrWhiteSpace(networkType) ? "LTE" : networkType.Trim(),
            SimNumber: simNumber?.Trim() ?? string.Empty,
            SimSlot: string.IsNullOrWhiteSpace(simSlot) ? "SIM1" : simSlot.Trim(),
            BaseSignalPercent: Math.Clamp(signalPercent, 0, 100),
            BaseSignalDbm: Math.Clamp(signalDbm, -120, -30),
            Rs232Connected: connections.Count(connection => connection.InterfaceType == "rs232"),
            Rs485Connected: connections.Count(connection => connection.InterfaceType == "rs485"),
            CanConnected: connections.Count(connection => connection.InterfaceType == "can"),
            EthernetConnected: connections.Count(connection => connection.InterfaceType == "ethernet"),
            DigitalInputs: connections.Count(connection => connection.InterfaceType == "digital-input"),
            DigitalOutputs: connections.Count(connection => connection.InterfaceType == "digital-output"),
            Firmware: string.IsNullOrWhiteSpace(firmware) ? "PNC-OS 2.4.1" : firmware.Trim(),
            MainboardStatus: string.IsNullOrWhiteSpace(mainboardStatus) ? "stabilna" : mainboardStatus.Trim(),
            MainboardTempC: Math.Clamp(mainboardTempC, 20, 95),
            SupplyVoltage: Math.Round(Math.Clamp(supplyVoltage, 5, 60), 2),
            Online: online,
            BoardRevision: string.IsNullOrWhiteSpace(boardRevision) ? "MB-1.0" : boardRevision.Trim(),
            BoardSerialNumber: string.IsNullOrWhiteSpace(boardSerialNumber) ? $"{normalizedCode}-MB-001" : boardSerialNumber.Trim().ToUpperInvariant(),
            BaseCpuLoadPercent: Math.Clamp(cpuLoadPercent, 0, 100),
            BaseMemoryPercent: Math.Clamp(memoryPercent, 0, 100),
            BaseStoragePercent: Math.Clamp(storagePercent, 0, 100),
            WatchdogHealthy: watchdogHealthy,
            UptimeHours: Math.Max(1, uptimeHours),
            Notes: notes?.Trim() ?? string.Empty,
            Connections: connections);
    }

    private static string CreateDeviceCode(string name)
    {
        var compact = new string(name
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(6)
            .ToArray());

        return string.IsNullOrWhiteSpace(compact)
            ? $"PNC-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            : $"PNC-{compact}";
    }

    private static string NormalizeInterfaceType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "rs232" => "rs232",
            "rs485" => "rs485",
            "can" => "can",
            "ethernet" => "ethernet",
            "digital-input" => "digital-input",
            "digital-output" => "digital-output",
            _ => "rs232"
        };

    private static string NormalizeConnectionStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "attention" => "attention",
            _ => "online"
        };

    private static string DefaultPortName(string interfaceType, int index) =>
        interfaceType switch
        {
            "rs232" => $"COM{index + 1}",
            "rs485" => "RS485",
            "can" => $"CAN-{index + 1}",
            "ethernet" => $"LAN{index + 1}",
            "digital-input" => $"DI{index + 1}",
            "digital-output" => $"DO{index + 1}",
            _ => $"PORT-{index + 1}"
        };

    private static string DefaultProtocol(string interfaceType) =>
        interfaceType switch
        {
            "rs232" => "MODBUS RTU",
            "rs485" => "MODBUS RTU",
            "can" => "CANopen",
            "ethernet" => "TCP/IP",
            "digital-input" => "GPIO",
            "digital-output" => "GPIO",
            _ => "Serial"
        };

    private static string CreateUniqueConnectionId(
        PncDeviceConfigRecord owner,
        string interfaceType,
        string portName,
        string deviceName)
    {
        var seed = new string($"{portName}-{deviceName}"
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        seed = string.Join("-", seed.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var baseId = $"{owner.DeviceCode.ToLowerInvariant()}-{interfaceType}-{(string.IsNullOrWhiteSpace(seed) ? "port" : seed)}";
        var candidate = baseId;
        var suffix = 2;
        var used = owner.Connections.Select(connection => connection.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (!used.Add(candidate))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }

        return candidate;
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
