using PathoNet.Api.Components.Pages;

namespace PathoNet.Api.Tests.Api;

public sealed class HardwareIntegrationWizardSupportTests
{
    [Fact]
    public void GetSelectablePorts_MergesDuplicatesAndUsesCanonicalHardwareOrder()
    {
        var connectedPorts = new[]
        {
            CreateStatus("ETH2", "ethernet", alias: "ETH2 connected"),
            CreateStatus("RS232/2", "rs232"),
            CreateStatus("CAN", "can")
        };

        var otherPorts = new[]
        {
            CreateStatus("RS232/1", "rs232"),
            CreateStatus("WE DRY", "dry-contact"),
            CreateStatus("ETH2", "ethernet", alias: "ETH2 duplicate")
        };

        var ports = HardwareIntegrationWizardSupport.GetSelectablePorts(connectedPorts, otherPorts);

        Assert.Equal(["RS232/1", "RS232/2", "CAN", "ETH2", "WE DRY"], ports.Select(port => port.PortId));
        Assert.Equal("ETH2 connected", ports.Single(port => port.PortId == "ETH2").Alias);
    }

    [Fact]
    public void GetInterfaceGroups_GroupsPortsByInterfaceAndKeepsPortOrder()
    {
        var ports = new[]
        {
            CreateStatus("RS232/3", "rs232"),
            CreateStatus("WE DRY", "dry-contact"),
            CreateStatus("RS232/1", "rs232"),
            CreateStatus("CAN", "can")
        };

        var groups = HardwareIntegrationWizardSupport.GetInterfaceGroups(ports);

        Assert.Equal(["RS232", "CAN", "WE DRY"], groups.Select(group => group.Label));
        Assert.Equal(["RS232/1", "RS232/3"], groups[0].Ports.Select(port => port.PortId));
    }

    [Fact]
    public void ResolveActiveInterfaceType_FallsBackToSelectedPortWhenRequestedFamilyIsMissing()
    {
        var groups = HardwareIntegrationWizardSupport.GetInterfaceGroups(
            [
                CreateStatus("RS232/1", "rs232"),
                CreateStatus("CAN", "can")
            ]);

        var activeInterface = HardwareIntegrationWizardSupport.ResolveActiveInterfaceType("ethernet", "CAN", groups);

        Assert.Equal("can", activeInterface);
    }

    [Fact]
    public void FindFirstPortIdForInterface_PrefersSuggestedPortWithinRequestedFamily()
    {
        var ports = new[]
        {
            CreateConfig("RS232/1", "rs232"),
            CreateConfig("RS232/2", "rs232"),
            CreateConfig("CAN", "can")
        };

        var portId = HardwareIntegrationWizardSupport.FindFirstPortIdForInterface(ports, "rs232", "RS232/2");

        Assert.Equal("RS232/2", portId);
    }

    [Fact]
    public void FindFirstPortIdForInterface_IgnoresSuggestionFromAnotherFamilyAndFallsBackToFirstPort()
    {
        var ports = new[]
        {
            CreateConfig("RS232/2", "rs232"),
            CreateConfig("RS232/1", "rs232"),
            CreateConfig("CAN", "can")
        };

        var portId = HardwareIntegrationWizardSupport.FindFirstPortIdForInterface(ports, "rs232", "CAN");

        Assert.Equal("RS232/1", portId);
    }

    private static HardwarePortStatusRecord CreateStatus(
        string portId,
        string interfaceType,
        string? alias = null) =>
        new(
            PortId: portId,
            Alias: alias ?? portId,
            InterfaceType: interfaceType,
            ExpectedPath: $"/dev/{portId.ToLowerInvariant().Replace('/', '-')}",
            ConnectionState: "link",
            ParserKind: "generic-text",
            FrameMode: "line",
            Purpose: $"Purpose for {portId}",
            Detected: true,
            CablePresent: true,
            LinkUp: interfaceType is "can" or "ethernet",
            RxActive: true,
            TxActive: false,
            SimulationFallback: false,
            Mode: "active",
            Status: "online",
            StateSinceUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            LastRxAtUtc: DateTimeOffset.UtcNow.AddSeconds(-10),
            LastTxAtUtc: null,
            RxCounter: 12,
            TxCounter: 0,
            LastSignalAt: "przed chwila",
            Summary: $"Summary for {portId}",
            Recommendation: $"Recommendation for {portId}",
            LastRaw: null,
            LastText: null);

    private static CollectorPortConfigRecord CreateConfig(string portId, string interfaceType) =>
        new(
            PortId: portId,
            Alias: portId,
            InterfaceType: interfaceType,
            DevicePath: $"/dev/{portId.ToLowerInvariant().Replace('/', '-')}",
            NetworkInterfaceName: interfaceType is "can" or "ethernet" ? portId.ToLowerInvariant().Replace("/", string.Empty) : null,
            BaudRate: interfaceType == "rs232" ? 115200 : 0,
            DataBits: 8,
            Parity: "none",
            StopBits: "one",
            ParserKind: interfaceType == "dry-contact" ? "passive" : "generic-text",
            FrameMode: interfaceType is "can" or "ethernet" or "dry-contact" ? "inactivity-flush" : "line",
            Enabled: true,
            AllowSimulationFallback: interfaceType is "rs232" or "rs485",
            Description: $"Description for {portId}");
}
