using PathoNet.Api.Tests.TestSupport;
using PathoNet.Infrastructure.Hosting;

namespace PathoNet.Api.Tests.Infrastructure;

public sealed class PathoNetRuntimePathsTests
{
    [Fact]
    public void ResolvePathoNetRoot_UsesExplicitEnvironmentVariable()
    {
        using var root = new PathoNetTestRoot();
        using var scope = root.UseAsPathoNetRoot();

        var resolved = PathoNetRuntimePaths.ResolvePathoNetRoot(@"C:\ignored");

        Assert.Equal(Path.GetFullPath(root.RootPath), resolved);
    }

    [Theory]
    [InlineData("PathoNet.Api", "pathonet-api")]
    [InlineData(" PathoNet Api / Beta ", "pathonet-api-beta")]
    [InlineData("$$$", "service")]
    public void NormalizeServiceFileName_ReturnsSafeSlug(string input, string expected)
    {
        var normalized = PathoNetRuntimePaths.NormalizeServiceFileName(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void ResolveSharedStateDirectory_UsesExplicitDirectoryAndCreatesIt()
    {
        using var root = new PathoNetTestRoot();
        var explicitPath = Path.Combine(root.RootPath, "custom", "runtime-state");
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PATHONET_SHARED_STATE_DIR"] = explicitPath
        });

        var resolved = PathoNetRuntimePaths.ResolveSharedStateDirectory(root.RootPath);

        Assert.Equal(Path.GetFullPath(explicitPath), resolved);
        Assert.True(Directory.Exists(resolved));
    }
}
