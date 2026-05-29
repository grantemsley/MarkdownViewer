using System.Collections.Generic;
using System.IO;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkdownViewer.Services;

public class HeadingEntry
{
    public int Level { get; init; }
    public string Text { get; init; } = "";
    public string Id { get; init; } = "";
}

public class RenderResult
{
    public string Html { get; init; } = "";
    public List<HeadingEntry> Headings { get; init; } = new();
}

public static class MarkdownService
{
    private static readonly MarkdownPipeline _pipeline = BuildPipeline(lineNumbers: false);
    private static readonly MarkdownPipeline _pipelineLines = BuildPipeline(lineNumbers: true);

    private static MarkdownPipeline BuildPipeline(bool lineNumbers)
    {
        // UseAdvancedExtensions already includes pipe + grid tables, footnotes,
        // autolinks, task lists, definition lists, emphasis extras, figures,
        // footers, citations, custom containers, abbreviations, media links,
        // auto-identifiers, generic attributes, and UseDiagrams (which wraps
        // fenced lang=mermaid in <pre class="mermaid">).
        var b = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseYamlFrontMatter()
            .UseMathematics();

        if (lineNumbers)
        {
            b = b.UsePreciseSourceLocation();
        }
        return b.Build();
    }

    public static RenderResult Render(string source, bool showLineNumbers)
    {
        var pipeline = showLineNumbers ? _pipelineLines : _pipeline;
        var doc = Markdown.Parse(source, pipeline);

        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            var renderer = new HtmlRenderer(writer);
            pipeline.Setup(renderer);

            if (showLineNumbers)
            {
                // Wrap each block in a div carrying md-block + data-line so the
                // gutter CSS can show source line numbers in the margin.
                AnnotateBlocks(doc);
            }

            renderer.Render(doc);
        }

        var headings = new List<HeadingEntry>();
        foreach (var block in doc.Descendants())
        {
            if (block is HeadingBlock h && h.Inline != null)
            {
                var text = ExtractText(h);
                var id = h.GetAttributes().Id ?? Slug(text);
                headings.Add(new HeadingEntry { Level = h.Level, Text = text, Id = id });
            }
        }

        return new RenderResult { Html = sb.ToString(), Headings = headings };
    }

    private static void AnnotateBlocks(MarkdownObject doc)
    {
        foreach (var node in doc.Descendants())
        {
            if (node is not Block block) continue;
            if (block.Parent is not MarkdownDocument) continue;
            // Top-level blocks only — nested blocks share their parent's gutter row.
            var attrs = block.GetAttributes();
            var line = block.Line + 1;
            attrs.AddClass("md-block");
            attrs.AddClass("md-block-" + block.GetType().Name.Replace("Block", "").ToLowerInvariant());
            attrs.AddProperty("data-line", line.ToString());
        }
    }

    private static string ExtractText(LeafBlock leaf)
    {
        if (leaf.Inline == null) return "";
        var sb = new StringBuilder();
        foreach (var inline in leaf.Inline.FindDescendants<Markdig.Syntax.Inlines.LiteralInline>())
            sb.Append(inline.Content.ToString());
        if (sb.Length == 0)
        {
            // Fall back to any descendant inline's full string repr
            foreach (var inline in leaf.Inline)
                sb.Append(inline.ToString());
        }
        return sb.ToString();
    }

    private static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        // collapse multiple dashes
        var s = sb.ToString();
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Trim('-');
    }
}
