using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PathoNet.Collector;
using PathoNet.Infrastructure.Configuration;
using PathoNet.Infrastructure.Hosting;

var contentRoot = AppContext.BaseDirectory;
var settings = await JsonSettingsFileLoader.LoadAsync<CollectorSettings>(contentRoot);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot
});

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(sp => new CollectorRuntimeStateStore(settings, contentRoot));
builder.Services.AddPathoNetSystemdSupport("PathoNet.Collector");
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
builder.Services.AddHostedService<CollectorWorker>();

LinuxServiceBootstrap.LogRuntimeMode("COLLECTOR", Console.WriteLine);

await builder.Build().RunAsync();
