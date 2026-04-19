using PathoNet.Contracts;

internal sealed class RuleActivationState(
    string ruleId,
    string port,
    string alias,
    string lastMessage,
    DateTimeOffset timestamp)
{
    public string RuleId { get; } = ruleId;
    public string Port { get; } = port;
    public string Alias { get; private set; } = alias;
    public string LastMessage { get; private set; } = lastMessage;
    public DateTimeOffset FirstSeenAtUtc { get; private set; } = timestamp;
    public DateTimeOffset LastSeenAtUtc { get; private set; } = timestamp;
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public int DispatchCount { get; private set; }
    public int OccurrenceCount { get; private set; } = 1;

    public void Touch(DeviceNotification notification, DateTimeOffset timestampUtc)
    {
        Alias = notification.Alias;
        LastMessage = notification.Text;
        LastSeenAtUtc = timestampUtc;
        OccurrenceCount++;
    }

    public void MarkDispatched(DateTimeOffset dispatchedAtUtc, int dispatchCount)
    {
        DispatchedAtUtc = dispatchedAtUtc;
        DispatchCount = dispatchCount;
    }
}
