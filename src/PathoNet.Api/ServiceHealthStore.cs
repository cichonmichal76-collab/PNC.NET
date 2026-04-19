using System.Diagnostics;
using System.Text.Json;
using PathoNet.Infrastructure.Hosting;

internal sealed class ServiceHealthStore
{
    private static readonly ServiceDescriptorDefinition[] KnownServices =
    [
        new("PathoNet.Api", "API", "Backend HTTP i portal Razor Pages"),
        new("PathoNet.Hub", "Hub", "Bufor i dystrybucja komunikatow pomiedzy procesami"),
        new("PathoNet.ApiSender", "ApiSender", "Dostawa notify i heartbeat do backendu"),
        new("PathoNet.Collector", "Collector", "Symulacja hardware i generowanie ramek")
    ];

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private readonly string _root;
    private readonly string _sharedStateDirectory;
    private readonly string _tmpDirectory;
    private readonly string _pidFilePath;
    private readonly string _restartHistoryFilePath;
    private readonly string _restartScriptPath;

    public ServiceHealthStore(string contentRoot)
    {
        _root = PathoNetRuntimePaths.ResolvePathoNetRoot(contentRoot);
        _sharedStateDirectory = PathoNetRuntimePaths.ResolveSharedStateDirectory(_root);
        _tmpDirectory = Path.Combine(_root, "tmp");
        _pidFilePath = Path.Combine(_tmpDirectory, "pathonet-mock-pids.json");
        _restartHistoryFilePath = Path.Combine(_sharedStateDirectory, "restart-history.json");
        _restartScriptPath = Path.Combine(_root, "scripts", "Restart-PathoNet-Service.ps1");
    }

    public PortalServiceHealthStateRecord GetState()
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var logMap = LoadMockPidEntries();
        var history = LoadRestartHistory()
            .OrderByDescending(static entry => entry.RequestedAtUtc)
            .ToArray();

        var services = KnownServices
            .Select(definition => BuildStatus(definition, generatedAtUtc, logMap))
            .ToArray();

        var summary = new PortalServiceHealthSummaryRecord(
            TotalCount: services.Length,
            OnlineCount: services.Count(static service => service.Status == "online"),
            AttentionCount: services.Count(static service => service.Status == "attention"),
            CriticalCount: services.Count(static service => service.Status == "critical"),
            SystemdCount: services.Count(static service => service.SystemdDetected),
            WatchdogCount: services.Count(static service => service.WatchdogActive),
            TotalRestartCount: services.Sum(static service => service.RestartCount),
            RecentRestartCount: history.Length,
            FailedRestartCount: history.Count(static entry => entry.Status == "failed"),
            PendingRestartCount: history.Count(static entry => entry.Status == "scheduled"));

        var restartHistory = history
            .Take(20)
            .Select(MapRestartHistory)
            .ToArray();

