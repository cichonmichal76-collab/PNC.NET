using System.Text.Json;

internal sealed class FleetMockStore
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PortalFleetConfig _current;

    public FleetMockStore(string contentRoot)
    {
        var dataDirectory = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "portal-fleet.json");
        _current = LoadOrSeed();
    }

    public PortalFleetConfig GetConfig() => _current;

    public async Task<PortalFleetConfig> SaveAsync(
        PortalFleetConfig candidate,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = Normalize(candidate);
            var json = JsonSerializer.Serialize(normalized, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
            _current = normalized;
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private PortalFleetConfig LoadOrSeed()
    {
        if (!File.Exists(_filePath))
        {
            var seeded = Normalize(DefaultFleet());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(seeded, _jsonOptions));
            return seeded;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<PortalFleetConfig>(json, _jsonOptions);
            return Normalize(loaded ?? DefaultFleet());
        }
        catch (JsonException)
        {
            var fallback = Normalize(DefaultFleet());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(fallback, _jsonOptions));
            return fallback;
        }
    }

    private static PortalFleetConfig Normalize(PortalFleetConfig candidate)
    {
        var fallback = DefaultFleet();
        var lteSource = candidate.Lte ?? fallback.Lte;
        var deviceSources = candidate.PncDevices is { Length: > 0 } ? candidate.PncDevices : fallback.PncDevices;

        var lte = new LteModemConfigRecord(
            SimSlot: string.IsNullOrWhiteSpace(lteSource.SimSlot) ? fallback.Lte.SimSlot : lteSource.SimSlot.Trim(),
            ModemName: string.IsNullOrWhiteSpace(lteSource.ModemName) ? fallback.Lte.ModemName : lteSource.ModemName.Trim(),
            OperatorName: string.IsNullOrWhiteSpace(lteSource.OperatorName) ? fallback.Lte.OperatorName : lteSource.OperatorName.Trim(),
            NetworkType: string.IsNullOrWhiteSpace(lteSource.NetworkType) ? fallback.Lte.NetworkType : lteSource.NetworkType.Trim(),
            SimNumber: string.IsNullOrWhiteSpace(lteSource.SimNumber) ? fallback.Lte.SimNumber : lteSource.SimNumber.Trim(),
            Iccid: string.IsNullOrWhiteSpace(lteSource.Iccid) ? fallback.Lte.Iccid : lteSource.Iccid.Trim(),
            Imsi: string.IsNullOrWhiteSpace(lteSource.Imsi) ? fallback.Lte.Imsi : lteSource.Imsi.Trim(),
            Imei: string.IsNullOrWhiteSpace(lteSource.Imei) ? fallback.Lte.Imei : lteSource.Imei.Trim(),
            Apn: string.IsNullOrWhiteSpace(lteSource.Apn) ? fallback.Lte.Apn : lteSource.Apn.Trim(),
            CellId: string.IsNullOrWhiteSpace(lteSource.CellId) ? fallback.Lte.CellId : lteSource.CellId.Trim(),
            IpAddress: string.IsNullOrWhiteSpace(lteSource.IpAddress) ? fallback.Lte.IpAddress : lteSource.IpAddress.Trim(),
            BaseSignalPercent: Math.Clamp(lteSource.BaseSignalPercent, 12, 100),
            BaseSignalDbm: lteSource.BaseSignalDbm == 0 ? fallback.Lte.BaseSignalDbm : lteSource.BaseSignalDbm,
            BaseDownloadMbps: Math.Round(Math.Max(1, lteSource.BaseDownloadMbps), 1),
            BaseUploadMbps: Math.Round(Math.Max(1, lteSource.BaseUploadMbps), 1),
            RegistrationStatus: string.IsNullOrWhiteSpace(lteSource.RegistrationStatus) ? fallback.Lte.RegistrationStatus : lteSource.RegistrationStatus.Trim(),
            Plmn: string.IsNullOrWhiteSpace(lteSource.Plmn) ? fallback.Lte.Plmn : lteSource.Plmn.Trim(),
            MccMnc: string.IsNullOrWhiteSpace(lteSource.MccMnc) ? fallback.Lte.MccMnc : lteSource.MccMnc.Trim(),
            Roaming: lteSource.Roaming,
            PinState: string.IsNullOrWhiteSpace(lteSource.PinState) ? fallback.Lte.PinState : lteSource.PinState.Trim(),
            Smsc: string.IsNullOrWhiteSpace(lteSource.Smsc) ? fallback.Lte.Smsc : lteSource.Smsc.Trim(),
            Tac: string.IsNullOrWhiteSpace(lteSource.Tac) ? fallback.Lte.Tac : lteSource.Tac.Trim(),
            BaseRsrpDbm: lteSource.BaseRsrpDbm == 0 ? fallback.Lte.BaseRsrpDbm : lteSource.BaseRsrpDbm,
            BaseRsrqDb: lteSource.BaseRsrqDb == 0 ? fallback.Lte.BaseRsrqDb : lteSource.BaseRsrqDb,
            BaseSinrDb: lteSource.BaseSinrDb == 0 ? fallback.Lte.BaseSinrDb : lteSource.BaseSinrDb,
            DnsPrimary: string.IsNullOrWhiteSpace(lteSource.DnsPrimary) ? fallback.Lte.DnsPrimary : lteSource.DnsPrimary.Trim(),
            DnsSecondary: string.IsNullOrWhiteSpace(lteSource.DnsSecondary) ? fallback.Lte.DnsSecondary : lteSource.DnsSecondary.Trim(),
            LastAttachAtUtc: lteSource.LastAttachAtUtc == default ? fallback.Lte.LastAttachAtUtc : lteSource.LastAttachAtUtc,
            Notes: string.IsNullOrWhiteSpace(lteSource.Notes) ? fallback.Lte.Notes : lteSource.Notes.Trim());

        var devices = deviceSources
            .Select(NormalizeDevice)
            .OrderBy(static device => device.DeviceCode, StringComparer.Ordinal)
            .ToArray();

        return new PortalFleetConfig(
            Lte: lte,
            PncDevices: devices);
    }

    private static PncDeviceConfigRecord NormalizeDevice(PncDeviceConfigRecord source)
    {
        var fallbackCode = string.IsNullOrWhiteSpace(source.DeviceCode) ? $"PNC-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" : source.DeviceCode.Trim().ToUpperInvariant();
        var connections = (source.Connections ?? [])
            .Select((connection, index) => NormalizeConnection(connection, fallbackCode, index))
            .ToArray();

        return new PncDeviceConfigRecord(
            DeviceCode: fallbackCode,
            Name: string.IsNullOrWhiteSpace(source.Name) ? fallbackCode : source.Name.Trim(),
            Location: NormalizeStructuredLocation(fallbackCode, source.Location),
            OperatorName: string.IsNullOrWhiteSpace(source.OperatorName) ? "Orange PL" : source.OperatorName.Trim(),
            NetworkType: string.IsNullOrWhiteSpace(source.NetworkType) ? "LTE" : source.NetworkType.Trim(),
            SimNumber: string.IsNullOrWhiteSpace(source.SimNumber) ? "+48 500 000 000" : source.SimNumber.Trim(),
            SimSlot: string.IsNullOrWhiteSpace(source.SimSlot) ? "SIM1" : source.SimSlot.Trim(),
            BaseSignalPercent: Math.Clamp(source.BaseSignalPercent, 8, 100),
            BaseSignalDbm: source.BaseSignalDbm == 0 ? -76 : source.BaseSignalDbm,
            Rs232Connected: connections.Count(static connection => connection.InterfaceType == "rs232"),
            Rs485Connected: connections.Count(static connection => connection.InterfaceType == "rs485"),
            CanConnected: connections.Count(static connection => connection.InterfaceType == "can"),
            EthernetConnected: connections.Count(static connection => connection.InterfaceType == "ethernet"),
            DigitalInputs: connections.Count(static connection => connection.InterfaceType == "digital-input"),
            DigitalOutputs: connections.Count(static connection => connection.InterfaceType == "digital-output"),
            Firmware: string.IsNullOrWhiteSpace(source.Firmware) ? "PNC-OS 2.4.1" : source.Firmware.Trim(),
            MainboardStatus: string.IsNullOrWhiteSpace(source.MainboardStatus) ? "stabilna" : source.MainboardStatus.Trim(),
            MainboardTempC: Math.Clamp(source.MainboardTempC, 20, 95),
            SupplyVoltage: Math.Round(Math.Clamp(source.SupplyVoltage, 5, 60), 2),
            Online: source.Online,
            BoardRevision: string.IsNullOrWhiteSpace(source.BoardRevision) ? "MB-1.0" : source.BoardRevision.Trim(),
            BoardSerialNumber: string.IsNullOrWhiteSpace(source.BoardSerialNumber)
                ? $"{fallbackCode}-MB-001"
                : source.BoardSerialNumber.Trim().ToUpperInvariant(),
            BaseCpuLoadPercent: Math.Clamp(source.BaseCpuLoadPercent, 0, 100),
            BaseMemoryPercent: Math.Clamp(source.BaseMemoryPercent, 0, 100),
            BaseStoragePercent: Math.Clamp(source.BaseStoragePercent, 0, 100),
            WatchdogHealthy: source.WatchdogHealthy,
            UptimeHours: Math.Max(1, source.UptimeHours),
            Notes: string.IsNullOrWhiteSpace(source.Notes) ? "Brak opisu dla wezla PNC." : source.Notes.Trim(),
            Connections: connections);
    }

    private static PncExternalConnectionConfigRecord NormalizeConnection(
        PncExternalConnectionConfigRecord source,
        string deviceCode,
        int index)
    {
        var interfaceType = NormalizeInterfaceType(source.InterfaceType);

        return new PncExternalConnectionConfigRecord(
            Id: string.IsNullOrWhiteSpace(source.Id) ? $"{deviceCode.ToLowerInvariant()}-{interfaceType}-{index + 1}" : source.Id.Trim().ToLowerInvariant(),
            InterfaceType: interfaceType,
            PortName: string.IsNullOrWhiteSpace(source.PortName) ? DefaultPortName(interfaceType, index) : source.PortName.Trim(),
            DeviceName: string.IsNullOrWhiteSpace(source.DeviceName) ? $"Urzadzenie {index + 1}" : source.DeviceName.Trim(),
            Protocol: string.IsNullOrWhiteSpace(source.Protocol) ? DefaultProtocol(interfaceType) : source.Protocol.Trim(),
            Status: NormalizeConnectionStatus(source.Status),
            Notes: source.Notes?.Trim() ?? string.Empty,
            BaudRate: interfaceType is "rs232" or "rs485"
                ? Math.Max(source.BaudRate ?? 9600, 1200)
                : source.BaudRate);
    }

    private static string NormalizeInterfaceType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "rs232" => "rs232",
            "rs485" => "rs485",
            "can" => "can",
            "ethernet" => "ethernet",
            "digital-input" => "digital-input",
            "digital-output" => "digital-output",
            _ => "rs232"
        };
    }

    private static string NormalizeConnectionStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "attention" => "attention",
            _ => "online"
        };
    }

    private static string DefaultPortName(string interfaceType, int index)
    {
        return interfaceType switch
        {
            "rs232" => $"COM{index + 1}",
            "rs485" => "RS485",
            "can" => $"CAN-{index + 1}",
            "ethernet" => $"LAN{index + 1}",
            "digital-input" => $"DI{index + 1}",
            "digital-output" => $"DO{index + 1}",
            _ => $"PORT-{index + 1}"
        };
    }

    private static string DefaultProtocol(string interfaceType)
    {
        return interfaceType switch
        {
            "rs232" => "MODBUS RTU",
            "rs485" => "MODBUS RTU",
            "can" => "CANopen",
            "ethernet" => "TCP/IP",
            "digital-input" => "GPIO",
            "digital-output" => "GPIO",
            _ => "Serial"
        };
    }

    private static string NormalizeStructuredLocation(string deviceCode, string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return DefaultStructuredLocation(deviceCode);
        }

        var trimmed = string.Join(" / ",
            location.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (trimmed.Count(character => character == '/') >= 3)
        {
            return trimmed;
        }

        return deviceCode switch
        {
            "PNC-001" => "Mazowieckie / Warszawa / Centralny Szpital Kliniczny / Serwerownia glowna",
            "PNC-002" => "Malopolskie / Krakow / Szpital Uniwersytecki / Oddzial intensywnej terapii",
            "PNC-003" => "Pomorskie / Gdansk / Uniwersyteckie Centrum Kliniczne / Laboratorium analityczne",
            "PNC-004" => "Wielkopolskie / Poznan / Wojewodzki Szpital Specjalistyczny / Magazyn centralny",
            "PNC-005" => "Dolnoslaskie / Wroclaw / Dolnoslaskie Centrum Medyczne / Punkt przyjec",
            "PNC-006" => "Lodzkie / Lodz / Instytut Onkologii / Zaklad histopatologii",
            "PNC-007" => "Slaskie / Katowice / Centrum Urazowe / Blok operacyjny",
            _ => $"Nieznane / Nieznane / Obiekt PathoNet / {trimmed}"
        };
    }

    private static string DefaultStructuredLocation(string deviceCode) =>
        deviceCode switch
        {
            "PNC-001" => "Mazowieckie / Warszawa / Centralny Szpital Kliniczny / Serwerownia glowna",
            "PNC-002" => "Malopolskie / Krakow / Szpital Uniwersytecki / Oddzial intensywnej terapii",
            "PNC-003" => "Pomorskie / Gdansk / Uniwersyteckie Centrum Kliniczne / Laboratorium analityczne",
            "PNC-004" => "Wielkopolskie / Poznan / Wojewodzki Szpital Specjalistyczny / Magazyn centralny",
            "PNC-005" => "Dolnoslaskie / Wroclaw / Dolnoslaskie Centrum Medyczne / Punkt przyjec",
            "PNC-006" => "Lodzkie / Lodz / Instytut Onkologii / Zaklad histopatologii",
            "PNC-007" => "Slaskie / Katowice / Centrum Urazowe / Blok operacyjny",
            _ => "Nieznane / Nieznane / Obiekt PathoNet / Lokalizacja techniczna"
        };

    private static PortalFleetConfig DefaultFleet() =>
        new(
            Lte: new LteModemConfigRecord(
                SimSlot: "SIM1 na plycie glownej",
                ModemName: "Quectel EG25-G",
                OperatorName: "Orange PL",
                NetworkType: "LTE Cat.4",
                SimNumber: "+48 510 200 100",
                Iccid: "8948103000000001201",
                Imsi: "260031234567890",
                Imei: "359762110000001",
                Apn: "pathonet.m2m",
                CellId: "PL-260-03-44121",
                IpAddress: "10.64.12.44",
                BaseSignalPercent: 78,
                BaseSignalDbm: -67,
                BaseDownloadMbps: 24.8,
                BaseUploadMbps: 8.1,
                RegistrationStatus: "zalogowana do operatora",
                Plmn: "Orange Polska",
                MccMnc: "260-03",
                Roaming: false,
                PinState: "PIN gotowy",
                Smsc: "+48501000310",
                Tac: "0x10AF",
                BaseRsrpDbm: -91,
                BaseRsrqDb: -11,
                BaseSinrDb: 18,
                DnsPrimary: "172.20.10.10",
                DnsSecondary: "8.8.8.8",
                LastAttachAtUtc: DateTimeOffset.UtcNow.AddMinutes(-28),
                Notes: "Modem LTE dla lacznosci serwisowej i telemetrii edge-to-cloud."),
            PncDevices:
            [
                new(
                    DeviceCode: "PNC-001",
                    Name: "PNC Centrala 1",
                    Location: "Mazowieckie / Warszawa / Centralny Szpital Kliniczny / Serwerownia glowna",
                    OperatorName: "Orange PL",
                    NetworkType: "LTE",
                    SimNumber: "+48 510 200 101",
                    SimSlot: "SIM1",
                    BaseSignalPercent: 81,
                    BaseSignalDbm: -65,
                    Rs232Connected: 3,
                    Rs485Connected: 0,
                    CanConnected: 1,
                    EthernetConnected: 1,
                    DigitalInputs: 1,
                    DigitalOutputs: 0,
                    Firmware: "PNC-OS 2.4.1",
                    MainboardStatus: "stabilna",
                    MainboardTempC: 43,
                    SupplyVoltage: 24.2,
                    Online: true,
                    BoardRevision: "MB-2.1",
                    BoardSerialNumber: "PNC001-MB-2401",
                    BaseCpuLoadPercent: 31,
                    BaseMemoryPercent: 44,
                    BaseStoragePercent: 58,
                    WatchdogHealthy: true,
                    UptimeHours: 428,
                    Notes: "Agreguje sygnaly z glownego obiektu i sluzy jako brama nadrzedna.",
                    Connections:
                    [
                        new("pnc001-rs232-1", "rs232", "COM1", "Respirator Hamilton C6", "HL7", "online", "Glowne lozko intensywnej terapii.", 115200),
                        new("pnc001-rs232-2", "rs232", "COM2", "Pompa infuzyjna B. Braun", "MODBUS RTU", "online", "Kanaly zasilajace sekcji A.", 9600),
                        new("pnc001-rs232-3", "rs232", "COM3", "Monitor pacjenta Drager", "Serial", "online", "Monitoring podstawowych parametrow zyciowych.", 19200),
                        new("pnc001-can-1", "can", "CAN-A", "Modul zasilania UPS", "CANopen", "online", "Nadzor zasilania awaryjnego.", null),
                        new("pnc001-ethernet-1", "ethernet", "LAN1", "Switch obiektowy", "TCP/IP", "online", "Uplink do sieci lokalnej i VPN.", null),
                        new("pnc001-di-1", "digital-input", "DI1", "Czujnik otwarcia szafy", "GPIO", "attention", "Wymaga domkniecia styku.", null)
                    ]),
                new(
                    DeviceCode: "PNC-002",
                    Name: "PNC OIOM",
                    Location: "Malopolskie / Krakow / Szpital Uniwersytecki / Oddzial intensywnej terapii",
                    OperatorName: "T-Mobile PL",
                    NetworkType: "LTE",
                    SimNumber: "+48 510 200 102",
                    SimSlot: "SIM1",
                    BaseSignalPercent: 69,
                    BaseSignalDbm: -74,
                    Rs232Connected: 0,
                    Rs485Connected: 2,
                    CanConnected: 1,
                    EthernetConnected: 2,
                    DigitalInputs: 0,
                    DigitalOutputs: 0,
                    Firmware: "PNC-OS 2.4.1",
                    MainboardStatus: "stabilna",
                    MainboardTempC: 46,
                    SupplyVoltage: 24.0,
                    Online: true,
                    BoardRevision: "MB-2.1",
                    BoardSerialNumber: "PNC002-MB-2402",
                    BaseCpuLoadPercent: 38,
                    BaseMemoryPercent: 49,
                    BaseStoragePercent: 61,
                    WatchdogHealthy: true,
                    UptimeHours: 316,
                    Notes: "Zbiera dane z urzadzen wysokiego priorytetu medycznego.",
                    Connections:
                    [
                        new("pnc002-rs485-1", "rs485", "RS485", "Sterownik pompni prozni", "MODBUS RTU", "online", "Magistrala pomocnicza OIOM.", 19200),
                        new("pnc002-rs485-2", "rs485", "RS485", "Czujnik temperatury komory", "MODBUS RTU", "online", "Drugi wezel na wspolnej magistrali RS-485.", 19200),
                        new("pnc002-can-1", "can", "CAN-A", "Sterownik panelu alarmowego", "CANopen", "attention", "Wysoki czas odpowiedzi na magistrali.", null),
                        new("pnc002-ethernet-1", "ethernet", "LAN1", "Access point oddzialowy", "TCP/IP", "online", "Lokalna komunikacja serwisowa.", null)
                        ,
                        new("pnc002-ethernet-2", "ethernet", "LAN2", "Brama LIS OIOM", "TCP/IP", "online", "Wymiana komunikatow z middleware oddzialowym.", null)
                    ]),
                new(
                    DeviceCode: "PNC-003",
                    Name: "PNC Diagnostyka",
                    Location: "Pomorskie / Gdansk / Uniwersyteckie Centrum Kliniczne / Laboratorium analityczne",
                    OperatorName: "Plus PL",
                    NetworkType: "LTE-M",
                    SimNumber: "+48 510 200 103",
                    SimSlot: "SIM2",
                    BaseSignalPercent: 58,
                    BaseSignalDbm: -81,
                    Rs232Connected: 2,
                    Rs485Connected: 1,
                    CanConnected: 0,
                    EthernetConnected: 1,
                    DigitalInputs: 0,
                    DigitalOutputs: 1,
                    Firmware: "PNC-OS 2.3.9",
                    MainboardStatus: "do obserwacji",
                    MainboardTempC: 49,
                    SupplyVoltage: 23.8,
                    Online: true,
                    BoardRevision: "MB-2.0",
                    BoardSerialNumber: "PNC003-MB-2314",
                    BaseCpuLoadPercent: 53,
                    BaseMemoryPercent: 67,
                    BaseStoragePercent: 72,
                    WatchdogHealthy: true,
                    UptimeHours: 214,
                    Notes: "Wezel z podwyzszonym ruchem danych laboratoryjnych.",
                    Connections:
                    [
                        new("pnc003-rs232-1", "rs232", "COM1", "Analizator gazometrii", "ASTM", "online", "Integracja z laboratorium.", 38400),
                        new("pnc003-rs232-2", "rs232", "COM2", "Waga laboratoryjna", "Serial", "online", "Raportowanie wagi probek.", 9600),
                        new("pnc003-rs485-1", "rs485", "RS485", "Epredia Excelsior AS", "MODBUS RTU", "attention", "Procesor tkankowy z przejsciowym monitoringiem recipe.", 19200),
                        new("pnc003-ethernet-1", "ethernet", "LAN1", "Serwer middleware", "TCP/IP", "online", "Sync z warstwa middleware.", null),
                        new("pnc003-do-1", "digital-output", "DO1", "Sygnalizator optyczny", "GPIO", "online", "Lokalna sygnalizacja laboratorium.", null)
                    ]),
                new(
                    DeviceCode: "PNC-004",
                    Name: "PNC Logistyka",
                    Location: "Wielkopolskie / Poznan / Wojewodzki Szpital Specjalistyczny / Magazyn centralny",
                    OperatorName: "Orange PL",
                    NetworkType: "LTE",
                    SimNumber: "+48 510 200 104",
                    SimSlot: "SIM1",
                    BaseSignalPercent: 74,
                    BaseSignalDbm: -70,
                    Rs232Connected: 1,
                    Rs485Connected: 0,
                    CanConnected: 1,
                    EthernetConnected: 1,
                    DigitalInputs: 1,
                    DigitalOutputs: 1,
                    Firmware: "PNC-OS 2.4.0",
                    MainboardStatus: "stabilna",
                    MainboardTempC: 41,
                    SupplyVoltage: 24.4,
                    Online: true,
                    BoardRevision: "MB-2.1",
                    BoardSerialNumber: "PNC004-MB-2407",
                    BaseCpuLoadPercent: 27,
                    BaseMemoryPercent: 38,
                    BaseStoragePercent: 54,
                    WatchdogHealthy: true,
                    UptimeHours: 508,
                    Notes: "Monitoruje infrastrukture pomocnicza i zasilanie awaryjne.",
                    Connections:
                    [
                        new("pnc004-rs232-1", "rs232", "COM1", "Czytnik wag magazynowych", "Serial", "online", "Bilans dostaw i wydan.", 9600),
                        new("pnc004-can-1", "can", "CAN-A", "Modul baterii UPS", "CANopen", "online", "Zasilanie magazynu centralnego.", null),
                        new("pnc004-ethernet-1", "ethernet", "LAN1", "Switch logistyczny", "TCP/IP", "online", "Integracja z monitoringiem obiektu.", null),
                        new("pnc004-di-1", "digital-input", "DI1", "Czujnik zalania", "GPIO", "online", "Kontrola strefy serwisowej.", null),
                        new("pnc004-do-1", "digital-output", "DO1", "Przekaznik alarmowy", "GPIO", "online", "Sygnal wyzwolenia alarmu lokalnego.", null)
                    ]),
                new(
                    DeviceCode: "PNC-005",
                    Name: "PNC Ambulatorium",
                    Location: "Dolnoslaskie / Wroclaw / Dolnoslaskie Centrum Medyczne / Punkt przyjec",
                    OperatorName: "Play PL",
                    NetworkType: "LTE",
                    SimNumber: "+48 510 200 105",
                    SimSlot: "SIM2",
                    BaseSignalPercent: 52,
                    BaseSignalDbm: -86,
                    Rs232Connected: 1,
                    Rs485Connected: 2,
                    CanConnected: 0,
                    EthernetConnected: 1,
                    DigitalInputs: 1,
                    DigitalOutputs: 1,
                    Firmware: "PNC-OS 2.3.7",
                    MainboardStatus: "do obserwacji",
                    MainboardTempC: 51,
                    SupplyVoltage: 23.6,
                    Online: true,
                    BoardRevision: "MB-1.9",
                    BoardSerialNumber: "PNC005-MB-2298",
                    BaseCpuLoadPercent: 61,
                    BaseMemoryPercent: 73,
                    BaseStoragePercent: 78,
                    WatchdogHealthy: true,
                    UptimeHours: 167,
                    Notes: "Wezel skrajny z bardziej zmiennym sygnalem radiowym.",
                    Connections:
                    [
                        new("pnc005-rs232-1", "rs232", "COM1", "Terminal rejestracji pacjenta", "Serial", "attention", "Zdarzaja sie opoznienia ramek.", 19200),
                        new("pnc005-rs485-1", "rs485", "RS485", "Licznik energii technicznej", "MODBUS RTU", "online", "Pomiar obciazenia punktu przyjec.", 9600),
                        new("pnc005-rs485-2", "rs485", "RS485", "Modul wejsc pokojowych", "MODBUS RTU", "attention", "Okresowe zaniki odpowiedzi z podmodulu.", 9600),
                        new("pnc005-ethernet-1", "ethernet", "LAN1", "Drukarka etykiet", "TCP/IP", "online", "Druk etykiet identyfikacyjnych.", null),
                        new("pnc005-di-1", "digital-input", "DI1", "Przycisk przywolania", "GPIO", "online", "Wejscie alarmowe punktu przyjec.", null),
                        new("pnc005-do-1", "digital-output", "DO1", "Sygnalizator recepcji", "GPIO", "critical", "Wymaga wymiany modulu wykonawczego.", null)
                    ]),
                new(
                    DeviceCode: "PNC-006",
                    Name: "PNC Patomorfologia",
                    Location: "Lodzkie / Lodz / Instytut Onkologii / Zaklad histopatologii",
                    OperatorName: "Orange PL",
                    NetworkType: "LTE",
                    SimNumber: "+48 510 200 106",
                    SimSlot: "SIM1",
                    BaseSignalPercent: 76,
                    BaseSignalDbm: -69,
                    Rs232Connected: 1,
                    Rs485Connected: 1,
                    CanConnected: 0,
                    EthernetConnected: 3,
                    DigitalInputs: 0,
                    DigitalOutputs: 0,
                    Firmware: "PNC-OS 2.4.2",
                    MainboardStatus: "stabilna",
                    MainboardTempC: 44,
                    SupplyVoltage: 24.1,
                    Online: true,
                    BoardRevision: "MB-2.2",
                    BoardSerialNumber: "PNC006-MB-2412",
                    BaseCpuLoadPercent: 42,
                    BaseMemoryPercent: 57,
                    BaseStoragePercent: 63,
                    WatchdogHealthy: true,
                    UptimeHours: 291,
                    Notes: "Wezel histopatologii z przewaga urzadzen laboratoryjnych i integracji po Ethernet.",
                    Connections:
                    [
                        new("pnc006-rs232-1", "rs232", "COM1", "Leica TP1020", "Serial", "online", "Klasyczny procesor tkankowy po RS-232.", 9600),
                        new("pnc006-rs485-1", "rs485", "RS485", "Modul czujnikow reagentow", "MODBUS RTU", "online", "Wspolna magistrala pomiarowa dla reagentow.", 19200),
                        new("pnc006-ethernet-1", "ethernet", "LAN1", "Serwer obrazu", "TCP/IP", "online", "Przesyl obrazow do zakladu histopatologii.", null),
                        new("pnc006-ethernet-2", "ethernet", "LAN2", "Stacja opisowa", "TCP/IP", "online", "Stanowisko opisowe patomorfologa.", null),
                        new("pnc006-ethernet-3", "ethernet", "LAN3", "Switch laboratoryjny", "TCP/IP", "online", "Segment laboratoryjny i uplink do sieci budynkowej.", null)
                    ]),
                new(
                    DeviceCode: "PNC-007",
                    Name: "PNC Blok operacyjny",
                    Location: "Slaskie / Katowice / Centrum Urazowe / Blok operacyjny",
                    OperatorName: "T-Mobile PL",
                    NetworkType: "LTE",
                    SimNumber: "+48 510 200 107",
                    SimSlot: "SIM2",
                    BaseSignalPercent: 47,
                    BaseSignalDbm: -89,
                    Rs232Connected: 1,
                    Rs485Connected: 1,
                    CanConnected: 2,
                    EthernetConnected: 1,
                    DigitalInputs: 0,
                    DigitalOutputs: 0,
                    Firmware: "PNC-OS 2.3.8",
                    MainboardStatus: "do obserwacji",
                    MainboardTempC: 54,
                    SupplyVoltage: 23.4,
                    Online: false,
                    BoardRevision: "MB-2.0",
                    BoardSerialNumber: "PNC007-MB-2331",
                    BaseCpuLoadPercent: 68,
                    BaseMemoryPercent: 79,
                    BaseStoragePercent: 81,
                    WatchdogHealthy: false,
                    UptimeHours: 73,
                    Notes: "Wezel problematyczny testowo: nizszy sygnal, watchdog do obserwacji i mieszane interfejsy sali operacyjnej.",
                    Connections:
                    [
                        new("pnc007-rs232-1", "rs232", "COM1", "Kardiomonitor", "Serial", "attention", "Okresowe przerwy w nadawaniu ramek.", 19200),
                        new("pnc007-rs485-1", "rs485", "RS485", "Pompa prozni technicznej", "MODBUS RTU", "critical", "Bledy odpowiedzi na magistrali RS-485.", 19200),
                        new("pnc007-can-1", "can", "CAN-A", "Sterownik zasilania sali", "CANopen", "online", "Monitoring sekcji zasilania.", null),
                        new("pnc007-can-2", "can", "CAN-B", "Panel wentylacji operacyjnej", "CANopen", "attention", "Rosnacy czas odpowiedzi sterownika.", null),
                        new("pnc007-ethernet-1", "ethernet", "LAN1", "Brama zdalnego nadzoru", "TCP/IP", "online", "Tunel do centrum serwisowego.", null)
                    ])
            ]);
}

