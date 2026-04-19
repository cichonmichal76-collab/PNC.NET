using PathoNet.Contracts;

internal sealed partial class SimulationStore
{
    private PortalRulebookStateRecord BuildRulebookState(PortalRulebookConfig rulebook, DateTimeOffset now)
    {
        var activations = BuildRuleActivationViews(rulebook, now);
        var dispatches = _dispatches
            .ToArray()
            .OrderByDescending(static dispatch => dispatch.TriggeredAtUtc)
            .Take(24)
            .ToArray();

        return new PortalRulebookStateRecord(
            Summary: new PortalRulebookSummaryRecord(
                UserCount: rulebook.Users.Length,
                RuleCount: rulebook.Rules.Length,
                EnabledRuleCount: rulebook.Rules.Count(static rule => rule.Enabled),
                ActiveMatchCount: activations.Length,
                EscalatedMatchCount: activations.Count(static activation => activation.ThresholdReached),
                DispatchCount: dispatches.Length),
            Users: rulebook.Users,
            Rules: rulebook.Rules,
            ActiveMatches: activations,
            Dispatches: dispatches);
    }

    private void TrackRuleMatch(DeviceNotification notification)
    {
        var rulebook = _rulebookStore.GetConfig();
        var matchedRule = FindMatchingRule(notification, rulebook.Rules);
        if (matchedRule is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var key = ActivationKey(matchedRule.Id, notification.Port);
        var state = _ruleStates.GetOrAdd(key, _ => new RuleActivationState(
            matchedRule.Id,
            notification.Port,
            notification.Alias,
            notification.Text,
            now));

        lock (state)
        {
            state.Touch(notification, now);
        }

        EnsurePendingEscalations(rulebook);
    }

    private void EnsurePendingEscalations(PortalRulebookConfig rulebook)
    {
        var now = DateTimeOffset.UtcNow;
        var rulesById = rulebook.Rules.ToDictionary(static rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        var usersById = rulebook.Users.ToDictionary(static user => user.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _ruleStates)
        {
            if (!rulesById.TryGetValue(pair.Value.RuleId, out var rule) || !rule.Enabled)
            {
                continue;
            }

            var state = pair.Value;
            lock (state)
            {
                if (state.DispatchedAtUtc.HasValue)
                {
                    continue;
                }

                if (!ShouldDispatch(rule, state, now))
                {
                    continue;
                }

                var recipients = ResolveRecipients(rule, usersById);
                var channels = ResolveChannels(rule);
                if (recipients.Length == 0 || channels.Length == 0)
                {
                    continue;
                }

                var dispatchCount = 0;
                foreach (var channel in channels)
                {
                    foreach (var recipient in recipients)
                    {
                        dispatchCount++;
                        var dispatch = new EscalationDispatchViewRecord(
                            Id: Guid.NewGuid().ToString("N"),
                            RuleId: rule.Id,
                            RuleName: rule.Name,
                            Port: state.Port,
                            Alias: state.Alias,
                            Channel: channel,
                            RecipientName: recipient.DisplayName,
                            RecipientAddress: channel == "sms" ? recipient.Phone : recipient.Email,
                            TriggeredAtUtc: now,
                            Message: $"{rule.Name}: {state.LastMessage}");

                        _dispatches.Enqueue(dispatch);
                    }
                }

                state.MarkDispatched(now, dispatchCount);
                Trim(_dispatches, 80);
            }
        }
    }

    private void PruneRuleStates(PortalRulebookConfig rulebook)
    {
        var validRuleIds = rulebook.Rules
            .Where(static rule => rule.Enabled)
            .Select(static rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _ruleStates)
        {
            if (!validRuleIds.Contains(pair.Value.RuleId))
            {
                _ruleStates.TryRemove(pair.Key, out _);
            }
        }
    }

    private RuleActivationViewRecord[] BuildRuleActivationViews(PortalRulebookConfig rulebook, DateTimeOffset now)
    {
        var usersById = rulebook.Users.ToDictionary(static user => user.Id, StringComparer.OrdinalIgnoreCase);
        var rulesById = rulebook.Rules.ToDictionary(static rule => rule.Id, StringComparer.OrdinalIgnoreCase);

        return _ruleStates
            .Values
            .Where(state => rulesById.TryGetValue(state.RuleId, out var rule) && rule.Enabled)
            .Select(state =>
            {
                var rule = rulesById[state.RuleId];
                var dueAt = state.FirstSeenAtUtc.AddHours(rule.ThresholdHours);
                var recipients = ResolveRecipients(rule, usersById)
                    .Select(static user => user.DisplayName)
                    .ToArray();
                var channels = ResolveChannels(rule);

                return new RuleActivationViewRecord(
                    RuleId: rule.Id,
                    RuleName: rule.Name,
                    MatchText: rule.MatchText,
                    MessageType: rule.MessageType,
                    Port: state.Port,
                    Alias: state.Alias,
                    LastMessage: state.LastMessage,
                    FirstSeenAtUtc: state.FirstSeenAtUtc,
                    LastSeenAtUtc: state.LastSeenAtUtc,
                    DueAtUtc: dueAt,
                    ThresholdHours: rule.ThresholdHours,
                    ElapsedHours: Math.Round(Math.Max(0d, (now - state.FirstSeenAtUtc).TotalHours), 2),
                    ThresholdReached: now >= dueAt,
                    Dispatched: state.DispatchedAtUtc.HasValue,
                    Channels: channels,
                    Recipients: recipients);
            })
            .OrderByDescending(static activation => activation.ThresholdReached)
            .ThenByDescending(static activation => activation.LastSeenAtUtc)
            .ToArray();
    }

    private RuleActivationState? TryGetRuleState(string ruleId, string port) =>
        _ruleStates.TryGetValue(ActivationKey(ruleId, port), out var state)
            ? state
            : null;

    private static string ActivationKey(string ruleId, string port) => $"{ruleId}::{port}";

    private static PortalMessageRuleRecord? FindMatchingRule(
        DeviceNotification notification,
        IEnumerable<PortalMessageRuleRecord> rules)
    {
        var searchable = $"{notification.Text}|{notification.Port}|{notification.Alias}|{notification.Raw}";
        var messageType = NormalizeLevel(notification.Level);

        return rules
            .Where(rule =>
                rule.Enabled
                && !string.IsNullOrWhiteSpace(rule.MatchText)
                && (rule.MessageType == "any" || rule.MessageType == messageType))
            .OrderByDescending(static rule => rule.MatchText.Length)
            .FirstOrDefault(rule => searchable.Contains(rule.MatchText, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldDispatch(
        PortalMessageRuleRecord rule,
        RuleActivationState state,
        DateTimeOffset now)
    {
        if (rule.ThresholdHours < 0)
        {
            return false;
        }

        var hasChannels = rule.SendEmail || rule.SendSms;
        if (!hasChannels || rule.RecipientIds.Length == 0)
        {
            return false;
        }

        return now >= state.FirstSeenAtUtc.AddHours(rule.ThresholdHours);
    }

    private static string BuildEscalationSummary(
        PortalMessageRuleRecord? rule,
        RuleActivationState? state,
        PortalRulebookConfig rulebook,
        DateTimeOffset now)
    {
        if (rule is null)
        {
            return string.Empty;
        }

        var usersById = rulebook.Users.ToDictionary(static user => user.Id, StringComparer.OrdinalIgnoreCase);
        var recipients = ResolveRecipients(rule, usersById);
        var recipientLabel = recipients.Length == 0
            ? "bez odbiorcow"
            : string.Join(", ", recipients.Select(static user => user.DisplayName));
        var channelLabel = ResolveChannelLabel(rule);

        if (state is null)
        {
            return $"Prog {FormatHours(rule.ThresholdHours)} -> {channelLabel} do: {recipientLabel}";
        }

        var dueAt = state.FirstSeenAtUtc.AddHours(rule.ThresholdHours);
        if (now >= dueAt)
        {
            return state.DispatchedAtUtc.HasValue
                ? $"Eskalacja wyslana przez {channelLabel} do: {recipientLabel}"
                : $"Prog przekroczony. Oczekuje na wysylke przez {channelLabel} do: {recipientLabel}";
        }

        return $"Eskalacja po {FormatHours(rule.ThresholdHours)} do: {recipientLabel}";
    }

    private static bool IsThresholdReached(
        PortalMessageRuleRecord? rule,
        RuleActivationState? state,
        DateTimeOffset now) =>
        rule is not null
        && state is not null
        && now >= state.FirstSeenAtUtc.AddHours(rule.ThresholdHours);

    private static PortalUserRecord[] ResolveRecipients(
        PortalMessageRuleRecord rule,
        IReadOnlyDictionary<string, PortalUserRecord> usersById)
    {
        return rule.RecipientIds
            .Select(recipientId => usersById.TryGetValue(recipientId, out var user) ? user : null)
            .Where(static user => user is not null)
            .Cast<PortalUserRecord>()
            .ToArray();
    }

    private static string[] ResolveChannels(PortalMessageRuleRecord rule)
    {
        var channels = new List<string>(2);
        if (rule.SendSms)
        {
            channels.Add("sms");
        }

        if (rule.SendEmail)
        {
            channels.Add("email");
        }

        return channels.ToArray();
    }

    private static string ResolveChannelLabel(PortalMessageRuleRecord rule)
    {
        var channels = ResolveChannels(rule);
        return channels.Length switch
        {
            0 => "brak kanalow",
            1 when channels[0] == "sms" => "SMS",
            1 => "e-mail",
            _ => "SMS i e-mail"
        };
    }

    private static string FormatHours(double hours)
    {
        var rounded = Math.Round(Math.Max(hours, 0), 2);
        return $"{rounded:0.##} h";
    }
}
