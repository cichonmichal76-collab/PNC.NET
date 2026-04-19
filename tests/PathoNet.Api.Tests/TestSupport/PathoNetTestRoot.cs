using System.Text.Json;
using PathoNet.Infrastructure.Hosting;

namespace PathoNet.Api.Tests.TestSupport;

internal sealed class PathoNetTestRoot : IDisposable
{
    private readonly string _rootPath;

    public PathoNetTestRoot()
    {
        _rootPath = Path.Combine(
            Path.GetTempPath(),
            "PathoNet.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(Path.Combine(_rootPath, "data"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "tmp"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "scripts"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "wwwroot"));
        File.WriteAllText(Path.Combine(_rootPath, "PathoNet.sln"), "test");
        SeedPortalFiles();
    }

    public string RootPath => _rootPath;

    public string SharedStateDirectory => Path.Combine(_rootPath, "tmp", "service-health");

    public string PidFilePath => Path.Combine(_rootPath, "tmp", "pathonet-mock-pids.json");

    public string RestartScriptPath => Path.Combine(_rootPath, "scripts", "Restart-PathoNet-Service.ps1");

    public string ServiceStateFilePath(string serviceName) =>
        Path.Combine(SharedStateDirectory, $"{PathoNetRuntimePaths.NormalizeServiceFileName(serviceName)}.json");

    public void WriteRestartScript()
    {
        File.WriteAllText(
            RestartScriptPath,
            "param([string]$ServiceName,[string]$EventId,[string]$RequestedBy,[int]$DelaySeconds)\nStart-Sleep -Milliseconds 1\n");
    }

    public void WritePidEntries(params object[] entries)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        Directory.CreateDirectory(Path.GetDirectoryName(PidFilePath)!);
        File.WriteAllText(PidFilePath, JsonSerializer.Serialize(entries, options));
    }

    public void WriteJsonFile(string filePath, object payload)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(payload, options));
    }

    public EnvironmentVariableScope UseAsPathoNetRoot() =>
        new(new Dictionary<string, string?>
        {
            ["PATHONET_CONTENT_ROOT"] = _rootPath,
            ["PATHONET_ROOT"] = _rootPath,
            ["PATHONET_SHARED_STATE_DIR"] = SharedStateDirectory
        });

    private void SeedPortalFiles()
    {
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home.html"] = "<html><body>Portal hosta i pracy zdalnej</body></html>",
            ["local.html"] = "<html><body>Pulpit lokalny PNC</body></html>",
            ["index.html"] = "<html><body>legacy</body></html>",
            ["analysis.html"] = "<html><body>analysis</body></html>",
            ["mock.html"] = "<html><body>mock</body></html>",
            ["client.html"] = "<html><body>client</body></html>",
            ["hdmi.html"] = "<html><body>hdmi</body></html>",
            ["blazor-shell.css"] = "body { font-family: sans-serif; }"
        };

        foreach (var page in pages)
        {
            File.WriteAllText(Path.Combine(_rootPath, "wwwroot", page.Key), page.Value);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
