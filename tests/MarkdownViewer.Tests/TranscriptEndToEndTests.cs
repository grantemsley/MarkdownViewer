using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MarkdownViewer.Services;
using Xunit;
using Xunit.Abstractions;

namespace MarkdownViewer.Tests;

/// <summary>
/// Exercises the full TranscriptService → MarkdownService pipeline against
/// the real .jsonl transcripts living under Projects/MarkdownViewer/notes/transcripts/.
/// Catches Markdig parsing regressions and pathological transcript shapes
/// that synthetic per-record tests don't cover.
/// </summary>
public class TranscriptEndToEndTests
{
    private readonly ITestOutputHelper _out;

    public TranscriptEndToEndTests(ITestOutputHelper output) { _out = output; }

    public static IEnumerable<object[]> RealTranscripts()
    {
        var root = TryFindRepoRoot();
        if (root is null) yield break;
        var dir = Path.Combine(root, "Projects", "MarkdownViewer", "notes", "transcripts");
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl"))
            yield return new object[] { f };
    }

    [Theory]
    [MemberData(nameof(RealTranscripts))]
    public void RealTranscript_RendersWithoutThrowing(string filePath)
    {
        var jsonl = File.ReadAllText(filePath);

        // Stage 1: transform.
        var markdown = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("<div class=\"t-doc\">", markdown);

        // Stage 2: Markdig render (this is where embedded HTML blocks can
        // surprise us).
        var result = MarkdownService.Render(markdown, showLineNumbers: false);
        Assert.NotNull(result.Html);
        Assert.NotEmpty(result.Html);

        // Stage 3: URL rewrite layer applied by MainWindow before sending
        // to the WebView. It must not touch class/id attrs.
        var rewritten = UrlRewriter.RewriteRelativeUrls(result.Html, "https://vault.local/");
        Assert.Contains("class=\"t-doc\"", rewritten);
        Assert.Contains("id=\"tf-conversation\"", rewritten);

        _out.WriteLine($"{Path.GetFileName(filePath)}: {result.Headings.Count} headings, {result.Html.Length} chars");
    }

    [Theory]
    [MemberData(nameof(RealTranscripts))]
    public void RealTranscript_ProducesOutlineHeadings(string filePath)
    {
        // Any real Claude Code transcript has at least one user message,
        // which means at least one ### heading for the outline panel.
        var jsonl = File.ReadAllText(filePath);
        var markdown = TranscriptService.ToMarkdown(jsonl);
        var result = MarkdownService.Render(markdown, showLineNumbers: false);

        Assert.NotEmpty(result.Headings);
        Assert.Contains(result.Headings, h => h.Level == 3);
    }

    [Theory]
    [MemberData(nameof(RealTranscripts))]
    public void RealTranscript_SessionHeaderPresent(string filePath)
    {
        // Real transcripts always carry sessionId + timestamp + branch.
        var jsonl = File.ReadAllText(filePath);
        var markdown = TranscriptService.ToMarkdown(jsonl);
        var result = MarkdownService.Render(markdown, showLineNumbers: false);

        Assert.Contains("t-session-header", result.Html);
    }

    private static string? TryFindRepoRoot()
    {
        // Walk up from the test assembly's bin dir until we see the
        // project-specific notes folder. Lets the test be hermetic without
        // copying ~7 MB of transcripts into the test project.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "Projects", "MarkdownViewer", "notes", "transcripts");
            if (Directory.Exists(probe)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
