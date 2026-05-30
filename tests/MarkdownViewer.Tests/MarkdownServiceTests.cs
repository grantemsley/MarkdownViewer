using System.Linq;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class MarkdownServiceTests
{
    [Fact]
    public void Render_SurfacesYamlFrontmatter_InCollapsedDetails()
    {
        var src = "---\ntitle: Surfaced\nauthor: Me\n---\n# Visible\n\nBody.";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        // Surfaced in a collapsed <details>, not dumped into the body and not
        // dropped entirely. The fence lines are stripped; the YAML body shows.
        Assert.Contains("<details class=\"frontmatter\">", result.Html);
        Assert.Contains("<summary>Frontmatter</summary>", result.Html);
        Assert.Contains("title: Surfaced", result.Html);
        Assert.Contains("author: Me", result.Html);
        // The details block precedes the rendered body.
        Assert.True(result.Html.IndexOf("frontmatter") < result.Html.IndexOf("Visible"));
        Assert.Contains("Visible", result.Html);
    }

    [Fact]
    public void Render_NoFrontmatter_EmitsNoDetailsBlock()
    {
        var result = MarkdownService.Render("# Just a heading\n\nText.", showLineNumbers: false);

        Assert.DoesNotContain("class=\"frontmatter\"", result.Html);
    }

    [Fact]
    public void Render_ExtractsHeadings_WithSlug()
    {
        var src = "# Hello World\n\n## Sub Section\n\n### Deeper Heading";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        Assert.Equal(3, result.Headings.Count);
        Assert.Equal(1, result.Headings[0].Level);
        Assert.Equal("Hello World", result.Headings[0].Text);
        Assert.Equal("hello-world", result.Headings[0].Id);
        Assert.Equal(2, result.Headings[1].Level);
        Assert.Equal("sub-section", result.Headings[1].Id);
        Assert.Equal(3, result.Headings[2].Level);
        Assert.Equal("deeper-heading", result.Headings[2].Id);
    }

    [Fact]
    public void Render_MathDoubleDollar_ProducesMathClass()
    {
        var src = "Some math: $$x^2 + y^2 = z^2$$";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        Assert.Contains("math", result.Html);
    }

    [Fact]
    public void Render_FencedMermaid_BecomesDivMermaid()
    {
        // bridge.js queries `.mermaid` to find diagrams; the actual element
        // must be a <div> (Markdig's UseDiagrams emits a div, not a pre).
        var src = "```mermaid\ngraph TD; A-->B;\n```";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        Assert.Contains("<div class=\"mermaid\">", result.Html);
        Assert.DoesNotContain("<pre class=\"mermaid\">", result.Html);
    }

    [Fact]
    public void Render_WithLineNumbers_AnnotatesTopLevelBlocks()
    {
        var src = "# Heading\n\nParagraph here.\n\n```\ncode\n```\n";
        var result = MarkdownService.Render(src, showLineNumbers: true);

        Assert.Contains("md-block", result.Html);
        Assert.Contains("data-line=\"1\"", result.Html);
        Assert.Contains("data-line=\"3\"", result.Html);
        Assert.Contains("data-line=\"5\"", result.Html);
    }

    [Fact]
    public void Render_WithoutLineNumbers_DoesNotAnnotateBlocks()
    {
        var src = "# Heading\n\nParagraph.";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        Assert.DoesNotContain("md-block", result.Html);
        Assert.DoesNotContain("data-line", result.Html);
    }

    [Fact]
    public void Render_Tables_ProduceTableHtml()
    {
        var src = "| a | b |\n|---|---|\n| 1 | 2 |";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        Assert.Contains("<table", result.Html);
        Assert.Contains("<th", result.Html);
        Assert.Contains("<td", result.Html);
    }

    [Fact]
    public void Render_TaskLists_RenderCheckboxes()
    {
        var src = "- [x] done\n- [ ] todo";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        Assert.Contains("type=\"checkbox\"", result.Html);
    }

    [Fact]
    public void Render_EmptySource_ProducesEmptyHtml()
    {
        var result = MarkdownService.Render("", showLineNumbers: false);

        Assert.Equal("", result.Html.Trim());
        Assert.Empty(result.Headings);
    }

    [Fact]
    public void Render_SlugCollapsesPunctuation()
    {
        var src = "## What's up, doc?";
        var result = MarkdownService.Render(src, showLineNumbers: false);

        var id = result.Headings.Single().Id;
        Assert.DoesNotContain("'", id);
        Assert.DoesNotContain("?", id);
        Assert.DoesNotContain(",", id);
    }
}
