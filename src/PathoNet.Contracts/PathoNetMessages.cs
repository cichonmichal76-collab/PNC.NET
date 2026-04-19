namespace PathoNet.Contracts;

public static class HubEnvelopeKinds
{
    public const string Notify = "notify";
    public const string Heartbeat = "heartbeat";
}

public sealed record NotificationMeta(
    string MessGuid,
    string DateMess,
    bool Exported,
    string? ReceivedDeviceKey = null);

public sealed record DeviceNotification(
    string DeviceId,
    string Source,
    string Port,
    string Alias,
    string Level,
    string Text,
    string Raw,
    NotificationMeta Meta);

public sealed record DeviceHeartbeat(
    string DeviceId,
    string ClientId,
    string ClientName,
    string CurrentVersion,
    DateTimeOffset Timestamp,
    string Topic,
    int PortCount,
    int PathoNetId);

public sealed record HubEnvelope(
    string Kind,
    string Topic,
    DateTimeOffset EmittedAtUtc,
    string? DeviceKey,
    DeviceNotification? Notification,
    DeviceHeartbeat? Heartbeat)
{
    public static HubEnvelope ForHeartbeat(string topic, DeviceHeartbeat heartbeat) =>
        new(
            Kind: HubEnvelopeKinds.Heartbeat,
            Topic: topic,
            EmittedAtUtc: DateTimeOffset.UtcNow,
            DeviceKey: null,
            Notification: null,
            Heartbeat: heartbeat);

    public static HubEnvelope ForNotification(string topic, string deviceKey, DeviceNotification notification) =>
        new(
            Kind: HubEnvelopeKinds.Notify,
            Topic: topic,
            EmittedAtUtc: DateTimeOffset.UtcNow,
            DeviceKey: deviceKey,
            Notification: notification,
            Heartbeat: null);
}

public sealed record HubPing(
    string Type,
    string Client,
    DateTimeOffset Time);

public sealed record HubPong(
    string Type,
    string Status,
    DateTimeOffset Time,
    long NotificationsForwarded,
    long HeartbeatsForwarded);
