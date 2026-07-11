using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MarkdownViewer.Services;

/// <summary>
/// Turns a Claude Code transcript .jsonl file into a markdown document with
/// category-filterable blocks. The output is fed through MarkdownService.Render
/// like any other markdown source — Markdig's raw-HTML passthrough is what makes
/// the &lt;details&gt;/&lt;style&gt;/checkbox widgets survive.
///
/// Filter state is held entirely in the DOM via :has() CSS — no JavaScript,
/// which matters because bridge.js sets page.innerHTML and inline &lt;script&gt;
/// tags inserted that way don't execute.
/// </summary>
public static class TranscriptService
{
    // Filter category keys — also used as CSS class suffixes: t-<key>, #tf-<key>.
    public const string CatConversation = "conversation";
    public const string CatTool = "tool";
    public const string CatThinking = "thinking";
    public const string CatHook = "hook";
    public const string CatSkill = "skill";
    public const string CatMcp = "mcp";
    public const string CatToolsDelta = "toolsdelta";
    public const string CatQueue = "queue";
    public const string CatMeta = "meta";

    private static readonly (string Key, string Label, bool DefaultOn)[] Categories =
    {
        (CatConversation, "conversation", true),
        (CatTool,         "tool calls",    true),
        (CatThinking,     "thinking",      false),
        (CatHook,         "hooks",         false),
        (CatSkill,        "skill listings",false),
        (CatMcp,          "MCP instructions", false),
        (CatToolsDelta,   "tool deltas",   false),
        (CatQueue,        "queue ops",     false),
        (CatMeta,         "meta",          false),
    };

    // Anything longer than this in a single text value is truncated with a marker.
    // Protects WebView2 from pathological inputs without dropping useful content.
    private const int MaxTextChars = 200_000;

    // A piece of a tool_result's content. Text runs render as a fenced block;
    // image blocks render as an inline &lt;img&gt; data URI instead of dumping
    // their base64 as text — base64 screenshots otherwise dominate a
    // transcript's size and render as an unreadable wall of characters.
    private readonly record struct ResultPart(bool IsImage, string Value);

