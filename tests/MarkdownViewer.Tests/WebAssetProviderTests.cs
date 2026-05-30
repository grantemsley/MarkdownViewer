using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class WebAssetProviderTests
{
    [Theory]
    [InlineData("render.html", "text/html; charset=utf-8")]
    [InlineData("reader.css", "text/css; charset=utf-8")]
    [InlineData("bridge.js", "text/javascript; charset=utf-8")]
    [InlineData("lib/mermaid/mermaid.min.js", "text/javascript; charset=utf-8")]
    [InlineData("lib/highlight/styles/github.min.css", "text/css; charset=utf-8")]
    [InlineData("data.json", "application/json; charset=utf-8")]
    [InlineData("icon.svg", "image/svg+xml")]
    [InlineData("pic.PNG", "image/png")]          // case-insensitive
    [InlineData("noext", "application/octet-stream")]
    [InlineData("archive.zip", "application/octet-stream")]
    public void ContentType_MapsByExtension(string path, string expected)
    {
        Assert.Equal(expected, WebAssetProvider.ContentType(path));
    }

    [Theory]
    [InlineData("lib\\mermaid\\mermaid.min.js", "lib/mermaid/mermaid.min.js")]
    [InlineData("/render.html", "render.html")]
    [InlineData("reader.css", "reader.css")]
    [InlineData("/lib/highlight/highlight.min.js", "lib/highlight/highlight.min.js")]
    public void Normalize_ForwardSlashes_NoLeadingSlash(string input, string expected)
    {
        Assert.Equal(expected, WebAssetProvider.Normalize(input));
    }

    [Fact]
    public void Open_MissingAsset_ReturnsNull()
    {
        // The test assembly has no embedded WebAssets, so any lookup misses
        // cleanly rather than throwing.
        Assert.Null(WebAssetProvider.Open("does/not/exist.js"));
        Assert.Null(WebAssetProvider.ReadText("does/not/exist.js"));
    }
}
