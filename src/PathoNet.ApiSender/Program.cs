using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PathoNet.ApiSender;
using PathoNet.Infrastructure.Configuration;
using PathoNet.Infrastructure.Hosting;

var contentRoot = AppContext.BaseDirectory;
var settings = await JsonSettingsFileLoader.LoadAsync<ApiSenderSettings>(contentRoot);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot
});

builder.Services.AddSingleton(settings);
builder.Services.AddPathoNetSystemdSupport("PathoNet.ApiSender");
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
builder.Services.AddHostedService<ApiSenderWorker>();

LinuxServiceBootstrap.LogRuntimeMode("SENDER", Console.WriteLine);

await builder.Build().RunAsync();
