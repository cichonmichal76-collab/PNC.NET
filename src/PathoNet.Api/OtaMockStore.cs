using System.Text.Json;

internal sealed class OtaMockStore
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PortalOtaConfig _current;

    public OtaMockStore(string contentRoot)
    {
        var dataDirectory = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "portal-ota.json");
        _current = LoadOrSeed();
    }

    public PortalOtaConfig GetConfig() => _current;

    public async Task<PortalOtaStateRecord> GetStateAsync(
        PortalFleetConfig fleet,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = Normalize(_current);
            var (materialized, changed) = Materialize(normalized, fleet, DateTimeOffset.UtcNow);
            _current = materialized;
            if (changed)
            {
                await PersistLockedAsync(_current, cancellationToken);
            }

            return BuildState(_current, fleet);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PortalOtaStateRecord> SaveAsync(
        PortalOtaConfig candidate,
        PortalFleetConfig fleet,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = Normalize(candidate);
            var (materialized, _) = Materialize(normalized, fleet, DateTimeOffset.UtcNow);
            _current = materialized;
            await PersistLockedAsync(_current, cancellationToken);
            return BuildState(_current, fleet);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistLockedAsync(PortalOtaConfig config, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken);
    }

    private PortalOtaConfig LoadOrSeed()
    {
        if (!File.Exists(_filePath))
        {
            var seeded = Normalize(DefaultConfig());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(seeded, _jsonOptions));
            return seeded;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<PortalOtaConfig>(json, _jsonOptions);
            return Normalize(loaded ?? DefaultConfig());
        }
        catch (JsonException)
        {
            var fallback = Normalize(DefaultConfig());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(fallback, _jsonOptions));
            return fallback;
        }
    }

    private static PortalOtaConfig Normalize(PortalOtaConfig candidate)
    {
        var fallback = DefaultConfig();
        var packageSources = candidate.Packages is { Length: > 0 } ? candidate.Packages : fallback.Packages;
        var recipientSources = candidate.ServiceRecipients is { Length: > 0 } ? candidate.ServiceRecipients : fallback.ServiceRecipients;

        var normalizedPackages = new List<PortalOtaPackageRecord>();
        var usedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (package, index) in packageSources.Select(static (item, idx) => (item, idx)))
        {
            var name = string.IsNullOrWhiteSpace(package.Name) ? $"Pakiet OTA {index + 1}" : package.Name.Trim();
            var version = string.IsNullOrWhiteSpace(package.Version) ? $"1.0.{index}" : package.Version.Trim();
            var id = MakeUniqueId(Slugify(package.Id, "pkg", $"{name}-{version}", index + 1), usedPackageIds);

            normalizedPackages.Add(new PortalOtaPackageRecord(
                Id: id,
                Name: name,
                Version: version,
                Target: string.IsNullOrWhiteSpace(package.Target) ? "PNC" : package.Target.Trim(),
                FileName: string.IsNullOrWhiteSpace(package.FileName) ? $"{id}.bin" : package.FileName.Trim(),
                SizeMb: Math.Round(Math.Clamp(package.SizeMb, 1, 4096), 1),
                Description: package.Description?.Trim() ?? string.Empty,
                ReleaseNotes: package.ReleaseNotes?.Trim() ?? string.Empty,
                Mandatory: package.Mandatory));
        }

        var normalizedRecipients = new List<PortalOtaServiceRecipientRecord>();
        var usedRecipientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (recipient, index) in recipientSources.Select(static (item, idx) => (item, idx)))
        {
            var displayName = string.IsNullOrWhiteSpace(recipient.DisplayName)
                ? $"Serwis {index + 1}"
                : recipient.DisplayName.Trim();
            var id = MakeUniqueId(Slugify(recipient.Id, "service", displayName, index + 1), usedRecipientIds);

            normalizedRecipients.Add(new PortalOtaServiceRecipientRecord(
                Id: id,
                DisplayName: displayName,
                Role: string.IsNullOrWhiteSpace(recipient.Role) ? "Serwis" : recipient.Role.Trim(),
                Email: string.IsNullOrWhiteSpace(recipient.Email)
                    ? $"{id}@pathonet.local"
                    : recipient.Email.Trim().ToLowerInvariant()));
        }

        var packageIds = normalizedPackages.Select(static package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recipientIds = normalizedRecipients.Select(static recipient => recipient.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedCampaigns = new List<PortalOtaCampaignRecord>();
        var usedCampaignIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (campaign, index) in (candidate.Campaigns ?? []).Select(static (item, idx) => (item, idx)))
        {
            var packageId = packageIds.Contains(campaign.PackageId)
                ? campaign.PackageId
                : normalizedPackages[0].Id;
            var title = string.IsNullOrWhiteSpace(campaign.Title)
                ? $"Aktualizacja {normalizedPackages.First(static item => true).Name}"
                : campaign.Title.Trim();
            var id = MakeUniqueId(Slugify(campaign.Id, "campaign", title, index + 1), usedCampaignIds);
            var scheduledForUtc = campaign.ScheduledForUtc == default
                ? DateTimeOffset.UtcNow.AddHours(index + 1)
                : campaign.ScheduledForUtc;

            normalizedCampaigns.Add(new PortalOtaCampaignRecord(
                Id: id,
                Title: title,
                PackageId: packageId,
                TargetDeviceCodes: (campaign.TargetDeviceCodes ?? [])
                    .Where(static code => !string.IsNullOrWhiteSpace(code))
                    .Select(static code => code.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ScheduledForUtc: scheduledForUtc,
                Transport: string.IsNullOrWhiteSpace(campaign.Transport) ? "LTE" : campaign.Transport.Trim(),
                Window: string.IsNullOrWhiteSpace(campaign.Window) ? "okno serwisowe 00:00-04:00" : campaign.Window.Trim(),
                RetryLimit: Math.Clamp(campaign.RetryLimit, 0, 10),
                NotifyServiceByEmail: campaign.NotifyServiceByEmail,
                RecipientIds: (campaign.RecipientIds ?? [])
                    .Where(recipientId => !string.IsNullOrWhiteSpace(recipientId) && recipientIds.Contains(recipientId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Status: NormalizeCampaignStatus(campaign.Status),
                Notes: campaign.Notes?.Trim() ?? string.Empty,
                CreatedAtUtc: campaign.CreatedAtUtc == default
                    ? scheduledForUtc.AddHours(-2)
                    : campaign.CreatedAtUtc,
                StartedAtUtc: campaign.StartedAtUtc,
                CompletedAtUtc: campaign.CompletedAtUtc));
        }

        var normalizedLogs = (candidate.Logs ?? [])
            .Where(static log => !string.IsNullOrWhiteSpace(log.CampaignId))
            .Select((log, index) => new PortalOtaExecutionLogRecord(
                Id: string.IsNullOrWhiteSpace(log.Id)
                    ? $"log-{index + 1}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                    : log.Id.Trim(),
                CampaignId: log.CampaignId.Trim(),
                DeviceCode: string.IsNullOrWhiteSpace(log.DeviceCode) ? "PNC-UNK" : log.DeviceCode.Trim().ToUpperInvariant(),
                Level: NormalizeMessageType(log.Level),
                Message: string.IsNullOrWhiteSpace(log.Message) ? "Brak opisu logu." : log.Message.Trim(),
                OccurredAtUtc: log.OccurredAtUtc == default ? DateTimeOffset.UtcNow : log.OccurredAtUtc))
            .OrderBy(static log => log.OccurredAtUtc)
            .ToArray();

        var normalizedEmails = (candidate.EmailLogs ?? [])
            .Where(static email => !string.IsNullOrWhiteSpace(email.CampaignId))
            .Select((email, index) => new PortalOtaEmailLogRecord(
                Id: string.IsNullOrWhiteSpace(email.Id)
                    ? $"email-{index + 1}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                    : email.Id.Trim(),
                CampaignId: email.CampaignId.Trim(),
                Subject: string.IsNullOrWhiteSpace(email.Subject) ? "Powiadomienie OTA" : email.Subject.Trim(),
                Recipients: (email.Recipients ?? [])
                    .Where(static recipient => !string.IsNullOrWhiteSpace(recipient))
                    .Select(static recipient => recipient.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SentAtUtc: email.SentAtUtc == default ? DateTimeOffset.UtcNow : email.SentAtUtc,
                Body: email.Body?.Trim() ?? string.Empty))
            .OrderBy(static email => email.SentAtUtc)
            .ToArray();

        return new PortalOtaConfig(
            Packages: normalizedPackages.ToArray(),
            Campaigns: normalizedCampaigns
                .OrderByDescending(static campaign => campaign.ScheduledForUtc)
                .ToArray(),
            ServiceRecipients: normalizedRecipients.ToArray(),
            Logs: normalizedLogs,
            EmailLogs: normalizedEmails);
    }

    private static (PortalOtaConfig Config, bool Changed) Materialize(
        PortalOtaConfig config,
        PortalFleetConfig fleet,
        DateTimeOffset now)
    {
        var devicesByCode = fleet.PncDevices.ToDictionary(static device => device.DeviceCode, StringComparer.OrdinalIgnoreCase);
        var packagesById = config.Packages.ToDictionary(static package => package.Id, StringComparer.OrdinalIgnoreCase);
        var recipientsById = config.ServiceRecipients.ToDictionary(static recipient => recipient.Id, StringComparer.OrdinalIgnoreCase);

        var logs = config.Logs.ToList();
        var emails = config.EmailLogs.ToList();
        var campaigns = new List<PortalOtaCampaignRecord>(config.Campaigns.Length);
        var changed = false;

        foreach (var campaign in config.Campaigns.OrderBy(static item => item.ScheduledForUtc))
        {
            if (!string.Equals(campaign.Status, "scheduled", StringComparison.OrdinalIgnoreCase)
                || campaign.ScheduledForUtc > now)
            {
                campaigns.Add(campaign);
                continue;
            }

            changed = true;

            packagesById.TryGetValue(campaign.PackageId, out var package);
            package ??= config.Packages[0];
            var targetCodes = campaign.TargetDeviceCodes.Length > 0
                ? campaign.TargetDeviceCodes
                : fleet.PncDevices.Take(1).Select(static device => device.DeviceCode).ToArray();

            var startedAtUtc = campaign.StartedAtUtc ?? campaign.ScheduledForUtc;
            var cursor = startedAtUtc;
            var successCount = 0;
            var failedCount = 0;

            foreach (var deviceCode in targetCodes)
            {
                cursor = cursor.AddMinutes(1);
                logs.Add(new PortalOtaExecutionLogRecord(
                    Id: $"log-{campaign.Id}-{deviceCode.ToLowerInvariant()}-start",
                    CampaignId: campaign.Id,
                    DeviceCode: deviceCode,
                    Level: "info",
                    Message: $"Rozpoczeto wysylke OTA pakietu {package.Name} {package.Version} przez {campaign.Transport}.",
                    OccurredAtUtc: cursor));

                if (!devicesByCode.TryGetValue(deviceCode, out var device))
                {
                    failedCount++;
                    cursor = cursor.AddMinutes(1);
                    logs.Add(new PortalOtaExecutionLogRecord(
                        Id: $"log-{campaign.Id}-{deviceCode.ToLowerInvariant()}-missing",
                        CampaignId: campaign.Id,
                        DeviceCode: deviceCode,
                        Level: "error",
                        Message: "Nie znaleziono docelowego wezla PNC w konfiguracji floty.",
                        OccurredAtUtc: cursor));
                    continue;
                }

                if (!device.Online)
                {
                    failedCount++;
                    cursor = cursor.AddMinutes(1);
                    logs.Add(new PortalOtaExecutionLogRecord(
                        Id: $"log-{campaign.Id}-{deviceCode.ToLowerInvariant()}-offline",
                        CampaignId: campaign.Id,
                        DeviceCode: deviceCode,
                        Level: "alarm",
                        Message: $"Wezel {device.Name} jest offline. Dostarczenie paczki OTA przez LTE nie powiodlo sie.",
                        OccurredAtUtc: cursor));
                    continue;
                }

                cursor = cursor.AddMinutes(1);
                logs.Add(new PortalOtaExecutionLogRecord(
                    Id: $"log-{campaign.Id}-{deviceCode.ToLowerInvariant()}-transfer",
                    CampaignId: campaign.Id,
                    DeviceCode: deviceCode,
                    Level: "info",
                    Message: $"Pakiet {package.FileName} dostarczony do {device.Name}; sygnal LTE {device.BaseSignalPercent}% i firmware bazowy {device.Firmware}.",
                    OccurredAtUtc: cursor));

                cursor = cursor.AddMinutes(1);
                logs.Add(new PortalOtaExecutionLogRecord(
                    Id: $"log-{campaign.Id}-{deviceCode.ToLowerInvariant()}-verify",
                    CampaignId: campaign.Id,
                    DeviceCode: deviceCode,
                    Level: "info",
                    Message: $"Weryfikacja sumy kontrolnej, restart kontrolowany i test powdrozeniowy zakonczone powodzeniem na {device.Name}.",
                    OccurredAtUtc: cursor));

                successCount++;
            }

            var completedAtUtc = campaign.CompletedAtUtc ?? cursor.AddMinutes(1);
            var status = failedCount == 0
                ? "completed"
                : successCount > 0
                    ? "partial"
                    : "failed";

            if (campaign.NotifyServiceByEmail && !emails.Any(email => email.CampaignId == campaign.Id))
            {
                var recipients = (campaign.RecipientIds.Length > 0 ? campaign.RecipientIds : config.ServiceRecipients.Select(static recipient => recipient.Id).ToArray())
                    .Select(recipientId => recipientsById.TryGetValue(recipientId, out var recipient) ? recipient : null)
                    .Where(static recipient => recipient is not null)
                    .Cast<PortalOtaServiceRecipientRecord>()
                    .ToArray();

                if (recipients.Length > 0)
                {
                    emails.Add(new PortalOtaEmailLogRecord(
                        Id: $"email-{campaign.Id}",
                        CampaignId: campaign.Id,
                        Subject: $"PathoNet OTA: {campaign.Title}",
                        Recipients: recipients.Select(static recipient => recipient.Email).ToArray(),
                        SentAtUtc: completedAtUtc.AddMinutes(1),
                        Body: BuildEmailBody(campaign, package, targetCodes, successCount, failedCount, status)));
                }
            }

            campaigns.Add(campaign with
            {
                Status = status,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            });
        }

        var materialized = new PortalOtaConfig(
            Packages: config.Packages,
            Campaigns: campaigns.OrderByDescending(static campaign => campaign.ScheduledForUtc).ToArray(),
            ServiceRecipients: config.ServiceRecipients,
            Logs: logs.OrderByDescending(static log => log.OccurredAtUtc).ToArray(),
            EmailLogs: emails.OrderByDescending(static email => email.SentAtUtc).ToArray());

        return (materialized, changed);
    }

    private static PortalOtaStateRecord BuildState(PortalOtaConfig config, PortalFleetConfig fleet)
    {
        var packagesById = config.Packages.ToDictionary(static package => package.Id, StringComparer.OrdinalIgnoreCase);
        var recipientsById = config.ServiceRecipients.ToDictionary(static recipient => recipient.Id, StringComparer.OrdinalIgnoreCase);
        var devicesByCode = fleet.PncDevices.ToDictionary(static device => device.DeviceCode, StringComparer.OrdinalIgnoreCase);

        var campaignViews = config.Campaigns
            .Select(campaign =>
            {
                packagesById.TryGetValue(campaign.PackageId, out var package);
                package ??= config.Packages[0];

                var campaignLogs = config.Logs
                    .Where(log => string.Equals(log.CampaignId, campaign.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(static log => log.OccurredAtUtc)
                    .ToArray();
                var successfulTargets = campaignLogs.Count(static log => log.Message.Contains("powodzeniem", StringComparison.OrdinalIgnoreCase));
                var failedTargets = campaignLogs.Count(static log => log.Level is "alarm" or "error");
                var targetLabels = campaign.TargetDeviceCodes
                    .Select(code => devicesByCode.TryGetValue(code, out var device)
                        ? $"{device.Name} ({code})"
                        : code)
                    .ToArray();
                var recipientEmails = campaign.RecipientIds
                    .Select(id => recipientsById.TryGetValue(id, out var recipient) ? recipient.Email : null)
                    .Where(static email => !string.IsNullOrWhiteSpace(email))
                    .Cast<string>()
                    .ToArray();
                var summary = campaign.Status switch
                {
                    "completed" => $"Aktualizacja zakonczona powodzeniem na {successfulTargets} docelowych PNC.",
                    "partial" => $"Aktualizacja zakonczona czesciowo: sukces {successfulTargets}, bledy {failedTargets}.",
                    "failed" => "Aktualizacja zakonczona niepowodzeniem i wymaga interwencji serwisu.",
                    _ => $"Zaplanowano wysylke przez {campaign.Transport} w oknie {campaign.Window}."
                };

                return new PortalOtaCampaignViewRecord(
                    Id: campaign.Id,
                    Title: campaign.Title,
                    PackageId: campaign.PackageId,
                    PackageName: package.Name,
                    PackageVersion: package.Version,
                    Status: campaign.Status,
                    TargetDeviceCodes: campaign.TargetDeviceCodes,
                    TargetLabels: targetLabels,
                    ScheduledForUtc: campaign.ScheduledForUtc,
                    StartedAtUtc: campaign.StartedAtUtc,
                    CompletedAtUtc: campaign.CompletedAtUtc,
                    Transport: campaign.Transport,
                    Window: campaign.Window,
                    RetryLimit: campaign.RetryLimit,
                    NotifyServiceByEmail: campaign.NotifyServiceByEmail,
                    RecipientIds: campaign.RecipientIds,
                    RecipientEmails: recipientEmails,
                    Notes: campaign.Notes,
                    TargetCount: campaign.TargetDeviceCodes.Length,
                    SuccessfulCount: successfulTargets,
                    FailedCount: failedTargets,
                    Summary: summary);
            })
            .OrderByDescending(static campaign => campaign.ScheduledForUtc)
            .ToArray();

        var summary = new PortalOtaSummaryRecord(
            PackageCount: config.Packages.Length,
            CampaignCount: config.Campaigns.Length,
            ScheduledCount: config.Campaigns.Count(static campaign => campaign.Status == "scheduled"),
            CompletedCount: config.Campaigns.Count(static campaign => campaign.Status == "completed"),
            PartialCount: config.Campaigns.Count(static campaign => campaign.Status == "partial"),
            FailedCount: config.Campaigns.Count(static campaign => campaign.Status == "failed"),
            LogCount: config.Logs.Length,
            EmailCount: config.EmailLogs.Length);

        return new PortalOtaStateRecord(
            Summary: summary,
            Packages: config.Packages,
            ServiceRecipients: config.ServiceRecipients,
            Campaigns: campaignViews,
            Logs: config.Logs.Take(48).ToArray(),
            EmailLogs: config.EmailLogs.Take(12).ToArray());
    }

    private static string BuildEmailBody(
        PortalOtaCampaignRecord campaign,
        PortalOtaPackageRecord package,
        IReadOnlyCollection<string> targetCodes,
        int successCount,
        int failedCount,
        string status)
    {
        return
            $"Automatyczne podsumowanie wdrozenia OTA.\n" +
            $"Kampania: {campaign.Title}\n" +
            $"Pakiet: {package.Name} {package.Version}\n" +
            $"Transport: {campaign.Transport}\n" +
            $"Status: {status}\n" +
            $"Cele: {string.Join(", ", targetCodes)}\n" +
            $"Sukces: {successCount}\n" +
            $"Bledy: {failedCount}\n" +
            $"Opis: {campaign.Notes}";
    }

    private static string NormalizeCampaignStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "completed" => "completed",
            "partial" => "partial",
            "failed" => "failed",
            _ => "scheduled"
        };
    }

    private static string NormalizeMessageType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "alarm" => "alarm",
            "warn" => "warn",
            "error" => "error",
            _ => "info"
        };
    }

    private static string MakeUniqueId(string baseId, ISet<string> usedIds)
    {
        var candidate = baseId;
        var suffix = 2;

        while (!usedIds.Add(candidate))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string Slugify(string? preferredId, string prefix, string seed, int fallbackNumber)
    {
        var source = string.IsNullOrWhiteSpace(preferredId) ? seed : preferredId;
        var slug = new string(source
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        slug = string.Join("-", slug
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(slug)
            ? $"{prefix}-{fallbackNumber}"
            : slug;
    }

    private static PortalOtaConfig DefaultConfig()
    {
        var now = DateTimeOffset.UtcNow;

        return new PortalOtaConfig(
            Packages:
            [
                new(
                    Id: "pkg-pnc-core-2-5-0",
                    Name: "PNC Core",
                    Version: "2.5.0",
                    Target: "PNC OS",
                    FileName: "pnc-core-2.5.0.bin",
                    SizeMb: 128.4,
                    Description: "Pakiet systemowy dla wezlow PNC z poprawkami stabilnosci i telemetrii.",
                    ReleaseNotes: "Poprawki OTA, nowszy agent LTE i rozszerzone logi wdrozeniowe.",
                    Mandatory: true),
                new(
                    Id: "pkg-modem-1-8-4",
                    Name: "LTE Modem Bundle",
                    Version: "1.8.4",
                    Target: "Sterownik modemu",
                    FileName: "lte-modem-1.8.4.swu",
                    SizeMb: 32.7,
                    Description: "Aktualizacja firmware modemu i profili operatorow.",
                    ReleaseNotes: "Lepsza obsluga roamingu, odczytu SIM i fallbacku LTE-M.",
                    Mandatory: false),
                new(
                    Id: "pkg-ui-3-1-0",
                    Name: "PathoNet UI Runtime",
                    Version: "3.1.0",
                    Target: "Warstwa HMI",
                    FileName: "pathonet-ui-3.1.0.pkg",
                    SizeMb: 18.2,
                    Description: "Aktualizacja lokalnego runtime UI dla panelu klienta.",
                    ReleaseNotes: "Nowe karty statusu i lepsze logowanie stanu aktualizacji.",
                    Mandatory: false)
            ],
            Campaigns:
            [
                new(
                    Id: "campaign-core-rollout",
                    Title: "Rollout PNC Core 2.5.0",
                    PackageId: "pkg-pnc-core-2-5-0",
                    TargetDeviceCodes: ["PNC-001", "PNC-002"],
                    ScheduledForUtc: now.AddHours(-3),
                    Transport: "LTE",
                    Window: "okno serwisowe 01:00-04:00",
                    RetryLimit: 2,
                    NotifyServiceByEmail: true,
                    RecipientIds: ["serwis-centralny", "wdrozenia"],
                    Status: "scheduled",
                    Notes: "Aktualizacja bazowa dla dwoch wezlow nadrzednych z restartem kontrolowanym.",
                    CreatedAtUtc: now.AddHours(-5),
                    StartedAtUtc: null,
                    CompletedAtUtc: null),
                new(
                    Id: "campaign-modem-nightly",
                    Title: "Aktualizacja modemu LTE dla wezlow terenowych",
                    PackageId: "pkg-modem-1-8-4",
                    TargetDeviceCodes: ["PNC-004", "PNC-005"],
                    ScheduledForUtc: now.AddHours(5),
                    Transport: "LTE",
                    Window: "okno serwisowe 23:00-02:00",
                    RetryLimit: 3,
                    NotifyServiceByEmail: true,
                    RecipientIds: ["serwis-centralny"],
                    Status: "scheduled",
                    Notes: "Pakiet radiowy z poprawa odczytu MCC/MNC, RSRP i SINR.",
                    CreatedAtUtc: now.AddHours(-1),
                    StartedAtUtc: null,
                    CompletedAtUtc: null)
            ],
            ServiceRecipients:
            [
                new("serwis-centralny", "Serwis Centralny", "Serwis", "serwis@pathonet.local"),
                new("wdrozenia", "Dzial Wdrozen", "Wdrozenia", "wdrozenia@pathonet.local"),
                new("nadzor", "Nadzor Platformy", "Administrator", "nadzor@pathonet.local")
            ],
            Logs: [],
            EmailLogs: []);
    }
}

internal sealed record PortalOtaConfig(
    PortalOtaPackageRecord[] Packages,
    PortalOtaCampaignRecord[] Campaigns,
    PortalOtaServiceRecipientRecord[] ServiceRecipients,
    PortalOtaExecutionLogRecord[] Logs,
    PortalOtaEmailLogRecord[] EmailLogs);

internal sealed record PortalOtaStateRecord(
    PortalOtaSummaryRecord Summary,
    PortalOtaPackageRecord[] Packages,
    PortalOtaServiceRecipientRecord[] ServiceRecipients,
    PortalOtaCampaignViewRecord[] Campaigns,
    PortalOtaExecutionLogRecord[] Logs,
    PortalOtaEmailLogRecord[] EmailLogs);

internal sealed record PortalOtaSummaryRecord(
    int PackageCount,
    int CampaignCount,
    int ScheduledCount,
    int CompletedCount,
    int PartialCount,
    int FailedCount,
    int LogCount,
    int EmailCount);

public sealed record PortalOtaPackageRecord(
    string Id,
    string Name,
    string Version,
    string Target,
    string FileName,
    double SizeMb,
    string Description,
    string ReleaseNotes,
    bool Mandatory);

internal sealed record PortalOtaCampaignRecord(
    string Id,
    string Title,
    string PackageId,
    string[] TargetDeviceCodes,
    DateTimeOffset ScheduledForUtc,
    string Transport,
    string Window,
    int RetryLimit,
    bool NotifyServiceByEmail,
    string[] RecipientIds,
    string Status,
    string Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

internal sealed record PortalOtaCampaignViewRecord(
    string Id,
    string Title,
    string PackageId,
    string PackageName,
    string PackageVersion,
    string Status,
    string[] TargetDeviceCodes,
    string[] TargetLabels,
    DateTimeOffset ScheduledForUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Transport,
    string Window,
    int RetryLimit,
    bool NotifyServiceByEmail,
    string[] RecipientIds,
    string[] RecipientEmails,
    string Notes,
    int TargetCount,
    int SuccessfulCount,
    int FailedCount,
    string Summary);

public sealed record PortalOtaServiceRecipientRecord(
    string Id,
    string DisplayName,
    string Role,
    string Email);

internal sealed record PortalOtaExecutionLogRecord(
    string Id,
    string CampaignId,
    string DeviceCode,
    string Level,
    string Message,
    DateTimeOffset OccurredAtUtc);

internal sealed record PortalOtaEmailLogRecord(
    string Id,
    string CampaignId,
    string Subject,
    string[] Recipients,
    DateTimeOffset SentAtUtc,
    string Body);
