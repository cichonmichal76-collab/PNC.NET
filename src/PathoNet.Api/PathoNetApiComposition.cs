using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using PathoNet.Contracts;

internal static class PathoNetApiComposition
{
    private const int LocalMachinePort = 5000;
    public static IServiceCollection AddPathoNetMockServices(this IServiceCollection services, string contentRoot)
    {
        var identityDbPath = Path.Combine(contentRoot, "data", "pathonet-identity.db");
        var predictionDbPath = Path.Combine(contentRoot, "data", "pathonet-prediction.db");
        Directory.CreateDirectory(Path.GetDirectoryName(identityDbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(predictionDbPath)!);

        services.AddDbContextFactory<PathoNetIdentityDbContext>(options =>
            options.UseSqlite($"Data Source={identityDbPath}"));
        services.AddDbContextFactory<PathoNetPredictionDbContext>(options =>
            options.UseSqlite($"Data Source={predictionDbPath}"));
        services.AddSingleton<IPasswordHasher<PathoNetIdentityUserEntity>, PasswordHasher<PathoNetIdentityUserEntity>>();
        services.AddSingleton<IdentityAccessService>();
        services.AddSingleton(_ => new RulebookStore(contentRoot));
        services.AddSingleton(_ => new FleetMockStore(contentRoot));
        services.AddSingleton(_ => new OtaMockStore(contentRoot));
        services.AddSingleton(_ => new ServiceHealthStore(contentRoot));
        services.AddSingleton(_ => new CollectorConfigStore(contentRoot));
        services.AddSingleton(_ => new CollectorRuntimeStateStore(contentRoot));
        services.AddSingleton<PredictionDatasetService>();
        services.AddSingleton<TissueProcessorPredictionService>();
        services.AddSingleton<HardwareIntegrationStateService>();
        services.AddSingleton<HardwareSignalTestService>();
        services.AddSingleton<HardwareCollectorConfigService>();
        services.AddSingleton<SimulationStore>();

        return services;
    }

    public static WebApplication MapPortalUi(this WebApplication app, string dashboardRoot)
    {
        app.MapGet("/", (HttpContext httpContext) =>
        {
            if (httpContext.User.Identity?.IsAuthenticated == true)
            {
                return Results.Redirect(PortalRoutes.Start);
            }

            var page = ResolveRootPage(httpContext);
            return Results.File(Path.Combine(dashboardRoot, page), "text/html; charset=utf-8");
        });
        app.MapGet("/local", () => Results.File(Path.Combine(dashboardRoot, "local.html"), "text/html; charset=utf-8"));
        app.MapGet("/host", () => Results.File(Path.Combine(dashboardRoot, "home.html"), "text/html; charset=utf-8"));
        foreach (var redirect in PortalLegacyRedirects.EndpointRedirects)
        {
            app.MapGet(redirect.Key, () => Results.Redirect(redirect.Value));
        }
        app.MapGet("/hdmi", () => Results.File(Path.Combine(dashboardRoot, "hdmi.html"), "text/html; charset=utf-8"));

        return app;
    }

    public static WebApplication MapPathoNetApi(this WebApplication app)
    {
        app.MapGet("/api/info", () => Results.Ok(new
        {
            name = "PathoNet Mock API",
            status = "running",
            pipeline = "collector -> hub -> api sender -> backend",
            endpoints = new[]
            {
                "/api/notify",
                "/api/register-device-sec",
                "/api/diagnostics/snapshot",
                "/api/portal/state",
                "/api/portal/hdmi",
                "/api/portal/rulebook",
                "/api/portal/fleet",
                "/api/portal/ota",
                "/api/portal/collector-config",
                "/api/portal/service-health",
                "/api/prediction/dataset/manifest",
                "/api/prediction/dataset/export",
                "/api/identity/state",
                "/api/identity/session",
                "/api/identity/login",
                "/local",
                "/host"
            }
        }));

        app.MapPost("/api/notify", (DeviceNotification notification, HttpRequest request, SimulationStore store) =>
        {
            var deviceKey = request.Headers["x-device-key"].ToString();
            store.AddNotification(notification with
            {
                Meta = notification.Meta with
                {
                    ReceivedDeviceKey = deviceKey
                }
            });

            return Results.Ok(new
            {
                accepted = true,
                type = "notify",
                notification.DeviceId,
                notification.Port,
                notification.Level
            });
        });

        app.MapPost("/api/register-device-sec", (DeviceHeartbeat heartbeat, SimulationStore store) =>
        {
            store.AddHeartbeat(heartbeat);
            return Results.Ok(new
            {
                accepted = true,
                type = "heartbeat",
                heartbeat.DeviceId,
                heartbeat.PortCount
            });
        });

        app.MapGet("/api/diagnostics/snapshot", (SimulationStore store) => Results.Ok(store.Snapshot()));
        app.MapGet("/api/portal/state", (SimulationStore store) => Results.Ok(store.PortalState()));
        app.MapGet("/api/portal/hdmi", (SimulationStore store) => Results.Ok(store.HdmiState()));
        app.MapGet("/api/portal/rulebook", (SimulationStore store) => Results.Ok(store.RulebookState()));
        app.MapPut("/api/portal/rulebook", async (PortalRulebookConfig rulebook, SimulationStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.UpdateRulebookAsync(rulebook, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.RulesManage);
        app.MapGet("/api/portal/fleet", (SimulationStore store) => Results.Ok(store.FleetState()));
        app.MapPut("/api/portal/fleet", async (PortalFleetConfig fleet, SimulationStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.UpdateFleetAsync(fleet, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.PncManage);
        app.MapGet("/api/portal/collector-config", (CollectorConfigStore store) => Results.Ok(store.GetState()))
            .RequireAuthorization(PathoNetPermissions.PncView);
        app.MapPut("/api/portal/collector-config", async (CollectorPortConfigRecord input, CollectorConfigStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.SavePortAsync(input, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.PncManage);
        app.MapGet("/api/portal/ota", async (SimulationStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.OtaStateAsync(cancellationToken)));
        app.MapPut("/api/portal/ota", async (PortalOtaConfig ota, SimulationStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.UpdateOtaAsync(ota, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.OtaManage);
        app.MapGet("/api/portal/service-health", (ServiceHealthStore store) => Results.Ok(store.GetState()));
        app.MapGet("/api/prediction/dataset/manifest", (PredictionDatasetService service) =>
            Results.Ok(service.GetTrainingManifest()))
            .RequireAuthorization(PathoNetPermissions.AnalysisView);
        app.MapGet("/api/prediction/dataset/export", (
                string? format,
                bool? resolvedOnly,
                PredictionDatasetService service) =>
            {
                var exportFormat = string.Equals(format, "jsonl", StringComparison.OrdinalIgnoreCase)
                    ? "jsonl"
                    : "csv";
                var onlyResolved = resolvedOnly ?? true;

                return exportFormat switch
                {
                    "jsonl" => Results.Text(service.ExportTrainingDatasetJsonl(onlyResolved), "application/x-ndjson", System.Text.Encoding.UTF8),
                    _ => Results.Text(service.ExportTrainingDatasetCsv(onlyResolved), "text/csv", System.Text.Encoding.UTF8)
                };
            })
            .RequireAuthorization(PathoNetPermissions.AnalysisView);
        app.MapPost("/identity/login", async (
                HttpContext httpContext,
                [FromForm] string userName,
                [FromForm] string password,
                [FromForm] string? returnUrl,
                IdentityAccessService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.AuthenticateAsync(userName, password, cancellationToken);
                if (!result.Success || result.Principal is null)
                {
                    var target = string.IsNullOrWhiteSpace(returnUrl) ? $"{PortalRoutes.Login}?error=invalid" : $"{PortalRoutes.Login}?error=invalid&returnUrl={Uri.EscapeDataString(returnUrl)}";
                    return SeeOther(target);
                }

                await httpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    result.Principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    });

                return SeeOther(SanitizeReturnUrl(returnUrl));
            })
            .AllowAnonymous()
            .DisableAntiforgery();

        app.MapGet("/identity/logout", async (HttpContext httpContext, IdentityAccessService service, string? returnUrl, CancellationToken cancellationToken) =>
        {
            await service.RecordLogoutAsync(httpContext.User, cancellationToken);
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect(SanitizeReturnUrl(returnUrl, PortalRoutes.Login));
        });

        app.MapPost("/api/identity/login", async (HttpContext httpContext, IdentityLoginRequestRecord input, IdentityAccessService service, CancellationToken cancellationToken) =>
        {
            var result = await service.AuthenticateAsync(input.UserName, input.Password, cancellationToken);
            if (result.Success && result.Principal is not null)
            {
                await httpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    result.Principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    });
            }

            return result.Success
                ? Results.Ok(result.Session)
                : Results.Unauthorized();
        })
        .AllowAnonymous();

        app.MapPost("/api/identity/logout", async (HttpContext httpContext, IdentityAccessService service, CancellationToken cancellationToken) =>
        {
            await service.RecordLogoutAsync(httpContext.User, cancellationToken);
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { success = true });
        })
        .RequireAuthorization(PathoNetPolicyNames.Authenticated);

        app.MapGet("/api/identity/session", async (HttpContext httpContext, IdentityAccessService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSessionAsync(httpContext.User, cancellationToken)))
            .RequireAuthorization(PathoNetPolicyNames.Authenticated);

        app.MapGet("/api/identity/state", async (IdentityAccessService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetStateAsync(cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.UsersView);
        app.MapPost("/api/identity/users", async (BlazorAccessUserInputRecord input, IdentityAccessService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.SaveUserAsync(input, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.UsersManage);
        app.MapDelete("/api/identity/users/{userId}", async (string userId, IdentityAccessService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.DeleteUserAsync(userId, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.UsersManage);
        app.MapPost("/api/identity/roles", async (BlazorAccessRoleInputRecord input, IdentityAccessService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.SaveRoleAsync(input, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.UsersManage);
        app.MapDelete("/api/identity/roles/{roleId}", async (string roleId, IdentityAccessService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.DeleteRoleAsync(roleId, cancellationToken)))
            .RequireAuthorization(PathoNetPermissions.UsersManage);
        app.MapPost("/api/portal/service-health/{serviceName}/restart", async (string serviceName, ServiceHealthStore store, CancellationToken cancellationToken) =>
        {
            var result = await store.RequestRestartAsync(serviceName, "api-service-health", cancellationToken);
            return result.Accepted
                ? Results.Accepted("/api/portal/service-health", result)
                : Results.BadRequest(result);
        })
        .RequireAuthorization(PathoNetPermissions.HealthManage);

        return app;
    }

    public static async Task InitializePathoNetIdentityAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var identityService = app.Services.GetRequiredService<IdentityAccessService>();
        await identityService.InitializeAsync(cancellationToken);
    }

    public static async Task InitializePathoNetPredictionAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var predictionService = app.Services.GetRequiredService<PredictionDatasetService>();
        await predictionService.InitializeAsync(cancellationToken);
    }

    private static string SanitizeReturnUrl(string? returnUrl, string fallback = PortalRoutes.Start)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return fallback;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out var relativeUri))
        {
            var value = relativeUri.ToString();
            if (value.StartsWith('/'))
            {
                return value;
            }
        }

        return fallback;
    }

    private static string ResolveRootPage(HttpContext httpContext)
    {
        var port = httpContext.Request.Host.Port ?? httpContext.Connection.LocalPort;
        return port == LocalMachinePort ? "local.html" : "home.html";
    }

    private static IResult SeeOther(string location) => new SeeOtherResult(location);

    private sealed class SeeOtherResult(string location) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
            httpContext.Response.Headers.Location = location;
            return Task.CompletedTask;
        }
    }
}
