using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    public const string CatSystem = "system";
    public const string CatContext = "context";
    public const string CatApi = "api";
    public const string CatNoise = "noise";

    // Not a category: the "JSON" chip that shows every block's source-record
    // expander. Persisted alongside the categories (#tf-raw).
    public const string RawToggleKey = "raw";

    private static readonly (string Key, string Label, bool DefaultOn)[] Categories =
    {
        (CatConversation, "conversation", true),
        (CatTool,         "tool calls",    true),
        (CatThinking,     "thinking",      false),
        (CatSystem,       "system messages", false),
        (CatHook,         "hooks",         false),
        (CatContext,      "context injections", false),
        (CatSkill,        "skill listings",false),
        (CatMcp,          "MCP instructions", false),
        (CatToolsDelta,   "tool deltas",   false),
        (CatQueue,        "queue ops",     false),
        (CatApi,          "API requests",  false),
        (CatNoise,        "noise",         false),
    };

    // Housekeeping categories the "hide noise" preset switches off together.
    private static readonly HashSet<string> NoiseCategories =
        new() { CatHook, CatContext, CatSkill, CatMcp, CatToolsDelta, CatQueue, CatApi, CatNoise };

    // Attachments that carry reminder text but repeat every turn or two
    // (token counts, the output-style nudge): context in name only, so they
    // go to noise rather than drown the real injections.
    private static readonly HashSet<string> NoiseAttachments = new()
    {
        "total_tokens_reminder", "output_style", "silent_turn_reminder",
        "bash_output_audience_note", "batching_reminder_sent",
    };

    // Fields every record carries; left out of a record's one-line gist.
    private static readonly HashSet<string> EnvelopeKeys = new()
    {
        "type", "subtype", "sessionId", "session_id", "uuid", "parentUuid", "logicalParentUuid",
        "isSidechain", "userType", "entrypoint", "cwd", "version", "gitBranch", "slug",
        "timestamp", "agentId", "isMeta", "level", "sessionKind", "rendered", "attachment",
    };

    // Anything longer than this in a single text value is truncated with a marker.
    // Protects WebView2 from pathological inputs without dropping useful content.
    private const int MaxTextChars = 200_000;

    // A piece of a tool_result's content. Text runs render as a fenced block;
    // image blocks render as an inline &lt;img&gt; data URI instead of dumping
    // their base64 as text — base64 screenshots otherwise dominate a
    // transcript's size and render as an unreadable wall of characters.
    private readonly record struct ResultPart(bool IsImage, string Value);

    // A tool_result's rendered parts plus the user record it came from, so
    // the tool_use block can offer that record's JSON too.
    private sealed record ToolResult(List<ResultPart> Parts, Rec Record);

    // One parsed record and its 0-based line in the file. JSON blocks name
    // their lines (data-lines) so the host can supply the JSON when one is
    // opened, instead of every record riding in the page twice: API request
    // records alone run to megabytes. Embed writes the JSON into the page
    // instead, for exports, which have no host to ask.
    private readonly record struct Rec(int Line, JsonElement Root, bool Embed);

    /// <param name="embedJson">Write each block's JSON into the output
    /// (exported HTML) rather than leaving it for the host to supply through
    /// <see cref="RecordJson"/> when the block is opened (the viewer).</param>
    public static string ToMarkdown(string jsonl, IDictionary<string, bool>? visibleCategories = null,
        bool embedJson = false)
    {
        var recs = ParseLines(jsonl, embedJson);
        var roots = recs.Select(r => r.Root).ToList();
        var resultLookup = BuildResultLookup(recs);

        var used = new HashSet<string>();
        var body = new StringBuilder();
        var consumed = new HashSet<string>();

        foreach (var rec in recs)
            EmitRecord(rec, body, used, resultLookup, consumed);

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

    private static List<Rec> ParseLines(string jsonl, bool embed)
    {
        var roots = new List<Rec>();
        var lines = SplitLines(jsonl);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                roots.Add(new Rec(i, doc.RootElement.Clone(), embed));
            }
            catch (JsonException)
            {
                // Malformed line — skip silently so neighbors still render.
            }
        }
        return roots;
    }

    private static Dictionary<string, ToolResult> BuildResultLookup(List<Rec> recs)
    {
        // Pre-scan every user-message tool_result so an assistant-message
        // tool_use can embed its output directly, even though they sit in
        // different records.
        var map = new Dictionary<string, ToolResult>();
        foreach (var rec in recs)
        {
            var root = rec.Root;
            if (!TryGetStr(root, "type", out var t) || t != "user") continue;
            if (!root.TryGetProperty("message", out var msg)) continue;
            if (!msg.TryGetProperty("content", out var content)) continue;
            if (content.ValueKind != JsonValueKind.Array) continue;
            foreach (var block in content.EnumerateArray())
            {
                if (!TryGetStr(block, "type", out var bt) || bt != "tool_result") continue;
                if (!TryGetStr(block, "tool_use_id", out var id)) continue;
                map[id] = new ToolResult(ExtractResult(block, "content"), rec);
            }
        }
        return map;
    }

    // ─── Per-record dispatch ────────────────────────────────────────────────

    private static void EmitRecord(
        Rec rec, StringBuilder body, HashSet<string> used,
        Dictionary<string, ToolResult> resultLookup, HashSet<string> consumed)
    {
        var root = rec.Root;
        if (!TryGetStr(root, "type", out var t))
        {
            EmitJsonBlock(CatNoise, "(no type)", Gist(root), rec, body, used);
            return;
        }

        switch (t)
        {
            case "user":            EmitUser(rec, body, used, consumed); break;
            case "assistant":       EmitAssistant(rec, body, used, resultLookup, consumed); break;
            case "queue-operation": EmitQueue(rec, body, used); break;
            case "attachment":      EmitAttachment(rec, body, used); break;
            case "system":          EmitSystem(rec, body, used); break;
            case "api-request":
            case "api-request-blob":
            case "api-request-shape":
                EmitJsonBlock(CatApi, t, ApiGist(root), rec, body, used); break;
            // agent-name, mode, atis-latch, last-prompt, cost-state, ... and
            // any type Claude Code adds later: a name or an id, shown as JSON.
            default:                EmitJsonBlock(CatNoise, t, Gist(root), rec, body, used); break;
        }
    }

    private static void EmitUser(
        Rec rec, StringBuilder body, HashSet<string> used, HashSet<string> consumed)
    {
        if (!rec.Root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("content", out var content)) return;
        EmitUserContent(content, "User", rec, body, used, consumed);
    }

    /// <summary>
    /// A user turn's content: a plain string, or an array of text / image /
    /// tool_result blocks. The record's JSON expander rides on the last block
    /// it produces, so a multi-block record shows its JSON once.
    /// </summary>
    private static void EmitUserContent(
        JsonElement content, string label, Rec rec,
        StringBuilder body, HashSet<string> used, HashSet<string> consumed)
    {
        // A queued mid-turn message is the user's by its origin already.
        var classify = label == "User";
        if (content.ValueKind == JsonValueKind.String)
        {
            var str = content.GetString() ?? "";
            if (!(classify && TryEmitNonHumanText(rec, str, rec, body, used)))
                EmitConversation("User", str, body, used, rec, label);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array) return;

        var blocks = content.EnumerateArray().ToList();
        var last = blocks.FindLastIndex(b => TryGetStr(b, "type", out var bt)
            && (bt is "text" or "image"
                || (bt == "tool_result" && !consumed.Contains(TryGetStr(b, "tool_use_id", out var i) ? i : ""))));
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var raw = i == last ? rec : default;
            if (!TryGetStr(block, "type", out var bt)) continue;
            if (bt == "text")
            {
                var s = TryGetStr(block, "text", out var tx) ? tx : "";
                if (!(classify && TryEmitNonHumanText(rec, s, raw, body, used)))
                    EmitConversation("User", s, body, used, raw, label);
            }
            else if (bt == "image")
            {
                // A pasted/attached image in the user turn (e.g. a screenshot).
                // One that can't be shown still leaves a visible placeholder.
                EmitUserImage(TryRenderImage(block, out var imgTag)
                    ? imgTag : "*" + EscapeMd(ImagePlaceholder(block)) + "*", body, used, raw);
            }
            else if (bt == "tool_result")
            {
                var id = TryGetStr(block, "tool_use_id", out var tid) ? tid : "";
                if (consumed.Contains(id)) continue; // already shown inside the tool_use
                EmitOrphanResult(id, ExtractResult(block, "content"), body, used, raw);
            }
        }
    }

    private static void EmitAssistant(
        Rec rec, StringBuilder body, HashSet<string> used,
        Dictionary<string, ToolResult> resultLookup, HashSet<string> consumed)
    {
        if (!rec.Root.TryGetProperty("message", out var msg)) return;
        if (!msg.TryGetProperty("content", out var content)) return;
        if (content.ValueKind != JsonValueKind.Array) return;

        var blocks = content.EnumerateArray().ToList();
        var last = blocks.FindLastIndex(b => TryGetStr(b, "type", out var bt)
            && (bt is "text" or "tool_use"
                || (bt == "thinking" && !string.IsNullOrWhiteSpace(TryGetStr(b, "thinking", out var th) ? th : ""))));
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var raw = i == last ? rec : default;
            if (!TryGetStr(block, "type", out var bt)) continue;
            switch (bt)
            {
                case "text":
                    EmitConversation("Assistant", TryGetStr(block, "text", out var s) ? s : "", body, used, raw);
                    break;
                case "thinking":
                    var thinking = TryGetStr(block, "thinking", out var th) ? th : "";
                    if (!string.IsNullOrWhiteSpace(thinking))
                        EmitThinking(thinking, body, used, raw);
                    break;
                case "tool_use":
                    var name = TryGetStr(block, "name", out var n) ? n : "(unknown)";
                    var id = TryGetStr(block, "id", out var tid) ? tid : "";
                    var inputJson = block.TryGetProperty("input", out var input)
                        ? SerializeIndented(input)
                        : "";
                    resultLookup.TryGetValue(id, out var result);
                    if (id.Length > 0 && result is not null) consumed.Add(id);
                    EmitToolUse(name, inputJson, result?.Parts, body, used, raw, result?.Record ?? default);
                    break;
            }
        }
    }

    private static void EmitQueue(Rec rec, StringBuilder body, HashSet<string> used)
    {
        var root = rec.Root;
        var op = TryGetStr(root, "operation", out var o) ? o : "";
        var content = TryGetStr(root, "content", out var c) ? c : null;
        used.Add(CatQueue);
        body.AppendLine($"<div class=\"t-block t-{CatQueue}\">");
        body.AppendLine();
        body.AppendLine(content is null
            ? $"*queue {EscapeMd(op)}*"
            : $"*queue {EscapeMd(op)}*: {EscapeMd(Trim(content, 200))}");
        body.AppendLine();
        AppendRaw(body, used, rec);
        body.AppendLine("</div>");
        body.AppendLine();
    }

    private static void EmitAttachment(Rec rec, StringBuilder body, HashSet<string> used)
    {
        var root = rec.Root;
        if (!root.TryGetProperty("attachment", out var att))
        {
            EmitJsonBlock(CatNoise, "attachment", "", rec, body, used);
            return;
        }
        var atype = TryGetStr(att, "type", out var x) ? x : "";

        if (atype.StartsWith("hook_", StringComparison.Ordinal))
        {
            EmitHook(att, atype, rec, body, used);
            return;
        }
        switch (atype)
        {
            case "skill_listing":
                var content = TryGetStr(att, "content", out var sc) ? sc : "";
                EmitDetails(CatSkill, "Skill listing", content, rec, body, used);
                break;
            case "mcp_instructions_delta":
                EmitDetails(CatMcp, "MCP instructions", JoinStringArray(att, "addedBlocks"), rec, body, used);
                break;
            case "deferred_tools_delta":
                EmitDetails(CatToolsDelta, "Deferred tools delta",
                    FormatDelta(att, "addedNames", "removedNames"), rec, body, used);
                break;
            case "agent_listing_delta":
                EmitDetails(CatToolsDelta, "Agent listing delta",
                    FormatDelta(att, "addedTypes", "removedTypes"), rec, body, used);
                break;
            case "queued_command":
                EmitQueuedCommand(att, rec, body, used);
                break;
            case "prompt_snapshot":
                EmitJsonBlock(CatApi, "attachment: " + atype, "", rec, body, used);
                break;
            default:
                // Most attachments carry `rendered`: the text the model was
                // actually shown (CLAUDE.md files, edited-file notes, the
                // date). That text is the useful part; the rest is plumbing.
                var rendered = RenderedText(root);
                if (rendered.Length > 0 && !NoiseAttachments.Contains(atype))
                    EmitDetails(CatContext, Join(atype, Gist(att)), rendered, rec, body, used);
                else
                    EmitJsonBlock(CatNoise, "attachment: " + atype, Gist(att), rec, body, used);
                break;
        }
    }

    /// <summary>
    /// A message that arrived while a turn was running. One the user typed
    /// is a real user turn (it never appears as its own user record); one
    /// from a peer session or a finished background task is a system message.
    /// </summary>
    private static void EmitQueuedCommand(JsonElement att, Rec rec, StringBuilder body, HashSet<string> used)
    {
        var origin = att.TryGetProperty("origin", out var o) && TryGetStr(o, "kind", out var k) ? k : "";
        if (!att.TryGetProperty("prompt", out var prompt))
        {
            EmitJsonBlock(CatNoise, "attachment: queued_command", Gist(att), rec, body, used);
            return;
        }
        if (origin == "human")
        {
            EmitUserContent(prompt, "User (mid-turn)", rec, body, used, new HashSet<string>());
            return;
        }
        var text = prompt.ValueKind == JsonValueKind.String ? prompt.GetString() ?? "" : SerializeRaw(prompt);
        EmitDetails(CatSystem, OriginTitle(origin), text, rec, body, used);
    }

    private static string OriginTitle(string origin) => origin switch
    {
        "peer" => "📨 Agent message",
        "coordinator" => "📨 Coordinator message",
        "task-notification" => "📨 Task notification",
        _ => "📨 Queued message" + (origin.Length > 0 ? " (" + origin + ")" : ""),
    };

    private static readonly Regex CommandOutputTags = new(
        "</?(local-command-stdout|local-command-stderr|bash-stdout|bash-stderr)>", RegexOptions.Compiled);

    /// <summary>
    /// Text in a user record that the user didn't type as a message: skill
    /// bodies, reminders and caveats Claude Code adds (isMeta), compaction
    /// summaries, messages from other sessions and background tasks, and the
    /// plumbing around slash commands and <c>!</c> shell commands. Emits it
    /// in its own category and returns true; false means a real user turn.
    /// </summary>
    private static bool TryEmitNonHumanText(Rec rec, string text, Rec raw, StringBuilder body, HashSet<string> used)
    {
        var root = rec.Root;
        var origin = root.TryGetProperty("origin", out var o) && TryGetStr(o, "kind", out var k) ? k : "";
        var t = text.TrimStart();

        if (IsTrue(root, "isCompactSummary"))
            EmitDetails(CatSystem, "📦 Compaction summary", text, raw, body, used);
        else if (origin is "peer" or "coordinator" or "task-notification")
            EmitDetails(CatSystem, OriginTitle(origin), text, raw, body, used);
        else if (t.StartsWith("<command-name>", StringComparison.Ordinal)
                 || t.StartsWith("<command-message>", StringComparison.Ordinal))
        {
            // A slash command the user ran: show what they typed.
            var name = TagText(t, "command-name");
            var args = TagText(t, "command-args").Trim();
            var shown = CodeSpan(name.Length > 0 ? name : "/" + TagText(t, "command-message"));
            if (args.Length > 0) shown += Environment.NewLine + Environment.NewLine + FenceText("", args);
            EmitConversation("User", shown, body, used, raw);
        }
        else if (t.StartsWith("<bash-input>", StringComparison.Ordinal))
            EmitConversation("User", CodeSpan("! " + TagText(t, "bash-input").Trim()), body, used, raw);
        else if (t.StartsWith("<local-command-stdout>", StringComparison.Ordinal)
                 || t.StartsWith("<bash-stdout>", StringComparison.Ordinal))
        {
            var output = CommandOutputTags.Replace(text, "").Trim();
            if (output.Length > 0) EmitDetails(CatSystem, "Command output", output, raw, body, used);
            else EmitJsonBlock(CatNoise, "command output (empty)", "", rec, body, used);
        }
        else if (t.StartsWith("<local-command-caveat>", StringComparison.Ordinal))
            EmitJsonBlock(CatNoise, "local-command-caveat", "", rec, body, used);
        else if (t.StartsWith("Base directory for this skill:", StringComparison.Ordinal))
        {
            var dir = OneLine(t["Base directory for this skill:".Length..].Split('\n')[0], 300);
            EmitDetails(CatContext, "Skill: " + Path.GetFileName(dir.TrimEnd('\\', '/')), text, raw, body, used);
        }
        else if (IsTrue(root, "isMeta") || t.StartsWith("<fork-boilerplate>", StringComparison.Ordinal))
            EmitDetails(CatContext, "Injected: " + OneLine(t, 100), text, raw, body, used);
        else
            return false;
        return true;
    }

    private static bool IsTrue(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    // The text inside the first <tag>...</tag>, or "" when absent.
    private static string TagText(string s, string tag)
    {
        var m = Regex.Match(s, "<" + tag + ">(.*?)</" + tag + ">", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static void EmitHook(JsonElement att, string atype, Rec rec, StringBuilder body, HashSet<string> used)
    {
        var hookName = TryGetStr(att, "hookName", out var hn) ? hn : "";
        var hookEvent = TryGetStr(att, "hookEvent", out var he) ? he : "";
        var cmd = TryGetStr(att, "command", out var cmdv) ? cmdv : "";
        // GetRawText, not GetInt32: a large/unsigned exit code (e.g. 4294967295
        // or an NTSTATUS-style value) doesn't fit Int32 and would throw, which
        // — with no per-record guard — aborts rendering of the whole transcript.
        var exitCode = att.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? ec.GetRawText() : null;
        var stdout = TryGetStr(att, "stdout", out var so) ? so : "";
        var stderr = TryGetStr(att, "stderr", out var se) ? se : "";
        // hook_additional_context carries what the hook added to the context,
        // as a string or an array of strings.
        var added = TryGetStr(att, "content", out var cs) ? cs : JoinStringArray(att, "content");

        var detail = hookEvent;
        if (atype != "hook_success") detail = Join(detail, atype["hook_".Length..].Replace('_', ' '), ", ");
        if (exitCode is not null) detail = Join(detail, "exit " + exitCode, ", ");

        used.Add(CatHook);
        body.AppendLine($"<details class=\"t-block t-{CatHook}\">");
        body.AppendLine($"<summary>📎 Hook: {EscapeHtml(OneLine(hookName, 100))} ({EscapeHtml(OneLine(detail, 100))})</summary>");
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
        if (!string.IsNullOrWhiteSpace(added))
        {
            body.AppendLine("**Added context:**");
            AppendFence(body, "", added);
        }
        AppendRaw(body, used, rec);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    // ─── System records ─────────────────────────────────────────────────────

    private static void EmitSystem(Rec rec, StringBuilder body, HashSet<string> used)
    {
        var root = rec.Root;
        var sub = TryGetStr(root, "subtype", out var s) ? s : "";
        if (sub == "stop_hook_summary")
        {
            EmitDetails(CatHook, "📎 Stop hooks", FormatStopHooks(root), rec, body, used);
            return;
        }

        var content = TryGetStr(root, "content", out var c) ? c : "";
        string? title = null, text = null;
        var fenced = false;
        switch (sub)
        {
            case "away_summary":        title = "Away summary"; break;
            case "informational":       title = "Info"; break;
            case "bridge_status":       title = "Remote control"; break;
            case "local_command":
                title = "Local command output";
                text = content.Replace("<local-command-stdout>", "").Replace("</local-command-stdout>", "")
                              .Replace("<local-command-stderr>", "").Replace("</local-command-stderr>", "");
                fenced = true;
                break;
            case "compact_boundary":
                title = "Conversation compacted";
                text = root.TryGetProperty("compactMetadata", out var cm) ? Gist(cm, 300) : "";
                break;
            case "api_error":
                title = "API error";
                if (root.TryGetProperty("error", out var err))
                    text = TryGetStr(err, "formatted", out var f) ? f
                         : TryGetStr(err, "message", out var m) ? m : "";
                if (root.TryGetProperty("retryAttempt", out var ra) && root.TryGetProperty("maxRetries", out var mr))
                    text += $" (retry {ra.GetRawText()} of {mr.GetRawText()})";
                break;
            case "scheduled_task_fire":
                title = "Scheduled task";
                text = Join(content, TryGetStr(root, "prompt", out var p) ? p : "", "\n\n");
                break;
            case "model_refusal_no_fallback":
                title = "Model refusal";
                text = Join(content, TryGetStr(root, "apiRefusalExplanation", out var ex) ? ex : "", "\n\n");
                break;
        }
        text ??= content;

        // turn_duration, and any subtype with nothing to read, is noise.
        if (string.IsNullOrWhiteSpace(text))
        {
            EmitJsonBlock(CatNoise, "system: " + sub, Gist(root), rec, body, used);
            return;
        }

        used.Add(CatSystem);
        body.AppendLine($"<div class=\"t-block t-{CatSystem}\">");
        body.AppendLine();
        body.AppendLine($"**ℹ️ {EscapeHtml(title ?? "system: " + sub)}**");
        body.AppendLine();
        if (fenced) AppendFence(body, "", text);
        else
        {
            body.AppendLine(Trim(text, MaxTextChars));
            body.AppendLine();
        }
        AppendRaw(body, used, rec);
        body.AppendLine("</div>");
        body.AppendLine();
    }

    private static string FormatStopHooks(JsonElement root)
    {
        var sb = new StringBuilder();
        if (root.TryGetProperty("hookInfos", out var infos) && infos.ValueKind == JsonValueKind.Array)
            foreach (var h in infos.EnumerateArray())
            {
                var cmd = TryGetStr(h, "command", out var c) ? c : "(hook)";
                var ms = h.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number
                    ? $" ({d.GetRawText()} ms)" : "";
                sb.AppendLine(cmd + ms);
            }
        var errors = JoinStringArray(root, "hookErrors");
        if (errors.Length > 0) sb.AppendLine("errors:").Append(errors);
        return sb.ToString();
    }

    private static void EmitDetails(string cat, string title, string content,
        Rec rec, StringBuilder body, HashSet<string> used)
    {
        used.Add(cat);
        body.AppendLine($"<details class=\"t-block t-{cat}\">");
        body.AppendLine($"<summary>{EscapeHtml(OneLine(title, 140))}</summary>");
        body.AppendLine();
        if (!string.IsNullOrEmpty(content))
        {
            // Mostly prose (reminders, skill bodies, summaries): wrap it
            // rather than scroll a paragraph-long line sideways.
            body.AppendLine("<div class=\"t-wrap\">");
            body.AppendLine();
            // "text": unlabeled, highlight.js guesses a language and colours
            // random words of the prose.
            AppendFence(body, "text", content);
            body.AppendLine("</div>");
            body.AppendLine();
        }
        AppendRaw(body, used, rec);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    /// <summary>
    /// A block whose body is the record's own JSON: records with nothing more
    /// to show than a name, a mode or an id. The summary carries their gist.
    /// </summary>
    private static void EmitJsonBlock(string cat, string title, string gist,
        Rec rec, StringBuilder body, HashSet<string> used)
    {
        used.Add(cat);
        body.AppendLine(rec.Embed
            ? $"<details class=\"t-block t-{cat}\">"
            : $"<details class=\"t-block t-{cat}\" data-lines=\"{rec.Line}\">");
        body.AppendLine($"<summary>{EscapeHtml(OneLine(Join(title, gist), 140))}</summary>");
        body.AppendLine();
        if (rec.Embed) AppendFence(body, "json", SerializeRaw(rec.Root));
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitConversation(string role, string text, StringBuilder body, HashSet<string> used,
        Rec raw, string? label = null)
    {
        used.Add(CatConversation);
        var roleClass = role.ToLowerInvariant();
        var emoji = role == "User" ? "🧑" : "🤖";
        var preview = HeadingPreview(text, 60);
        label ??= role;
        // H3 heading so the existing outline panel picks it up. Filters still
        // hide the wrapping block when toggled off; the outline entry stays in
        // place but its target is hidden — acceptable for a viewer-only doc.
        var heading = preview.Length > 0
            ? $"### {emoji} {label}: {preview}"
            : $"### {emoji} {label}";

        body.AppendLine($"<div class=\"t-block t-{CatConversation} t-{roleClass}\">");
        body.AppendLine();
        body.AppendLine(heading);
        body.AppendLine();
        body.AppendLine(Trim(text, MaxTextChars));
        body.AppendLine();
        AppendRaw(body, used, raw);
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

    private static void EmitThinking(string text, StringBuilder body, HashSet<string> used, Rec raw)
    {
        used.Add(CatThinking);
        body.AppendLine($"<details class=\"t-block t-{CatThinking}\">");
        body.AppendLine("<summary>💭 Thinking</summary>");
        body.AppendLine();
        body.AppendLine(Trim(text, MaxTextChars));
        body.AppendLine();
        AppendRaw(body, used, raw);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitToolUse(
        string name, string inputJson, List<ResultPart>? result,
        StringBuilder body, HashSet<string> used, Rec useRecord, Rec resultRecord)
    {
        used.Add(CatTool);
        var preview = SummaryPreview(inputJson, out var previewKey, out var previewFull);
        // A file path (Read/Edit/Write/...) becomes a link: bridge.js asks the
        // host to open it, which it does only when it lies inside the open vault.
        var previewHtml = PathKeys.Contains(previewKey) && IsFullyQualifiedPath(previewFull)
            ? $"<a class=\"t-path\" href=\"#\" data-path=\"{EscapeHtml(previewFull)}\">{EscapeHtml(preview)}</a>"
            : EscapeHtml(preview);
        var summaryText = preview.Length > 0
            ? $"🔧 {EscapeHtml(name)} — {previewHtml}"
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
        AppendRaw(body, used, useRecord, resultRecord);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitOrphanResult(string id, List<ResultPart> output, StringBuilder body, HashSet<string> used,
        Rec raw)
    {
        used.Add(CatTool);
        body.AppendLine($"<details class=\"t-block t-{CatTool}\">");
        body.AppendLine($"<summary>🔧 Orphan tool_result ({EscapeHtml(id)})</summary>");
        body.AppendLine();
        AppendResult(body, output);
        AppendRaw(body, used, raw);
        body.AppendLine("</details>");
        body.AppendLine();
    }

    private static void EmitUserImage(string imgTag, StringBuilder body, HashSet<string> used, Rec raw)
    {
        used.Add(CatConversation);
        body.AppendLine($"<div class=\"t-block t-{CatConversation} t-user\">");
        body.AppendLine();
        body.AppendLine(imgTag);
        body.AppendLine();
        AppendRaw(body, used, raw);
        body.AppendLine("</div>");
        body.AppendLine();
    }

    /// <summary>
    /// The "{ } JSON" expander at the foot of a parsed block: its source
    /// record(s) as indented JSON. Hidden until the JSON chip is ticked.
    /// Records left <c>default</c> (no source to show) are skipped.
    /// </summary>
    private static void AppendRaw(StringBuilder body, HashSet<string> used, params Rec[] records)
    {
        var present = records.Where(r => r.Root.ValueKind != JsonValueKind.Undefined).ToList();
        if (present.Count == 0) return;
        used.Add(RawToggleKey);
        body.AppendLine(present[0].Embed
            ? "<details class=\"t-raw\">"
            : $"<details class=\"t-raw\" data-lines=\"{string.Join(' ', present.Select(r => r.Line))}\">");
        body.AppendLine("<summary>{ } JSON</summary>");
        body.AppendLine();
        foreach (var r in present)
            if (r.Embed) AppendFence(body, "json", SerializeRaw(r.Root));
        body.AppendLine("</details>");
        body.AppendLine();
    }

    /// <summary>
    /// A transcript's lines as <see cref="ToMarkdown"/> numbers them (the
    /// data-lines of its JSON blocks).
    /// </summary>
    // Normalize line endings; transcripts written on Windows commonly use \r\n.
    public static string[] SplitLines(string jsonl) => jsonl.Replace("\r\n", "\n").Split('\n');

    /// <summary>
    /// The JSON a block shows for one of its source lines (from
    /// <see cref="SplitLines"/>): the record indented, base64 payloads cut.
    /// A line out of range or not valid JSON comes back as a note saying so.
    /// </summary>
    public static string RecordJson(string[] lines, int line)
    {
        if (line < 0 || line >= lines.Length) return "(line " + line + " is not in the file)";
        try
        {
            using var doc = JsonDocument.Parse(lines[line]);
            return SerializeRaw(doc.RootElement);
        }
        catch (JsonException) { return "(line " + line + " is not valid JSON)"; }
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
            else if (TryPrettyJson(part.Value, out var pretty))
            {
                AppendFence(body, "json", pretty);
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
        sb.AppendLine(".t-presets { margin-left: auto; display: flex; gap: 6px; }");
        sb.AppendLine(".t-preset { font: inherit; font-size: 0.85em; padding: 1px 8px; cursor: pointer; color: inherit; background: transparent; border: 1px solid rgba(127,127,127,0.4); border-radius: 4px; }");
        sb.AppendLine(".t-preset:hover { background: rgba(127,127,127,0.12); }");
        sb.AppendLine(".t-session-header { margin: 0 0 16px; padding: 8px 12px; background: rgba(127,127,127,0.08); border-radius: 4px; font-size: 0.9em; }");
        sb.AppendLine(".t-session-header ul { margin: 0; padding-left: 1.2em; }");
        sb.AppendLine(".t-block { margin: 8px 0; padding: 6px 10px; border-left: 3px solid rgba(127,127,127,0.3); }");
        sb.AppendLine(".t-block.t-user { border-left-color: #4a90e2; }");
        sb.AppendLine(".t-block.t-assistant { border-left-color: #7ed321; }");
        sb.AppendLine("details.t-block > summary { cursor: pointer; user-select: none; }");
        sb.AppendLine(".t-img { max-width: 100%; height: auto; display: block; margin: 8px 0; border: 1px solid rgba(127,127,127,0.3); border-radius: 4px; }");
        sb.AppendLine(".t-block.t-system { border-left-color: #f5a623; }");
        sb.AppendLine(".t-rawchip { padding-left: 12px; border-left: 1px solid rgba(127,127,127,0.4); }");
        sb.AppendLine("details.t-raw { margin: 6px 0 0; font-size: 0.85em; }");
        sb.AppendLine("details.t-raw > summary { cursor: pointer; user-select: none; opacity: 0.6; }");
        sb.AppendLine(".t-doc .t-wrap pre, .t-doc .t-wrap pre code { white-space: pre-wrap; overflow-wrap: anywhere; }");
        sb.AppendLine(".t-block, .t-raw { display: none; }");
        foreach (var (key, _, _) in Categories)
            sb.AppendLine($".t-doc:has(#tf-{key}:checked) .t-{key} {{ display: block; }}");
        sb.AppendLine($".t-doc:has(#tf-{RawToggleKey}:checked) .t-raw {{ display: block; }}");
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
        // Not a category: shows the JSON expander inside every block. Off by
        // default so the conversation isn't lined with them; the presets
        // leave it alone.
        if (used.Contains(RawToggleKey))
        {
            var rawOn = visible is not null && visible.TryGetValue(RawToggleKey, out var r) && r;
            sb.AppendLine($"  <label class=\"t-rawchip\"><input type=\"checkbox\" id=\"tf-{RawToggleKey}\"{(rawOn ? " checked" : "")}> JSON</label>");
        }
        // Presets: bridge.js sets every listed category to data-on and persists
        // each change like a manual click.
        var shown = Categories.Select(c => c.Key).Where(used.Contains).ToList();
        var noise = shown.Where(NoiseCategories.Contains).ToList();
        sb.AppendLine("  <span class=\"t-presets\">");
        sb.AppendLine($"    <button type=\"button\" class=\"t-preset\" data-on=\"1\" data-cats=\"{string.Join(' ', shown)}\">all</button>");
        sb.AppendLine($"    <button type=\"button\" class=\"t-preset\" data-on=\"0\" data-cats=\"{string.Join(' ', shown)}\">none</button>");
        if (noise.Count > 0)
        {
            sb.AppendLine($"    <button type=\"button\" class=\"t-preset\" data-on=\"0\" data-cats=\"{string.Join(' ', noise)}\">hide noise</button>");
            sb.AppendLine($"    <button type=\"button\" class=\"t-preset\" data-on=\"1\" data-cats=\"{string.Join(' ', noise)}\">show noise</button>");
        }
        sb.AppendLine("  </span>");
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
                else if (TryGetStr(item, "type", out var it) && it == "image")
                    text.AppendLine(ImagePlaceholder(item)); // not raw JSON (base64 noise)
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
    /// Text stand-in for an image block that can't be rendered (bad or missing
    /// payload, unsupported source): "[image: image/png]", or "[image]" when the
    /// media type is absent or not a clean image type.
    /// </summary>
    private static string ImagePlaceholder(JsonElement item)
    {
        var media = item.TryGetProperty("source", out var src) && TryGetStr(src, "media_type", out var mt)
            ? mt : "";
        return IsImageMediaType(media) ? $"[image: {media}]" : "[image]";
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

    private static string FormatDelta(JsonElement att, string addedField, string removedField)
    {
        var sb = new StringBuilder();
        if (att.TryGetProperty(addedField, out var added)
            && added.ValueKind == JsonValueKind.Array
            && added.GetArrayLength() > 0)
        {
            sb.AppendLine("added:");
            foreach (var n in added.EnumerateArray())
                if (n.ValueKind == JsonValueKind.String) sb.AppendLine("  " + n.GetString());
        }
        if (att.TryGetProperty(removedField, out var removed)
            && removed.ValueKind == JsonValueKind.Array
            && removed.GetArrayLength() > 0)
        {
            sb.AppendLine("removed:");
            foreach (var n in removed.EnumerateArray())
                if (n.ValueKind == JsonValueKind.String) sb.AppendLine("  " + n.GetString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// The text an attachment put in front of the model, from the record's
    /// <c>rendered[].content</c> strings; empty when there is none.
    /// </summary>
    private static string RenderedText(JsonElement root)
    {
        if (!root.TryGetProperty("rendered", out var r) || r.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var item in r.EnumerateArray())
            if (TryGetStr(item, "content", out var c) && c.Length > 0)
                sb.AppendLine(c);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// One line of an object's own fields ("agentName: ops, mode: normal"),
    /// for a summary: strings, numbers and booleans as-is, a non-empty array
    /// as its length, envelope fields and nested objects skipped.
    /// </summary>
    private static string Gist(JsonElement obj, int max = 100)
    {
        if (obj.ValueKind != JsonValueKind.Object) return "";
        var parts = new List<string>();
        foreach (var p in obj.EnumerateObject())
        {
            if (EnvelopeKeys.Contains(p.Name)) continue;
            var v = p.Value;
            var s = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() is { Length: > 0 } str ? str : null,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
                JsonValueKind.Array when v.GetArrayLength() > 0 => $"[{v.GetArrayLength()}]",
                _ => null,
            };
            if (s is not null) parts.Add(p.Name + ": " + s);
        }
        return OneLine(string.Join(", ", parts), max);
    }

    // api-request records are mostly hashes; the caller and model are what
    // tell one from the next.
    private static string ApiGist(JsonElement root)
    {
        var parts = new List<string>();
        if (TryGetStr(root, "querySource", out var qs)) parts.Add(qs);
        if (root.TryGetProperty("params", out var p) && TryGetStr(p, "model", out var m)) parts.Add(m);
        if (root.TryGetProperty("message", out var msg) && TryGetStr(msg, "role", out var role)) parts.Add(role);
        return OneLine(string.Join(", ", parts), 100);
    }

    /// <summary>Flatten to one line (a &lt;summary&gt; must not hold a blank
    /// line) and cap the length.</summary>
    private static string OneLine(string s, int max)
    {
        var line = string.Join(' ', s.Split('\n', '\r').Select(x => x.Trim()).Where(x => x.Length > 0));
        return line.Length <= max ? line : line[..(max - 3)] + "...";
    }

    private static string Join(string a, string b, string sep = " - ")
        => a.Length == 0 ? b : b.Length == 0 ? a : a + sep + b;

    private static string SerializeIndented(JsonElement el)
    {
        using var ms = new MemoryStream();
        // Relaxed escaping keeps non-ASCII and <>& readable; the output only
        // ever lands in a fenced code block, which Markdig HTML-escapes.
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
               { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            el.WriteTo(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// A whole record as indented JSON for its expander, with base64 payloads
    /// cut to a length note: a screenshot's encoding is megabytes of
    /// unreadable text, and the image itself already renders in the block.
    /// </summary>
    private static string SerializeRaw(JsonElement el)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
               { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteElided(w, el);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteElided(Utf8JsonWriter w, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                // An image source block ({"type":"base64","data":...}), or a
                // tool result's file.base64 (Read of an image).
                var isSource = TryGetStr(el, "type", out var t) && t == "base64";
                w.WriteStartObject();
                foreach (var p in el.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String
                        && ((isSource && p.Name == "data") || p.Name == "base64"))
                    {
                        w.WriteString(p.Name, $"[base64, {p.Value.GetString()!.Length} chars]");
                        continue;
                    }
                    w.WritePropertyName(p.Name);
                    WriteElided(w, p.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in el.EnumerateArray()) WriteElided(w, item);
                w.WriteEndArray();
                break;
            default:
                el.WriteTo(w);
                break;
        }
    }

    /// <summary>
    /// A tool output that is one whole JSON object or array (API responses, MCP
    /// results) re-indented for reading. Anything else, including text that
    /// merely starts with a brace, is left as-is.
    /// </summary>
    private static bool TryPrettyJson(string text, out string pretty)
    {
        pretty = "";
        var t = text.Trim();
        if (t.Length < 2 || !((t[0] == '{' && t[^1] == '}') || (t[0] == '[' && t[^1] == ']')))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(t);
            pretty = SerializeIndented(doc.RootElement);
            return true;
        }
        catch (JsonException) { return false; }
    }

    // Tool-input keys whose value is a file path worth linking.
    private static readonly HashSet<string> PathKeys = new() { "file_path", "notebook_path", "path" };

    private static bool IsFullyQualifiedPath(string s)
    {
        if (s.Length == 0 || s.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        try { return Path.IsPathFullyQualified(s); }
        catch { return false; }
    }

    private static string SummaryPreview(string inputJson, out string key, out string full)
    {
        key = "";
        full = "";
        if (string.IsNullOrEmpty(inputJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "";
            foreach (var k in new[] { "description", "command", "file_path", "notebook_path", "path", "pattern", "query" })
            {
                if (doc.RootElement.TryGetProperty(k, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    key = k;
                    full = v.GetString() ?? "";
                    // One line only: Trim's multi-line "truncated" marker would
                    // end the <summary> HTML block mid-tag.
                    var line = full.Split('\n')[0].TrimEnd('\r');
                    return line.Length <= 80 ? line : line[..79] + "...";
                }
            }
        }
        catch (JsonException) { }
        return "";
    }

    private static void AppendFence(StringBuilder body, string lang, string content)
    {
        body.AppendLine(FenceText(lang, content));
        body.AppendLine();
    }

    private static string FenceText(string lang, string content)
    {
        content = Trim(content, MaxTextChars);
        // Pick a fence longer than any backtick run inside the content.
        var fence = "```";
        while (content.Contains(fence)) fence += "`";
        var nl = Environment.NewLine;
        return fence + lang + nl + content.TrimEnd('\n', '\r') + nl + fence;
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
