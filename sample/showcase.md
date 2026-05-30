# MarkdownViewer

A fast, no-friction reader for folders of Markdown — and for Claude
Code transcripts. Click a file, read it.

```mermaid
flowchart LR
    A[Open a folder] --> B{File type?}
    B -- .md --> C[Markdown render]
    B -- .jsonl --> D[Transcript view]
    B -- other --> E[Auto-picked viewer]
```

## Code gets syntax highlighting

```csharp
public static class MarkdownService
{
    public static string Render(string source) =>
        Markdown.ToHtml(source, _pipeline);
}
```

## Tables and task lists, the usual

| Feature | Works? |
|---|:---:|
| GitHub-flavored Markdown | ✅ |
| Mermaid diagrams | ✅ |
| Syntax highlighting | ✅ |
| JSONL transcript viewer | ✅ |

- [x] Open a folder
- [x] Read a file
- [ ] Think about the code underneath (please don't)
