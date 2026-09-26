using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace MarkdownViewer.WikiLinks;

/// <summary>
/// Obsidian-style wiki links: <c>[[Note]]</c>, <c>[[Note|shown text]]</c>,
/// <c>[[Note#Heading]]</c>, <c>[[#Heading]]</c>. Rendered as
/// <c>&lt;a class="wikilink" data-wiki=".." data-anchor=".."&gt;</c>; the target
/// is resolved against the vault only when clicked (see WikiLinkResolver), since
/// rendering has no vault context. Embeds (<c>![[x]]</c>) are left alone.
/// </summary>
public sealed class WikiLinkExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Ahead of the standard link parser, which also opens on '['.
        if (!pipeline.InlineParsers.Contains<WikiLinkParser>())
            pipeline.InlineParsers.Insert(0, new WikiLinkParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<WikiLinkRenderer>())
            html.ObjectRenderers.Insert(0, new WikiLinkRenderer());
    }
}

public sealed class WikiLinkInline : LeafInline
{
    public string Target { get; init; } = "";
    public string Anchor { get; init; } = "";
    public string Label { get; init; } = "";
}

public sealed class WikiLinkParser : InlineParser
{
    public WikiLinkParser() => OpeningCharacters = new[] { '[' };

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var text = slice.Text;
        var start = slice.Start;
        if (start + 1 > slice.End || text[start + 1] != '[') return false;
        if (start > 0 && text[start - 1] == '!') return false; // embed: not ours

        var close = text.IndexOf("]]", start + 2, slice.End - start - 1, System.StringComparison.Ordinal);
        if (close < 0) return false;
        var inner = text.Substring(start + 2, close - start - 2);
        if (inner.Trim().Length == 0 || inner.IndexOfAny(new[] { '[', ']', '\n', '\r' }) >= 0) return false;

        var label = "";
        var bar = inner.IndexOf('|');
        if (bar >= 0) { label = inner[(bar + 1)..].Trim(); inner = inner[..bar]; }
        var target = inner;
        var anchor = "";
        var hash = inner.IndexOf('#');
        if (hash >= 0) { anchor = inner[(hash + 1)..].Trim(); target = inner[..hash]; }
        target = target.Trim();
        if (target.Length == 0 && anchor.Length == 0) return false;
        if (label.Length == 0)
            label = target.Length == 0 ? anchor : anchor.Length == 0 ? target : target + " > " + anchor;

        processor.Inline = new WikiLinkInline
        {
            Target = target,
            Anchor = anchor,
            Label = label,
            Span = new global::Markdig.Syntax.SourceSpan(processor.GetSourcePosition(start, out var line, out var col),
                processor.GetSourcePosition(close + 1)),
            Line = line,
            Column = col,
        };
        slice.Start = close + 2;
        return true;
    }
}

public sealed class WikiLinkRenderer : HtmlObjectRenderer<WikiLinkInline>
{
    protected override void Write(HtmlRenderer renderer, WikiLinkInline link)
    {
        renderer.Write("<a class=\"wikilink\" href=\"#\" data-wiki=\"").WriteEscape(link.Target).Write('"');
        if (link.Anchor.Length > 0)
            renderer.Write(" data-anchor=\"").WriteEscape(link.Anchor).Write('"');
        renderer.Write('>').WriteEscape(link.Label).Write("</a>");
    }
}
