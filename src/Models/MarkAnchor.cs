namespace MarkdownViewer.Models;

/// <summary>
/// Where a place marker sits in a document, described so it survives the file
/// being edited and re-rendered: the block's index among the rendered page's
/// top-level blocks, the first 60 chars of its normalized text, and the id of
/// the nearest preceding heading. bridge.js re-resolves this on every render -
/// index (only if the text still matches there), then a scan for the text,
/// then the heading as a coarse fallback, then the mark is dropped (a scaled-
/// down W3C Web Annotation quote + position selector). Marks inside a code
/// block (a fenced block, or the text viewer's whole-file block) additionally
/// address a single line: LineIndex/LineText repeat the position + quote
/// scheme within the block, and are null for ordinary blocks. Process-lifetime
/// only, keyed by file path in MainWindow._marks; deliberately not persisted.
/// </summary>
public sealed record MarkAnchor(int BlockIndex, string TextPrefix, string? HeadingId,
    int? LineIndex = null, string? LineText = null);
