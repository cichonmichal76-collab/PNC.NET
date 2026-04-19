using PathoNet.Api.Tests.TestSupport;

namespace PathoNet.Api.Tests.Api;

public sealed class RulebookStoreTests
{
    [Fact]
    public async Task SaveAsync_NormalizesIdsMessageTypesAndRecipients()
    {
        using var root = new PathoNetTestRoot();
        var store = new RulebookStore(root.RootPath);

        var candidate = new PortalRulebookConfig(
            Users:
            [
                new PortalUserRecord(" Jan Kowalski ", " Jan Kowalski ", "", " JAN@EXAMPLE.COM ", " 111 "),
                new PortalUserRecord("jan-kowalski", "Drugi Jan", "Serwis", " second@example.com ", " 222 ")
            ],
            Rules:
            [
                new PortalMessageRuleRecord(
                    Id: " Alarm Rule ",
                    Name: " Alarm Rule ",
                    MatchText: " WARN BUFFER HIGH ",
                    MessageType: "ostrzezenie",
                    Description: " test ",
                    ThresholdHours: 999,
                    SendSms: true,
                    SendEmail: true,
                    RecipientIds: ["jan-kowalski", "ghost"],
                    Enabled: true),
                new PortalMessageRuleRecord(
                    Id: "",
                    Name: "Pusta regula",
                    MatchText: " ",
                    MessageType: null!,
                    Description: "",
                    ThresholdHours: 1,
                    SendSms: false,
                    SendEmail: false,
                    RecipientIds: [],
                    Enabled: true)
            ]);

        var saved = await store.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(2, saved.Users.Length);
        Assert.Equal("jan-kowalski", saved.Users[0].Id);
        Assert.Equal("jan-kowalski-2", saved.Users[1].Id);
        Assert.Single(saved.Rules);

        var rule = saved.Rules[0];
        Assert.Equal("warn", rule.MessageType);
        Assert.Equal(720, rule.ThresholdHours);
        Assert.Equal(["jan-kowalski-2"], rule.RecipientIds);
        Assert.Equal("WARN BUFFER HIGH", rule.MatchText);
    }
}
