using System.Collections.Generic;
using System.Text.Json;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class TranscriptServiceTests
{
    // ─── User messages ──────────────────────────────────────────────────────

    [Fact]
    public void UserStringContent_ProducesConversationBlock()
    {
        var jsonl = """
{"type":"user","message":{"role":"user","content":"hello world"}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);

        Assert.Contains("t-conversation", md);
        Assert.Contains("t-user", md);
        Assert.Contains("hello world", md);
        Assert.Contains("### 🧑 User", md);
    }

    [Fact]
    public void UserArrayTextContent_ProducesConversationBlock()
    {
        var jsonl = """
{"type":"user","message":{"role":"user","content":[{"type":"text","text":"hi from array"}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("hi from array", md);
        Assert.Contains("t-conversation", md);
    }

    // ─── Assistant messages ─────────────────────────────────────────────────

    [Fact]
    public void AssistantText_ProducesConversationBlock()
    {
        var jsonl = """
{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"hello back"}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("hello back", md);
        Assert.Contains("### 🤖 Assistant", md);
        Assert.Contains("t-assistant", md);
    }

    [Fact]
    public void EmptyThinking_IsOmitted()
    {
        // CSS rules always reference every category by name, so we instead check
        // the filter chip (only emitted for used categories) and the visible block.
        var jsonl = """
{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":""}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.DoesNotContain("id=\"tf-thinking\"", md);
        Assert.DoesNotContain("💭", md);
    }

    [Fact]
    public void NonEmptyThinking_ProducesThinkingBlock()
    {
        var jsonl = """
{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"plotting course"}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-thinking", md);
        Assert.Contains("plotting course", md);
    }

    // ─── Tool pairing ───────────────────────────────────────────────────────

    [Fact]
    public void ToolUseAndResult_ArePaired_NoOrphan()
    {
        // tool_use comes from assistant, tool_result comes from next user msg.
        var assistant = """
{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"echo hi"}}]}}
""";
        var user = """
{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"hi"}]}}
""";
        var md = TranscriptService.ToMarkdown(assistant + "\n" + user);

        // Tool block is emitted once.
        var firstSummary = md.IndexOf("🔧 Bash");
        Assert.True(firstSummary >= 0, "tool_use summary missing");
        Assert.Equal(firstSummary, md.LastIndexOf("🔧 Bash"));

        // No orphan block for the result.
        Assert.DoesNotContain("Orphan tool_result", md);
        // Output is embedded.
        Assert.Contains("**Output:**", md);
        Assert.Contains("hi", md);
        // Input is rendered as fenced JSON.
        Assert.Contains("**Input:**", md);
        Assert.Contains("\"command\"", md);
    }

    [Fact]
    public void OrphanToolResult_IsEmitted()
    {
        // tool_result with no matching tool_use anywhere.
        var jsonl = """
{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"unmatched","content":"stranded output"}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("Orphan tool_result", md);
        Assert.Contains("stranded output", md);
        Assert.Contains("unmatched", md);
    }

    [Fact]
    public void ToolUseWithNoResult_RendersAsNoMatching()
    {
        var jsonl = """
{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_x","name":"Bash","input":{"command":"true"}}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("🔧 Bash", md);
        Assert.Contains("no matching tool_result", md);
    }

    [Fact]
    public void AssistantMixedTextAndToolUse_EmitsBoth()
    {
        var assistant = """
{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"running it"},{"type":"tool_use","id":"toolu_2","name":"Read","input":{"file_path":"/tmp/a"}}]}}
""";
        var user = """
{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_2","content":"file contents"}]}}
""";
        var md = TranscriptService.ToMarkdown(assistant + "\n" + user);
        Assert.Contains("running it", md);
        Assert.Contains("🔧 Read", md);
        // The summary preview should pick up file_path.
        Assert.Contains("/tmp/a", md);
        Assert.Contains("file contents", md);
    }

    // ─── Attachments ────────────────────────────────────────────────────────

    [Fact]
    public void HookAttachment_RoutesToHookCategory()
    {
        var jsonl = """
{"type":"attachment","attachment":{"type":"hook_success","hookName":"notes","hookEvent":"SessionStart","exitCode":0,"stdout":"ok\n","stderr":"","command":"bash x.sh"}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-hook", md);
        Assert.Contains("notes", md);
        Assert.Contains("SessionStart", md);
        Assert.Contains("ok", md);
    }

    [Fact]
    public void SkillListing_RoutesToSkillCategory()
    {
        var jsonl = """
{"type":"attachment","attachment":{"type":"skill_listing","content":"- skill-a\n- skill-b\n","skillCount":2}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-skill", md);
        Assert.Contains("skill-a", md);
    }

    [Fact]
    public void McpInstructionsDelta_RoutesToMcpCategory()
    {
        var jsonl = """
{"type":"attachment","attachment":{"type":"mcp_instructions_delta","addedNames":["foo"],"addedBlocks":["## foo\nuse foo to do bar"]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-mcp", md);
        Assert.Contains("use foo to do bar", md);
    }

    [Fact]
    public void DeferredToolsDelta_RoutesToToolsDeltaCategory()
    {
        var jsonl = """
{"type":"attachment","attachment":{"type":"deferred_tools_delta","addedNames":["X","Y"],"removedNames":[]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-toolsdelta", md);
        Assert.Contains("X", md);
        Assert.Contains("Y", md);
    }

    // ─── Other types ────────────────────────────────────────────────────────

    [Fact]
    public void QueueOperation_RoutesToQueueCategory()
    {
        var jsonl = """
{"type":"queue-operation","operation":"enqueue","content":"the user's prompt"}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-queue", md);
        Assert.Contains("enqueue", md);
        Assert.Contains("the user", md);
    }

    [Fact]
    public void UnknownType_RoutesToMetaCategory_NoCrash()
    {
        var jsonl = """
{"type":"some-future-event","payload":{"x":1}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-meta", md);
        Assert.Contains("unknown type", md);
    }

    [Fact]
    public void LastPrompt_RoutesToMetaCategory()
    {
        var jsonl = """
{"type":"last-prompt","lastPrompt":"hi","leafUuid":"x"}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-meta", md);
        Assert.Contains("last-prompt", md);
    }

    // ─── Robustness ─────────────────────────────────────────────────────────

    [Fact]
    public void MalformedLine_IsSkipped_NeighborsParse()
    {
        var jsonl =
            """{"type":"user","message":{"role":"user","content":"first"}}""" + "\n" +
            "this is not json" + "\n" +
            """{"type":"user","message":{"role":"user","content":"third"}}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("first", md);
        Assert.Contains("third", md);
    }

    [Fact]
    public void EmptyInput_StillProducesOuterWrapper()
    {
        var md = TranscriptService.ToMarkdown("");
        Assert.Contains("<div class=\"t-doc\">", md);
        Assert.Contains("</div>", md);
    }

    [Fact]
    public void CrlfLineEndings_AreHandled()
    {
        var jsonl =
            """{"type":"user","message":{"role":"user","content":"win line 1"}}""" + "\r\n" +
            """{"type":"user","message":{"role":"user","content":"win line 2"}}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("win line 1", md);
        Assert.Contains("win line 2", md);
    }

    // ─── Filter header ──────────────────────────────────────────────────────

    [Fact]
    public void FilterHeader_ListsOnlyUsedCategories()
    {
        // Only conversation appears — header should expose conversation
        // checkbox but not, say, hook.
        var jsonl = """
{"type":"user","message":{"role":"user","content":"just a chat"}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("id=\"tf-conversation\"", md);
        Assert.DoesNotContain("id=\"tf-hook\"", md);
        Assert.DoesNotContain("id=\"tf-skill\"", md);
    }

    [Fact]
    public void FilterHeader_PresentForEveryUsedCategory()
    {
        var jsonl =
            """{"type":"user","message":{"role":"user","content":"hi"}}""" + "\n" +
            """{"type":"queue-operation","operation":"enqueue","content":"hi"}""" + "\n" +
            """{"type":"attachment","attachment":{"type":"skill_listing","content":"x"}}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("id=\"tf-conversation\"", md);
        Assert.Contains("id=\"tf-queue\"", md);
        Assert.Contains("id=\"tf-skill\"", md);
    }

    [Fact]
    public void FilterWidget_IsNestedInsideTDoc()
    {
        // Regression: the :has() selectors only fire when the checkbox is a
        // descendant of .t-doc. If the filter widget renders outside .t-doc,
        // every block stays hidden behind the base `display: none` rule.
        var jsonl = """
{"type":"user","message":{"role":"user","content":"hi"}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        var docOpen = md.IndexOf("<div class=\"t-doc\">");
        var filters = md.IndexOf("<div class=\"t-filters\">");
        var docClose = md.LastIndexOf("</div>");
        Assert.True(docOpen >= 0 && filters >= 0 && docClose >= 0);
        Assert.True(docOpen < filters, ".t-filters must come after <div class=\"t-doc\">");
        Assert.True(filters < docClose, ".t-filters must come before the closing </div> of .t-doc");
    }

    [Fact]
    public void DefaultsCorrectlyChecked_OnlyConversationAndTool()
    {
        var jsonl =
            """{"type":"user","message":{"role":"user","content":"hi"}}""" + "\n" +
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"t1","name":"X","input":{}}]}}""" + "\n" +
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"out"}]}}""" + "\n" +
            """{"type":"queue-operation","operation":"enqueue","content":"hi"}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("id=\"tf-conversation\" checked", md);
        Assert.Contains("id=\"tf-tool\" checked", md);
        // queue defaults off — checkbox present but not checked.
        Assert.Contains("id=\"tf-queue\"", md);
        Assert.DoesNotContain("id=\"tf-queue\" checked", md);
    }

    // ─── Session header ─────────────────────────────────────────────────────

    [Fact]
    public void SessionHeader_RendersMetadataFromRecords()
    {
        var jsonl = """
{"type":"user","message":{"role":"user","content":"hi"},"sessionId":"abc-123","timestamp":"2026-05-25T20:58:37.659Z","gitBranch":"master","version":"2.1.149","cwd":"C:\\repo"}
{"type":"assistant","message":{"role":"assistant","model":"claude-opus-4-7","content":[{"type":"text","text":"hello"}]}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("t-session-header", md);
        Assert.Contains("abc-123", md);
        Assert.Contains("master", md);
        Assert.Contains("claude-opus-4-7", md);
        Assert.Contains("2026-05-25", md);
        Assert.Contains("20:58", md);
        Assert.Contains("UTC", md);
        Assert.Contains("2.1.149", md);
        Assert.Contains(@"C:\repo", md);          // single backslash — it's a code span
        Assert.DoesNotContain(@"C:\\repo", md);   // regression: EscapeMd used to double it
    }

    [Fact]
    public void SessionHeader_Omitted_WhenNoMetadata()
    {
        // Record with no recognized metadata fields. The CSS rule for
        // .t-session-header is always emitted in the <style> block; only the
        // actual <div> element should be absent.
        var jsonl = """{"type":"user","message":{"role":"user","content":"hi"}}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.DoesNotContain("<div class=\"t-session-header\">", md);
    }

    [Fact]
    public void SessionHeader_WindowsPath_NotDoubleEscaped()
    {
        // Regression: code-span values must not be run through EscapeMd, which
        // would turn C:\Users\me\Notes into C:\\Users\\me\\Notes inside the span.
        var jsonl = """{"type":"user","message":{"role":"user","content":"hi"},"cwd":"C:\\Users\\me\\Notes"}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains(@"C:\Users\me\Notes", md);
        Assert.DoesNotContain(@"C:\\Users", md);
    }

    [Fact]
    public void SessionHeader_BacktickValue_GrowsFence_PreservesContent()
    {
        // A value containing a backtick keeps it literal; the fence grows so the
        // backtick can't break out of the code span, and it isn't backslash-escaped.
        var jsonl = """{"type":"user","message":{"role":"user","content":"hi"},"gitBranch":"feat/`weird`"}""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("feat/`weird`", md);
        Assert.DoesNotContain("\\`", md);
    }

    // ─── Outline headings ───────────────────────────────────────────────────

    [Fact]
    public void Conversation_EmitsH3Heading_WithFirstLinePreview()
    {
        var jsonl = """
{"type":"user","message":{"role":"user","content":"first line of question\nsecond line ignored"}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("### 🧑 User: first line of question", md);
        Assert.DoesNotContain("### 🧑 User: first line of question\nsecond", md);
    }

    [Fact]
    public void Conversation_HeadingPreview_TruncatesLong()
    {
        var longText = new string('a', 200);
        var jsonl = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"" + longText + "\"}}";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("…", md);
    }

    [Fact]
    public void Conversation_HeadingPreview_StripsLeadingMarkers()
    {
        var jsonl = """
{"type":"user","message":{"role":"user","content":"# what about this?"}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        // Leading '#' stripped so it doesn't form a nested H1.
        Assert.Contains("### 🧑 User: what about this?", md);
    }

    [Fact]
    public void Conversation_EmptyMessage_StillEmitsHeading()
    {
        var jsonl = """
{"type":"user","message":{"role":"user","content":""}}
""";
        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("### 🧑 User", md);
    }

    [Fact]
    public void VisibleCategoriesOverride_HonorsSavedState()
    {
        // Saved state inverts the defaults: conversation off, queue on.
        var jsonl =
            """{"type":"user","message":{"role":"user","content":"hi"}}""" + "\n" +
            """{"type":"queue-operation","operation":"enqueue","content":"hi"}""";
        var saved = new Dictionary<string, bool>
        {
            ["conversation"] = false,
            ["queue"]        = true,
        };
        var md = TranscriptService.ToMarkdown(jsonl, saved);

        Assert.Contains("id=\"tf-conversation\"", md);
        Assert.DoesNotContain("id=\"tf-conversation\" checked", md);
        Assert.Contains("id=\"tf-queue\" checked", md);
    }

    [Fact]
    public void VisibleCategoriesOverride_MissingKeyFallsBackToDefault()
    {
        // Empty saved state → every category renders its static default.
        var jsonl =
            """{"type":"user","message":{"role":"user","content":"hi"}}""" + "\n" +
            """{"type":"queue-operation","operation":"enqueue","content":"hi"}""";
        var md = TranscriptService.ToMarkdown(jsonl, new Dictionary<string, bool>());

        Assert.Contains("id=\"tf-conversation\" checked", md);
        Assert.Contains("id=\"tf-queue\"", md);
        Assert.DoesNotContain("id=\"tf-queue\" checked", md);
    }

    // ─── Sanity: malformed nested content doesn't blow up parser ────────────

    [Fact]
    public void HtmlInUserContent_DoesNotCrash()
    {
        // Build the line with a serializer so braces and quotes are escaped correctly.
        var encoded = JsonSerializer.Serialize("contains </div> and <script>alert(1)</script>");
        var jsonl = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":" + encoded + "}}";

        var md = TranscriptService.ToMarkdown(jsonl);
        Assert.Contains("alert(1)", md);
    }
}
