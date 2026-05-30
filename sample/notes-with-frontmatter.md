---
title: "Notes with frontmatter"
author: Grant
tags: [sample, frontmatter]
date: 2026-05-26
status: draft
---

# Notes with frontmatter

The YAML block above is surfaced in a collapsed **Frontmatter** disclosure at
the top of the page — small and out of the way, but available on demand
instead of dropped entirely.

## What this exercises

- Frontmatter surfaced in a collapsed `<details>` (no leakage into the body)
- Custom-tag outlining (the `<example>` block below)
- Long-form prose paragraph flow
- Multi-paragraph blockquotes
- Mixed nested lists with code spans

## A custom tag

With "Highlight custom tags" on, non-standard tags are shown verbatim as small
chips so you can see the wrapper the author intended, not just its contents.
The inline case reads naturally — <example>Hello world</example> — with the
opening and closing tags rendered as chips around the text.

A block example, with the tags on their own lines:

<example>

This whole region was wrapped in an `<example>` tag. With the toggle off, the
tags are dropped and this content reads cleanly.

</example>

## Some prose

The renderer's job is small but particular. The biggest behaviours it has
to get right are the ones a reader will notice when they're missing:
proper paragraph spacing, comfortable line-length, code blocks that don't
overflow, and tables that don't break under wide content. Anything else
is polish.

> "The first 95% of any markdown renderer is easy. The remaining 95%
> takes the rest of the project."
>
> — somebody, probably

## A mixed list

1. Top-level item with **emphasis** and `inline code`.
2. Another top-level item.
   - Sub-item with a [link](https://example.com).
   - Sub-item with `code`.
     1. Triple-nested numbered.
     2. With another below it.
   - Back to bullets.
3. Final top-level item.

## A wide table

| Field        | Type     | Required | Notes |
|--------------|----------|:--------:|-------|
| `id`         | string   | ✅ | UUID v4 |
| `name`       | string   | ✅ | Display name; trimmed |
| `email`      | string   | ✅ | RFC 5321 |
| `created_at` | datetime | ✅ | ISO 8601 UTC |
| `notes`      | text     |    | Free-form, up to 4 KB |

## A code block with a long line

```javascript
const result = await fetch("https://example.com/api/v2/very/long/path/that/keeps/going/and/going").then(r => r.json()).then(data => data.results.filter(r => r.active));
```

The horizontal scroll on this block tells you whether overflow is wired
up correctly.

## End

If nothing above looks wrong, this transcript-adjacent format reads fine.
