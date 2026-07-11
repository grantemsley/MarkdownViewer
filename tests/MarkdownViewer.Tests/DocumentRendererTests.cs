using System;
using System.IO;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class DocumentRendererTests : IDisposable
{
    private readonly string _root;

    public DocumentRendererTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MdvDocRenderer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ─── VaultRelDir ─────────────────────────────────────────────────────

    [Fact]
    public void VaultRelDir_NullOrEmptyRoot_ReturnsEmpty()
    {
        Assert.Equal("", DocumentRenderer.VaultRelDir(null, @"C:\x\a.md"));
        Assert.Equal("", DocumentRenderer.VaultRelDir("", @"C:\x\a.md"));
    }

    [Fact]
    public void VaultRelDir_FileAtRoot_ReturnsEmpty()
    {
        Assert.Equal("", DocumentRenderer.VaultRelDir(_root, Path.Combine(_root, "a.md")));
    }

    [Fact]
    public void VaultRelDir_NestedFile_ReturnsForwardSlashDir()
    {
        var file = Path.Combine(_root, "sub", "deep", "a.md");
        Assert.Equal("sub/deep", DocumentRenderer.VaultRelDir(_root, file));
    }

    [Fact]
    public void VaultRelDir_FileOutsideRoot_ReturnsEmpty()
    {
        Assert.Equal("", DocumentRenderer.VaultRelDir(_root, @"C:\elsewhere\a.md"));
    }

    [Fact]
    public void VaultRelDir_TrailingSeparatorOnRoot_SameResult()
    {
        var file = Path.Combine(_root, "sub", "a.md");
        Assert.Equal("sub", DocumentRenderer.VaultRelDir(_root + Path.DirectorySeparatorChar, file));
    }

    [Fact]
    public void VaultRelDir_CaseInsensitiveRootMatch()
    {
        var file = Path.Combine(_root.ToUpperInvariant(), "sub", "a.md");
        Assert.Equal("sub", DocumentRenderer.VaultRelDir(_root.ToLowerInvariant(), file));
    }

    // ─── RenderMarkdownFile ──────────────────────────────────────────────

    [Fact]
    public void RenderMarkdownFile_RewritesRelativeUrls_AgainstFileDir()
    {
        var dir = Path.Combine(_root, "sub");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "doc.md");
        File.WriteAllText(file, "# Title\n\n![pic](img.png)\n");

        var doc = DocumentRenderer.RenderMarkdownFile(file, _root,
            showLineNumbers: false, highlightCustomTags: true);

        Assert.Equal(VaultUrlScheme.DirBase("sub"), doc.BasePath);
        Assert.Contains("https://app.local/__vault/sub/img.png", doc.Html);
        var h = Assert.Single(doc.Headings);
        Assert.Equal(1, h.Level);
        Assert.Equal("Title", h.Text);
    }

    [Fact]
    public void RenderMarkdownFile_OutsideVault_BasePathIsOrigin()
    {
        var file = Path.Combine(_root, "doc.md");
        File.WriteAllText(file, "hello");

        var doc = DocumentRenderer.RenderMarkdownFile(file, @"C:\some\other\root",
            showLineNumbers: false, highlightCustomTags: true);

        Assert.Equal(VaultUrlScheme.Origin, doc.BasePath);
    }

    // ─── RenderTranscriptFile ────────────────────────────────────────────

    [Fact]
    public void RenderTranscriptFile_RendersJsonl_BasePathIsOrigin()
    {
        var file = Path.Combine(_root, "session.jsonl");
        File.WriteAllText(file,
            """{"type":"user","message":{"role":"user","content":"hello transcript"}}""");

        var doc = DocumentRenderer.RenderTranscriptFile(file,
            visibleCategories: null, highlightCustomTags: true);

        Assert.Contains("hello transcript", doc.Html);
        Assert.Equal(VaultUrlScheme.Origin, doc.BasePath);
    }
}
