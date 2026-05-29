// markdown.jsx — tiny markdown parser + renderer with JS syntax highlighting.
// Exposes: <Markdown source theme />, parseHeadings(source) -> [{level, text, id}]

function mdSlug(s) {
  return String(s).toLowerCase().replace(/[^\w\s-]/g, "").trim().replace(/\s+/g, "-");
}

// Inline: **bold**, *em*, `code`, [text](url). Returns array of React nodes.
function mdInline(text, keyBase) {
  const out = [];
  let i = 0;
  let key = 0;
  const push = (node) => out.push(typeof node === "string" ? node : React.cloneElement(node, { key: keyBase + ":" + (key++) }));

  while (i < text.length) {
    const rest = text.slice(i);
    let m;
    // inline code
    if ((m = /^`([^`]+)`/.exec(rest))) {
      push(<code className="md-icode">{m[1]}</code>);
      i += m[0].length;
      continue;
    }
    // bold
    if ((m = /^\*\*([^*]+)\*\*/.exec(rest))) {
      push(<strong>{m[1]}</strong>);
      i += m[0].length;
      continue;
    }
    // em
    if ((m = /^\*([^*]+)\*/.exec(rest))) {
      push(<em>{m[1]}</em>);
      i += m[0].length;
      continue;
    }
    // link
    if ((m = /^\[([^\]]+)\]\(([^)]+)\)/.exec(rest))) {
      push(<a href={m[2]} className="md-link">{m[1]}</a>);
      i += m[0].length;
      continue;
    }
    // plain char run until next special
    const nextSpecial = rest.search(/[`*\[]/);
    if (nextSpecial === -1) { push(rest); break; }
    if (nextSpecial === 0) { push(rest[0]); i += 1; continue; }
    push(rest.slice(0, nextSpecial));
    i += nextSpecial;
  }
  return out;
}

// JS-ish syntax highlighter. Token classes: kw, str, num, com, fn, punct.
const JS_KW = new Set([
  "function", "const", "let", "var", "if", "else", "return", "for", "while",
  "do", "switch", "case", "break", "continue", "new", "class", "extends",
  "import", "export", "from", "default", "true", "false", "null", "undefined",
  "this", "typeof", "in", "of", "async", "await", "try", "catch", "finally",
  "throw",
]);
function highlightJS(src) {
  const out = [];
  let i = 0;
  let key = 0;
  const push = (cls, text) => {
    out.push(cls
      ? <span key={key++} className={"hl-" + cls}>{text}</span>
      : <React.Fragment key={key++}>{text}</React.Fragment>);
  };
  while (i < src.length) {
    const ch = src[i];
    // line comment
    if (ch === "/" && src[i + 1] === "/") {
      const e = src.indexOf("\n", i); const end = e === -1 ? src.length : e;
      push("com", src.slice(i, end)); i = end; continue;
    }
    // block comment
    if (ch === "/" && src[i + 1] === "*") {
      const e = src.indexOf("*/", i + 2); const end = e === -1 ? src.length : e + 2;
      push("com", src.slice(i, end)); i = end; continue;
    }
    // string
    if (ch === "\"" || ch === "'" || ch === "`") {
      let j = i + 1;
      while (j < src.length && src[j] !== ch) {
        if (src[j] === "\\") j += 2; else j += 1;
      }
      push("str", src.slice(i, j + 1)); i = j + 1; continue;
    }
    // number
    if (/[0-9]/.test(ch)) {
      let j = i; while (j < src.length && /[0-9.]/.test(src[j])) j += 1;
      push("num", src.slice(i, j)); i = j; continue;
    }
    // ident / keyword
    if (/[A-Za-z_$]/.test(ch)) {
      let j = i; while (j < src.length && /[A-Za-z0-9_$]/.test(src[j])) j += 1;
      const word = src.slice(i, j);
      // call: ident followed by (
      const isCall = src[j] === "(";
      const cls = JS_KW.has(word) ? "kw" : (isCall ? "fn" : null);
      push(cls, word); i = j; continue;
    }
    // punctuation cluster
    if (/[{}()\[\];,.<>=+\-*/%!&|?:]/.test(ch)) {
      let j = i; while (j < src.length && /[{}()\[\];,.<>=+\-*/%!&|?:]/.test(src[j])) j += 1;
      push("punct", src.slice(i, j)); i = j; continue;
    }
    push(null, ch); i += 1;
  }
  return out;
}

