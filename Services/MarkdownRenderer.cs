using System.Net;
using Markdig;

namespace Toetsr.Services;

/// <summary>Zet Markdown om naar veilige HTML. Ruwe HTML in de invoer wordt als tekst getoond.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? "" : Markdown.ToHtml(markdown, Pipeline);

    /// <summary>Rendert een stuk tekst inline (zonder &lt;p&gt;), met behoud van spaties aan de randen.</summary>
    public static string ToInlineHtml(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var html = Markdown.ToHtml(markdown.Trim(), Pipeline).Trim();
        if (html.StartsWith("<p>") && html.EndsWith("</p>"))
            html = html[3..^4];
        var lead = char.IsWhiteSpace(markdown[0]) ? " " : "";
        var trail = char.IsWhiteSpace(markdown[^1]) ? " " : "";
        return lead + html + trail;
    }

    /// <summary>Platte tekst voor previews (zonder Markdown-tekens en gaten).</summary>
    public static string ToPlainText(string? markdown, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = WebUtility.HtmlDecode(Markdown.ToPlainText(markdown, Pipeline));
        text = QuestionTypes.DropdownText.GapRegex().Replace(text, "…");
        text = string.Join(' ', text.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }
}