internal sealed record PortalFleetConfig(
    LteModemConfigRecord Lte,
    PncDeviceConfigRecord[] PncDevices);

internal sealed record LteModemConfigRecord(
    string SimSlot,
    string ModemName,
    string OperatorName,
    string NetworkType,
    string SimNumber,
    string Iccid,
    string Imsi,
    string Imei,
    string Apn,
    string CellId,
    string IpAddress,
    int BaseSignalPercent,
    int BaseSignalDbm,
    double BaseDownloadMbps,
    double BaseUploadMbps,
    string RegistrationStatus,
    string Plmn,
    string MccMnc,
    bool Roaming,
    string PinState,
    string Smsc,
    string Tac,
    int BaseRsrpDbm,
    int BaseRsrqDb,
    int BaseSinrDb,
    string DnsPrimary,
    string DnsSecondary,
    DateTimeOffset LastAttachAtUtc,
    string Notes);

public sealed record PncDeviceConfigRecord(
    string DeviceCode,
    string Name,
    string Location,
    string OperatorName,
    string NetworkType,
    string SimNumber,
    string SimSlot,
    int BaseSignalPercent,
    int BaseSignalDbm,
    int Rs232Connected,
    int Rs485Connected,
    int CanConnected,
    int EthernetConnected,
    int DigitalInputs,
    int DigitalOutputs,
    string Firmware,
    string MainboardStatus,
    int MainboardTempC,
    double SupplyVoltage,
    bool Online,
    string BoardRevision,
    string BoardSerialNumber,
    int BaseCpuLoadPercent,
    int BaseMemoryPercent,
    int BaseStoragePercent,
    bool WatchdogHealthy,
    int UptimeHours,
    string Notes,
    PncExternalConnectionConfigRecord[] Connections);

public sealed record PncExternalConnectionConfigRecord(
    string Id,
    string InterfaceType,
    string PortName,
    string DeviceName,
    string Protocol,
    string Status,
    string Notes,
    int? BaudRate);
