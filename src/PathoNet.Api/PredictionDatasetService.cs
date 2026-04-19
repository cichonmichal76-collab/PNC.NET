using Microsoft.EntityFrameworkCore;

internal sealed class PredictionDatasetService(IDbContextFactory<PathoNetPredictionDbContext> dbContextFactory)
{
    private readonly object _gate = new();
    private bool _initialized;
    private string? _lastFingerprint;
    private DateTimeOffset? _lastCaptureAtUtc;
    private static readonly string[] FeatureColumns =
    [
        "captured_at_utc",
        "port",
        "alias",
        "display_name",
        "current_level",
        "status",
        "risk_score",
        "health_score",
        "warn_count",
        "alarm_count",
        "total_events",
        "alert_pressure",
        "recent_alarm_events",
        "recent_warn_events",
        "fleet_pressure",
        "threshold_reached"
    ];

    private static readonly string[] TargetColumns =
    [
        "label_alarm_in_next_30m",
        "label_batch_failure",
        "label_service_intervention_24h"
    ];

    private static readonly string[] CategoricalColumns =
    [
        "port",
        "alias",
        "display_name",
        "current_level",
        "status"
    ];

    private static readonly string[] NumericColumns =
    [
        "risk_score",
        "health_score",
        "warn_count",
        "alarm_count",
        "total_events",
        "alert_pressure",
        "recent_alarm_events",
        "recent_warn_events",
        "fleet_pressure",
        "threshold_reached"
    ];

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();
        return Task.CompletedTask;
    }

    public PortalPredictionDatasetRecord CaptureAndGetState(
        PortalPredictionTargetRecord[] targets,
        PortalDeviceRecord[] devices,
        PortalAlertRecord[] alerts,
        HistoryEventRecord[] history,
        ActivityBucketRecord[] activity)
    {
        EnsureInitialized();

        using var db = dbContextFactory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;

        if (ShouldCapture(now, devices, alerts, history, activity))
        {
            CaptureSnapshots(db, now, devices, alerts, history, activity, targets);
        }

        FinalizePendingLabels(db, now);

        if (db.ChangeTracker.HasChanges())
        {
            db.SaveChanges();
        }

        return BuildDatasetRecord(db, targets);
    }

    public PortalPredictionTrainingManifestRecord GetTrainingManifest() =>
        new(
            DatasetName: "pathonet-tissue-processor-training",
            FeatureColumns: FeatureColumns,
            TargetColumns: TargetColumns,
            CategoricalColumns: CategoricalColumns,
            NumericColumns: NumericColumns,
            RecommendedPrimaryTarget: "label_alarm_in_next_30m",
            Notes:
            [
                "Export laczy snapshoty cech z auto-domknietymi etykietami targetow.",
                "resolvedOnly=true ogranicza dataset do rekordow z kompletem trzech etykiet treningowych.",
                "ThresholdReached jest eksportowane jako cecha 0/1, gotowa do modelu tablicowego typu XGBoost."
            ]);

    public string ExportTrainingDatasetCsv(bool resolvedOnly)
    {
        EnsureInitialized();

        using var db = dbContextFactory.CreateDbContext();
        var rows = BuildExportRows(db, resolvedOnly).ToArray();
        var columns = FeatureColumns.Concat(TargetColumns).ToArray();
        var lines = new List<string>(rows.Length + 1)
        {
            string.Join(",", columns)
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(",",
            [
                EscapeCsv(row.CapturedAtUtc.ToString("O")),
                EscapeCsv(row.Port),
                EscapeCsv(row.Alias),
                EscapeCsv(row.DisplayName),
                EscapeCsv(row.CurrentLevel),
                EscapeCsv(row.Status),
                row.RiskScore.ToString(),
                row.HealthScore.ToString(),
                row.WarnCount.ToString(),
                row.AlarmCount.ToString(),
                row.TotalEvents.ToString(),
                row.AlertPressure.ToString(),
                row.RecentAlarmEvents.ToString(),
                row.RecentWarnEvents.ToString(),
                row.FleetPressure.ToString(),
                row.ThresholdReached ? "1" : "0",
                FormatNullableLabel(row.LabelAlarmInNext30m),
                FormatNullableLabel(row.LabelBatchFailure),
                FormatNullableLabel(row.LabelServiceIntervention24h)
            ]));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string ExportTrainingDatasetJsonl(bool resolvedOnly)
    {
        EnsureInitialized();

        using var db = dbContextFactory.CreateDbContext();
        var rows = BuildExportRows(db, resolvedOnly);
        var jsonLines = rows.Select(row => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["captured_at_utc"] = row.CapturedAtUtc.ToString("O"),
            ["port"] = row.Port,
            ["alias"] = row.Alias,
            ["display_name"] = row.DisplayName,
            ["current_level"] = row.CurrentLevel,
            ["status"] = row.Status,
            ["risk_score"] = row.RiskScore,
            ["health_score"] = row.HealthScore,
            ["warn_count"] = row.WarnCount,
            ["alarm_count"] = row.AlarmCount,
            ["total_events"] = row.TotalEvents,
            ["alert_pressure"] = row.AlertPressure,
            ["recent_alarm_events"] = row.RecentAlarmEvents,
            ["recent_warn_events"] = row.RecentWarnEvents,
            ["fleet_pressure"] = row.FleetPressure,
            ["threshold_reached"] = row.ThresholdReached ? 1 : 0,
            ["label_alarm_in_next_30m"] = row.LabelAlarmInNext30m is null ? null : (row.LabelAlarmInNext30m.Value ? 1 : 0),
            ["label_batch_failure"] = row.LabelBatchFailure is null ? null : (row.LabelBatchFailure.Value ? 1 : 0),
            ["label_service_intervention_24h"] = row.LabelServiceIntervention24h is null ? null : (row.LabelServiceIntervention24h.Value ? 1 : 0)
        }));

        return string.Join(Environment.NewLine, jsonLines);
    }

    private void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            using var db = dbContextFactory.CreateDbContext();
            db.Database.EnsureCreated();
            _initialized = true;
        }
    }

    private bool ShouldCapture(
        DateTimeOffset now,
        PortalDeviceRecord[] devices,
        PortalAlertRecord[] alerts,
        HistoryEventRecord[] history,
        ActivityBucketRecord[] activity)
    {
        var fingerprint = string.Join('|',
            devices
                .OrderBy(device => device.Port, StringComparer.OrdinalIgnoreCase)
                .Select(device => $"{device.Port}:{device.CurrentLevel}:{device.RiskScore}:{device.HealthScore}:{device.TotalEvents}:{device.WarnCount}:{device.AlarmCount}:{device.ThresholdReached}"))
            + "||alerts:" + alerts.Length
            + "||history:" + history.Take(4).Select(item => $"{item.Port}:{item.Level}:{item.Message}").Aggregate(string.Empty, static (current, next) => current + next)
            + "||activity:" + activity.Take(4).Select(item => $"{item.Label}:{item.Count}:{item.AlarmCount}:{item.WarnCount}").Aggregate(string.Empty, static (current, next) => current + next);

        lock (_gate)
        {
            if (_lastFingerprint == fingerprint && _lastCaptureAtUtc is not null && now - _lastCaptureAtUtc < TimeSpan.FromMinutes(5))
            {
                return false;
            }

            _lastFingerprint = fingerprint;
            _lastCaptureAtUtc = now;
            return true;
        }
    }

    private static void CaptureSnapshots(
        PathoNetPredictionDbContext db,
        DateTimeOffset now,
        PortalDeviceRecord[] devices,
        PortalAlertRecord[] alerts,
        HistoryEventRecord[] history,
        ActivityBucketRecord[] activity,
        PortalPredictionTargetRecord[] targets)
    {
        var alertsByPort = alerts
            .GroupBy(alert => alert.Port, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var historyByPort = history
            .GroupBy(item => item.Port, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var fleetPressure = activity.TakeLast(4).Sum(bucket => (bucket.AlarmCount * 7) + (bucket.WarnCount * 3));

        foreach (var device in devices)
        {
            var portHistory = historyByPort.GetValueOrDefault(device.Port, []);
            var snapshot = new PathoNetPredictionFeatureSnapshotEntity
            {
                CapturedAtUtc = now,
                Alias = device.Alias,
                DisplayName = device.DisplayName,
                Port = device.Port,
                CurrentLevel = device.CurrentLevel,
                Status = device.Status,
                RiskScore = device.RiskScore,
                HealthScore = device.HealthScore,
                WarnCount = device.WarnCount,
                AlarmCount = device.AlarmCount,
                TotalEvents = device.TotalEvents,
                AlertPressure = alertsByPort.GetValueOrDefault(device.Port, 0),
                RecentAlarmEvents = portHistory.Count(item => item.Level is "alarm" or "error"),
                RecentWarnEvents = portHistory.Count(item => item.Level == "warn"),
                FleetPressure = fleetPressure,
                ThresholdReached = device.ThresholdReached,
                Recommendation = device.Recommendation
            };

            db.FeatureSnapshots.Add(snapshot);
            db.SaveChanges();

            foreach (var target in targets)
            {
                db.TargetLabels.Add(new PathoNetPredictionLabelEntity
                {
                    SnapshotId = snapshot.Id,
                    TargetCode = target.Code,
                    Status = "pending",
                    Value = null,
                    DueAtUtc = ResolveDueAtUtc(now, target.Code),
                    LabeledAtUtc = null,
                    Source = "backlog",
                    Summary = $"Oczekuje na label dla {target.Title}."
                });
            }
        }
    }

    private static void FinalizePendingLabels(PathoNetPredictionDbContext db, DateTimeOffset now)
    {
        var dueLabels = db.TargetLabels
            .Include(label => label.Snapshot)
            .AsEnumerable()
            .Where(label => label.Status == "pending" && label.DueAtUtc <= now)
            .OrderBy(label => label.DueAtUtc)
            .Take(500)
            .ToArray();

        foreach (var label in dueLabels)
        {
            var port = label.Snapshot.Port;
            var capturedAtUtc = label.Snapshot.CapturedAtUtc;
            var dueAtUtc = label.DueAtUtc;

            var futureSnapshots = db.FeatureSnapshots
                .Where(snapshot => snapshot.Port == port)
                .AsEnumerable()
                .Where(snapshot =>
                    snapshot.CapturedAtUtc > capturedAtUtc &&
                    snapshot.CapturedAtUtc <= dueAtUtc)
                .OrderBy(snapshot => snapshot.CapturedAtUtc)
                .ToArray();

            var value = ResolveLabelValue(label.TargetCode, futureSnapshots);
            label.Status = "auto";
            label.Value = value;
            label.LabeledAtUtc = now;
            label.Source = "auto";
            label.Summary = value
                ? $"System wykryl spelnienie targetu {label.TargetCode} w oknie czasowym."
                : $"System nie wykryl targetu {label.TargetCode} w zadanym horyzoncie.";
        }
    }

    private static bool ResolveLabelValue(string targetCode, PathoNetPredictionFeatureSnapshotEntity[] futureSnapshots) =>
        targetCode switch
        {
            "alarm_in_next_30m" => futureSnapshots.Any(snapshot =>
                snapshot.CurrentLevel is "alarm" or "error" ||
                snapshot.AlertPressure > 0),
            "batch_failure" => futureSnapshots.Any(snapshot =>
                snapshot.CurrentLevel is "error" ||
                snapshot.Status == "critical" ||
                snapshot.ThresholdReached),
            "service_intervention_24h" => futureSnapshots.Any(snapshot =>
                snapshot.Status == "critical" ||
                snapshot.HealthScore < 55 ||
                snapshot.RiskScore >= 80),
            _ => false
        };

    private static DateTimeOffset ResolveDueAtUtc(DateTimeOffset capturedAtUtc, string targetCode) =>
        targetCode switch
        {
            "alarm_in_next_30m" => capturedAtUtc.AddMinutes(30),
            "batch_failure" => capturedAtUtc.AddHours(8),
            "service_intervention_24h" => capturedAtUtc.AddHours(24),
            _ => capturedAtUtc.AddHours(1)
        };

    private static PortalPredictionDatasetRecord BuildDatasetRecord(
        PathoNetPredictionDbContext db,
        PortalPredictionTargetRecord[] targets)
    {
        var allSnapshots = db.FeatureSnapshots
            .AsEnumerable()
            .OrderByDescending(item => item.CapturedAtUtc)
            .ToArray();

        var snapshots = allSnapshots
            .Take(6)
            .ToArray();

        var allLabels = db.TargetLabels.ToArray();
        var lastCapturedAtUtc = allSnapshots.FirstOrDefault()?.CapturedAtUtc;

        var coverage = targets
            .Select(target =>
            {
                var targetLabels = allLabels.Where(label => label.TargetCode == target.Code).ToArray();
                return new PortalPredictionTargetCoverageRecord(
                    Code: target.Code,
                    Title: target.Title,
                    Horizon: target.Horizon,
                    PendingCount: targetLabels.Count(label => label.Status == "pending"),
                    ResolvedCount: targetLabels.Count(label => label.Status != "pending"),
                    PositiveCount: targetLabels.Count(label => label.Value == true),
                    NegativeCount: targetLabels.Count(label => label.Value == false));
            })
            .ToArray();

        return new PortalPredictionDatasetRecord(
            SnapshotCount: db.FeatureSnapshots.Count(),
            PendingLabelCount: allLabels.Count(label => label.Status == "pending"),
            ResolvedLabelCount: allLabels.Count(label => label.Status != "pending"),
            LastCapturedAtUtc: lastCapturedAtUtc,
            TargetCoverage: coverage,
            RecentSamples: snapshots
                .Select(snapshot => new PortalPredictionSampleRecord(
                    Alias: snapshot.Alias,
                    DisplayName: snapshot.DisplayName,
                    Port: snapshot.Port,
                    CapturedAtUtc: snapshot.CapturedAtUtc,
                    CurrentLevel: snapshot.CurrentLevel,
                    Status: snapshot.Status,
                    RiskScore: snapshot.RiskScore,
                    HealthScore: snapshot.HealthScore,
                    AlertPressure: snapshot.AlertPressure))
            .ToArray());
    }

    private static IEnumerable<PredictionTrainingExportRow> BuildExportRows(PathoNetPredictionDbContext db, bool resolvedOnly)
    {
        var snapshots = db.FeatureSnapshots
            .AsNoTracking()
            .AsEnumerable()
            .OrderBy(item => item.CapturedAtUtc)
            .ToArray();

        var labelsBySnapshot = db.TargetLabels
            .AsNoTracking()
            .GroupBy(label => label.SnapshotId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var rows = snapshots.Select(snapshot =>
        {
            var labels = labelsBySnapshot.GetValueOrDefault(snapshot.Id, []);
            return new PredictionTrainingExportRow(
                CapturedAtUtc: snapshot.CapturedAtUtc,
                Port: snapshot.Port,
                Alias: snapshot.Alias,
                DisplayName: snapshot.DisplayName,
                CurrentLevel: snapshot.CurrentLevel,
                Status: snapshot.Status,
                RiskScore: snapshot.RiskScore,
                HealthScore: snapshot.HealthScore,
                WarnCount: snapshot.WarnCount,
                AlarmCount: snapshot.AlarmCount,
                TotalEvents: snapshot.TotalEvents,
                AlertPressure: snapshot.AlertPressure,
                RecentAlarmEvents: snapshot.RecentAlarmEvents,
                RecentWarnEvents: snapshot.RecentWarnEvents,
                FleetPressure: snapshot.FleetPressure,
                ThresholdReached: snapshot.ThresholdReached,
                LabelAlarmInNext30m: ResolveLabel(labels, "alarm_in_next_30m"),
                LabelBatchFailure: ResolveLabel(labels, "batch_failure"),
                LabelServiceIntervention24h: ResolveLabel(labels, "service_intervention_24h"));
        });

        return resolvedOnly
            ? rows.Where(row =>
                row.LabelAlarmInNext30m is not null &&
                row.LabelBatchFailure is not null &&
                row.LabelServiceIntervention24h is not null)
            : rows;
    }

    private static bool? ResolveLabel(PathoNetPredictionLabelEntity[] labels, string targetCode) =>
        labels.FirstOrDefault(label => label.TargetCode == targetCode && label.Status != "pending")?.Value;

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private static string FormatNullableLabel(bool? value) =>
        value switch
        {
            true => "1",
            false => "0",
            null => string.Empty
        };

    private sealed record PredictionTrainingExportRow(
        DateTimeOffset CapturedAtUtc,
        string Port,
        string Alias,
        string DisplayName,
        string CurrentLevel,
        string Status,
        int RiskScore,
        int HealthScore,
        int WarnCount,
        int AlarmCount,
        int TotalEvents,
        int AlertPressure,
        int RecentAlarmEvents,
        int RecentWarnEvents,
        int FleetPressure,
        bool ThresholdReached,
        bool? LabelAlarmInNext30m,
        bool? LabelBatchFailure,
        bool? LabelServiceIntervention24h);
}
