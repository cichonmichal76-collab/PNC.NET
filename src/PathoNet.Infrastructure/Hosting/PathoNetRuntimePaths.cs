namespace PathoNet.Infrastructure.Hosting;

public static class PathoNetRuntimePaths
{
    public static string ResolvePathoNetRoot(string? startDirectory = null)
    {
        var explicitRoot = Environment.GetEnvironmentVariable("PATHONET_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        foreach (var candidate in EnumerateCandidates(startDirectory))
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (LooksLikePathoNetRoot(current.FullName))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory);
    }

    public static string ResolveSharedStateDirectory(string? startDirectory = null)
    {
        var explicitPath = Environment.GetEnvironmentVariable("PATHONET_SHARED_STATE_DIR");
        var stateDirectory = string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(ResolvePathoNetRoot(startDirectory), "tmp", "service-health")
            : Path.GetFullPath(explicitPath);

        Directory.CreateDirectory(stateDirectory);
        return stateDirectory;
    }

    public static string ResolveServiceStateFilePath(string serviceName, string? startDirectory = null) =>
        Path.Combine(ResolveSharedStateDirectory(startDirectory), $"{NormalizeServiceFileName(serviceName)}.json");

    public static string ResolveCollectorRuntimeStateFilePath(string? startDirectory = null)
    {
        var explicitPath = Environment.GetEnvironmentVariable("PATHONET_COLLECTOR_RUNTIME_FILE");
        var filePath = string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(ResolveSharedStateDirectory(startDirectory), "collector-runtime-state.json")
            : Path.GetFullPath(explicitPath);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        return filePath;
    }

    public static string NormalizeServiceFileName(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var slug = new string(serviceName
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        slug = string.Join("-", slug.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(slug) ? "service" : slug;
    }

    private static IEnumerable<string> EnumerateCandidates(string? startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(startDirectory))
        {
            yield return Path.GetFullPath(startDirectory);
        }

        yield return Path.GetFullPath(AppContext.BaseDirectory);
        yield return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    private static bool LooksLikePathoNetRoot(string directory) =>
        File.Exists(Path.Combine(directory, "PathoNet.sln"))
        || (Directory.Exists(Path.Combine(directory, "src")) && Directory.Exists(Path.Combine(directory, "scripts")));
}
