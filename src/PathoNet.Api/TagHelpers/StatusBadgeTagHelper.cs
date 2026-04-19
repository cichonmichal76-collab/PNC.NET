using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PathoNet.Api.TagHelpers;

[HtmlTargetElement("status-badge")]
public sealed class StatusBadgeTagHelper : TagHelper
{
    [HtmlAttributeName("tone")]
    public string Tone { get; set; } = "info";

    [HtmlAttributeName("kind")]
    public string Kind { get; set; } = "pill";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"{NormalizeKind(Kind)} {NormalizeTone(Tone)}");

        var childContent = await output.GetChildContentAsync();
        output.Content.SetHtmlContent(childContent);
    }

    private static string NormalizeKind(string? value) =>
        string.Equals(value, "chip", StringComparison.OrdinalIgnoreCase)
            ? "chip"
            : "pill";

    private static string NormalizeTone(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "warn" or "warning" => "attention",
            "error" => "critical",
            "alarm" => "alarm",
            "attention" => "attention",
            "critical" => "critical",
            "online" => "online",
            "soft" => "soft",
            _ => "info"
        };
}
