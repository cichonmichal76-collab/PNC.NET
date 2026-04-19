namespace PathoNet.Api.Components.Shared;

using PathoNet.Api;

public sealed class RuleFormModel
{
    public static RuleFormModel CreateEmpty() =>
        new()
        {
            RuleId = null,
            Name = string.Empty,
            MatchText = string.Empty,
            MessageType = "alarm",
            Description = string.Empty,
            ThresholdHours = 5,
            SendSms = true,
            SendEmail = true,
            Enabled = true,
            RecipientIds = []
        };

    internal static RuleFormModel FromRule(PortalMessageRuleRecord rule) =>
        new()
        {
            RuleId = rule.Id,
            Name = rule.Name,
            MatchText = rule.MatchText,
            MessageType = rule.MessageType,
            Description = rule.Description,
            ThresholdHours = rule.ThresholdHours,
            SendSms = rule.SendSms,
            SendEmail = rule.SendEmail,
            Enabled = rule.Enabled,
            RecipientIds = rule.RecipientIds.ToList()
        };

    public string? RuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MatchText { get; set; } = string.Empty;
    public string MessageType { get; set; } = "alarm";
    public string Description { get; set; } = string.Empty;
    public double ThresholdHours { get; set; } = 5;
    public bool SendSms { get; set; } = true;
    public bool SendEmail { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public List<string> RecipientIds { get; set; } = [];
}

public sealed class UserFormModel
{
    public static UserFormModel CreateEmpty() =>
        new()
        {
            UserId = null,
            DisplayName = string.Empty,
            Role = "Serwis",
            Email = string.Empty,
            Phone = string.Empty
        };

    public static UserFormModel FromUser(PortalUserRecord user) =>
        new()
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Role = user.Role,
            Email = user.Email,
            Phone = user.Phone
        };

