using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

// The C#<->JS bridge contract, in one place. Outbound messages are records
// serialized by BridgeJson.Serialize (camelCase, nulls omitted so "no field"
// stays "no field" like the old anonymous objects); inbound messages are
// parsed by BridgeInbound.Parse into typed records, with malformed input
// reported to the caller instead of silently swallowed. Adding a message kind
// (e.g. the planned folder-tree search) = one record here + one dispatch arm
// at each end; nothing else is coupled to the set of kinds.

// ─── Outbound (host → bridge.js) ─────────────────────────────────────────

public sealed record PrefsMsg(
    string Theme, string Accent, string Typeface, int FontSize, int MarginPct,
    bool ShowLineNumbers, string BodyStyle)
{
    public string Type => "setPrefs";
}

public sealed record MarkdownDocMsg(
    string TabId, string Path, string BasePath, string Html, bool Reloaded,
    double ScrollTop, string Modified, MarkAnchor? Mark = null)
{
    public string Type => "setDoc";
    public string Kind => "markdown";
}

public sealed record TextDocMsg(
    string TabId, string Path, string Lang, string Body, double ScrollTop,
    string Modified, bool Reloaded = false, MarkAnchor? Mark = null)
{
    public string Type => "setDoc";
    public string Kind => "text";
}

public sealed record ImageDocMsg(string TabId, string Path, string Url, string Modified)
{
    public string Type => "setDoc";
    public string Kind => "image";
}

public sealed record BinaryDocMsg(string TabId, string Path, string Modified)
{
    public string Type => "setDoc";
    public string Kind => "binary";
}

/// <summary>Raw-browser doc: exactly one of <paramref name="Html"/> (rendered
/// inline via sandboxed srcdoc) or <paramref name="Url"/> (same-origin
/// /__vault/ URL the iframe navigates to) is set; the other is omitted from
/// the JSON entirely.</summary>
public sealed record RawDocMsg(string TabId, string Path, string? Html, string? Url, string Modified)
{
    public string Type => "setDoc";
    public string Kind => "raw";
}

public sealed record EmptyDocMsg(string TabId, string Message)
{
    public string Type => "setDoc";
    public string Kind => "empty";
}

public sealed record ScrollToHeadingMsg(string TabId, string Id)
{
    public string Type => "scrollToHeading";
}

/// <summary>Ctrl+G jump: tells bridge.js to scroll the active tab's doc to
/// its resolved place marker, if one is applied.</summary>
public sealed record ScrollToMarkMsg(string TabId)
{
    public string Type => "scrollToMark";
}

public static class BridgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);
}

// ─── Inbound (bridge.js → host) ──────────────────────────────────────────

public sealed record ReadyMsg;
public sealed record OpenLinkMsg(string Href, string Base);
public sealed record RequestExternalMsg(string Url);
public sealed record ScrollMsg(string TabId, double Top, string Path);
public sealed record TranscriptFilterMsg(string Category, bool Checked);
/// <summary>Posted by bridge.js once a markdown/text doc is in the DOM, so the
/// host can run a find-in-page (scroll-to-match from a search result) only after
/// the target content is actually rendered.</summary>
public sealed record DocRenderedMsg(string TabId, string Path);
/// <summary>Gutter click set a place marker; the host stores the anchor keyed
/// by path so it survives renderer reloads and shows in every tab on the file.</summary>
public sealed record MarkSetMsg(string TabId, string Path, int BlockIndex,
    string TextPrefix, string? HeadingId);
/// <summary>Gutter click on the already-marked block cleared the marker.</summary>
public sealed record MarkClearedMsg(string TabId, string Path);