// Block-level parser. Returns an array of block objects, each tagged with the
// 1-indexed source line where the block STARTED. Line numbers are sparse on
// purpose: multi-line blocks (paragraphs, code fences, tables) collapse so
// downstream consumers can render an IDE-style gutter that mirrors the source.
function mdParse(source) {
  const lines = source.split("\n");
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const startLine = i + 1; // 1-indexed
    const line = lines[i];
    // fenced code
    if (/^```/.test(line)) {
      const lang = line.slice(3).trim();
      const body = [];
      i += 1;
      while (i < lines.length && !/^```/.test(lines[i])) {
        body.push(lines[i]);
        i += 1;
      }
      i += 1; // closing fence
      blocks.push({ type: "code", lang, body: body.join("\n"), srcLine: startLine });
      continue;
    }
    // heading
    let m;
    if ((m = /^(#{1,6})\s+(.*)$/.exec(line))) {
      blocks.push({ type: "heading", level: m[1].length, text: m[2], id: mdSlug(m[2]), srcLine: startLine });
      i += 1;
      continue;
    }
    // table (pipe-delimited, with a separator row)
    if (/^\s*\|/.test(line) && /^\s*\|?\s*-/.test(lines[i + 1] || "")) {
      const parseRow = (l) => l.replace(/^\s*\|/, "").replace(/\|\s*$/, "").split("|").map((c) => c.trim());
      const head = parseRow(line);
      i += 2;
      const rows = [];
      while (i < lines.length && /^\s*\|/.test(lines[i])) {
        rows.push(parseRow(lines[i]));
        i += 1;
      }
      blocks.push({ type: "table", head, rows, srcLine: startLine });
      continue;
    }
    // unordered list
    if (/^\s*[-*]\s+/.test(line)) {
      const items = [];
      while (i < lines.length && /^\s*[-*]\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\s*[-*]\s+/, ""));
        i += 1;
      }
      blocks.push({ type: "ul", items, srcLine: startLine });
      continue;
    }
    // ordered list
    if (/^\s*\d+\.\s+/.test(line)) {
      const items = [];
      while (i < lines.length && /^\s*\d+\.\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\s*\d+\.\s+/, ""));
        i += 1;
      }
      blocks.push({ type: "ol", items, srcLine: startLine });
      continue;
    }
    // blank line
    if (line.trim() === "") {
      i += 1;
      continue;
    }
    // paragraph: collect contiguous non-blank, non-special lines
    const para = [line];
    i += 1;
    while (
      i < lines.length &&
      lines[i].trim() !== "" &&
      !/^```/.test(lines[i]) &&
      !/^#{1,6}\s+/.test(lines[i]) &&
      !/^\s*\|/.test(lines[i]) &&
      !/^\s*[-*]\s+/.test(lines[i]) &&
      !/^\s*\d+\.\s+/.test(lines[i])
    ) {
      para.push(lines[i]);
      i += 1;
    }
    blocks.push({ type: "p", text: para.join(" "), srcLine: startLine });
  }
  return blocks;
}

function parseHeadings(source) {
  return mdParse(source)
    .filter((b) => b.type === "heading")
    .map((b) => ({ level: b.level, text: b.text, id: b.id }));
}

function Markdown({ source, showLineNumbers }) {
  const blocks = React.useMemo(() => mdParse(source), [source]);
  const renderBlock = (b, k) => {
    if (b.type === "heading") {
      const Tag = "h" + b.level;
      return <Tag id={b.id} className={"md-h md-h" + b.level}>{mdInline(b.text, k)}</Tag>;
    }
    if (b.type === "p") return <p className="md-p">{mdInline(b.text, k)}</p>;
    if (b.type === "code") {
      const tokens = (b.lang === "js" || b.lang === "javascript" || b.lang === "jsx" || b.lang === "ts")
        ? highlightJS(b.body)
        : [b.body];
      return (
        <div className="md-codewrap">
          {b.lang && <div className="md-codelang">{b.lang}</div>}
          <pre className="md-code"><code>{tokens}</code></pre>
        </div>
      );
    }
    if (b.type === "table") {
      return (
        <div className="md-tablewrap">
          <table className="md-table">
            <thead><tr>{b.head.map((h, j) => <th key={j}>{mdInline(h, k + "h" + j)}</th>)}</tr></thead>
            <tbody>
              {b.rows.map((row, r) => (
                <tr key={r}>{row.map((c, j) => <td key={j}>{mdInline(c, k + "r" + r + "c" + j)}</td>)}</tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    }
    if (b.type === "ul") return <ul className="md-list">{b.items.map((it, j) => <li key={j}>{mdInline(it, k + "i" + j)}</li>)}</ul>;
    if (b.type === "ol") return <ol className="md-list">{b.items.map((it, j) => <li key={j}>{mdInline(it, k + "i" + j)}</li>)}</ol>;
    return null;
  };
  return (
    <div className={"md-root" + (showLineNumbers ? " md-root-lineno" : "")}>
      {blocks.map((b, idx) => {
        const k = "b" + idx;
        return (
          <div key={k} className={"md-block md-block-" + b.type} data-line={b.srcLine}>
            {renderBlock(b, k)}
          </div>
        );
      })}
    </div>
  );
}

Object.assign(window, { Markdown, mdParse, parseHeadings, mdSlug });
