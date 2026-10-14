using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace StillMark.Core.Markdown;

public sealed record HeadingInfo(int Level, string Text, int Line, string? Id);

/// <summary>Zero-based, inclusive source line range of a code block (fences included).</summary>
public sealed record LineRange(int Start, int End);

public sealed record RenderResult(string Html, IReadOnlyList<HeadingInfo> Headings, IReadOnlyList<LineRange> CodeBlocks);

/// <summary>
/// Markdown → HTML. Rendering is display-only: it never feeds anything back into the source text.
/// The dialect is CommonMark + GitHub-style tables, task lists, strikethrough, autolinks, footnotes and alerts.
/// Extensions that would reinterpret ordinary prose (generic attributes "{...}", math "$", single "~") are off on purpose.
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>Virtual host the preview uses to load local files (images) relative to the document.</summary>
    public const string DocHost = "doc.stillmark";

    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseFootnotes()
        .UseAlertBlocks()
        .UseCjkFriendlyEmphasis()
        .UsePreciseSourceLocation()
        .Build();

    private static readonly Regex WindowsAbsolute = new(@"^[A-Za-z]:[\\/]", RegexOptions.Compiled);

    public static RenderResult Render(string markdown)
    {
        var doc = Markdig.Markdown.Parse(markdown, Pipeline);
        var headings = new List<HeadingInfo>();
        var codeBlocks = new List<LineRange>();

        foreach (var node in doc.Descendants())
        {
            switch (node)
            {
                case HeadingBlock h:
                    headings.Add(new HeadingInfo(h.Level, PlainText(h.Inline), h.Line, h.TryGetAttributes()?.Id));
                    h.GetAttributes().AddProperty("data-line", h.Line.ToString());
                    break;
                case FencedCodeBlock fenced:
                    codeBlocks.Add(new LineRange(fenced.Line, fenced.Line + fenced.Lines.Count + (fenced.ClosingFencedCharCount > 0 ? 1 : 0)));
                    fenced.GetAttributes().AddProperty("data-line", fenced.Line.ToString());
                    break;
                case CodeBlock indented:
                    codeBlocks.Add(new LineRange(indented.Line, indented.Line + Math.Max(0, indented.Lines.Count - 1)));
                    indented.GetAttributes().AddProperty("data-line", indented.Line.ToString());
                    break;
                case ParagraphBlock or ListItemBlock or QuoteBlock or ThematicBreakBlock or Markdig.Extensions.Tables.Table:
                    ((Block)node).GetAttributes().AddProperty("data-line", ((Block)node).Line.ToString());
                    break;
                case LinkInline link when link.Url != null:
                    link.Url = RewriteLocalUrl(link.Url);
                    break;
            }
        }

        var sw = new StringWriter(new StringBuilder(markdown.Length * 2));
        var renderer = new Markdig.Renderers.HtmlRenderer(sw);
        Pipeline.Setup(renderer);
        renderer.Render(doc);
        sw.Flush();
        return new RenderResult(sw.ToString(), headings, codeBlocks);
    }

    /// <summary>Absolute Windows paths and file:// URLs cannot be loaded by an https page; route them through the doc host.</summary>
    public static string RewriteLocalUrl(string url)
    {
        string? path = null;
        if (WindowsAbsolute.IsMatch(url)) path = url;
        else if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.IsFile) path = u.LocalPath;
        }
        return path == null ? url : "https://" + DocHost + "/" + EncodePath(path);
    }

    /// <summary>Base href for a document directory, so relative URLs (including "../") resolve against it.</summary>
    public static string BaseHrefFor(string? directory) =>
        directory == null ? $"https://{DocHost}/" : $"https://{DocHost}/{EncodePath(directory.TrimEnd('\\', '/'))}/";

    public static string EncodePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            // Keep the drive colon readable ("D:"); percent-encode everything else.
            parts[i] = i == 0 && parts[i].Length == 2 && parts[i][1] == ':' ? parts[i] : Uri.EscapeDataString(parts[i]);
        }
        return string.Join('/', parts);
    }

    /// <summary>Maps a doc-host URL back to a local file path; null if it is not one.</summary>
    public static string? LocalPathFromDocUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.Equals(DocHost, StringComparison.OrdinalIgnoreCase))
            return null;
        var p = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (p.Length < 2 || p[1] != ':') return null; // only drive-rooted paths
        try
        {
            return Path.GetFullPath(p.Replace('/', '\\'));
        }
        catch
        {
            return null;
        }
    }

    public static string PlainText(ContainerInline? inline)
    {
        if (inline == null) return "";
        var sb = new StringBuilder();
        foreach (var x in inline.Descendants())
        {
            switch (x)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
                case LineBreakInline: sb.Append(' '); break;
            }
        }
        return sb.ToString().Trim();
    }
}
