internal sealed partial class BlazorPortalService
{
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
