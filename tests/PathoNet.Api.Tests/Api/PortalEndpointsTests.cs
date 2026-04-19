using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PathoNet.Api.Tests.TestSupport;

namespace PathoNet.Api.Tests.Api;

public sealed class PortalEndpointsTests
{
    [Fact]
    public async Task Root_And_Blazor_AreServedSuccessfully()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var hostClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5080")
        });
        using var localClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000")
        });
        using var defaultClient = factory.CreateClient();

        var hostRootResponse = await hostClient.GetAsync("/");
        var localRootResponse = await localClient.GetAsync("/");
        var blazorResponse = await defaultClient.GetAsync("/blazor");
        var hostHtml = await hostRootResponse.Content.ReadAsStringAsync();
        var localHtml = await localRootResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, hostRootResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, localRootResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, blazorResponse.StatusCode);
        Assert.Contains("Portal hosta i pracy zdalnej", hostHtml);
        Assert.Contains("Pulpit lokalny PNC", localHtml);
    }

    [Fact]
    public async Task ApiInfo_ListsExpectedEndpoints()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        var payload = await client.GetFromJsonAsync<ApiInfoResponse>("/api/info");

        Assert.NotNull(payload);
        Assert.Contains("/api/portal/service-health", payload!.Endpoints);
        Assert.Contains("/api/portal/rulebook", payload.Endpoints);
        Assert.Contains("/api/identity/state", payload.Endpoints);
    }

    [Fact]
    public async Task IdentityState_Anonymous_ReturnsUnauthorized()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/identity/state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IdentityLogin_CreatesSessionCookie_AndReturnsSession()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        var loginResponse = await LoginAsync(client, "admin", "123");
        var session = await client.GetFromJsonAsync<PortalIdentitySessionDto>("/api/identity/session");

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.NotNull(session);
        Assert.True(session!.Authenticated);
        Assert.Equal("admin", session.UserName);
        Assert.Contains("users.manage", session.PermissionCodes);
    }

    [Fact]
    public async Task Root_WhenAuthenticated_RedirectsToBlazorStart()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000"),
            AllowAutoRedirect = false
        });

        await LoginAsync(client, "admin", "123");
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/blazor", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/service", "/blazor/administracja")]
    [InlineData("/service/health", "/blazor/health")]
    [InlineData("/service/ota", "/blazor/ota")]
    [InlineData("/service/pnc", "/blazor/pnc")]
    [InlineData("/service/rules", "/blazor/rules")]
    [InlineData("/service/analysis", "/blazor/analiza")]
    [InlineData("/service/mock", "/blazor/diagnostyka")]
    [InlineData("/analysis", "/blazor/analiza")]
    [InlineData("/mock", "/blazor/diagnostyka")]
    [InlineData("/client", "/blazor/urzadzenia")]
    [InlineData("/service-legacy", "/blazor/administracja")]
    [InlineData("/analysis.html", "/blazor/analiza")]
    [InlineData("/mock.html", "/blazor/diagnostyka")]
    [InlineData("/client.html", "/blazor/urzadzenia")]
    [InlineData("/index.html", "/blazor/administracja")]
    public async Task LegacyFrontendEntrypoints_RedirectToBlazor(string path, string expectedLocation)
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AdminSections_AreServedSuccessfully_AfterLogin()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");

        var startResponse = await client.GetAsync("/blazor");
        var reviewResponse = await client.GetAsync("/blazor/przeglad");
        var analysisResponse = await client.GetAsync("/blazor/analiza");
        var serviceResponse = await client.GetAsync("/blazor/serwis");
        var hardwareResponse = await client.GetAsync("/blazor/serwis/hardware");
        var itResponse = await client.GetAsync("/blazor/it");

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, analysisResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, serviceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hardwareResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, itResponse.StatusCode);
    }

    [Fact]
    public async Task DiagnosticsSnapshot_ChangesAfterNotifyAndHeartbeat()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        var before = await client.GetFromJsonAsync<PortalDiagnosticsDto>("/api/diagnostics/snapshot");

        var notifyResponse = await client.PostAsJsonAsync(
            "/api/notify",
            new
            {
                deviceId = "device-test",
                alias = "Device_Test",
                port = "/dev/ttyTEST0",
                level = "alarm",
                text = "ALARM TEST SIGNAL",
                raw = "Device_Test|ALARM TEST SIGNAL",
                meta = new
                {
                    dateMess = "17.04.2026 22:45:00",
                    receivedDeviceKey = ""
                }
            });

        var heartbeatResponse = await client.PostAsJsonAsync(
            "/api/register-device-sec",
            new
            {
                deviceId = "device-test",
                clientName = "Test Client",
                currentVersion = "1.2.3",
                portCount = 1
            });

        var after = await client.GetFromJsonAsync<PortalDiagnosticsDto>("/api/diagnostics/snapshot");

        Assert.Equal(HttpStatusCode.OK, notifyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, heartbeatResponse.StatusCode);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after!.NotificationCount >= before!.NotificationCount + 1);
        Assert.True(after.HeartbeatCount >= before.HeartbeatCount + 1);
        Assert.Contains("/dev/ttyTEST0", after.ActivePorts);
    }

    [Fact]
    public async Task PortalState_ReturnsOverviewDevicesAndLte()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/notify",
            new
            {
                deviceId = "device-state",
                alias = "Device_State",
                port = "/dev/ttySTATE0",
                level = "warn",
                text = "WARN STATE TEST",
                raw = "Device_State|WARN STATE TEST",
                meta = new
                {
                    dateMess = "17.04.2026 22:46:00",
                    receivedDeviceKey = ""
                }
            });

        var state = await client.GetFromJsonAsync<PortalStateDto>("/api/portal/state");

        Assert.NotNull(state);
        Assert.True(state!.Overview.ActiveDeviceCount >= 0);
        Assert.NotEmpty(state.Devices);
        Assert.NotEmpty(state.PncDevices);
        Assert.False(string.IsNullOrWhiteSpace(state.Lte.OperatorName));
        Assert.Equal("XGBoost", state.PredictionAnalysis.RecommendedModel.ModelName);
    }

    [Fact]
    public async Task PredictionDatasetExport_ReturnsCsvHeader_ForAdmin()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var response = await client.GetAsync("/api/prediction/dataset/export?format=csv&resolvedOnly=false");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("captured_at_utc", payload);
        Assert.Contains("label_alarm_in_next_30m", payload);
    }

    [Fact]
    public async Task PredictionDatasetManifest_ReturnsRecommendedPrimaryTarget()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var manifest = await client.GetFromJsonAsync<PredictionTrainingManifestDto>("/api/prediction/dataset/manifest");

        Assert.NotNull(manifest);
        Assert.Equal("label_alarm_in_next_30m", manifest!.RecommendedPrimaryTarget);
        Assert.Contains("risk_score", manifest.NumericColumns);
    }

    [Fact]
    public async Task CollectorConfig_RequiresAuthentication_AndReturnsConfiguredPorts()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        var unauthorized = await client.GetAsync("/api/portal/collector-config");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        await LoginAsync(client, "admin", "123");
        var state = await client.GetFromJsonAsync<CollectorConfigDto>("/api/portal/collector-config");

        Assert.NotNull(state);
        Assert.NotEmpty(state!.Ports);
        Assert.Contains(state.Ports, port => port.PortId == "RS232/1");
    }

    [Fact]
    public async Task CollectorConfigMutation_RequiresPncManagePermission()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "szpital", "123");
        var response = await client.PutAsJsonAsync(
            "/api/portal/collector-config",
            new
            {
                portId = "RS232/1",
                alias = "Test",
                interfaceType = "rs232",
                devicePath = "/dev/ttyUSB0",
                networkInterfaceName = (string?)null,
                baudRate = 9600,
                dataBits = 8,
                parity = "none",
                stopBits = "one",
                parserKind = "generic-text",
                frameMode = "line",
                enabled = true,
                allowSimulationFallback = false,
                description = "Test"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RulebookPut_NormalizesAndPersistsPayload()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var response = await client.PutAsJsonAsync(
            "/api/portal/rulebook",
            new
            {
                users = new[]
                {
                    new
                    {
                        id = "tester one",
                        displayName = "Tester One",
                        role = "Serwis",
                        email = "tester@example.com",
                        phone = "+48 500 100 100"
                    }
                },
                rules = new[]
                {
                    new
                    {
                        id = "rule one",
                        name = "Regula One",
                        matchText = "WARN TEST",
                        messageType = "ostrzezenie",
                        description = "opis",
                        thresholdHours = 12.5,
                        sendSms = true,
                        sendEmail = false,
                        recipientIds = new[] { "tester one", "ghost" },
                        enabled = true
                    }
                }
            });

        var payload = await response.Content.ReadFromJsonAsync<PortalRulebookStateDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Single(payload!.Users);
        Assert.Single(payload.Rules);
        Assert.Equal("tester-one", payload.Users[0].Id);
        Assert.Equal("warn", payload.Rules[0].MessageType);
        Assert.Equal(["tester-one"], payload.Rules[0].RecipientIds);
    }

    [Fact]
    public async Task ServiceHealth_EndpointReturnsFourKnownServices()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        var state = await client.GetFromJsonAsync<ServiceHealthDto>("/api/portal/service-health");

        Assert.NotNull(state);
        Assert.Equal(4, state!.Summary.TotalCount);
        Assert.Equal(4, state.Services.Length);
    }

    [Fact]
    public async Task RestartEndpoint_ReturnsBadRequestForUnknownService()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var response = await client.PostAsync("/api/portal/service-health/unknown-service/restart", content: null);
        var payload = await response.Content.ReadFromJsonAsync<RestartResultDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(payload!.Accepted);
        Assert.Equal("rejected", payload.Status);
    }

    [Fact]
    public async Task IdentityState_ReturnsSeededUsersRolesAndPermissions()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var state = await client.GetFromJsonAsync<PortalIdentityStateDto>("/api/identity/state");

        Assert.NotNull(state);
        Assert.NotEmpty(state!.Users);
        Assert.NotEmpty(state.Roles);
        Assert.NotEmpty(state.Permissions);
        Assert.Contains(state.Users, user => user.UserName == "admin");
        Assert.Contains(state.Roles, role => role.Id == "administrator");
        Assert.Contains(state.Permissions, permission => permission.Code == "users.manage");
    }

    [Fact]
    public async Task IdentityUsersPost_CreatesUserWithRolesAndDirectPermissions()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var before = await client.GetFromJsonAsync<PortalIdentityStateDto>("/api/identity/state");
        var operatorRoleId = Assert.Single(before!.Roles.Where(role => role.Id == "operator")).Id;
        var directPermissionId = Assert.Single(before.Permissions.Where(permission => permission.Code == "users.view")).Id;

        var response = await client.PostAsJsonAsync(
            "/api/identity/users",
            new
            {
                userId = (string?)null,
                userName = "installer",
                fullName = "Instalator Terenowy",
                email = "installer@pathonet.local",
                phone = "+48 600 300 400",
                isActive = true,
                isServiceAccount = false,
                password = "Installer!2026",
                roleIds = new[] { operatorRoleId },
                directPermissionIds = new[] { directPermissionId }
            });

        var mutation = await response.Content.ReadFromJsonAsync<IdentityMutationResultDto>();
        var after = await client.GetFromJsonAsync<PortalIdentityStateDto>("/api/identity/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(mutation);
        Assert.True(mutation!.Success);
        Assert.NotNull(after);

        var createdUser = Assert.Single(after!.Users.Where(user => user.UserName == "installer"));
        Assert.Contains(operatorRoleId, createdUser.RoleIds);
        Assert.Contains(directPermissionId, createdUser.DirectPermissionIds);
        Assert.Contains("dashboard.view", createdUser.EffectivePermissionCodes);
        Assert.Contains("users.view", createdUser.EffectivePermissionCodes);
    }

    [Fact]
    public async Task IdentityRolesDelete_RejectsSystemRole()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "admin", "123");
        var response = await client.DeleteAsync("/api/identity/roles/administrator");
        var payload = await response.Content.ReadFromJsonAsync<IdentityMutationResultDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.False(payload!.Success);
        Assert.Contains("systemowej", payload.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IdentityRolesPost_ForServiceManager_ReturnsForbidden()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var client = factory.CreateClient();

        await LoginAsync(client, "serwis", "123");
        var response = await client.PostAsJsonAsync(
            "/api/identity/roles",
            new
            {
                roleId = (string?)null,
                name = "Read Only Plus",
                description = "Test role",
                permissionIds = Array.Empty<string>()
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RulebookMutation_RequiresManagePermission()
    {
        using var root = new PathoNetTestRoot();
        using var factory = new PathoNetApiFactory(root);
        using var adminClient = factory.CreateClient();

        await LoginAsync(adminClient, "admin", "123");
        var state = await adminClient.GetFromJsonAsync<PortalIdentityStateDto>("/api/identity/state");
        var operatorRoleId = Assert.Single(state!.Roles.Where(role => role.Id == "operator")).Id;

        var createUserResponse = await adminClient.PostAsJsonAsync(
            "/api/identity/users",
            new
            {
                userId = (string?)null,
                userName = "operatorrules",
                fullName = "Operator Rules",
                email = "operator-rules@pathonet.local",
                phone = "+48 600 111 222",
                isActive = true,
                isServiceAccount = false,
                password = "Operator!2026",
                roleIds = new[] { operatorRoleId },
                directPermissionIds = Array.Empty<string>()
            });

        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

        using var operatorClient = factory.CreateClient();
        await LoginAsync(operatorClient, "operatorrules", "Operator!2026");
        var response = await operatorClient.PutAsJsonAsync(
            "/api/portal/rulebook",
            new
            {
                users = Array.Empty<object>(),
                rules = Array.Empty<object>()
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/identity/login",
            new IdentityLoginRequestDto(userName, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private sealed record ApiInfoResponse(string Name, string Status, string Pipeline, string[] Endpoints);

    private sealed record PortalDiagnosticsDto(
        int NotificationCount,
        int HeartbeatCount,
        string[] ActivePorts);

    private sealed record PortalStateDto(
        PortalOverviewDto Overview,
        PortalDeviceDto[] Devices,
        PortalPncDeviceDto[] PncDevices,
        PortalLteDto Lte,
        PortalPredictionAnalysisDto PredictionAnalysis);

    private sealed record PortalOverviewDto(int ActiveDeviceCount);

    private sealed record PortalDeviceDto(string Port);

    private sealed record PortalPncDeviceDto(string DeviceCode);

    private sealed record PortalLteDto(string OperatorName);

    private sealed record PortalPredictionAnalysisDto(
        PortalPredictionModelRecommendationDto RecommendedModel,
        PortalPredictionDatasetDto Dataset);

    private sealed record PortalPredictionModelRecommendationDto(
        string ModelName);

    private sealed record PortalPredictionDatasetDto(
        int SnapshotCount,
        int PendingLabelCount);

    private sealed record PredictionTrainingManifestDto(
        string RecommendedPrimaryTarget,
        string[] NumericColumns);

    private sealed record PortalRulebookStateDto(
        PortalUserDto[] Users,
        PortalRuleDto[] Rules);

    private sealed record CollectorConfigDto(
        string ConfigFilePath,
        string RestartHint,
        CollectorConfigPortDto[] Ports);

    private sealed record CollectorConfigPortDto(
        string PortId,
        string Alias,
        string DevicePath);

    private sealed record PortalUserDto(
        string Id,
        string DisplayName);

    private sealed record PortalRuleDto(
        string Id,
        string MessageType,
        string[] RecipientIds);

    private sealed record ServiceHealthDto(
        ServiceHealthSummaryDto Summary,
        ServiceHealthItemDto[] Services);

    private sealed record ServiceHealthSummaryDto(int TotalCount);

    private sealed record ServiceHealthItemDto(string Name);

    private sealed record RestartResultDto(
        bool Accepted,
        string Status,
        string Message);

    private sealed record PortalIdentityStateDto(
        PortalIdentitySummaryDto Summary,
        PortalIdentityUserDto[] Users,
        PortalIdentityRoleDto[] Roles,
        PortalIdentityPermissionDto[] Permissions,
        PortalIdentityAuditDto[] AuditLog);

    private sealed record PortalIdentitySummaryDto(
        int UserCount,
        int ActiveUserCount,
        int RoleCount,
        int PermissionCount,
        int RoleAssignmentCount,
        int RolePermissionAssignmentCount,
        int DirectPermissionAssignmentCount);

    private sealed record PortalIdentityUserDto(
        string Id,
        string UserName,
        string FullName,
        string[] RoleIds,
        string[] DirectPermissionIds,
        string[] EffectivePermissionCodes);

    private sealed record PortalIdentityRoleDto(
        string Id,
        string Name,
        string[] PermissionIds,
        string[] PermissionCodes);

    private sealed record PortalIdentityPermissionDto(
        string Id,
        string Code,
        string Name);

    private sealed record PortalIdentityAuditDto(
        long Id,
        string Action);

    private sealed record PortalIdentitySessionDto(
        bool Authenticated,
        string? UserId,
        string? UserName,
        string? FullName,
        string[] RoleNames,
        string[] PermissionCodes);

    private sealed record IdentityMutationResultDto(
        bool Success,
        string Message);

    private sealed record IdentityLoginRequestDto(
        string UserName,
        string Password);
}
