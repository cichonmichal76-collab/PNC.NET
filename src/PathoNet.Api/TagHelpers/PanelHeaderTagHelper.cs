using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PathoNet.Api.TagHelpers;

[HtmlTargetElement("panel-header")]
public sealed class PanelHeaderTagHelper : TagHelper
{
    [HtmlAttributeName("eyebrow")]
    public string Eyebrow { get; set; } = string.Empty;

    [HtmlAttributeName("title")]
    public string Title { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "panel-head");
        output.Content.SetHtmlContent(
            $"<p class=\"eyebrow\">{HtmlEncoder.Default.Encode(Eyebrow)}</p><h3>{HtmlEncoder.Default.Encode(Title)}</h3>");
    }
}
