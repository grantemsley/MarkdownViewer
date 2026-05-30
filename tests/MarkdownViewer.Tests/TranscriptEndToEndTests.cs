using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MarkdownViewer.Services;
using Xunit;
using Xunit.Abstractions;

namespace MarkdownViewer.Tests;

/// <summary>
/// Exercises the full TranscriptService → MarkdownService pipeline against real
/// .jsonl transcripts. A representative transcript is bundled next to the test
/// assembly (Fixtures/, copied from sample/demo-session.jsonl) so the theories
/// always have data — including on CI, where the developer's real
/// .claude/transcripts/ are gitignored. Locally, any real transcripts found are
/// added on top for messier, regression-catching coverage.
/// </summary>
public class TranscriptEndToEndTests
{
    private readonly ITestOutputHelper _out;

    public TranscriptEndToEndTests(ITestOutputHelper output) { _out = output; }

    public static IEnumerable<object[]> RealTranscripts()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Bundled fixture(s) copied next to the test assembly — always present,
        // so the theories have data on CI (no .claude/transcripts/ there).
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (Directory.Exists(fixtures))
            foreach (var f in Directory.EnumerateFiles(fixtures, "*.jsonl"))
                if (seen.Add(f)) yield return new object[] { f };

        // Plus any real transcripts found locally — extra, messier coverage.
        var dir = TryFindTranscriptsDir();
        if (dir is not null)
            foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl"))
                if (seen.Add(f)) yield return new object[] { f };
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
        var rewritten = UrlRewriter.RewriteRelativeUrls(result.Html, "https://app.local/__vault/");
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

    private static string? TryFindTranscriptsDir()
    {
        // Walk up from the test assembly's bin dir looking for a transcripts
        // folder. The Stop hook writes session transcripts to
        // .claude/transcripts/ at the project root; older layouts kept them
        // under notes/transcripts/. Lets the test run against real transcripts
        // without copying several MB into the test project.
        string[] relativeProbes =
        {
            Path.Combine(".claude", "transcripts"),
            Path.Combine("notes", "transcripts"),
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var rel in relativeProbes)
            {
                var probe = Path.Combine(dir.FullName, rel);
                if (Directory.Exists(probe) && Directory.EnumerateFiles(probe, "*.jsonl").Any())
                    return probe;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
