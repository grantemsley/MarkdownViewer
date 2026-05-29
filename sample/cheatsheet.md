# Markdown cheatsheet

A quick reference for everyday GFM syntax. Open this alongside `welcome.md`
to compare how the same primitives render.

## Headings

`# H1`, `## H2`, `### H3`, etc. through `######`.

## Emphasis

| Syntax | Result |
|---|---|
| `*italic*` or `_italic_` | *italic* |
| `**bold**` | **bold** |
| `***both***` | ***both*** |
| `~~strike~~` | ~~strike~~ |
| `` `code` `` | `code` |

## Links and images

- `[label](https://example.com)` → [label](https://example.com)
- `[ref][1]` with `[1]: https://example.com` somewhere below.
- `![alt](image.png)` for an image (resolves relative to the file).
- Auto-links: `<https://example.com>` → <https://example.com>.

## Lists

```
- one
- two
  - nested
  - also nested
- three

1. ordered
2. items
```

- one
- two
  - nested
  - also nested
- three

1. ordered
2. items

## Task lists

```
- [x] done
- [ ] open
```

- [x] done
- [ ] open

## Code blocks

Inline `code` between backticks. Fenced with three backticks and an
optional language tag for syntax highlighting:

````
```rust
fn add(a: i32, b: i32) -> i32 { a + b }
```
````

```rust
fn add(a: i32, b: i32) -> i32 { a + b }
```

## Tables

```
| left | center | right |
|:-----|:------:|------:|
| a    | b      | c     |
```

| left | center | right |
|:-----|:------:|------:|
| a    | b      | c     |
| longer cell | x | 42 |

## Blockquotes

```
> quote
>
> > nested
```

> quote
>
> > nested

## Horizontal rule

Three or more dashes, asterisks, or underscores on their own line:

---

## Footnotes

A reference[^note] points to the matching footnote at the bottom.

[^note]: Markdig handles footnotes; the link wraps both ways.

## Definition lists

```
Term
:   Definition
:   Alternative definition
```

Term
:   Definition
:   Alternative definition

## Abbreviations

`*[HTML]: HyperText Markup Language` makes every later `HTML` an
abbreviation with a tooltip.

*[HTML]: HyperText Markup Language

This sentence mentions HTML — hover for the expansion.