        return new PortalServiceHealthStateRecord(generatedAtUtc, summary, services, restartHistory);
    }

    public async Task<PortalServiceRestartRequestResultRecord> RequestRestartAsync(
        string serviceName,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return BuildRejectedResult("unknown", "Nie podano nazwy uslugi.");
        }

        var definition = FindDefinition(serviceName);
        if (definition is null)
        {
            return BuildRejectedResult(serviceName, "Nie znaleziono uslugi w stacku PathoNet.");
        }

        var logMap = LoadMockPidEntries();
        logMap.TryGetValue(definition.Name, out var logEntry);
        var snapshot = LoadSnapshot(definition.Name);

        if (!SupportsRestart(snapshot, logEntry))
        {
            var modeMessage = snapshot?.SystemdDetected == true
                ? "Restart z panelu jest przygotowany dla lokalnego mocka; dla systemd zostawiamy na razie tryb obserwacji."
                : "Brak wpisu procesu w lokalnym launcherze albo skrypt restartu nie jest dostepny.";
            return BuildRejectedResult(definition.Name, modeMessage);
        }

        var requestedAtUtc = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid().ToString("N");
        var historyEntry = new ServiceRestartHistoryEntry(
            Id: eventId,
            ServiceName: definition.Name,
            Status: "scheduled",
            Mode: "local-script",
            RequestedBy: string.IsNullOrWhiteSpace(requestedBy) ? "operator" : requestedBy,
            RequestedAtUtc: requestedAtUtc,
            CompletedAtUtc: null,
            PreviousProcessId: snapshot?.ProcessId ?? logEntry?.Pid,
            CurrentProcessId: null,
            Summary: $"Zaplanowano restart uslugi {definition.DisplayName} z panelu serwisowego.");

        await UpsertRestartHistoryEntryAsync(historyEntry, cancellationToken);

        try
        {
            LaunchRestartScript(definition.Name, historyEntry.Id, historyEntry.RequestedBy);
        }
        catch (Exception ex)
        {
            var failedEntry = historyEntry with
            {
                Status = "failed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Summary = $"Nie udalo sie uruchomic skryptu restartu: {ex.Message}"
            };

            await UpsertRestartHistoryEntryAsync(failedEntry, cancellationToken);

            return BuildRejectedResult(
                definition.Name,
                $"Nie udalo sie uruchomic restartu uslugi {definition.DisplayName}: {ex.Message}",
                "local-script",
                requestedAtUtc,
                historyEntry.Id);
        }

        var message = string.Equals(definition.Name, "PathoNet.Api", StringComparison.OrdinalIgnoreCase)
            ? "Restart API zostal zaplanowany. Odswiez panel po kilku sekundach."
            : $"Restart uslugi {definition.DisplayName} zostal uruchomiony.";

        return new PortalServiceRestartRequestResultRecord(
            Accepted: true,
            ServiceName: definition.Name,
            Status: "scheduled",
            Mode: "local-script",
            RequestedAtUtc: requestedAtUtc,
            EventId: historyEntry.Id,
            Message: message);
    }

    private PortalServiceStatusRecord BuildStatus(
        ServiceDescriptorDefinition definition,
        DateTimeOffset generatedAtUtc,
        IReadOnlyDictionary<string, MockPidEntry> logMap)
    {
        var snapshot = LoadSnapshot(definition.Name);
        logMap.TryGetValue(definition.Name, out var logEntry);

        var processId = snapshot?.ProcessId ?? logEntry?.Pid;
        var processAlive = processId is int pid && pid > 0 && IsProcessAlive(pid);
        var watchdogFresh = IsWatchdogFresh(snapshot, generatedAtUtc);
        var status = ResolveStatus(snapshot, processAlive, watchdogFresh);
        var runtimeMode = ResolveRuntimeMode(snapshot, logEntry);
        var summary = BuildSummary(definition, snapshot, processAlive, watchdogFresh, logEntry);

        return new PortalServiceStatusRecord(
            Name: definition.Name,
            DisplayName: definition.DisplayName,
            Status: status,
            RuntimeMode: runtimeMode,
            RestartCount: snapshot?.RestartCount ?? 0,
            ProcessAlive: processAlive,
            ProcessId: processId,
            SystemdDetected: snapshot?.SystemdDetected ?? false,
            NotifierEnabled: snapshot?.SystemdNotifierEnabled ?? false,
            WatchdogActive: snapshot?.WatchdogActive ?? false,
            WatchdogIntervalSeconds: snapshot?.WatchdogIntervalSeconds,
            StartedAtUtc: snapshot?.StartedAtUtc,
            UpdatedAtUtc: snapshot?.UpdatedAtUtc,
            LastWatchdogHeartbeatUtc: snapshot?.LastWatchdogHeartbeatUtc,
            LastStoppedAtUtc: snapshot?.LastStoppedAtUtc,
            HostEnvironment: snapshot?.HostEnvironment ?? "unknown",
            MachineName: snapshot?.MachineName ?? Environment.MachineName,
            WorkingDirectory: snapshot?.WorkingDirectory ?? string.Empty,
            SupportsRestart: SupportsRestart(snapshot, logEntry),
            RestartMode: ResolveRestartMode(snapshot, logEntry),
            Summary: summary,
            StdoutPath: logEntry?.Stdout,
            StderrPath: logEntry?.Stderr);
    }

    private ServiceRuntimeSnapshot? LoadSnapshot(string serviceName)
    {
        var filePath = Path.Combine(
            _sharedStateDirectory,
            $"{PathoNetRuntimePaths.NormalizeServiceFileName(serviceName)}.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ServiceRuntimeSnapshot>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IReadOnlyDictionary<string, MockPidEntry> LoadMockPidEntries()
    {
        if (!File.Exists(_pidFilePath))
        {
            return new Dictionary<string, MockPidEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(_pidFilePath);
            var entries = JsonSerializer.Deserialize<MockPidEntry[]>(json, _jsonOptions) ?? [];
            return entries.ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, MockPidEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private ServiceRestartHistoryEntry[] LoadRestartHistory()
    {
        if (!File.Exists(_restartHistoryFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_restartHistoryFilePath);
            var entries = JsonSerializer.Deserialize<ServiceRestartHistoryEntry[]>(json, _jsonOptions);
            if (entries is not null)
            {
                return entries;
            }

            var single = JsonSerializer.Deserialize<ServiceRestartHistoryEntry>(json, _jsonOptions);
            return single is null ? [] : [single];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task UpsertRestartHistoryEntryAsync(ServiceRestartHistoryEntry entry, CancellationToken cancellationToken)
    {
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            var history = LoadRestartHistory().ToList();
            var existingIndex = history.FindIndex(current => string.Equals(current.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                history[existingIndex] = entry;
            }
            else
            {
                history.Add(entry);
            }

            var normalized = history
                .OrderByDescending(static current => current.RequestedAtUtc)
                .Take(100)
                .ToArray();

            var json = JsonSerializer.Serialize(normalized, _jsonOptions);
            await File.WriteAllTextAsync(_restartHistoryFilePath, json, cancellationToken);
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private void LaunchRestartScript(string serviceName, string eventId, string requestedBy)
    {
        var powershellPath = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var delaySeconds = string.Equals(serviceName, "PathoNet.Api", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

        var arguments = string.Join(
            " ",
            [
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Quote(_restartScriptPath),
                "-ServiceName",
                Quote(serviceName),
                "-EventId",
                Quote(eventId),
                "-RequestedBy",
                Quote(requestedBy),
                "-DelaySeconds",
                delaySeconds.ToString()
            ]);

        var startInfo = new ProcessStartInfo
        {
            FileName = powershellPath,
            Arguments = arguments,
            WorkingDirectory = _root,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("System nie uruchomil procesu restartu.");
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsWatchdogFresh(ServiceRuntimeSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null || !snapshot.WatchdogActive || snapshot.LastWatchdogHeartbeatUtc is null)
        {
            return false;
        }

        var intervalSeconds = Math.Max(snapshot.WatchdogIntervalSeconds ?? 0, 1);
        return now - snapshot.LastWatchdogHeartbeatUtc.Value <= TimeSpan.FromSeconds(intervalSeconds * 2.2);
    }

    private static string ResolveStatus(
        ServiceRuntimeSnapshot? snapshot,
        bool processAlive,
        bool watchdogFresh)
    {
        if (snapshot is null)
        {
            return processAlive ? "attention" : "critical";
        }

        if (!processAlive)
        {
            return "critical";
        }

        if (snapshot.Status == "stopping")
        {
            return "attention";
        }

        if (snapshot.WatchdogActive)
        {
            return watchdogFresh ? "online" : "critical";
        }

        if (snapshot.SystemdDetected && !snapshot.SystemdNotifierEnabled)
        {
            return "attention";
        }

        return "online";
    }

    private static string ResolveRuntimeMode(ServiceRuntimeSnapshot? snapshot, MockPidEntry? logEntry)
    {
        if (snapshot is null)
        {
            return logEntry is null ? "brak danych" : "lokalny host (pid only)";
        }

        if (snapshot.WatchdogActive)
        {
            return $"systemd + watchdog {(snapshot.WatchdogIntervalSeconds ?? 0):0.#} s";
        }

        if (snapshot.SystemdDetected && snapshot.SystemdNotifierEnabled)
        {
            return "systemd notify";
        }

        if (snapshot.SystemdDetected)
        {
            return "systemd bez notify";
        }

        return "lokalny host";
    }

    private string ResolveRestartMode(ServiceRuntimeSnapshot? snapshot, MockPidEntry? logEntry)
    {
        if (snapshot?.SystemdDetected == true)
        {
            return "systemd";
        }

        if (logEntry is null || !File.Exists(_restartScriptPath))
        {
            return "brak";
        }

        return "panel lokalny";
    }

    private bool SupportsRestart(ServiceRuntimeSnapshot? snapshot, MockPidEntry? logEntry) =>
        OperatingSystem.IsWindows()
        && snapshot?.SystemdDetected != true
        && logEntry is not null
        && File.Exists(_restartScriptPath);

    private static string BuildSummary(
        ServiceDescriptorDefinition definition,
        ServiceRuntimeSnapshot? snapshot,
        bool processAlive,
        bool watchdogFresh,
        MockPidEntry? logEntry)
    {
        if (snapshot is null)
        {
            return processAlive
                ? $"{definition.Description}. Proces widoczny w launcherze, ale brak pliku stanu runtime."
                : $"{definition.Description}. Brak pliku stanu runtime.";
        }

        if (!processAlive)
        {
            return $"{definition.Description}. Proces nie odpowiada, ostatni zapis statusu: {snapshot.Status}.";
        }

        if (snapshot.WatchdogActive)
        {
            return watchdogFresh
                ? $"{definition.Description}. Watchdog i notify dzialaja poprawnie."
                : $"{definition.Description}. Watchdog nie odswiezyl sie w oczekiwanym oknie.";
        }

        if (snapshot.SystemdDetected)
        {
            return snapshot.SystemdNotifierEnabled
                ? $"{definition.Description}. Host pracuje pod systemd i nadaje notify."
                : $"{definition.Description}. Host pracuje pod systemd bez aktywnego watchdoga.";
        }

        return logEntry is not null
            ? $"{definition.Description}. Tryb lokalny z launcherem mocka."
            : $"{definition.Description}. Tryb lokalny bez launcherowego wpisu.";
    }

    private static PortalServiceRestartHistoryRecord MapRestartHistory(ServiceRestartHistoryEntry entry) =>
        new(
            Id: entry.Id,
            ServiceName: entry.ServiceName,
            DisplayName: FindDefinition(entry.ServiceName)?.DisplayName ?? entry.ServiceName,
            Status: entry.Status,
            Mode: entry.Mode,
            RequestedBy: entry.RequestedBy,
            RequestedAtUtc: entry.RequestedAtUtc,
            CompletedAtUtc: entry.CompletedAtUtc,
            PreviousProcessId: entry.PreviousProcessId,
            CurrentProcessId: entry.CurrentProcessId,
            Summary: entry.Summary);

    private static PortalServiceRestartRequestResultRecord BuildRejectedResult(
        string serviceName,
        string message,
        string mode = "brak",
        DateTimeOffset? requestedAtUtc = null,
        string? eventId = null) =>
        new(
            Accepted: false,
            ServiceName: serviceName,
            Status: "rejected",
            Mode: mode,
            RequestedAtUtc: requestedAtUtc ?? DateTimeOffset.UtcNow,
            EventId: eventId,
            Message: message);

    private static ServiceDescriptorDefinition? FindDefinition(string serviceName) =>
        KnownServices.FirstOrDefault(definition => string.Equals(definition.Name, serviceName, StringComparison.OrdinalIgnoreCase));

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private sealed record ServiceDescriptorDefinition(string Name, string DisplayName, string Description);

    private sealed record MockPidEntry(
        string Name,
        int Pid,
        string Executable,
        string Arguments,
        string Stdout,
        string Stderr);

    private sealed record ServiceRuntimeSnapshot(
        string ServiceName,
        string Status,
        int RestartCount,
        int ProcessId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? LastWatchdogHeartbeatUtc,
        DateTimeOffset? LastStoppedAtUtc,
        bool SystemdDetected,
        bool SystemdNotifierEnabled,
        bool WatchdogActive,
        double? WatchdogIntervalSeconds,
        string HostEnvironment,
        string MachineName,
        string WorkingDirectory);

    private sealed record ServiceRestartHistoryEntry(
        string Id,
        string ServiceName,
        string Status,
        string Mode,
        string RequestedBy,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        int? PreviousProcessId,
        int? CurrentProcessId,
        string Summary);
}