    public static string ToMarkdown(string jsonl, IDictionary<string, bool>? visibleCategories = null)
    {
        var roots = ParseLines(jsonl);
        var resultLookup = BuildResultLookup(roots);

        var used = new HashSet<string>();
        var body = new StringBuilder();
        var consumed = new HashSet<string>();

        foreach (var root in roots)
            EmitRecord(root, body, used, resultLookup, consumed);

        var sb = new StringBuilder();
        // The filter widget must live INSIDE .t-doc so the CSS
        // `.t-doc:has(#tf-X:checked) .t-X` selectors actually match the
        // checkbox as a descendant.
        sb.AppendLine("<div class=\"t-doc\">");
        sb.AppendLine();
        AppendHeader(sb, used, visibleCategories);
        sb.AppendLine();
        AppendSessionHeader(sb, roots);
        sb.AppendLine();
        sb.Append(body);
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    // ─── Session header (always visible, not behind a filter) ───────────────

    private static void AppendSessionHeader(StringBuilder sb, List<JsonElement> roots)
    {
        string? sessionId = null, timestamp = null, branch = null, version = null,
                model = null, cwd = null;
        foreach (var root in roots)
        {
            if (sessionId is null && TryGetStr(root, "sessionId", out var sid)) sessionId = sid;
            if (timestamp is null && TryGetStr(root, "timestamp", out var ts)) timestamp = ts;
            if (branch is null && TryGetStr(root, "gitBranch", out var b)) branch = b;
            if (version is null && TryGetStr(root, "version", out var v)) version = v;
            if (cwd is null && TryGetStr(root, "cwd", out var c)) cwd = c;
            if (model is null
                && TryGetStr(root, "type", out var t) && t == "assistant"
                && root.TryGetProperty("message", out var msg)
                && TryGetStr(msg, "model", out var m))
                model = m;
        }

        if (sessionId is null && timestamp is null && model is null
            && branch is null && cwd is null && version is null) return;

        sb.AppendLine("<div class=\"t-session-header\">");
        sb.AppendLine();
        if (timestamp is not null) sb.AppendLine($"- **Started:** {EscapeMd(FormatTimestamp(timestamp))}");
        if (model is not null)     sb.AppendLine($"- **Model:** {CodeSpan(model)}");
        if (branch is not null)    sb.AppendLine($"- **Branch:** {CodeSpan(branch)}");
        if (cwd is not null)       sb.AppendLine($"- **cwd:** {CodeSpan(cwd)}");
        if (sessionId is not null) sb.AppendLine($"- **Session:** {CodeSpan(sessionId)}");
        if (version is not null)   sb.AppendLine($"- **Version:** {CodeSpan(version)}");
        sb.AppendLine();
        sb.AppendLine("</div>");
    }

    private static string FormatTimestamp(string iso)
    {
        // "2026-05-25T20:58:37.659Z" → "2026-05-25 20:58 UTC". Cheap and
        // deterministic; avoids DateTime parsing for what's just a display label.
        var t = iso.IndexOf('T');
        if (t < 0) return iso;
        var dot = iso.IndexOf('.', t);
        var z = iso.IndexOf('Z', t);
        var endHM = dot > 0 ? dot : (z > 0 ? z : iso.Length);
        // Keep HH:MM, drop seconds.
        var datePart = iso.Substring(0, t);
        var timeAfterT = iso.Substring(t + 1, endHM - t - 1);
        var hm = timeAfterT.Length >= 5 ? timeAfterT.Substring(0, 5) : timeAfterT;
        return $"{datePart} {hm} UTC";
    }

    // ─── Parsing ────────────────────────────────────────────────────────────

    private static List<JsonElement> ParseLines(string jsonl)
    {
        var roots = new List<JsonElement>();
        // Normalize line endings; transcripts written on Windows commonly use \r\n.
        var lines = jsonl.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                roots.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Malformed line — skip silently so neighbors still render.
            }
        }
        return roots;
    }

    private static Dictionary<string, List<ResultPart>> BuildResultLookup(List<JsonElement> roots)
    {
        // Pre-scan every user-message tool_result so an assistant-message
        // tool_use can embed its output directly, even though they sit in
        // different records.
        var map = new Dictionary<string, List<ResultPart>>();
        foreach (var root in roots)
        {
            if (!TryGetStr(root, "type", out var t) || t != "user") continue;
            if (!root.TryGetProperty("message", out var msg)) continue;
            if (!msg.TryGetProperty("content", out var content)) continue;
            if (content.ValueKind != JsonValueKind.Array) continue;
            foreach (var block in content.EnumerateArray())
            {
                if (!TryGetStr(block, "type", out var bt) || bt != "tool_result") continue;
                if (!TryGetStr(block, "tool_use_id", out var id)) continue;
                map[id] = ExtractResult(block, "content");
            }
        }
        return map;
    }

    // ─── Per-record dispatch ────────────────────────────────────────────────

    private static void EmitRecord(
        JsonElement root, StringBuilder body, HashSet<string> used,
        Dictionary<string, List<ResultPart>> resultLookup, HashSet<string> consumed)
    {
        if (!TryGetStr(root, "type", out var t))
        {
            EmitSimpleMeta("(no type)", body, used);
            return;
        }

        switch (t)
        {
            case "user":            EmitUser(root, body, used, consumed); break;
            case "assistant":       EmitAssistant(root, body, used, resultLookup, consumed); break;
            case "queue-operation": EmitQueue(root, body, used); break;
            case "attachment":      EmitAttachment(root, body, used); break;
            case "last-prompt":     EmitSimpleMeta("last-prompt", body, used); break;
            default:                EmitSimpleMeta("unknown type: " + t, body, used); break;
        }
    }

    private static void EmitUser(
        JsonElement root, StringBuilder body, HashSet<string> used, HashSet<string> consumed)
    {
        if (!root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("content", out var content)) return;

        if (content.ValueKind == JsonValueKind.String)
        {
            EmitConversation("User", content.GetString() ?? "", body, used);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;

        foreach (var block in content.EnumerateArray())
        {
            if (!TryGetStr(block, "type", out var bt)) continue;
            if (bt == "text")
            {
                EmitConversation("User", TryGetStr(block, "text", out var s) ? s : "", body, used);
            }
            else if (bt == "image" && TryRenderImage(block, out var imgTag))
            {
                // A pasted/attached image in the user turn (e.g. a screenshot).
                EmitUserImage(imgTag, body, used);
            }
            else if (bt == "tool_result")
            {
                var id = TryGetStr(block, "tool_use_id", out var tid) ? tid : "";
                if (consumed.Contains(id)) continue; // already shown inside the tool_use
                EmitOrphanResult(id, ExtractResult(block, "content"), body, used);
            }
        }
    }

    private static void EmitAssistant(
        JsonElement root, StringBuilder body, HashSet<string> used,
        Dictionary<string, List<ResultPart>> resultLookup, HashSet<string> consumed)
    {
        if (!root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.Array) return;

        foreach (var block in content.EnumerateArray())
        {
            if (!TryGetStr(block, "type", out var bt)) continue;
            switch (bt)
            {
                case "text":
                    EmitConversation("Assistant", TryGetStr(block, "text", out var s) ? s : "", body, used);
                    break;
                case "thinking":
                    var thinking = TryGetStr(block, "thinking", out var th) ? th : "";
                    if (!string.IsNullOrWhiteSpace(thinking))
                        EmitThinking(thinking, body, used);
                    break;
                case "tool_use":
                    var name = TryGetStr(block, "name", out var n) ? n : "(unknown)";
                    var id = TryGetStr(block, "id", out var tid) ? tid : "";
                    var inputJson = block.TryGetProperty("input", out var input)
                        ? SerializeIndented(input)
                        : "";
                    resultLookup.TryGetValue(id, out var resultParts);
                    if (id.Length > 0 && resultParts is not null) consumed.Add(id);
                    EmitToolUse(name, inputJson, resultParts, body, used);
                    break;
            }
        }
    }

    private static void EmitQueue(JsonElement root, StringBuilder body, HashSet<string> used)
    {
        var op = TryGetStr(root, "operation", out var o) ? o : "";
        var content = TryGetStr(root, "content", out var c) ? c : null;
        used.Add(CatQueue);
        body.AppendLine($"<div class=\"t-block t-{CatQueue}\">");
        body.AppendLine();
        body.AppendLine(content is null
            ? $"*queue {EscapeMd(op)}*"
            : $"*queue {EscapeMd(op)}*: {EscapeMd(Trim(content, 200))}");
        body.AppendLine();
        body.AppendLine("</div>");
        body.AppendLine();
    }

    private static void EmitAttachment(JsonElement root, StringBuilder body, HashSet<string> used)
    {
        if (!root.TryGetProperty("attachment", out var att)) return;
        var atype = TryGetStr(att, "type", out var x) ? x : "";

        if (atype.StartsWith("hook_", StringComparison.Ordinal))
        {
            EmitHook(att, body, used);
        }
        else if (atype == "skill_listing")
        {
            var content = TryGetStr(att, "content", out var sc) ? sc : "";
            EmitDetails(CatSkill, "Skill listing", content, body, used);
        }
        else if (atype == "mcp_instructions_delta")
        {
            EmitDetails(CatMcp, "MCP instructions", JoinStringArray(att, "addedBlocks"), body, used);
        }
        else if (atype == "deferred_tools_delta")
        {
            EmitDetails(CatToolsDelta, "Deferred tools delta", FormatToolsDelta(att), body, used);
        }
        else
        {
            EmitDetails(CatMeta, "attachment: " + atype, "", body, used);
        }
    }

    private static void EmitHook(JsonElement att, StringBuilder body, HashSet<string> used)
    {
        var hookName = TryGetStr(att, "hookName", out var hn) ? hn : "";
        var hookEvent = TryGetStr(att, "hookEvent", out var he) ? he : "";
        var cmd = TryGetStr(att, "command", out var cmdv) ? cmdv : "";
        // GetRawText, not GetInt32: a large/unsigned exit code (e.g. 4294967295
        // or an NTSTATUS-style value) doesn't fit Int32 and would throw, which
        // — with no per-record guard — aborts rendering of the whole transcript.
        var exitCode = att.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? ec.GetRawText() : "?";
        var stdout = TryGetStr(att, "stdout", out var so) ? so : "";
        var stderr = TryGetStr(att, "stderr", out var se) ? se : "";

        used.Add(CatHook);
        body.AppendLine($"<details class=\"t-block t-{CatHook}\">");
        body.AppendLine($"<summary>📎 Hook: {EscapeHtml(hookName)} ({EscapeHtml(hookEvent)}, exit {EscapeHtml(exitCode)})</summary>");
        body.AppendLine();
        if (!string.IsNullOrEmpty(cmd))
        {
            body.AppendLine($"**Command:** {CodeSpan(cmd)}");
            body.AppendLine();
        }
        if (!string.IsNullOrEmpty(stdout))
        {
            body.AppendLine("**stdout:**");
            AppendFence(body, "", stdout);
        }
        if (!string.IsNullOrEmpty(stderr))
        {
            body.AppendLine("**stderr:**");
            AppendFence(body, "", stderr);
        }
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitDetails(string cat, string title, string content, StringBuilder body, HashSet<string> used)
    {
        used.Add(cat);
        body.AppendLine($"<details class=\"t-block t-{cat}\">");
        body.AppendLine($"<summary>{EscapeHtml(title)}</summary>");
        body.AppendLine();
        if (!string.IsNullOrEmpty(content))
            AppendFence(body, "", content);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitSimpleMeta(string title, StringBuilder body, HashSet<string> used)
    {
        used.Add(CatMeta);
        body.AppendLine($"<div class=\"t-block t-{CatMeta}\">");
        body.AppendLine();
        body.AppendLine($"*{EscapeMd(title)}*");
        body.AppendLine();
        body.AppendLine("</div>");
        body.AppendLine();
    }

    private static void EmitConversation(string role, string text, StringBuilder body, HashSet<string> used)
    {
        used.Add(CatConversation);
        var roleClass = role.ToLowerInvariant();
        var emoji = role == "User" ? "🧑" : "🤖";
        var preview = HeadingPreview(text, 60);
        // H3 heading so the existing outline panel picks it up. Filters still
        // hide the wrapping block when toggled off; the outline entry stays in
        // place but its target is hidden — acceptable for a viewer-only doc.
        var heading = preview.Length > 0
            ? $"### {emoji} {role}: {preview}"
            : $"### {emoji} {role}";

        body.AppendLine($"<div class=\"t-block t-{CatConversation} t-{roleClass}\">");
        body.AppendLine();
        body.AppendLine(heading);
        body.AppendLine();
        body.AppendLine(Trim(text, MaxTextChars));
        body.AppendLine();
        body.AppendLine("</div>");
        body.AppendLine();
    }

    private static string HeadingPreview(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // First non-blank line, with leading markdown markers stripped so the
        // outline label reads cleanly.
        string? first = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            first = line;
            break;
        }
        if (first is null) return "";
        first = first.TrimStart('#', '>', '*', '-', ' ', '\t');
        if (first.Length > max) first = first.Substring(0, max).TrimEnd() + "…";
        return first;
    }

    private static void EmitThinking(string text, StringBuilder body, HashSet<string> used)
    {
        used.Add(CatThinking);
        body.AppendLine($"<details class=\"t-block t-{CatThinking}\">");
        body.AppendLine("<summary>💭 Thinking</summary>");
        body.AppendLine();
        body.AppendLine(Trim(text, MaxTextChars));
        body.AppendLine();
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitToolUse(
        string name, string inputJson, List<ResultPart>? result,
        StringBuilder body, HashSet<string> used)
    {
        used.Add(CatTool);
        var preview = SummaryPreview(inputJson);
        var summaryText = preview.Length > 0
            ? $"🔧 {EscapeHtml(name)} — {EscapeHtml(preview)}"
            : $"🔧 {EscapeHtml(name)}";

        body.AppendLine($"<details class=\"t-block t-{CatTool}\">");
        body.AppendLine($"<summary>{summaryText}</summary>");
        body.AppendLine();
        if (!string.IsNullOrEmpty(inputJson))
        {
            body.AppendLine("**Input:**");
            AppendFence(body, "json", inputJson);
        }
        if (result is not null)
        {
            body.AppendLine("**Output:**");
            AppendResult(body, result);
        }
        else
        {
            body.AppendLine("*(no matching tool_result in transcript)*");
            body.AppendLine();
        }
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitOrphanResult(string id, List<ResultPart> output, StringBuilder body, HashSet<string> used)
    {
        used.Add(CatTool);
        body.AppendLine($"<details class=\"t-block t-{CatTool}\">");
        body.AppendLine($"<summary>🔧 Orphan tool_result ({EscapeHtml(id)})</summary>");
        body.AppendLine();
        AppendResult(body, output);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitUserImage(string imgTag, StringBuilder body, HashSet<string> used)
    {
        used.Add(CatConversation);
        body.AppendLine($"<div class=\"t-block t-{CatConversation} t-user\">");
        body.AppendLine();
        body.AppendLine(imgTag);
        body.AppendLine();
        body.AppendLine("</div>");
        body.AppendLine();
    }

    /// <summary>
    /// Render a tool_result's parts: text runs as fenced blocks, image parts as
    /// raw &lt;img&gt; HTML emitted OUTSIDE any fence so the browser renders them.
    /// An empty list (a result with no content) still emits an empty fence so
    /// the "**Output:**" label isn't left dangling, matching the prior behavior.
    /// </summary>
    private static void AppendResult(StringBuilder body, List<ResultPart> parts)
    {
        if (parts.Count == 0)
        {
            AppendFence(body, "", "");
            return;
        }
        foreach (var part in parts)
        {
            if (part.IsImage)
            {
                body.AppendLine();
                body.AppendLine(part.Value);
                body.AppendLine();
            }
            else
            {
                AppendFence(body, "", part.Value);
            }
        }
    }

    // ─── Header (filter widget) ─────────────────────────────────────────────

    private static void AppendHeader(StringBuilder sb, HashSet<string> used, IDictionary<string, bool>? visible)
    {
        // The :has() selector drives visibility from the inline checkboxes —
        // no JS needed (and innerHTML wouldn't execute inline scripts anyway).
        sb.AppendLine("<style>");
        sb.AppendLine(".t-doc { font-family: inherit; }");
        sb.AppendLine(".t-filters { position: sticky; top: 0; background: var(--bg, #fff); padding: 8px 0; margin: 0 0 12px; border-bottom: 1px solid rgba(127,127,127,0.3); display: flex; flex-wrap: wrap; gap: 12px; z-index: 10; }");
        sb.AppendLine(".t-filters label { cursor: pointer; user-select: none; font-size: 0.9em; }");
        sb.AppendLine(".t-session-header { margin: 0 0 16px; padding: 8px 12px; background: rgba(127,127,127,0.08); border-radius: 4px; font-size: 0.9em; }");
        sb.AppendLine(".t-session-header ul { margin: 0; padding-left: 1.2em; }");
        sb.AppendLine(".t-block { margin: 8px 0; padding: 6px 10px; border-left: 3px solid rgba(127,127,127,0.3); }");
        sb.AppendLine(".t-block.t-user { border-left-color: #4a90e2; }");
        sb.AppendLine(".t-block.t-assistant { border-left-color: #7ed321; }");
        sb.AppendLine("details.t-block > summary { cursor: pointer; user-select: none; }");
        sb.AppendLine(".t-img { max-width: 100%; height: auto; display: block; margin: 8px 0; border: 1px solid rgba(127,127,127,0.3); border-radius: 4px; }");
        sb.AppendLine(".t-block { display: none; }");
        foreach (var (key, _, _) in Categories)
            sb.AppendLine($".t-doc:has(#tf-{key}:checked) .t-{key} {{ display: block; }}");
        sb.AppendLine("</style>");
        sb.AppendLine();
        sb.AppendLine("<div class=\"t-filters\">");
        foreach (var (key, label, defaultOn) in Categories)
        {
            if (!used.Contains(key)) continue;
            // Saved state wins; missing keys fall through to the static default.
            var isOn = visible is not null && visible.TryGetValue(key, out var saved) ? saved : defaultOn;
            var checkedAttr = isOn ? " checked" : "";
            sb.AppendLine($"  <label><input type=\"checkbox\" id=\"tf-{key}\"{checkedAttr}> {EscapeHtml(label)}</label>");
        }
        sb.AppendLine("</div>");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static bool TryGetStr(JsonElement el, string name, out string value)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    /// <summary>
    /// Split a tool_result's content into renderable parts. Consecutive text
    /// items coalesce into one fenced run; image blocks become inline
    /// &lt;img&gt; data URIs. Mirrors the old single-string extraction for the
    /// common all-text case (one text part), so output is unchanged there.
    /// </summary>
    private static List<ResultPart> ExtractResult(JsonElement block, string field)
    {
        var parts = new List<ResultPart>();
        var text = new StringBuilder();
        void FlushText()
        {
            if (text.Length > 0)
            {
                parts.Add(new ResultPart(false, text.ToString().TrimEnd('\r', '\n')));
                text.Clear();
            }
        }

        if (!block.TryGetProperty(field, out var c)) return parts;

        if (c.ValueKind == JsonValueKind.String)
        {
            parts.Add(new ResultPart(false, c.GetString() ?? ""));
            return parts;
        }
        if (c.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in c.EnumerateArray())
            {
                if (TryRenderImage(item, out var imgTag))
                {
                    FlushText();
                    parts.Add(new ResultPart(true, imgTag));
                }
                else if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("text", out var t)
                    && t.ValueKind == JsonValueKind.String)
                    text.AppendLine(t.GetString());
                else if (item.ValueKind == JsonValueKind.String)
                    text.AppendLine(item.GetString());
                else
                    text.AppendLine(item.GetRawText());
            }
            FlushText();
            return parts;
        }
        parts.Add(new ResultPart(false, c.GetRawText()));
        return parts;
    }

    /// <summary>
    /// Detect a Claude content image block and turn it into an inline
    /// &lt;img&gt; tag. Handles <c>source.type == "base64"</c> (the common case —
    /// screenshots, pasted images) and <c>"url"</c>. Returns false for anything
    /// that isn't a well-formed image so the caller falls back to text. The
    /// base64 alphabet contains none of <c>" &lt; &gt;</c>, so a validated payload
    /// can't break out of the quoted <c>src</c> attribute; the url path is
    /// HTML-escaped and limited to http(s), which the page CSP's img-src allows.
    /// </summary>
    private static bool TryRenderImage(JsonElement item, out string imgTag)
    {
        imgTag = "";
        if (item.ValueKind != JsonValueKind.Object) return false;
        if (!TryGetStr(item, "type", out var bt) || bt != "image") return false;
        if (!item.TryGetProperty("source", out var src) || src.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryGetStr(src, "type", out var st)) return false;

        var media = TryGetStr(src, "media_type", out var mt) ? mt : "";
        string srcAttr;
        if (st == "base64")
        {
            // media_type is interpolated raw into src="data:<media>;base64,...".
            // A loose StartsWith("image/") would let media_type carry a quote and
            // angle brackets (e.g. image/png"><iframe ...) and break out of the
            // attribute, so validate strictly here.
            if (!IsImageMediaType(media)) return false;
            if (!TryGetStr(src, "data", out var data) || data.Length == 0) return false;
            if (!IsBase64Payload(data)) return false;
            srcAttr = "data:" + media + ";base64," + data;
        }
        else if (st == "url")
        {
            if (!TryGetStr(src, "url", out var url) || url.Length == 0) return false;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return false;
            srcAttr = EscapeHtml(url);
        }
        else return false;

        var alt = media.Length > 0 ? EscapeHtml(media) : "image";
        imgTag = $"<img class=\"t-img\" alt=\"{alt}\" src=\"{srcAttr}\">";
        return true;
    }

    /// <summary>
    /// Strict image MIME check for a value interpolated raw into
    /// <c>&lt;img src="data:&lt;media&gt;;base64,..."&gt;</c>. A real image media type
    /// (<c>image/png</c>, <c>image/svg+xml</c>, ...) contains none of <c>" &lt; &gt;</c>
    /// or whitespace, so this both confirms it is an image type and guarantees it
    /// cannot escape the quoted src attribute — closing the transcript
    /// <c>media_type</c> injection vector.
    /// </summary>
    private static bool IsImageMediaType(string media)
    {
        if (!media.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var ch in media)
            if (!(char.IsLetterOrDigit(ch) || ch is '/' or '.' or '+' or '-')) return false;
        return true;
    }

    /// <summary>
    /// True if every char is in the (URL-safe-tolerant) base64 alphabet plus
    /// whitespace. Cheap guard that both keeps non-base64 junk out of the page
    /// and guarantees the payload can't contain a quote/angle bracket that would
    /// escape the &lt;img src&gt; attribute. Permissive on padding/wrapping.
    /// </summary>
    private static bool IsBase64Payload(string s)
    {
        foreach (var ch in s)
        {
            bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')
                   || (ch >= '0' && ch <= '9')
                   || ch == '+' || ch == '/' || ch == '=' || ch == '-' || ch == '_'
                   || ch == '\r' || ch == '\n';
            if (!ok) return false;
        }
        return true;
    }

    private static string JoinStringArray(JsonElement parent, string field)
    {
        if (!parent.TryGetProperty(field, out var arr)) return "";
        if (arr.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                sb.AppendLine(item.GetString());
        return sb.ToString();
    }

    private static string FormatToolsDelta(JsonElement att)
    {
        var sb = new StringBuilder();
        if (att.TryGetProperty("addedNames", out var added)
            && added.ValueKind == JsonValueKind.Array
            && added.GetArrayLength() > 0)
        {
            sb.AppendLine("added:");
            foreach (var n in added.EnumerateArray())
                if (n.ValueKind == JsonValueKind.String) sb.AppendLine("  " + n.GetString());
        }
        if (att.TryGetProperty("removedNames", out var removed)
            && removed.ValueKind == JsonValueKind.Array
            && removed.GetArrayLength() > 0)
        {
            sb.AppendLine("removed:");
            foreach (var n in removed.EnumerateArray())
                if (n.ValueKind == JsonValueKind.String) sb.AppendLine("  " + n.GetString());
        }
        return sb.ToString();
    }

    private static string SerializeIndented(JsonElement el)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            el.WriteTo(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SummaryPreview(string inputJson)
    {
        if (string.IsNullOrEmpty(inputJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "";
            foreach (var key in new[] { "description", "command", "file_path", "path", "pattern", "query" })
            {
                if (doc.RootElement.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    var s = (v.GetString() ?? "").Split('\n')[0];
                    return Trim(s, 80);
                }
            }
        }
        catch (JsonException) { }
        return "";
    }

    private static void AppendFence(StringBuilder body, string lang, string content)
    {
        content = Trim(content, MaxTextChars);
        // Pick a fence longer than any backtick run inside the content.
        var fence = "```";
        while (content.Contains(fence)) fence += "`";
        body.AppendLine(fence + lang);
        body.AppendLine(content.TrimEnd('\n', '\r'));
        body.AppendLine(fence);
        body.AppendLine();
    }

    private static string Trim(string s, int max)
    {
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "\n\n[... truncated, " + (s.Length - max) + " more chars ...]";
    }

    private static string EscapeHtml(string s)
    {
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }

    private static string EscapeMd(string s)
    {
        // Only the chars that would format unintentionally on an inline emit.
        return s.Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_");
    }

    /// <summary>
    /// Render a value as an inline code span WITHOUT markdown-escaping its
    /// contents. Backslashes, <c>*</c>, <c>_</c> etc. are literal inside a code
    /// span, so running them through <see cref="EscapeMd"/> would double every
    /// backslash (a cwd of C:\Notes rendering as C:\\Notes). Only backticks
    /// need care: grow the fence past the longest backtick run in the content,
    /// per CommonMark, and pad with a space when the content begins or ends
    /// with a backtick so the delimiters stay unambiguous.
    /// </summary>
    private static string CodeSpan(string s)
    {
        // Inline code spans live on one line; collapse any embedded newlines
        // so an interpolated multi-line value can't break out of the span.
        s = s.Replace("\r", " ").Replace("\n", " ");

        int maxRun = 0, cur = 0;
        foreach (var ch in s)
        {
            if (ch == '`') { cur++; if (cur > maxRun) maxRun = cur; }
            else cur = 0;
        }
        var fence = new string('`', maxRun + 1);
        var pad = s.Length > 0 && (s[0] == '`' || s[^1] == '`') ? " " : "";
        return fence + pad + s + pad + fence;
    }
}
