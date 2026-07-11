using System;
using System.IO;
using System.Linq;
using System.Text;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class ContentRouterTests : IDisposable
{
    private readonly string _dir;

    public ContentRouterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mvtest_router_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private string WriteText(string name, string content = "hello\n")
        => WriteFile(name, Encoding.UTF8.GetBytes(content));

    [Theory]
    [InlineData("a.md", ViewerKind.Markdown)]
    [InlineData("a.markdown", ViewerKind.Markdown)]
    [InlineData("a.mdown", ViewerKind.Markdown)]
    [InlineData("a.mkd", ViewerKind.Markdown)]
    public void Route_MarkdownExtensions_Markdown(string name, ViewerKind expected)
    {
        var p = WriteText(name);
        var kind = ContentRouter.Route(p, out _);
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("a.html")]
    [InlineData("a.htm")]
    [InlineData("a.xhtml")]
    [InlineData("a.pdf")]
    public void Route_RawBrowserExtensions_RawBrowser(string name)
    {
        var p = WriteText(name);
        var kind = ContentRouter.Route(p, out _);
        Assert.Equal(ViewerKind.RawBrowser, kind);
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.jpg")]
    [InlineData("a.svg")]
    [InlineData("a.webp")]
    public void Route_ImageExtensions_Image(string name)
    {
        var p = WriteText(name);
        var kind = ContentRouter.Route(p, out _);
        Assert.Equal(ViewerKind.Image, kind);
    }

    [Fact]
    public void Route_Ps1_TextWithPowerShellHighlight()
    {
        var p = WriteText("a.ps1");
        var kind = ContentRouter.Route(p, out var lang);
        Assert.Equal(ViewerKind.Text, kind);
        Assert.Equal("powershell", lang);
    }

    [Fact]
    public void Route_Py_TextWithPythonHighlight()
    {
        var p = WriteText("a.py");
        var kind = ContentRouter.Route(p, out var lang);
        Assert.Equal(ViewerKind.Text, kind);
        Assert.Equal("python", lang);
    }

    [Fact]
    public void Route_Jsonl_JsonlTranscript()
    {
        var p = WriteText("session.jsonl");
        var kind = ContentRouter.Route(p, out _);
        Assert.Equal(ViewerKind.JsonlTranscript, kind);
    }

    [Fact]
    public void Route_Txt_TextNoHighlight()
    {
        var p = WriteText("a.txt");
        var kind = ContentRouter.Route(p, out var lang);
        Assert.Equal(ViewerKind.Text, kind);
        Assert.Equal("", lang);
    }

    [Fact]
    public void Route_UnknownExtensionTextContent_Text()
    {
        var p = WriteText("a.qwerty");
        var kind = ContentRouter.Route(p, out _);
        Assert.Equal(ViewerKind.Text, kind);
    }

    [Fact]
    public void Route_UnknownExtensionBinaryContent_Binary()
    {
        var p = WriteFile("a.qwerty", new byte[] { 0x48, 0x00, 0x69 });
        var kind = ContentRouter.Route(p, out _);
        Assert.Equal(ViewerKind.Binary, kind);
    }

    [Fact]
    public void Route_NonexistentFile_None()
    {
        var kind = ContentRouter.Route(Path.Combine(_dir, "ghost.md"), out _);
        Assert.Equal(ViewerKind.None, kind);
    }

    [Fact]
    public void Route_EmptyPath_None()
    {
        var kind = ContentRouter.Route("", out _);
        Assert.Equal(ViewerKind.None, kind);
    }

    [Fact]
    public void ReadTextFile_Utf8Bom_StripsBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("héllo")).ToArray();
        var p = WriteFile("a.txt", bytes);
        var text = ContentRouter.ReadTextFile(p);
        Assert.Equal("héllo", text);
    }

    [Fact]
    public void ReadTextFile_Utf16LeBom_Decodes()
    {
        var bytes = new byte[] { 0xFF, 0xFE }
            .Concat(Encoding.Unicode.GetBytes("héllo")).ToArray();
        var p = WriteFile("a.txt", bytes);
        var text = ContentRouter.ReadTextFile(p);
        Assert.Equal("héllo", text);
    }

    [Fact]
    public void ReadTextFile_Utf8NoBom_Decodes()
    {
        var p = WriteFile("a.txt", Encoding.UTF8.GetBytes("héllo world"));
        var text = ContentRouter.ReadTextFile(p);
        Assert.Equal("héllo world", text);
    }

    [Fact]
    public void ReadTextFile_Latin1Bytes_FallsBackToCp1252()
    {
        // 0xE9 is "é" in cp1252, but invalid UTF-8 — must trip the fallback.
        var bytes = new byte[] { 0x68, 0xE9, 0x6C, 0x6C, 0x6F };
        var p = WriteFile("a.log", bytes);
        var text = ContentRouter.ReadTextFile(p);
        Assert.Equal("héllo", text);
    }

    [Fact]
    public void ReadTextFile_OversizeFile_TruncatedWithNotice()
    {
        // Just past the 50 MB cap: must not load the whole thing, and must mark
        // the result truncated rather than freezing/OOMing on a pathological file.
        var big = new byte[51L * 1024 * 1024];
        Array.Fill(big, (byte)'a');
        var p = WriteFile("big.txt", big);
        var text = ContentRouter.ReadTextFile(p);
        Assert.Contains("truncated by MarkdownViewer", text);
        Assert.True(text.Length < big.Length);
    }

    [Fact]
    public void ReadTextFile_NormalFile_NotTruncated()
    {
        var p = WriteFile("ok.txt", Encoding.UTF8.GetBytes("hello world"));
        var text = ContentRouter.ReadTextFile(p);
        Assert.Equal("hello world", text);
        Assert.DoesNotContain("truncated", text);
    }
}
