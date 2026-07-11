using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class FileSearchServiceTests : IDisposable
{
    private readonly string _dir;

    public FileSearchServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mvtest_search_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string Write(string relPath, string content)
    {
        var full = Path.Combine(_dir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    private string WriteBytes(string relPath, byte[] bytes)
    {
        var full = Path.Combine(_dir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    private static SearchOptions Opts(
        long maxBytes = 5_000_000, bool scanAll = false, bool includeHidden = false,
        IEnumerable<string>? allowed = null, IEnumerable<string>? excludedDirs = null,
        int maxHitsPerFile = 50, int maxTotal = 5000, int dop = 4) => new(
            maxBytes,
            new HashSet<string>(allowed ?? ContentRouter.KnownTextExtensions, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(excludedDirs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
            includeHidden, scanAll, dop, maxHitsPerFile, maxTotal);

    // Synchronous, thread-safe collector — SearchAsync calls Report from worker
    // threads, so lock; everything is done once the awaited task returns.
    private sealed class Collector : IProgress<SearchFileResult>
    {
        private readonly object _lock = new();
        public readonly List<SearchFileResult> Results = new();
        public void Report(SearchFileResult r) { lock (_lock) Results.Add(r); }
        public SearchFileResult? ByName(string name) =>
            Results.FirstOrDefault(r => string.Equals(Path.GetFileName(r.FullPath), name, StringComparison.OrdinalIgnoreCase));
    }

    private Task<SearchSummary> Run(string query, SearchOptions opts, Collector c, CancellationToken ct = default)
        => FileSearchService.SearchAsync(_dir, query, opts, c, ct);

    // ── ContentRouter helper surface (Phase 1 additions) ─────────────────────

    [Theory]
    [InlineData(".md", true)]
    [InlineData(".py", true)]
    [InlineData(".txt", true)]
    [InlineData(".png", false)]
    [InlineData(".exe", false)]
    public void IsKnownTextExtension_MatchesViewerTextSet(string ext, bool expected)
        => Assert.Equal(expected, ContentRouter.IsKnownTextExtension(ext));

    [Fact]
    public void KnownTextExtensions_ContainsMarkdownAndCode()
    {
        Assert.Contains(".md", ContentRouter.KnownTextExtensions);
        Assert.Contains(".cs", ContentRouter.KnownTextExtensions);
    }

    [Fact]
    public void DecodeCappedFile_UnderCap_NotTruncated()
    {
        var p = Write("a.txt", "hello world");
        var text = ContentRouter.DecodeCappedFile(p, 5_000_000, out var truncated);
        Assert.False(truncated);
        Assert.Equal("hello world", text);
    }

    [Fact]
    public void DecodeCappedFile_OverCap_TruncatedToHead()
    {
        var p = Write("a.txt", new string('a', 1000));
        var text = ContentRouter.DecodeCappedFile(p, 100, out var truncated);
        Assert.True(truncated);
        Assert.Equal(100, text.Length);
    }

    // ── content matching ─────────────────────────────────────────────────────

    [Fact]
    public async Task ContentMatch_ReportsCorrectLineNumber()
    {
        Write("note.md", "alpha\nbeta\nneedle here\ndelta\n");
        var c = new Collector();
        var summary = await Run("needle", Opts(), c);

        var r = c.ByName("note.md");
        Assert.NotNull(r);
        var hit = Assert.Single(r!.Hits);
        Assert.Equal(3, hit.Line);
        Assert.Equal("needle here", hit.Preview);
        Assert.Equal(0, hit.MatchStart);
        Assert.Equal(6, hit.MatchLength);
        Assert.Equal(1, summary.FilesMatched);
    }

    [Fact]
    public async Task ContentMatch_CaseInsensitive()
    {
        Write("note.md", "The NEEDLE is sharp");
        var c = new Collector();
        await Run("needle", Opts(), c);
        Assert.NotNull(c.ByName("note.md"));
    }

    [Fact]
    public async Task Preview_TrimsLeadingIndentAndKeepsMatchStart()
    {
        Write("note.md", "\t    indented needle\n");
        var c = new Collector();
        await Run("needle", Opts(), c);
        var hit = c.ByName("note.md")!.Hits.Single();
        Assert.Equal("indented needle", hit.Preview);
        Assert.Equal("indented needle".IndexOf("needle", StringComparison.Ordinal), hit.MatchStart);
    }

    // ── filename matching ────────────────────────────────────────────────────

    [Fact]
    public async Task FilenameMatch_OnBinaryFile_ReportedWithoutContentScan()
    {
        // A .png with a NUL byte: name matches "logo", contents are never read.
        WriteBytes("logo-needle.png", new byte[] { 0x89, 0x00, 0x50, 0x4E });
        var c = new Collector();
        await Run("needle", Opts(), c);

        var r = c.ByName("logo-needle.png");
        Assert.NotNull(r);
        Assert.True(r!.NameMatched);
        Assert.Empty(r.Hits);
    }

    [Fact]
    public async Task FilenameMatch_AndContentMatch_BothOnSameFile()
    {
        Write("needle.md", "also needle in body");
        var c = new Collector();
        await Run("needle", Opts(), c);
        var r = c.ByName("needle.md")!;
        Assert.True(r.NameMatched);
        Assert.Single(r.Hits);
    }

    // ── filters (the SMB perf levers) ────────────────────────────────────────

    [Fact]
    public async Task ExtensionNotAllowlisted_ContentNotScanned()
    {
        // .xyz is not a known text ext; body has the term but the name does not.
        Write("data.xyz", "contains needle text");
        var c = new Collector();
        await Run("needle", Opts(), c);
        Assert.Null(c.ByName("data.xyz"));
    }

    [Fact]
    public async Task ScanAllText_ScansUnknownExtensionThatIsNotBinary()
    {
        Write("data.xyz", "contains needle text");
        var c = new Collector();
        await Run("needle", Opts(scanAll: true), c);
        var r = c.ByName("data.xyz");
        Assert.NotNull(r);
        Assert.Single(r!.Hits);
    }

    [Fact]
    public async Task ScanAllText_SkipsBinaryContent()
    {
        // Unknown ext with a NUL byte: even under ScanAllText the binary peek skips it.
        WriteBytes("blob.xyz", Encoding.UTF8.GetBytes("needle").Concat(new byte[] { 0x00 }).ToArray());
        var c = new Collector();
        await Run("needle", Opts(scanAll: true), c);
        Assert.Null(c.ByName("blob.xyz"));
    }

    [Fact]
    public async Task SizeCap_SkipsContent()
    {
        // Body has the term, name does not; over the byte cap -> no content scan, no result.
        Write("big.md", new string('x', 500) + " needle " + new string('y', 500));
        var c = new Collector();
        await Run("needle", Opts(maxBytes: 100), c);
        Assert.Null(c.ByName("big.md"));
    }

    [Fact]
    public async Task SizeCap_UnderCap_ContentScanned()
    {
        Write("small.md", "needle");
        var c = new Collector();
        await Run("needle", Opts(maxBytes: 5_000_000), c);
        Assert.NotNull(c.ByName("small.md"));
    }

    [Fact]
    public async Task ExcludedDir_NotDescended()
    {
        Write("root.md", "needle");
        Write(Path.Combine("node_modules", "dep.md"), "needle");
        var c = new Collector();
        await Run("needle", Opts(excludedDirs: new[] { "node_modules" }), c);

        Assert.NotNull(c.ByName("root.md"));
        Assert.Null(c.ByName("dep.md"));
    }

    [Fact]
    public async Task HiddenFile_SkippedByDefault_FoundWhenIncluded()
    {
        Write(".secret.md", "needle");

        var c1 = new Collector();
        await Run("needle", Opts(includeHidden: false), c1);
        Assert.Null(c1.ByName(".secret.md"));

        var c2 = new Collector();
        await Run("needle", Opts(includeHidden: true), c2);
        Assert.NotNull(c2.ByName(".secret.md"));
    }

    [Fact]
    public async Task HiddenDir_SkippedByDefault()
    {
        Write(Path.Combine(".git", "config.md"), "needle");
        var c = new Collector();
        await Run("needle", Opts(includeHidden: false), c);
        Assert.Null(c.ByName("config.md"));
    }

    // ── caps + cancellation ──────────────────────────────────────────────────

    [Fact]
    public async Task MaxHitsPerFile_Caps()
    {
        Write("many.md", string.Join("\n", Enumerable.Repeat("needle", 100)));
        var c = new Collector();
        await Run("needle", Opts(maxHitsPerFile: 5), c);
        Assert.Equal(5, c.ByName("many.md")!.Hits.Count);
    }

    [Fact]
    public async Task MaxTotalHits_TruncatesAndStops()
    {
        for (int i = 0; i < 40; i++)
            Write($"f{i}.md", string.Join("\n", Enumerable.Repeat("needle", 10)));
        var c = new Collector();
        var summary = await Run("needle", Opts(maxTotal: 50, dop: 2), c);

        Assert.True(summary.Truncated);
        Assert.False(summary.Cancelled);
        Assert.True(summary.TotalHits >= 50);
    }

    [Fact]
    public async Task Cancellation_ReturnsCancelled()
    {
        for (int i = 0; i < 5; i++) Write($"f{i}.md", "needle");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var c = new Collector();
        var summary = await Run("needle", Opts(), c, cts.Token);
        Assert.True(summary.Cancelled);
    }

    [Fact]
    public async Task Summary_CountsScannedAndMatched()
    {
        Write("a.md", "needle");
        Write("b.md", "nothing here");
        Write("c.md", "needle again");
        var c = new Collector();
        var summary = await Run("needle", Opts(), c);

        Assert.Equal(3, summary.FilesScanned);
        Assert.Equal(2, summary.FilesMatched);
        Assert.False(summary.Truncated);
        Assert.False(summary.Cancelled);
    }

    [Fact]
    public async Task ShortAndMissingInputs_ReturnEmpty()
    {
        Write("a.md", "needle");
        var c = new Collector();

        var s1 = await FileSearchService.SearchAsync(_dir, "", Opts(), c, default);
        Assert.Equal(0, s1.FilesScanned);

        var s2 = await FileSearchService.SearchAsync(
            Path.Combine(_dir, "does-not-exist"), "needle", Opts(), c, default);
        Assert.Equal(0, s2.FilesScanned);

        Assert.Empty(c.Results);
    }

    // ── SearchOptions.From / SearchPrefs.Normalize (Phase 2) ─────────────────

    [Fact]
    public void OptionsFrom_EmptyInclude_UsesKnownTextSet()
    {
        var o = SearchOptions.From(new SearchPrefs());
        Assert.Contains(".md", o.AllowedExtensions);
        Assert.Contains(".cs", o.AllowedExtensions);
        Assert.DoesNotContain(".png", o.AllowedExtensions);
    }

    [Fact]
    public void OptionsFrom_IncludeOverridesDefault()
    {
        var o = SearchOptions.From(new SearchPrefs { IncludeExtensions = new() { "txt" } });
        Assert.Contains(".txt", o.AllowedExtensions);
        Assert.DoesNotContain(".md", o.AllowedExtensions);
    }

    [Fact]
    public void OptionsFrom_ExcludeRemovesFromDefault()
    {
        var o = SearchOptions.From(new SearchPrefs { ExcludeExtensions = new() { "md" } });
        Assert.DoesNotContain(".md", o.AllowedExtensions);
        Assert.Contains(".cs", o.AllowedExtensions);
    }

    [Theory]
    [InlineData("md")]
    [InlineData(".md")]
    [InlineData(".MD")]
    [InlineData("*.md")]
    public void OptionsFrom_NormalizesExtensionForms(string form)
    {
        var o = SearchOptions.From(new SearchPrefs { IncludeExtensions = new() { form } });
        Assert.Contains(".md", o.AllowedExtensions);
    }

    [Fact]
    public void OptionsFrom_DefaultExcludeFolders_Present()
    {
        var o = SearchOptions.From(new SearchPrefs());
        Assert.Contains("node_modules", o.ExcludedDirNames);
        Assert.Contains(".git", o.ExcludedDirNames);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var p = new SearchPrefs
        {
            MaxDegreeOfParallelism = 999,
            MaxFileBytes = 1,
            MaxHitsPerFile = 0,
            MaxTotalHits = 0,
        };
        p.Normalize();
        Assert.Equal(64, p.MaxDegreeOfParallelism);
        Assert.Equal(64L * 1024, p.MaxFileBytes);
        Assert.Equal(1, p.MaxHitsPerFile);
        Assert.Equal(1, p.MaxTotalHits);
    }
}
