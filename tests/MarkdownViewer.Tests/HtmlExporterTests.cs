using System;
using System.IO;
using System.Text.RegularExpressions;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class HtmlExporterTests : IDisposable
{
    private readonly string _dir;

    public HtmlExporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "MdvExporter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteFile(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Markdown_ProducesStandaloneDocWithContent()
    {
        var file = WriteFile("doc.md", "# Hello Export\n\nbody text\n");
        var html = HtmlExporter.BuildStandaloneHtml(file, null, highlightCustomTags: true);

        Assert.NotNull(html);
        Assert.StartsWith("<!doctype html", html);
        Assert.Contains("Hello Export", html);
        Assert.Contains("body text", html);
    }

    [Fact]
    public void Csp_HasNoncedScriptSrc_WithoutUnsafeInline()
    {
        var file = WriteFile("doc.md", "# T\n");
        var html = HtmlExporter.BuildStandaloneHtml(file, null, highlightCustomTags: true)!;

        var csp = Regex.Match(html, @"Content-Security-Policy"" content=""([^""]*)""").Groups[1].Value;
        Assert.NotEqual("", csp);
        var scriptSrc = Regex.Match(csp, @"script-src ([^;]*)").Groups[1].Value;
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.Contains("https://cdnjs.cloudflare.com", scriptSrc);

        // The nonce in the CSP must match the one on our init script tag.
        var nonce = Regex.Match(scriptSrc, @"'nonce-([0-9a-f]{32})'").Groups[1].Value;
        Assert.NotEqual("", nonce);
        Assert.Contains($"<script nonce=\"{nonce}\">", html);
    }

    [Fact]
    public void Nonce_IsFreshPerExport()
    {
        var file = WriteFile("doc.md", "# T\n");
        var a = HtmlExporter.BuildStandaloneHtml(file, null, true)!;
        var b = HtmlExporter.BuildStandaloneHtml(file, null, true)!;
        string NonceOf(string h) => Regex.Match(h, @"'nonce-([0-9a-f]{32})'").Groups[1].Value;
        Assert.NotEqual(NonceOf(a), NonceOf(b));
    }

    [Fact]
    public void Title_IsHtmlEncoded()
    {
        var file = WriteFile("a&b.md", "hi");
        var html = HtmlExporter.BuildStandaloneHtml(file, null, true)!;
        Assert.Contains("<title>a&amp;b.md</title>", html);
    }

    [Fact]
    public void Transcript_RendersJsonl()
    {
        var file = WriteFile("s.jsonl",
            """{"type":"user","message":{"role":"user","content":"exported words"}}""");
        var html = HtmlExporter.BuildStandaloneHtml(file, null, true);
        Assert.NotNull(html);
        Assert.Contains("exported words", html);
    }

    [Fact]
    public void NonRenderableKinds_ReturnNull()
    {
        var png = WriteFile("x.png", "not really a png");
        Assert.Null(HtmlExporter.BuildStandaloneHtml(png, null, true));
        Assert.Null(HtmlExporter.BuildStandaloneHtml(Path.Combine(_dir, "missing.md"), null, true));
    }
}
