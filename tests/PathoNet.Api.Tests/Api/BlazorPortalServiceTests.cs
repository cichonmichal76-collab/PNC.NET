using PathoNet.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using PathoNet.Infrastructure.Hosting;

namespace PathoNet.Api.Tests.Api;

public sealed class BlazorPortalServiceTests
{
    [Fact]
    public async Task SaveRuleAsync_PersistsOnlyKnownRecipients()
    {
        using var root = new PathoNetTestRoot();
        var (_, service) = CreatePortalService(root.RootPath);
        var state = service.GetRulebookState();
        var validUserId = state.Users[0].Id;

        var result = await service.SaveRuleAsync(
            new BlazorRuleInputRecord(
                RuleId: null,
                Name: "Nowa regula alarmowa",
                MatchText: "ALARM TEST",
                MessageType: "alarm",
                Description: "Opis",
                ThresholdHours: 6.5,
                SendSms: true,
                SendEmail: true,
                Enabled: true,
                RecipientIds: [validUserId, "ghost-user"]),
            CancellationToken.None);

        Assert.True(result.Success);

        var updated = service.GetRulebookState();
        var savedRule = Assert.Single(updated.Rules.Where(rule => rule.Name == "Nowa regula alarmowa"));
        Assert.Equal(["" + validUserId], savedRule.RecipientIds);
        Assert.Equal("alarm", savedRule.MessageType);
        Assert.Equal(6.5, savedRule.ThresholdHours);
    }

    [Fact]
    public async Task SaveConnectionAsync_RecalculatesCountersAndDefaults()
    {
        using var root = new PathoNetTestRoot();
        var (_, service) = CreatePortalService(root.RootPath);
        var owner = service.GetFleetState().PncDevices[0];
        var beforeRs232 = owner.Rs232Connected;

        var result = await service.SaveConnectionAsync(
            new BlazorPncConnectionInputRecord(
                OwnerDeviceCode: owner.DeviceCode,
                OriginalConnectionId: null,
                InterfaceType: "rs232",
                PortName: null,
                DeviceName: "Sterownik testowy",
                Protocol: null,
                Status: null,
                Notes: "Nowe mapowanie",
                BaudRate: 4800),
            CancellationToken.None);

        Assert.True(result.Success);

        var updatedOwner = service.GetFleetState().PncDevices.Single(device => device.DeviceCode == owner.DeviceCode);
        var connection = Assert.Single(updatedOwner.Connections.Where(item => item.DeviceName == "Sterownik testowy"));
        Assert.Equal(beforeRs232 + 1, updatedOwner.Rs232Connected);
        Assert.StartsWith("COM", connection.PortName, StringComparison.Ordinal);
        Assert.Equal("MODBUS RTU", connection.Protocol);
        Assert.Equal(4800, connection.BaudRate);
        Assert.Equal("online", connection.Status);
    }

