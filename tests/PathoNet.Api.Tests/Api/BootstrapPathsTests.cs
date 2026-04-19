using PathoNet.Api.Tests.TestSupport;

namespace PathoNet.Api.Tests.Api;

public sealed class BootstrapPathsTests
{
    [Fact]
    public void ResolveContentRoot_PrefersExplicitContentRootEnvironmentVariable()
    {
        using var root = new PathoNetTestRoot();
        using var scope = root.UseAsPathoNetRoot();

        var resolved = BootstrapPaths.ResolveContentRoot();

        Assert.Equal(Path.GetFullPath(root.RootPath), resolved);
    }
}