public static class BridgeInbound
{
    /// <summary>
    /// Parse one raw JSON message posted by bridge.js. Returns the typed
    /// message, or null with <paramref name="error"/> set when the input is
    /// malformed or of an unknown type — the caller should log that (a renamed
    /// field or typo'd kind must surface as a diagnosable protocol error, not
    /// a silent blank pane).
    /// </summary>
    public static object? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var t) ||
                t.ValueKind != JsonValueKind.String)
            {
                error = "message has no string 'type'";
                return null;
            }

            switch (t.GetString())
            {
                case "ready":
                    return new ReadyMsg();
                case "openLink":
                    if (!TryStr(root, "href", out var href)) { error = "openLink: missing 'href'"; return null; }
                    return new OpenLinkMsg(href, OptStr(root, "base"));
                case "requestExternal":
                    if (!TryStr(root, "url", out var url)) { error = "requestExternal: missing 'url'"; return null; }
                    return new RequestExternalMsg(url);
                case "scroll":
                    if (!TryStr(root, "tabId", out var tab)) { error = "scroll: missing 'tabId'"; return null; }
                    if (!root.TryGetProperty("top", out var top) || top.ValueKind != JsonValueKind.Number)
                    { error = "scroll: missing numeric 'top'"; return null; }
                    if (!TryStr(root, "path", out var path)) { error = "scroll: missing 'path'"; return null; }
                    return new ScrollMsg(tab, top.GetDouble(), path);
                case "docRendered":
                    if (!TryStr(root, "tabId", out var drTab)) { error = "docRendered: missing 'tabId'"; return null; }
                    if (!TryStr(root, "path", out var drPath)) { error = "docRendered: missing 'path'"; return null; }
                    return new DocRenderedMsg(drTab, drPath);
                case "markSet":
                    if (!TryStr(root, "tabId", out var msTab)) { error = "markSet: missing 'tabId'"; return null; }
                    if (!TryStr(root, "path", out var msPath)) { error = "markSet: missing 'path'"; return null; }
                    if (!root.TryGetProperty("blockIndex", out var msIdx) || msIdx.ValueKind != JsonValueKind.Number)
                    { error = "markSet: missing numeric 'blockIndex'"; return null; }
                    if (!TryStr(root, "textPrefix", out var msPrefix)) { error = "markSet: missing 'textPrefix'"; return null; }
                    return new MarkSetMsg(msTab, msPath, msIdx.GetInt32(), msPrefix,
                        OptStr(root, "headingId") is { Length: > 0 } msHeading ? msHeading : null);
                case "markCleared":
                    if (!TryStr(root, "tabId", out var mcTab)) { error = "markCleared: missing 'tabId'"; return null; }
                    if (!TryStr(root, "path", out var mcPath)) { error = "markCleared: missing 'path'"; return null; }
                    return new MarkClearedMsg(mcTab, mcPath);
                case "transcriptFilter":
                    if (!TryStr(root, "category", out var cat)) { error = "transcriptFilter: missing 'category'"; return null; }
                    if (!root.TryGetProperty("checked", out var chk) ||
                        chk.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    { error = "transcriptFilter: missing boolean 'checked'"; return null; }
                    return new TranscriptFilterMsg(cat, chk.GetBoolean());
                default:
                    error = $"unknown message type '{t.GetString()}'";
                    return null;
            }
        }
        catch (JsonException ex)
        {
            error = "invalid JSON: " + ex.Message;
            return null;
        }
    }

    private static bool TryStr(JsonElement root, string name, out string value)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static string OptStr(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? "" : "";
}

/// <summary>
/// Host-side identity gates for inbound bridge messages. Pure so the race
/// semantics are unit-testable.
/// </summary>
public static class BridgeGates
{
    /// <summary>
    /// A scroll report applies only when it names the active tab AND the doc
    /// that tab is currently showing. The tab token kills the cross-tab
    /// clobber (two tabs showing the same file: a stale trailing report from
    /// the backgrounded tab carries its own id and is dropped); the path
    /// check kills the same-tab navigation race (a trailing report for the
    /// previous file arriving after the tab moved to a new one).
    /// </summary>
    public static bool ScrollApplies(ScrollMsg message, string activeTabId, string? activeFile) =>
        message.TabId == activeTabId &&
        activeFile != null &&
        string.Equals(message.Path, activeFile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Same identity gate for place-marker messages, shared by markSet and
    /// markCleared (hence tabId/path parameters rather than a message record):
    /// a queued gutter click from before a tab switch or a same-tab navigation
    /// must not store or clear a mark against the wrong file.
    /// </summary>
    public static bool MarkApplies(string tabId, string path, string activeTabId, string? activeFile) =>
        tabId == activeTabId &&
        activeFile != null &&
        string.Equals(path, activeFile, StringComparison.OrdinalIgnoreCase);
}