    [Fact]
    public async Task DeletePackageAsync_RejectsPackageAssignedToCampaign()
    {
        using var root = new PathoNetTestRoot();
        var (otaStore, service) = CreatePortalService(root.RootPath);
        var packageId = otaStore.GetConfig().Campaigns[0].PackageId;

        var result = await service.DeletePackageAsync(packageId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("kampanii OTA", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestHardwarePortSignalAsync_ForKnownPort_ReturnsDiagnosticPayload()
    {
        using var root = new PathoNetTestRoot();
        var (_, service) = CreatePortalService(root.RootPath);

        var result = await service.TestHardwarePortSignalAsync("RS232/1", CancellationToken.None);

        Assert.Equal("RS232/1", result.PortId);
        Assert.NotEmpty(result.Observations);
        Assert.False(string.IsNullOrWhiteSpace(result.Recommendation));
    }

    [Fact]
    public async Task TestHardwarePortSignalAsync_ForUnknownPort_ReturnsFailure()
    {
        using var root = new PathoNetTestRoot();
        var (_, service) = CreatePortalService(root.RootPath);

        var result = await service.TestHardwarePortSignalAsync("UNKNOWN/PORT", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("critical", result.Tone);
        Assert.Contains("Nie znaleziono", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HardwareIntegrationStateAsync_UsesStructuredCollectorRuntimeState()
    {
        using var root = new PathoNetTestRoot();
        var runtimeFilePath = PathoNetRuntimePaths.ResolveCollectorRuntimeStateFilePath(root.RootPath);
        root.WriteJsonFile(
            runtimeFilePath,
            new CollectorRuntimeStateSnapshotDocument
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                DeviceId = "test-device",
                Ports =
                [
                    new CollectorPortRuntimeStateDocument
                    {
                        PortId = "RS232/1",
                        Alias = "Procesor tkankowy",
                        InterfaceType = "rs232",
                        DevicePath = "/dev/ttyEM0",
                        State = "rx",
                        CablePresent = true,
                        LinkUp = null,
                        RxActive = true,
                        TxActive = false,
                        SimulationFallback = false,
                        StateSinceUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
                        LastTransitionAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
                        LastDetectedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
                        LastOpenedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-25),
                        LastRxAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                        LastTxAtUtc = null,
                        RxCounter = 128,
                        TxCounter = 0,
                        Summary = "Aktywny odbior danych na RS232/1.",
                        LastRaw = "53 54 41 54 55 53",
                        LastText = "STATUS;RUN;STEP=5"
                    }
                ]
            });

        var (_, service) = CreatePortalService(root.RootPath);

        var state = await service.GetHardwareIntegrationStateAsync(CancellationToken.None);
        var port = Assert.Single(state.Ports.Where(item => item.PortId == "RS232/1"));

        Assert.Equal("rx", port.ConnectionState);
        Assert.True(port.Detected);
        Assert.True(port.CablePresent);
        Assert.True(port.RxActive);
        Assert.False(port.TxActive);
        Assert.Equal(128, port.RxCounter);
        Assert.Equal("STATUS;RUN;STEP=5", port.LastText);
    }

    [Fact]
    public async Task DashboardState_ContainsPredictionModelRecommendation()
    {
        using var root = new PathoNetTestRoot();
        var (_, service) = CreatePortalService(root.RootPath);

        var state = await service.GetServiceDashboardAsync(CancellationToken.None);

        Assert.Equal("XGBoost", state.PortalState.PredictionAnalysis.RecommendedModel.ModelName);
        Assert.NotEmpty(state.PortalState.PredictionAnalysis.TargetLabels);
        Assert.NotEmpty(state.PortalState.PredictionAnalysis.FeatureGroups);
        Assert.NotEmpty(state.PortalState.PredictionAnalysis.NormalizationPipeline);
        Assert.NotNull(state.PortalState.PredictionAnalysis.Dataset);
        Assert.NotEmpty(state.PortalState.PredictionAnalysis.Dataset.TargetCoverage);
    }

    [Fact]
    public async Task PredictionDatasetService_FinalizesPendingLabels_ForSqliteSnapshots()
    {
        using var root = new PathoNetTestRoot();
        var predictionDbPath = Path.Combine(root.RootPath, "data", "pathonet-prediction.db");
        Directory.CreateDirectory(Path.GetDirectoryName(predictionDbPath)!);
        var predictionOptions = new DbContextOptionsBuilder<PathoNetPredictionDbContext>()
            .UseSqlite($"Data Source={predictionDbPath}")
            .Options;
        var predictionDbFactory = new TestDbContextFactory<PathoNetPredictionDbContext>(predictionOptions);
        var datasetService = new PredictionDatasetService(predictionDbFactory);

        await datasetService.InitializeAsync(CancellationToken.None);

        using (var db = predictionDbFactory.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var origin = new PathoNetPredictionFeatureSnapshotEntity
            {
                CapturedAtUtc = now.AddHours(-2),
                Alias = "PROC-1",
                DisplayName = "Procesor testowy",
                Port = "RS232/1",
                CurrentLevel = "info",
                Status = "online",
                RiskScore = 15,
                HealthScore = 91,
                WarnCount = 0,
                AlarmCount = 0,
                TotalEvents = 4,
                AlertPressure = 0,
                RecentAlarmEvents = 0,
                RecentWarnEvents = 0,
                FleetPressure = 1,
                ThresholdReached = false,
                Recommendation = "OK"
            };
            db.FeatureSnapshots.Add(origin);
            db.SaveChanges();

            db.TargetLabels.Add(new PathoNetPredictionLabelEntity
            {
                SnapshotId = origin.Id,
                TargetCode = "alarm_in_next_30m",
                Status = "pending",
                Value = null,
                DueAtUtc = now.AddHours(-1),
                LabeledAtUtc = null,
                Source = "backlog",
                Summary = "pending"
            });

            db.FeatureSnapshots.Add(new PathoNetPredictionFeatureSnapshotEntity
            {
                CapturedAtUtc = now.AddHours(-1).AddMinutes(-10),
                Alias = "PROC-1",
                DisplayName = "Procesor testowy",
                Port = "RS232/1",
                CurrentLevel = "alarm",
                Status = "attention",
                RiskScore = 78,
                HealthScore = 63,
                WarnCount = 2,
                AlarmCount = 1,
                TotalEvents = 7,
                AlertPressure = 1,
                RecentAlarmEvents = 1,
                RecentWarnEvents = 2,
                FleetPressure = 5,
                ThresholdReached = false,
                Recommendation = "Sprawdz"
            });

            db.SaveChanges();
        }

        var record = datasetService.CaptureAndGetState([], [], [], [], []);

        Assert.Equal(0, record.PendingLabelCount);
        Assert.Equal(1, record.ResolvedLabelCount);

        using var verificationDb = predictionDbFactory.CreateDbContext();
        var label = Assert.Single(verificationDb.TargetLabels);
        Assert.Equal("auto", label.Status);
        Assert.True(label.Value);
    }

    private static (OtaMockStore OtaStore, BlazorPortalService Service) CreatePortalService(string rootPath)
    {
        var rulebookStore = new RulebookStore(rootPath);
        var fleetStore = new FleetMockStore(rootPath);
        var otaStore = new OtaMockStore(rootPath);
        var predictionDbPath = Path.Combine(rootPath, "data", "pathonet-prediction.db");
        Directory.CreateDirectory(Path.GetDirectoryName(predictionDbPath)!);
        var predictionOptions = new DbContextOptionsBuilder<PathoNetPredictionDbContext>()
            .UseSqlite($"Data Source={predictionDbPath}")
            .Options;
        var predictionDbFactory = new TestDbContextFactory<PathoNetPredictionDbContext>(predictionOptions);
        var datasetService = new PredictionDatasetService(predictionDbFactory);
        var predictionService = new TissueProcessorPredictionService(datasetService);
        var simulationStore = new SimulationStore(rulebookStore, fleetStore, otaStore, predictionService);
        var healthStore = new ServiceHealthStore(rootPath);
        var collectorConfigStore = new CollectorConfigStore(rootPath);
        var collectorRuntimeStateStore = new CollectorRuntimeStateStore(rootPath);
        var hardwareStateService = new HardwareIntegrationStateService(simulationStore, healthStore, collectorRuntimeStateStore);
        var hardwareSignalTestService = new HardwareSignalTestService(hardwareStateService);
        var hardwareCollectorConfigService = new HardwareCollectorConfigService(collectorConfigStore);
        var service = new BlazorPortalService(
            simulationStore,
            healthStore,
            hardwareStateService,
            hardwareSignalTestService,
            hardwareCollectorConfigService);
        return (otaStore, service);
    }
}
