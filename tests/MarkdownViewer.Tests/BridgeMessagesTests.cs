using System.Text.Json;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class BridgeMessagesTests
{
    // ─── Outbound serialization ──────────────────────────────────────────

    private static JsonElement Roundtrip<T>(T msg) =>
        JsonDocument.Parse(BridgeJson.Serialize(msg)).RootElement;

    [Fact]
    public void MarkdownDoc_SerializesCamelCase_WithTypeKindAndTabId()
    {
        var el = Roundtrip(new MarkdownDocMsg(
            "t1", @"C:\v\a.md", "https://app.local/__vault/t1/", "<h1>x</h1>",
            Reloaded: true, ScrollTop: 42.5, Modified: "6/1/2026 2:32 PM"));

        Assert.Equal("setDoc", el.GetProperty("type").GetString());
        Assert.Equal("markdown", el.GetProperty("kind").GetString());
        Assert.Equal("t1", el.GetProperty("tabId").GetString());
        Assert.Equal(@"C:\v\a.md", el.GetProperty("path").GetString());
        Assert.Equal("<h1>x</h1>", el.GetProperty("html").GetString());
        Assert.True(el.GetProperty("reloaded").GetBoolean());
        Assert.Equal(42.5, el.GetProperty("scrollTop").GetDouble());
        Assert.Equal("6/1/2026 2:32 PM", el.GetProperty("modified").GetString());
    }

    [Fact]
    public void RawDoc_OmitsTheNullVariant()
    {
        var srcdoc = Roundtrip(new RawDocMsg("t1", "a.html", Html: "<p>hi</p>", Url: null, Modified: ""));
        Assert.Equal("<p>hi</p>", srcdoc.GetProperty("html").GetString());
        Assert.False(srcdoc.TryGetProperty("url", out _));

        var byUrl = Roundtrip(new RawDocMsg("t1", "a.pdf", Html: null,
            Url: "https://app.local/__vault/t1/a.pdf", Modified: ""));
        Assert.False(byUrl.TryGetProperty("html", out _));
        Assert.Equal("https://app.local/__vault/t1/a.pdf", byUrl.GetProperty("url").GetString());
    }

    [Fact]
    public void OtherDocKinds_CarryTheirKindTag()
    {
        Assert.Equal("text", Roundtrip(new TextDocMsg("t1", "a.txt", "python", "x", 0, ""))
            .GetProperty("kind").GetString());
        Assert.Equal("image", Roundtrip(new ImageDocMsg("t1", "a.png", "u", ""))
            .GetProperty("kind").GetString());
        Assert.Equal("binary", Roundtrip(new BinaryDocMsg("t1", "a.bin", ""))
            .GetProperty("kind").GetString());
        Assert.Equal("empty", Roundtrip(new EmptyDocMsg("t1", "Open a folder."))
            .GetProperty("kind").GetString());
        Assert.Equal("setPrefs", Roundtrip(new PrefsMsg("dark", "#FF0000", "system", 14, 85, false, "win11"))
            .GetProperty("type").GetString());
        Assert.Equal("scrollToHeading", Roundtrip(new ScrollToHeadingMsg("t1", "h-1"))
            .GetProperty("type").GetString());
    }

    // ─── Inbound parsing ─────────────────────────────────────────────────

    [Fact]
    public void Parse_Ready() =>
        Assert.IsType<ReadyMsg>(BridgeInbound.Parse("""{"type":"ready"}""", out _));

    [Fact]
    public void Parse_Scroll()
    {
        var m = Assert.IsType<ScrollMsg>(BridgeInbound.Parse(
            """{"type":"scroll","tabId":"t1","top":123.5,"path":"C:\\v\\a.md"}""", out var err));
        Assert.Null(err);
        Assert.Equal("t1", m.TabId);
        Assert.Equal(123.5, m.Top);
        Assert.Equal(@"C:\v\a.md", m.Path);
    }

    // ─── Identity gates (races 2.1/2.2 regression coverage) ──────────────

    [Fact]
    public void ScrollGate_SameFileInTwoTabs_DropsTheBackgroundTabsReport()
    {
        // Race 2.2: tabs t1 and t2 both show a.md; t1 was just backgrounded
        // and its trailing rAF scroll report arrives while t2 is active. The
        // path matches, so only the tab token can (and must) reject it.
        var stale = new ScrollMsg("t1", 500, @"C:\v\a.md");
        Assert.False(BridgeGates.ScrollApplies(stale, activeTabId: "t2", activeFile: @"C:\v\a.md"));

        var live = new ScrollMsg("t2", 500, @"C:\v\a.md");
        Assert.True(BridgeGates.ScrollApplies(live, activeTabId: "t2", activeFile: @"C:\v\a.md"));
    }

    [Fact]
    public void ScrollGate_SameTabNavigation_DropsPreviousDocsReport()
    {
        // Same tab navigated a.md -> b.md; a trailing report for a.md carries
        // the right tab id but the wrong path.
        var stale = new ScrollMsg("t1", 500, @"C:\v\a.md");
        Assert.False(BridgeGates.ScrollApplies(stale, activeTabId: "t1", activeFile: @"C:\v\b.md"));
    }

    [Fact]
    public void ScrollGate_PathMatchIsCaseInsensitive_AndNullFileNeverApplies()
    {
        var m = new ScrollMsg("t1", 10, @"C:\V\A.MD");
        Assert.True(BridgeGates.ScrollApplies(m, "t1", @"c:\v\a.md"));
        Assert.False(BridgeGates.ScrollApplies(m, "t1", null));
    }

    [Fact]
    public void Parse_OpenLink_BaseOptional()
    {
        var m = Assert.IsType<OpenLinkMsg>(BridgeInbound.Parse(
            """{"type":"openLink","href":"sub/x.md"}""", out _));
        Assert.Equal("sub/x.md", m.Href);
        Assert.Equal("", m.Base);

        var m2 = Assert.IsType<OpenLinkMsg>(BridgeInbound.Parse(
            """{"type":"openLink","href":"x.md","base":"https://app.local/__vault/sub/"}""", out _));
        Assert.Equal("https://app.local/__vault/sub/", m2.Base);
    }

    [Fact]
    public void Parse_RequestExternal_And_TranscriptFilter()
    {
        var ext = Assert.IsType<RequestExternalMsg>(BridgeInbound.Parse(
            """{"type":"requestExternal","url":"https://example.com/"}""", out _));
        Assert.Equal("https://example.com/", ext.Url);

        var tf = Assert.IsType<TranscriptFilterMsg>(BridgeInbound.Parse(
            """{"type":"transcriptFilter","category":"tools","checked":false}""", out _));
        Assert.Equal("tools", tf.Category);
        Assert.False(tf.Checked);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"noType":1}""")]
    [InlineData("""{"type":42}""")]
    [InlineData("""{"type":"headings","headings":[]}""")]   // retired message kind
    [InlineData("""{"type":"scroll","tabId":"t1","path":"x"}""")]        // missing top
    [InlineData("""{"type":"scroll","top":1,"path":"x"}""")]             // missing tabId
    [InlineData("""{"type":"scroll","tabId":"t1","top":"NaN","path":"x"}""")]
    [InlineData("""{"type":"openLink"}""")]                 // missing href
    [InlineData("""{"type":"requestExternal"}""")]
    [InlineData("""{"type":"transcriptFilter","category":"a","checked":"yes"}""")]
    [InlineData("""{"type":"searchResults"}""")]            // unknown/future kind
    public void Parse_Malformed_ReturnsNullWithError(string json)
    {
        var msg = BridgeInbound.Parse(json, out var error);
        Assert.Null(msg);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
