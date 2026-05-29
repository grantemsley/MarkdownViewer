---
title: "Notes with frontmatter"
author: Grant
tags: [sample, frontmatter]
date: 2026-05-26
status: draft
---

# Notes with frontmatter

The YAML block above gets parsed by Markdig's frontmatter extension and
suppressed from the output — you should not see it in the rendered page.

## What this exercises

- Frontmatter strip (no leakage into the body)
- Long-form prose paragraph flow
- Multi-paragraph blockquotes
- Mixed nested lists with code spans

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
