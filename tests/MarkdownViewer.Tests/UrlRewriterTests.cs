using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class UrlRewriterTests
{
    private const string Base = "https://vault.local/";

    [Fact]
    public void RelativeSrc_GetsBasePathPrefix()
    {
        var input = "<img src=\"image.png\">";
        var output = UrlRewriter.RewriteRelativeUrls(input, Base);
        Assert.Contains("src=\"https://vault.local/image.png\"", output);
    }

    [Fact]
    public void AbsoluteUrl_LeftAlone()
    {
        var input = "<a href=\"https://example.com/x\">x</a>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void AnchorLink_LeftAlone()
    {
        var input = "<a href=\"#section\">x</a>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void DataUri_LeftAlone()
    {
        var input = "<img src=\"data:image/png;base64,abc\">";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void Mailto_LeftAlone()
    {
        var input = "<a href=\"mailto:a@b.c\">x</a>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void RootSlash_LeftAlone()
    {
        var input = "<a href=\"/abs/path\">x</a>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void ClassAttribute_NeverTouched()
    {
        // Regression: the mermaid bug was about a selector mismatch, but
        // pinning the URL-rewrite scope so it cannot drift into class
        // attributes is cheap insurance.
        var input = "<div class=\"mermaid\">flowchart LR\n  A --> B</div>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void IdAttribute_NeverTouched()
    {
        var input = "<input type=\"checkbox\" id=\"tf-conversation\" checked>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }

    [Fact]
    public void EmptyHref_LeftAlone()
    {
        var input = "<a href=\"\">x</a>";
        Assert.Equal(input, UrlRewriter.RewriteRelativeUrls(input, Base));
    }
}
