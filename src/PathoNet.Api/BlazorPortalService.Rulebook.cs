internal sealed partial class BlazorPortalService
{
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
}
