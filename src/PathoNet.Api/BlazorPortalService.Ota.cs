internal sealed partial class BlazorPortalService
{
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
}
