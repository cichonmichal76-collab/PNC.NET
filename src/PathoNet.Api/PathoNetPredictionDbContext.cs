using Microsoft.EntityFrameworkCore;

internal sealed class PathoNetPredictionDbContext(DbContextOptions<PathoNetPredictionDbContext> options) : DbContext(options)
{
    public DbSet<PathoNetPredictionFeatureSnapshotEntity> FeatureSnapshots => Set<PathoNetPredictionFeatureSnapshotEntity>();
    public DbSet<PathoNetPredictionLabelEntity> TargetLabels => Set<PathoNetPredictionLabelEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PathoNetPredictionFeatureSnapshotEntity>(entity =>
        {
            entity.ToTable("PredictionFeatureSnapshots");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Alias).HasMaxLength(120);
            entity.Property(item => item.DisplayName).HasMaxLength(160);
            entity.Property(item => item.Port).HasMaxLength(80);
            entity.Property(item => item.CurrentLevel).HasMaxLength(40);
            entity.Property(item => item.Status).HasMaxLength(40);
            entity.Property(item => item.Recommendation).HasMaxLength(512);
            entity.HasIndex(item => new { item.Port, item.CapturedAtUtc });
        });

        modelBuilder.Entity<PathoNetPredictionLabelEntity>(entity =>
        {
            entity.ToTable("PredictionTargetLabels");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TargetCode).HasMaxLength(80);
            entity.Property(item => item.Status).HasMaxLength(40);
            entity.Property(item => item.Source).HasMaxLength(80);
            entity.Property(item => item.Summary).HasMaxLength(512);
            entity.HasIndex(item => new { item.TargetCode, item.Status, item.DueAtUtc });
            entity.HasOne(item => item.Snapshot)
                .WithMany()
                .HasForeignKey(item => item.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

internal sealed class PathoNetPredictionFeatureSnapshotEntity
{
    public long Id { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public string CurrentLevel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RiskScore { get; set; }
    public int HealthScore { get; set; }
    public int WarnCount { get; set; }
    public int AlarmCount { get; set; }
    public int TotalEvents { get; set; }
    public int AlertPressure { get; set; }
    public int RecentAlarmEvents { get; set; }
    public int RecentWarnEvents { get; set; }
    public int FleetPressure { get; set; }
    public bool ThresholdReached { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

internal sealed class PathoNetPredictionLabelEntity
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }
    public PathoNetPredictionFeatureSnapshotEntity Snapshot { get; set; } = default!;
    public string TargetCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool? Value { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public DateTimeOffset? LabeledAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
