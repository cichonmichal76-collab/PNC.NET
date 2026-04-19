using System.Text.Json;

namespace PathoNet.Infrastructure.Configuration;

public static class JsonSettingsFileLoader
{
    public static async Task<T> LoadAsync<T>(string baseDirectory, string fileName = "appsettings.json")
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var settingsPath = Path.Combine(baseDirectory, fileName);
        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException($"Unable to find configuration file '{settingsPath}'.", settingsPath);
        }

        var json = await File.ReadAllTextAsync(settingsPath);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Unable to load settings from '{settingsPath}'.");
    }
}
