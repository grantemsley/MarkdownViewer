using System;
using System.IO;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class WikiLinkTests : IDisposable
{
    private readonly string _root;

    public WikiLinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mvtest_wiki_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "projects", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));
        File.WriteAllText(Path.Combine(_root, "Home.md"), "# Home");
        File.WriteAllText(Path.Combine(_root, "projects", "Plan.md"), "# Plan");
        File.WriteAllText(Path.Combine(_root, "projects", "deep", "Plan.md"), "# Deeper plan");
        File.WriteAllText(Path.Combine(_root, "other", "Plan.md"), "# Other plan");
        File.WriteAllText(Path.Combine(_root, "other", "report.pdf"), "%PDF");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string Html(string md) => MarkdownService.Render(md, showLineNumbers: false).Html;

    // --- Rendering ---

    [Theory]
    [InlineData("[[Home]]", "<a class=\"wikilink\" href=\"#\" data-wiki=\"Home\">Home</a>")]
    [InlineData("[[Home|start here]]", "<a class=\"wikilink\" href=\"#\" data-wiki=\"Home\">start here</a>")]
    [InlineData("[[Home#Setup]]", "<a class=\"wikilink\" href=\"#\" data-wiki=\"Home\" data-anchor=\"Setup\">Home &gt; Setup</a>")]
    [InlineData("[[#Setup]]", "<a class=\"wikilink\" href=\"#\" data-wiki=\"\" data-anchor=\"Setup\">Setup</a>")]
    [InlineData("[[a \"b\" <c>]]", "data-wiki=\"a &quot;b&quot; &lt;c&gt;\"")]
    public void Renders_wiki_links(string md, string expected)
    {
        Assert.Contains(expected, Html(md));
    }

    [Theory]
    [InlineData("![[pic.png]]")]          // embed: not a link
    [InlineData("`[[Home]]`")]            // code span
    [InlineData("[[]]")]
    [InlineData("[[unclosed")]
    public void Leaves_non_links_alone(string md)
    {
        Assert.DoesNotContain("wikilink", Html(md));
    }

    [Fact]
    public void Ordinary_links_and_task_lists_still_work()
    {
        var html = Html("- [ ] todo\n\n[text](x.md) and [ref][r]\n\n[r]: y.md");
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("<a href=\"x.md\">text</a>", html);
        Assert.Contains("<a href=\"y.md\">ref</a>", html);
    }

    [Fact]
    public void HeadingId_matches_rendered_heading_id()
    {
        var id = MarkdownService.HeadingId("Setup & Install (v2)");
        Assert.NotEmpty(id);
        Assert.Contains($"id=\"{id}\"", Html("## Setup & Install (v2)"));
    }

    // --- Resolution ---

    [Fact]
    public void Resolves_relative_to_current_folder_first()
    {
        var found = WikiLinkResolver.Resolve(_root, Path.Combine(_root, "other"), "Plan");
        Assert.Equal(Path.Combine(_root, "other", "Plan.md"), found, ignoreCase: true);
    }

    [Fact]
    public void Falls_back_to_vault_search_preferring_the_shortest_path()
    {
        var found = WikiLinkResolver.Resolve(_root, _root, "plan");
        Assert.Equal(Path.Combine(_root, "other", "Plan.md"), found, ignoreCase: true);
    }

    [Fact]
    public void Folder_qualified_target_must_match_the_path_tail()
    {
        var found = WikiLinkResolver.Resolve(_root, _root, "deep/Plan");
        Assert.Equal(Path.Combine(_root, "projects", "deep", "Plan.md"), found, ignoreCase: true);
    }

    [Fact]
    public void Target_with_extension_resolves_as_named()
    {
        var found = WikiLinkResolver.Resolve(_root, _root, "report.pdf");
        Assert.Equal(Path.Combine(_root, "other", "report.pdf"), found, ignoreCase: true);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("../outside")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("")]
    public void Unresolvable_or_outside_targets_return_null(string target)
    {
        Assert.Null(WikiLinkResolver.Resolve(_root, _root, target));
    }
}
