using PathoNet.Api.Tests.TestSupport;

namespace PathoNet.Api.Tests.Api;

public sealed class CollectorConfigStoreTests
{
    [Fact]
    public void GetState_CreatesFallbackConfig_WhenProjectConfigIsMissing()
    {
        using var root = new PathoNetTestRoot();
        var store = new CollectorConfigStore(root.RootPath);

        var state = store.GetState();

        Assert.NotNull(state);
        Assert.EndsWith("collector-appsettings.json", state.ConfigFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(state.Ports);
        Assert.Contains(state.Ports, port => port.PortId == "RS232/1");
    }

    [Fact]
    public async Task SavePortAsync_PersistsUpdatedSerialParameters()
    {
        using var root = new PathoNetTestRoot();
        var store = new CollectorConfigStore(root.RootPath);

        await store.SavePortAsync(
            new CollectorPortConfigRecord(
                PortId: "RS232/1",
                Alias: "Respirator sala 2",
                InterfaceType: "rs232",
                DevicePath: "/dev/ttyUSB7",
                NetworkInterfaceName: null,
                BaudRate: 9600,
                DataBits: 7,
                Parity: "even",
                StopBits: "two",
                ParserKind: "generic-text",
                FrameMode: "line",
                Enabled: true,
                AllowSimulationFallback: false,
                Description: "Testowa konfiguracja onsite."),
            CancellationToken.None);

        var state = store.GetState();
        var port = Assert.Single(state.Ports.Where(port => port.PortId == "RS232/1"));

        Assert.Equal("Respirator sala 2", port.Alias);
        Assert.Equal("/dev/ttyUSB7", port.DevicePath);
        Assert.Equal(9600, port.BaudRate);
        Assert.Equal(7, port.DataBits);
        Assert.Equal("even", port.Parity);
        Assert.Equal("two", port.StopBits);
        Assert.False(port.AllowSimulationFallback);
        Assert.Equal("Testowa konfiguracja onsite.", port.Description);
    }
}
