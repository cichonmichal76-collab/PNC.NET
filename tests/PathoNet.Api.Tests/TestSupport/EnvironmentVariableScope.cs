namespace PathoNet.Api.Tests.TestSupport;

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

    public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var pair in values)
        {
            _previousValues[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    public void Dispose()
    {
        foreach (var pair in _previousValues)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
