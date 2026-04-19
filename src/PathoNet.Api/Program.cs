using Microsoft.AspNetCore.Authentication.Cookies;
using PathoNet.Api.Components;
using PathoNet.Infrastructure.Hosting;

var contentRoot = BootstrapPaths.ResolveContentRoot();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot")
});

var dashboardRoot = Path.Combine(contentRoot, "wwwroot");

builder.WebHost.UseUrls(LinuxServiceBootstrap.ResolveHttpUrls("http://localhost:5000;http://localhost:5080"));
builder.Services.AddPathoNetSystemdSupport("PathoNet.Api");
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "PathoNet.Auth";
        options.LoginPath = PortalRoutes.Login;
        options.AccessDeniedPath = PortalRoutes.Login;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PathoNetPolicyNames.Authenticated, policy => policy.RequireAuthenticatedUser());

    foreach (var permission in PathoNetPermissions.All)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireClaim(PathoNetClaimTypes.Permission, permission));
    }
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddPathoNetMockServices(contentRoot);
builder.Services.AddSingleton<BlazorPortalService>();

LinuxServiceBootstrap.LogRuntimeMode("API", Console.WriteLine);

var app = builder.Build();
await app.InitializePathoNetIdentityAsync();
await app.InitializePathoNetPredictionAsync();

app.Use(async (context, next) =>
{
    if (PortalLegacyRedirects.TryResolveStaticRedirect(context.Request.Path, out var redirectTarget))
    {
        context.Response.Redirect(redirectTarget);
        return;
    }

    await next();
});

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.MapPortalUi(dashboardRoot);
app.MapPathoNetApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
}