    public string? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Serwis";
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public sealed class PackageFormModel
{
    public static PackageFormModel CreateEmpty() =>
        new()
        {
            PackageId = null,
            Name = string.Empty,
            Version = string.Empty,
            Target = "PNC OS",
            FileName = string.Empty,
            SizeMb = 32,
            Description = string.Empty,
            ReleaseNotes = string.Empty,
            Mandatory = false
        };

    public static PackageFormModel FromPackage(PortalOtaPackageRecord package) =>
        new()
        {
            PackageId = package.Id,
            Name = package.Name,
            Version = package.Version,
            Target = package.Target,
            FileName = package.FileName,
            SizeMb = package.SizeMb,
            Description = package.Description,
            ReleaseNotes = package.ReleaseNotes,
            Mandatory = package.Mandatory
        };

    public string? PackageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Target { get; set; } = "PNC OS";
    public string FileName { get; set; } = string.Empty;
    public double SizeMb { get; set; } = 32;
    public string Description { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public bool Mandatory { get; set; }
}

public sealed class CampaignFormModel
{
    public static CampaignFormModel CreateEmpty(string? packageId = null) =>
        new()
        {
            CampaignId = null,
            Title = string.Empty,
            PackageId = packageId ?? string.Empty,
            ScheduledLocal = DateTime.Now.AddHours(2),
            Transport = "LTE",
            Window = "okno serwisowe 00:00-04:00",
            RetryLimit = 2,
            NotifyServiceByEmail = true,
            Notes = string.Empty,
            TargetDeviceCodes = [],
            RecipientIds = []
        };

    internal static CampaignFormModel FromCampaign(PortalOtaCampaignViewRecord campaign) =>
        new()
        {
            CampaignId = campaign.Id,
            Title = campaign.Title,
            PackageId = campaign.PackageId,
            ScheduledLocal = campaign.ScheduledForUtc.ToLocalTime().DateTime,
            Transport = campaign.Transport,
            Window = campaign.Window,
            RetryLimit = campaign.RetryLimit,
            NotifyServiceByEmail = campaign.NotifyServiceByEmail,
            Notes = campaign.Notes,
            TargetDeviceCodes = campaign.TargetDeviceCodes.ToList(),
            RecipientIds = campaign.RecipientIds.ToList()
        };

    public string? CampaignId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public DateTime ScheduledLocal { get; set; } = DateTime.Now.AddHours(2);
    public string Transport { get; set; } = "LTE";
    public string Window { get; set; } = "okno serwisowe 00:00-04:00";
    public int RetryLimit { get; set; } = 2;
    public bool NotifyServiceByEmail { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public List<string> TargetDeviceCodes { get; set; } = [];
    public List<string> RecipientIds { get; set; } = [];
}

public sealed class PncFormModel
{
    public static PncFormModel CreateEmpty() =>
        new()
        {
            OriginalDeviceCode = null,
            DeviceCode = string.Empty,
            Name = string.Empty,
            Location = string.Empty,
            OperatorName = "Orange PL",
            NetworkType = "LTE",
            SimNumber = string.Empty,
            SimSlot = "SIM1",
            SignalPercent = 70,
            SignalDbm = -72,
            Firmware = "PNC-OS 2.4.1",
            MainboardStatus = "stabilna",
            MainboardTempC = 44,
            SupplyVoltage = 24.0,
            BoardRevision = "MB-1.0",
            BoardSerialNumber = string.Empty,
            CpuLoadPercent = 35,
            MemoryPercent = 45,
            StoragePercent = 55,
            UptimeHours = 24,
            Online = true,
            WatchdogHealthy = true,
            Notes = string.Empty
        };

    public static PncFormModel FromDevice(PncDeviceConfigRecord device) =>
        new()
        {
            OriginalDeviceCode = device.DeviceCode,
            DeviceCode = device.DeviceCode,
            Name = device.Name,
            Location = device.Location,
            OperatorName = device.OperatorName,
            NetworkType = device.NetworkType,
            SimNumber = device.SimNumber,
            SimSlot = device.SimSlot,
            SignalPercent = device.BaseSignalPercent,
            SignalDbm = device.BaseSignalDbm,
            Firmware = device.Firmware,
            MainboardStatus = device.MainboardStatus,
            MainboardTempC = device.MainboardTempC,
            SupplyVoltage = device.SupplyVoltage,
            BoardRevision = device.BoardRevision,
            BoardSerialNumber = device.BoardSerialNumber,
            CpuLoadPercent = device.BaseCpuLoadPercent,
            MemoryPercent = device.BaseMemoryPercent,
            StoragePercent = device.BaseStoragePercent,
            UptimeHours = device.UptimeHours,
            Online = device.Online,
            WatchdogHealthy = device.WatchdogHealthy,
            Notes = device.Notes
        };

    public string? OriginalDeviceCode { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string OperatorName { get; set; } = "Orange PL";
    public string NetworkType { get; set; } = "LTE";
    public string SimNumber { get; set; } = string.Empty;
    public string SimSlot { get; set; } = "SIM1";
    public int SignalPercent { get; set; } = 70;
    public int SignalDbm { get; set; } = -72;
    public string Firmware { get; set; } = "PNC-OS 2.4.1";
    public string MainboardStatus { get; set; } = "stabilna";
    public int MainboardTempC { get; set; } = 44;
    public double SupplyVoltage { get; set; } = 24.0;
    public string BoardRevision { get; set; } = "MB-1.0";
    public string BoardSerialNumber { get; set; } = string.Empty;
    public int CpuLoadPercent { get; set; } = 35;
    public int MemoryPercent { get; set; } = 45;
    public int StoragePercent { get; set; } = 55;
    public int UptimeHours { get; set; } = 24;
    public bool Online { get; set; } = true;
    public bool WatchdogHealthy { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public sealed class ConnectionFormModel
{
    public static ConnectionFormModel CreateEmpty(string ownerDeviceCode) =>
        new()
        {
            OwnerDeviceCode = ownerDeviceCode,
            OriginalConnectionId = null,
            InterfaceType = "rs232",
            PortName = string.Empty,
            DeviceName = string.Empty,
            Protocol = string.Empty,
            Status = "online",
            Notes = string.Empty,
            BaudRate = null
        };

    internal static ConnectionFormModel FromConnection(string ownerDeviceCode, PncExternalConnectionConfigRecord connection) =>
        new()
        {
            OwnerDeviceCode = ownerDeviceCode,
            OriginalConnectionId = connection.Id,
            InterfaceType = connection.InterfaceType,
            PortName = connection.PortName,
            DeviceName = connection.DeviceName,
            Protocol = connection.Protocol,
            Status = connection.Status,
            Notes = connection.Notes,
            BaudRate = connection.BaudRate
        };

    public string OwnerDeviceCode { get; set; } = string.Empty;
    public string? OriginalConnectionId { get; set; }
    public string InterfaceType { get; set; } = "rs232";
    public string PortName { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Status { get; set; } = "online";
    public string Notes { get; set; } = string.Empty;
    public int? BaudRate { get; set; }
}

public sealed class HardwarePortFormModel
{
    public static HardwarePortFormModel CreateEmpty() =>
        new()
        {
            PortId = string.Empty,
            Alias = string.Empty,
            InterfaceType = "rs232",
            DevicePath = string.Empty,
            NetworkInterfaceName = string.Empty,
            BaudRate = 115200,
            DataBits = 8,
            Parity = "none",
            StopBits = "one",
            ParserKind = "generic-text",
            FrameMode = "line",
            Enabled = true,
            AllowSimulationFallback = false,
            Description = string.Empty
        };

    internal static HardwarePortFormModel FromConfig(CollectorPortConfigRecord port) =>
        new()
        {
            PortId = port.PortId,
            Alias = port.Alias,
            InterfaceType = port.InterfaceType,
            DevicePath = port.DevicePath,
            NetworkInterfaceName = port.NetworkInterfaceName ?? string.Empty,
            BaudRate = port.BaudRate,
            DataBits = port.DataBits,
            Parity = port.Parity,
            StopBits = port.StopBits,
            ParserKind = port.ParserKind,
            FrameMode = port.FrameMode,
            Enabled = port.Enabled,
            AllowSimulationFallback = port.AllowSimulationFallback,
            Description = port.Description
        };

    public string PortId { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = "rs232";
    public string DevicePath { get; set; } = string.Empty;
    public string NetworkInterfaceName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "none";
    public string StopBits { get; set; } = "one";
    public string ParserKind { get; set; } = "generic-text";
    public string FrameMode { get; set; } = "line";
    public bool Enabled { get; set; } = true;
    public bool AllowSimulationFallback { get; set; }
    public string Description { get; set; } = string.Empty;
}
