using System.Text.Json;

internal sealed class RulebookStore
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PortalRulebookConfig _current;

    public RulebookStore(string contentRoot)
    {
        var dataDirectory = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "portal-rulebook.json");
        _current = LoadOrSeed();
    }

    public PortalRulebookConfig GetConfig() => _current;

    public async Task<PortalRulebookConfig> SaveAsync(
        PortalRulebookConfig candidate,
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

    private PortalRulebookConfig LoadOrSeed()
    {
        if (!File.Exists(_filePath))
        {
            var seeded = Normalize(DefaultRulebook());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(seeded, _jsonOptions));
            return seeded;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<PortalRulebookConfig>(json, _jsonOptions);
            return Normalize(loaded ?? DefaultRulebook());
        }
        catch (JsonException)
        {
            var fallback = Normalize(DefaultRulebook());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(fallback, _jsonOptions));
            return fallback;
        }
    }

    private static PortalRulebookConfig Normalize(PortalRulebookConfig candidate)
    {
        var normalizedUsers = new List<PortalUserRecord>();
        var usedUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userIdAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (user, index) in (candidate.Users ?? []).Select(static (user, index) => (user, index)))
        {
            var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? $"Uzytkownik {index + 1}"
                : user.DisplayName.Trim();
            var id = MakeUniqueId(
                Slugify(user.Id, "user", displayName, index + 1),
                usedUserIds);

            normalizedUsers.Add(new PortalUserRecord(
                Id: id,
                DisplayName: displayName,
                Role: string.IsNullOrWhiteSpace(user.Role) ? "Operator" : user.Role.Trim(),
                Email: user.Email?.Trim() ?? string.Empty,
                Phone: user.Phone?.Trim() ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(user.Id))
            {
                userIdAliases[user.Id.Trim()] = id;
            }

            userIdAliases[id] = id;
        }

        var userIds = normalizedUsers.Select(static user => user.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedRules = new List<PortalMessageRuleRecord>();
        var usedRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rule, index) in (candidate.Rules ?? []).Select(static (rule, index) => (rule, index)))
        {
            if (string.IsNullOrWhiteSpace(rule.MatchText))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(rule.Name)
                ? $"Regula {index + 1}"
                : rule.Name.Trim();
            var id = MakeUniqueId(
                Slugify(rule.Id, "rule", name, index + 1),
                usedRuleIds);

            normalizedRules.Add(new PortalMessageRuleRecord(
                Id: id,
                Name: name,
                MatchText: rule.MatchText.Trim(),
                MessageType: NormalizeMessageType(rule.MessageType, rule.MatchText),
                Description: rule.Description?.Trim() ?? string.Empty,
                ThresholdHours: Math.Round(Math.Clamp(rule.ThresholdHours, 0, 720), 2),
                SendSms: rule.SendSms,
                SendEmail: rule.SendEmail,
                RecipientIds: (rule.RecipientIds ?? [])
                    .Select(recipientId =>
                    {
                        if (string.IsNullOrWhiteSpace(recipientId))
                        {
                            return null;
                        }

                        var trimmedRecipientId = recipientId.Trim();
                        return userIdAliases.TryGetValue(trimmedRecipientId, out var normalizedRecipientId)
                            ? normalizedRecipientId
                            : userIds.Contains(trimmedRecipientId)
                                ? trimmedRecipientId
                                : null;
                    })
                    .OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Enabled: rule.Enabled));
        }

        return new PortalRulebookConfig(
            Users: normalizedUsers.ToArray(),
            Rules: normalizedRules.ToArray());
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

    private static string NormalizeMessageType(string? value, string? matchText = null)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "alarm" => "alarm",
                "warn" or "warning" or "ostrzezenie" => "warn",
                "error" or "blad" => "error",
                "info" or "information" or "informacja" => "info",
                _ => "any"
            };
        }

        var source = matchText?.ToLowerInvariant() ?? string.Empty;
        if (source.Contains("alarm", StringComparison.Ordinal))
        {
            return "alarm";
        }

        if (source.Contains("warn", StringComparison.Ordinal))
        {
            return "warn";
        }

        if (source.Contains("error", StringComparison.Ordinal))
        {
            return "error";
        }

        if (source.Contains("info", StringComparison.Ordinal))
        {
            return "info";
        }

        return "any";
    }

    private static PortalRulebookConfig DefaultRulebook() =>
        new(
            Users:
            [
                new("admin-anna", "Anna Koordynator", "Administrator", "anna.koordynator@pathonet.local", "+48 600 100 101"),
                new("serwis-marek", "Marek Serwis", "Serwis", "marek.serwis@pathonet.local", "+48 600 100 202"),
                new("utrzymanie-ewa", "Ewa Utrzymanie", "Utrzymanie", "ewa.utrzymanie@pathonet.local", "+48 600 100 303")
            ],
            Rules:
            [
                new(
                    Id: "rule-device-4-emergency-stop",
                    Name: "Awaryjne zatrzymanie silnika",
                    MatchText: "Device_4|ALARM SENSOR_TIMEOUT",
                    MessageType: "alarm",
                    Description: "Regula specyficzna dla Device_4 z eskalacja na poziomie krytycznym.",
                    ThresholdHours: 5,
                    SendSms: true,
                    SendEmail: true,
                    RecipientIds: ["admin-anna", "serwis-marek"],
                    Enabled: true),
                new(
                    Id: "rule-generic-sensor-timeout",
                    Name: "Krytyczny timeout czujnika",
                    MatchText: "ALARM SENSOR_TIMEOUT",
                    MessageType: "alarm",
                    Description: "Mapa biznesowa dla timeoutow czujnika na pozostalych urzadzeniach.",
                    ThresholdHours: 4,
                    SendSms: false,
                    SendEmail: true,
                    RecipientIds: ["serwis-marek"],
                    Enabled: true),
                new(
                    Id: "rule-buffer-high",
                    Name: "Bufor komunikacji blisko limitu",
                    MatchText: "WARN BUFFER HIGH",
                    MessageType: "warn",
                    Description: "Wczesne ostrzezenie przed opoznieniem lub utrata ramek.",
                    ThresholdHours: 2,
                    SendSms: false,
                    SendEmail: true,
                    RecipientIds: ["utrzymanie-ewa"],
                    Enabled: true)
            ]);
}

internal static class BootstrapPaths
{
    public static string ResolveContentRoot()
    {
        var explicitContentRoot = Environment.GetEnvironmentVariable("PATHONET_CONTENT_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitContentRoot))
        {
            return Path.GetFullPath(explicitContentRoot);
        }

        var explicitRoot = Environment.GetEnvironmentVariable("PATHONET_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var fullRoot = Path.GetFullPath(explicitRoot);
            if (Directory.Exists(Path.Combine(fullRoot, "wwwroot")))
            {
                return fullRoot;
            }
        }

        var cursor = new DirectoryInfo(AppContext.BaseDirectory);

        while (cursor is not null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "wwwroot", "index.html")))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
