using System.Text.Json;
using MarkdownViewer.Models;
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
    public void TextDoc_CarriesReloadedFlag_DefaultFalse()
    {
        // The watcher-triggered reload path sets reloaded so bridge.js keeps
        // the live scroll position (audit race 2.3), like the markdown path.
        Assert.False(Roundtrip(new TextDocMsg("t1", "a.log", "", "x", 0, ""))
            .GetProperty("reloaded").GetBoolean());
        Assert.True(Roundtrip(new TextDocMsg("t1", "a.log", "", "x", 0, "", Reloaded: true))
            .GetProperty("reloaded").GetBoolean());
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
        Assert.Equal("scrollToMark", Roundtrip(new ScrollToMarkMsg("t1"))
            .GetProperty("type").GetString());
    }

    [Fact]
    public void DocMsgs_OmitMarkWhenNull_CarryItCamelCaseWhenSet()
    {
        // No mark on the file: the field is absent, not null — bridge.js
        // treats "no mark" as "nothing to apply".
        var bare = Roundtrip(new MarkdownDocMsg("t1", @"C:\v\a.md", "b", "<p>x</p>", false, 0, ""));
        Assert.False(bare.TryGetProperty("mark", out _));

        var md = Roundtrip(new MarkdownDocMsg("t1", @"C:\v\a.md", "b", "<p>x</p>", false, 0, "",
            new MarkAnchor(3, "hello world", "h-intro")));
        var mark = md.GetProperty("mark");
        Assert.Equal(3, mark.GetProperty("blockIndex").GetInt32());
        Assert.Equal("hello world", mark.GetProperty("textPrefix").GetString());
        Assert.Equal("h-intro", mark.GetProperty("headingId").GetString());

        // Null heading id is omitted from the nested object too.
        var noHeading = Roundtrip(new TextDocMsg("t1", "a.log", "", "x", 0, "",
            Reloaded: false, Mark: new MarkAnchor(0, "line one", null)));
        Assert.Equal(0, noHeading.GetProperty("mark").GetProperty("blockIndex").GetInt32());
        Assert.False(noHeading.GetProperty("mark").TryGetProperty("headingId", out _));
    }

    [Fact]
    public void MarkAnchor_LineFields_OmittedWhenNull_CamelCaseWhenSet()
    {
        // An ordinary block mark carries no line half at all.
        var block = Roundtrip(new MarkdownDocMsg("t1", @"C:\v\a.md", "b", "<p>x</p>", false, 0, "",
            new MarkAnchor(3, "hello world", "h-intro")));
        Assert.False(block.GetProperty("mark").TryGetProperty("lineIndex", out _));
        Assert.False(block.GetProperty("mark").TryGetProperty("lineText", out _));

        // A code-block mark addresses a line inside the block.
        var line = Roundtrip(new TextDocMsg("t1", "a.ps1", "powershell", "x", 0, "",
            Mark: new MarkAnchor(0, "whole file prefix", null, 12, "Get-Thing -Name x")));
        var mark = line.GetProperty("mark");
        Assert.Equal(12, mark.GetProperty("lineIndex").GetInt32());
        Assert.Equal("Get-Thing -Name x", mark.GetProperty("lineText").GetString());
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
    public void Parse_MarkSet_HeadingIdOptional()
    {
        var m = Assert.IsType<MarkSetMsg>(BridgeInbound.Parse(
            """{"type":"markSet","tabId":"t1","path":"C:\\v\\a.md","blockIndex":4,"textPrefix":"The quick brown","headingId":"h-2"}""",
            out var err));
        Assert.Null(err);
        Assert.Equal("t1", m.TabId);
        Assert.Equal(@"C:\v\a.md", m.Path);
        Assert.Equal(4, m.BlockIndex);
        Assert.Equal("The quick brown", m.TextPrefix);
        Assert.Equal("h-2", m.HeadingId);

        // No heading above the marked block: bridge.js sends null (or omits).
        var noHeading = Assert.IsType<MarkSetMsg>(BridgeInbound.Parse(
            """{"type":"markSet","tabId":"t1","path":"C:\\v\\a.md","blockIndex":0,"textPrefix":"intro","headingId":null}""",
            out _));
        Assert.Null(noHeading.HeadingId);
    }

    [Fact]
    public void Parse_MarkSet_LineFieldsOptional()
    {
        // A code-block click carries which line inside the block.
        var line = Assert.IsType<MarkSetMsg>(BridgeInbound.Parse(
            """{"type":"markSet","tabId":"t1","path":"C:\\v\\a.ps1","blockIndex":0,"textPrefix":"whole file","headingId":null,"lineIndex":7,"lineText":"Get-Thing"}""",
            out var err));
        Assert.Null(err);
        Assert.Equal(7, line.LineIndex);
        Assert.Equal("Get-Thing", line.LineText);

        // An ordinary block click sends neither field.
        var noLine = Assert.IsType<MarkSetMsg>(BridgeInbound.Parse(
            """{"type":"markSet","tabId":"t1","path":"C:\\v\\a.md","blockIndex":4,"textPrefix":"p","headingId":"h-2"}""",
            out _));
        Assert.Null(noLine.LineIndex);
        Assert.Null(noLine.LineText);

        // A marked blank line sends lineText "": stored as null, like absent -
        // bridge.js resolves an empty quote by position only either way.
        var blank = Assert.IsType<MarkSetMsg>(BridgeInbound.Parse(
            """{"type":"markSet","tabId":"t1","path":"x","blockIndex":0,"textPrefix":"p","lineIndex":2,"lineText":""}""",
            out _));
        Assert.Equal(2, blank.LineIndex);
        Assert.Null(blank.LineText);
    }

    [Fact]
    public void Parse_MarkCleared()
    {
        var m = Assert.IsType<MarkClearedMsg>(BridgeInbound.Parse(
            """{"type":"markCleared","tabId":"t2","path":"C:\\v\\b.md"}""", out var err));
        Assert.Null(err);
        Assert.Equal("t2", m.TabId);
        Assert.Equal(@"C:\v\b.md", m.Path);
    }

    // ─── Mark identity gate ──────────────────────────────────────────────

    [Fact]
    public void MarkGate_StaleTab_WrongPath_AndNullFile_AllDrop()
    {
        // Live: active tab, active file.
        Assert.True(BridgeGates.MarkApplies("t1", @"C:\v\a.md", "t1", @"C:\v\a.md"));
        // Stale tab: click queued before a tab switch names the old tab.
        Assert.False(BridgeGates.MarkApplies("t1", @"C:\v\a.md", "t2", @"C:\v\a.md"));
        // Wrong path: same tab navigated a.md -> b.md before the click landed.
        Assert.False(BridgeGates.MarkApplies("t1", @"C:\v\a.md", "t1", @"C:\v\b.md"));
        // No file open at all.
        Assert.False(BridgeGates.MarkApplies("t1", @"C:\v\a.md", "t1", null));
        // Path match is case-insensitive, like ScrollApplies.
        Assert.True(BridgeGates.MarkApplies("t1", @"C:\V\A.MD", "t1", @"c:\v\a.md"));
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

    [Fact]
    public void Parse_DocRendered()
    {
        var m = Assert.IsType<DocRenderedMsg>(BridgeInbound.Parse(
            """{"type":"docRendered","tabId":"t3","path":"C:\\v\\a.md"}""", out var err));
        Assert.Null(err);
        Assert.Equal("t3", m.TabId);
        Assert.Equal(@"C:\v\a.md", m.Path);

        Assert.Null(BridgeInbound.Parse("""{"type":"docRendered","tabId":"t3"}""", out var err2));
        Assert.NotNull(err2);
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
    [InlineData("""{"type":"markSet","tabId":"t1","path":"x","textPrefix":"p"}""")]          // missing blockIndex
    [InlineData("""{"type":"markSet","tabId":"t1","path":"x","blockIndex":"3","textPrefix":"p"}""")] // non-numeric
    [InlineData("""{"type":"markSet","tabId":"t1","path":"x","blockIndex":3}""")]            // missing textPrefix
    [InlineData("""{"type":"markSet","path":"x","blockIndex":3,"textPrefix":"p"}""")]        // missing tabId
    [InlineData("""{"type":"markCleared","tabId":"t1"}""")]                                  // missing path
    [InlineData("""{"type":"searchResults"}""")]            // unknown/future kind
    public void Parse_Malformed_ReturnsNullWithError(string json)
    {
        var msg = BridgeInbound.Parse(json, out var error);
        Assert.Null(msg);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
