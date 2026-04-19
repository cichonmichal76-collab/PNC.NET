using Microsoft.AspNetCore.Mvc.Testing;

namespace PathoNet.Api.Tests.TestSupport;

internal sealed class PathoNetApiFactory : WebApplicationFactory<Program>
{
    private readonly EnvironmentVariableScope _scope;

    public PathoNetApiFactory(PathoNetTestRoot root)
    {
        _scope = root.UseAsPathoNetRoot();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _scope.Dispose();
        }
    }
}
