// Place-marker behaviour: gutter click -> markSet/markCleared, and the
// anchor resolution order on each render (index if the text still matches,
// then text scan, then nearest-heading fallback, then drop). jsdom has no
// layout, so tests stub getBoundingClientRect on #page and its blocks to
// give the gutter hit-test real geometry.

import { test, expect } from "vitest";
import { boot, settle } from "./harness.js";

const HTML =
  '<h2 id="intro">Intro</h2>' +
  "<p>First paragraph of prose.</p>" +
  "<p>Second paragraph, quite distinctive text.</p>" +
  '<h2 id="details">Details</h2>' +
  "<p>Third paragraph under details.</p>";

function mdDoc(over = {}) {
  return {
    type: "setDoc", kind: "markdown", tabId: "t1",
    path: "C:\\docs\\a.md", basePath: "C:\\docs",
    html: HTML, scrollTop: 0, reloaded: false, modified: "", ...over,
  };
}

// Stub geometry: #page content starts at x=200; top-level block i occupies
// y in [i*100, (i+1)*100). Must re-run after every setDoc (fresh elements).
function layout(h) {
  const page = h.document.getElementById("page");
  page.getBoundingClientRect = () =>
    ({ left: 200, right: 800, top: 0, bottom: 10000 });
  const root = page.querySelector("article.markdown-body") || page;
  Array.from(root.children).forEach((el, i) => {
    el.getBoundingClientRect = () =>
      ({ top: i * 100, bottom: (i + 1) * 100, left: 200, right: 800 });
  });
  return root;
}

function click(h, { x, y }) {
  h.document.getElementById("scroll").dispatchEvent(
    new h.window.MouseEvent("click",
      { bubbles: true, cancelable: true, clientX: x, clientY: y }));
}

const gutterClick = (h, y) => click(h, { x: 40, y }); // left of page's box
const textClick = (h, y) => click(h, { x: 400, y }); // inside the text column

const marked = (h) => [...h.document.querySelectorAll("#page .md-mark")];
const markMsgs = (h) =>
  h.sent.filter((m) => m.type === "markSet" || m.type === "markCleared");

// ─── Gutter clicks ───────────────────────────────────────────────────────

test("a gutter click posts markSet with blockIndex, textPrefix, and headingId", () => {
  const h = boot();
  h.send(mdDoc());
  layout(h);
  gutterClick(h, 250); // block 2: the "Second paragraph" <p>
  expect(markMsgs(h)).toEqual([{
    type: "markSet", tabId: "t1", path: "C:\\docs\\a.md",
    blockIndex: 2, textPrefix: "Second paragraph, quite distinctive text.",
    headingId: "intro",
  }]);
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["Second paragraph, quite distinctive text."]);
});

test("headingId is the nearest heading above, not the first in the doc", () => {
  const h = boot();
  h.send(mdDoc());
  layout(h);
  gutterClick(h, 450); // block 4, below the "details" heading
  expect(markMsgs(h)[0]).toMatchObject({ blockIndex: 4, headingId: "details" });
});

test("a gutter click on the already-marked block posts markCleared", () => {
  const h = boot();
  h.send(mdDoc());
  layout(h);
  gutterClick(h, 250);
  gutterClick(h, 250);
  expect(markMsgs(h)).toEqual([
    expect.objectContaining({ type: "markSet", blockIndex: 2 }),
    { type: "markCleared", tabId: "t1", path: "C:\\docs\\a.md" },
  ]);
  expect(marked(h)).toEqual([]);
});

test("a gutter click on a different block moves the single mark", () => {
  const h = boot();
  h.send(mdDoc());
  layout(h);
  gutterClick(h, 250); // mark block 2
  gutterClick(h, 450); // move to block 4
  expect(markMsgs(h).map((m) => m.type)).toEqual(["markSet", "markSet"]);
  expect(markMsgs(h)[1].blockIndex).toBe(4);
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["Third paragraph under details."]);
});

test("a click inside the text column posts neither", () => {
  const h = boot();
  h.send(mdDoc());
  layout(h);
  textClick(h, 250);
  expect(markMsgs(h)).toEqual([]);
  expect(marked(h)).toEqual([]);
});

// ─── Anchor resolution on render ─────────────────────────────────────────

test("setDoc carrying a mark whose index and text agree marks that block", () => {
  const h = boot();
  h.send(mdDoc({ mark: {
    blockIndex: 2, textPrefix: "Second paragraph, quite distinctive text.",
    headingId: "intro",
  } }));
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["Second paragraph, quite distinctive text."]);
});

test("an edit above the mark: the text wins over the stale index", () => {
  // The case the whole design exists for. A paragraph was inserted above,
  // so the marked text now lives at index 3; index 2 holds different text.
  const h = boot();
  const edited =
    '<h2 id="intro">Intro</h2>' +
    "<p>A brand new paragraph inserted by an edit.</p>" +
    "<p>First paragraph of prose.</p>" +
    "<p>Second paragraph, quite distinctive text.</p>" +
    '<h2 id="details">Details</h2>';
  h.send(mdDoc({ html: edited, mark: {
    blockIndex: 2, textPrefix: "Second paragraph, quite distinctive text.",
    headingId: "intro",
  } }));
  const hits = marked(h);
  expect(hits.map((el) => el.textContent)).toEqual(
    ["Second paragraph, quite distinctive text."]);
  const root = h.document.getElementById("page");
  expect(hits[0]).toBe(root.children[3]);
  expect(root.children[2].classList.contains("md-mark")).toBe(false);
});

test("marked text gone entirely: falls back to the heading", () => {
  const h = boot();
  h.send(mdDoc({ mark: {
    blockIndex: 2, textPrefix: "This text no longer exists anywhere in the doc.",
    headingId: "details",
  } }));
  const hits = marked(h);
  expect(hits).toHaveLength(1);
  expect(hits[0].id).toBe("details");
});

test("text and heading both gone: the mark is dropped, nothing is marked", () => {
  const h = boot();
  h.send(mdDoc({ mark: {
    blockIndex: 2, textPrefix: "This text no longer exists anywhere in the doc.",
    headingId: "h-gone",
  } }));
  expect(marked(h)).toEqual([]);
});

test("a mark with no heading fallback resolves by text alone", () => {
  const h = boot();
  h.send(mdDoc({ mark: {
    blockIndex: 1, textPrefix: "First paragraph of prose.", headingId: null,
  } }));
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["First paragraph of prose."]);
});

test("the mark survives a reloaded setDoc (host resends it with the doc)", () => {
  const h = boot();
  const mark = {
    blockIndex: 2, textPrefix: "Second paragraph, quite distinctive text.",
    headingId: "intro",
  };
  h.send(mdDoc({ mark }));
  h.send(mdDoc({ mark, reloaded: true }));
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["Second paragraph, quite distinctive text."]);
});

test("github body style: gutter click and mark land on the article's blocks", () => {
  const h = boot();
  h.send({
    type: "setPrefs", theme: "light", accent: "#0078d4", typeface: "system",
    fontSize: 14, marginPct: 85, showLineNumbers: false, bodyStyle: "github",
  });
  h.send(mdDoc());
  layout(h); // stubs the article's children
  gutterClick(h, 250);
  expect(markMsgs(h)[0]).toMatchObject({ type: "markSet", blockIndex: 2 });
  const article = h.document.querySelector("#page > article.markdown-body");
  expect(article.children[2].classList.contains("md-mark")).toBe(true);
});

test("text kind: the whole file is one block, and the click addresses a line", () => {
  const h = boot();
  h.send({
    type: "setDoc", kind: "text", tabId: "t1", path: "C:\\docs\\notes.txt",
    lang: "", body: "line one\nline two", scrollTop: 0, reloaded: false,
    modified: "",
  });
  layout(h);
  const code = h.document.querySelector("#page pre code");
  code.getBoundingClientRect = () =>
    ({ top: 0, bottom: 100, left: 200, right: 800 });
  h.observers[0].cb(); // reflow: drop any line boxes measured pre-stub
  gutterClick(h, 25); // top half: line 0 of the even split
  expect(markMsgs(h)).toEqual([{
    type: "markSet", tabId: "t1", path: "C:\\docs\\notes.txt",
    blockIndex: 0, textPrefix: "line one line two", headingId: null,
    lineIndex: 0, lineText: "line one",
  }]);
});

// ─── List-item granularity ───────────────────────────────────────────────
// Lists explode into their <li>s as markable units (nested ones too), so a
// single step in step-by-step instructions carries its own mark instead of
// the whole list lighting up.

const LIST_HTML =
  '<h2 id="steps">Steps</h2>' +
  "<p>Intro paragraph.</p>" +
  "<ol>" +
  "<li>Download the installer.</li>" +
  "<li>Run it as admin.<ul>" +
  "<li>Accept the UAC prompt.</li>" +
  "<li>Pick the default path.</li>" +
  "</ul></li>" +
  "<li>Reboot.</li>" +
  "</ol>" +
  "<p>Closing notes.</p>";

// Flattened markable units for LIST_HTML:
//   0 h2#steps · 1 p intro · 2 li download · 3 li run-as-admin (spans its
//   nested list) · 4 li accept-UAC · 5 li default-path · 6 li reboot ·
//   7 p closing
// Geometry: h2 0-100, p 100-200, ol 200-700 with li download 200-300,
// li run-as-admin 300-600 (own line 300-400, nested 400-600: accept
// 400-500, path 500-600), li reboot 600-700, p closing 700-800.
function listLayout(h) {
  const page = h.document.getElementById("page");
  page.getBoundingClientRect = () =>
    ({ left: 200, right: 800, top: 0, bottom: 10000 });
  const rect = (el, top, bottom) => {
    el.getBoundingClientRect = () => ({ top, bottom, left: 200, right: 800 });
  };
  const [h2, p1, ol, p2] = page.children;
  rect(h2, 0, 100);
  rect(p1, 100, 200);
  rect(ol, 200, 700);
  const [liDownload, liAdmin, liReboot] = ol.children;
  rect(liDownload, 200, 300);
  rect(liAdmin, 300, 600);
  const [liUac, liPath] = liAdmin.querySelector("ul").children;
  rect(liUac, 400, 500);
  rect(liPath, 500, 600);
  rect(liReboot, 600, 700);
  rect(p2, 700, 800);
}

test("a gutter click beside one list item marks just that item", () => {
  const h = boot();
  h.send(mdDoc({ html: LIST_HTML }));
  listLayout(h);
  gutterClick(h, 250); // beside "Download the installer."
  expect(markMsgs(h)).toEqual([{
    type: "markSet", tabId: "t1", path: "C:\\docs\\a.md",
    blockIndex: 2, textPrefix: "Download the installer.", headingId: "steps",
  }]);
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["Download the installer."]);
});

test("a click beside a nested sub-step marks the deepest item, not its parent", () => {
  const h = boot();
  h.send(mdDoc({ html: LIST_HTML }));
  listLayout(h);
  gutterClick(h, 450); // inside li run-as-admin's span, on the UAC sub-step
  expect(markMsgs(h)[0]).toMatchObject({
    blockIndex: 4, textPrefix: "Accept the UAC prompt.", headingId: "steps",
  });
  const hits = marked(h);
  expect(hits).toHaveLength(1);
  expect(hits[0].textContent).toBe("Accept the UAC prompt.");
});

test("a click beside a parent item's own line marks the parent item", () => {
  const h = boot();
  h.send(mdDoc({ html: LIST_HTML }));
  listLayout(h);
  gutterClick(h, 350); // run-as-admin's own text line, above its sub-list
  expect(markMsgs(h)[0]).toMatchObject({ blockIndex: 3 });
  expect(marked(h)[0].textContent.startsWith("Run it as admin.")).toBe(true);
});

test("re-click on the marked list item clears it", () => {
  const h = boot();
  h.send(mdDoc({ html: LIST_HTML }));
  listLayout(h);
  gutterClick(h, 250);
  gutterClick(h, 250);
  expect(markMsgs(h).map((m) => m.type)).toEqual(["markSet", "markCleared"]);
  expect(marked(h)).toEqual([]);
});

test("setDoc carrying a list-item mark applies it to that item", () => {
  const h = boot();
  h.send(mdDoc({ html: LIST_HTML, mark: {
    blockIndex: 5, textPrefix: "Pick the default path.", headingId: "steps",
  } }));
  expect(marked(h).map((el) => el.textContent)).toEqual(
    ["Pick the default path."]);
});

test("an edit above the list: the item's text wins over its stale index", () => {
  const h = boot();
  const edited = "<p>New paragraph pushed everything down.</p>" + LIST_HTML;
  h.send(mdDoc({ html: edited, mark: {
    blockIndex: 4, textPrefix: "Accept the UAC prompt.", headingId: "steps",
  } }));
  const hits = marked(h);
  expect(hits).toHaveLength(1);
  expect(hits[0].textContent).toBe("Accept the UAC prompt.");
  expect(hits[0].tagName).toBe("LI");
});

test("a marked list item carries its indent so the bar sits in the page margin", () => {
  // The bar is drawn at left: calc(-12px - --mark-indent). For an indented
  // <li> the indent is its box left minus the page content-box left, so
  // every bar lands at the same gutter x as a top-level paragraph's.
  const h = boot();
  h.send(mdDoc({ html: LIST_HTML }));
  listLayout(h);
  // Give the download li a real indent: page content starts at 200, the li
  // box starts at 256 (list padding).
  const ol = h.document.getElementById("page").children[2];
  const liDownload = ol.children[0];
  liDownload.getBoundingClientRect = () =>
    ({ top: 200, bottom: 300, left: 256, right: 800 });
  gutterClick(h, 250);
  expect(marked(h)[0].style.getPropertyValue("--mark-indent")).toBe("56px");
});

test("a marked top-level paragraph carries no indent", () => {
  const h = boot();
  h.send(mdDoc());
  layout(h); // every block's left equals the page's left: indent 0
  gutterClick(h, 150); // the first <p>
  expect(marked(h)[0].style.getPropertyValue("--mark-indent")).toBe("0px");
});

// ─── scrollToMark (Ctrl+G jump) ──────────────────────────────────────────

const MARK2 = {
  blockIndex: 2, textPrefix: "Second paragraph, quite distinctive text.",
  headingId: "intro",
};

test("scrollToMark scrolls to the marked block and cancels the growth watch", async () => {
  const h = boot();
  // Clamp #scroll so the setDoc restore leaves a growth watch running -
  // the case where a mermaid diagram below is still growing the doc.
  const scrollEl = h.document.getElementById("scroll");
  let top = 0;
  Object.defineProperty(scrollEl, "scrollTop", {
    configurable: true,
    get: () => top,
    set: (x) => { top = Math.min(x, 100); },
  });
  h.send(mdDoc({ scrollTop: 500, mark: MARK2 }));
  await settle();
  const watch = h.observers.at(-1);
  expect(watch.disconnected).toBe(false);

  h.send({ type: "scrollToMark", tabId: "t1" });
  expect(watch.disconnected).toBe(true); // else the watch yanks the view back
  expect(h.scrolledIntoView).toHaveLength(1);
  expect(h.scrolledIntoView[0].el.textContent).toBe(
    "Second paragraph, quite distinctive text.");
  expect(h.scrolledIntoView[0].opts).toEqual({ behavior: "smooth", block: "start" });
});

test("scrollToMark with a stale tabId is dropped", () => {
  const h = boot();
  h.send(mdDoc({ mark: MARK2 }));
  h.send({ type: "scrollToMark", tabId: "t9" });
  expect(h.scrolledIntoView).toEqual([]);
});

test("scrollToMark with no mark applied does nothing", () => {
  const h = boot();
  h.send(mdDoc());
  h.send({ type: "scrollToMark", tabId: "t1" });
  expect(h.scrolledIntoView).toEqual([]);
});

// ─── Code-block line marks ───────────────────────────────────────────────
// A code block is one markable unit but line-addressed: the anchor carries
// lineIndex + lineText, and the bar is an overlay in #scroll (pre's
// overflow-x clips the ::after the other blocks use) with no tint on the
// code. jsdom measures no Range rects, so line boxes come from the even-
// split fallback over the stubbed code rect.

const CODE_HTML =
  '<h2 id="setup">Setup</h2>' +
  "<p>Some prose before.</p>" +
  "<pre><code>first line\nsecond line\nthird line</code></pre>" +
  "<p>After the code.</p>";

// h2 0-100, p 100-200, pre 200-320 (code 210-300: three 30px lines at
// 210-240 / 240-270 / 270-300), p 320-400.
function codeLayout(h, codeRect = { top: 210, bottom: 300 }) {
  const page = h.document.getElementById("page");
  page.getBoundingClientRect = () =>
    ({ left: 200, right: 800, top: 0, bottom: 10000 });
  const tops = [[0, 100], [100, 200], [200, 320], [320, 400]];
  Array.from(page.children).forEach((el, i) => {
    const [top, bottom] = tops[i];
    el.getBoundingClientRect = () => ({ top, bottom, left: 200, right: 800 });
  });
  const code = page.querySelector("pre code");
  code.getBoundingClientRect = () =>
    ({ ...codeRect, left: 210, right: 790 });
  h.observers[0].cb(); // reflow: drop line boxes measured before the stubs
}

const bar = (h) => h.document.querySelector("#scroll > .code-mark-bar:not(.ghost)");
const ghost = (h) => h.document.querySelector("#scroll > .code-mark-bar.ghost");

test("a gutter click beside a code line posts a line-addressed markSet", () => {
  const h = boot();
  h.send(mdDoc({ html: CODE_HTML }));
  codeLayout(h);
  gutterClick(h, 245); // second line's band
  expect(markMsgs(h)).toEqual([{
    type: "markSet", tabId: "t1", path: "C:\\docs\\a.md",
    blockIndex: 2, textPrefix: "first line second line third line",
    headingId: "setup", lineIndex: 1, lineText: "second line",
  }]);
  // Bar only: the pre carries no .md-mark (no tint), the overlay does the work.
  expect(marked(h)).toEqual([]);
  expect(bar(h).hidden).toBe(false);
  expect(bar(h).style.top).toBe("242px");    // line box 240-270, 2px inset
  expect(bar(h).style.height).toBe("26px");
  expect(bar(h).style.left).toBe("188px");   // gutterEdge 200 - 12
});

test("re-clicking the marked line clears; another line moves the mark", () => {
  const h = boot();
  h.send(mdDoc({ html: CODE_HTML }));
  codeLayout(h);
  gutterClick(h, 245); // mark line 1
  gutterClick(h, 275); // move to line 2
  gutterClick(h, 280); // same line 2: clear
  expect(markMsgs(h).map((m) => m.type)).toEqual(
    ["markSet", "markSet", "markCleared"]);
  expect(markMsgs(h)[1]).toMatchObject({ lineIndex: 2, lineText: "third line" });
  expect(bar(h).hidden).toBe(true);
});

test("setDoc carrying a line mark applies it to that line", () => {
  const h = boot();
  h.send(mdDoc({ html: CODE_HTML, mark: {
    blockIndex: 2, textPrefix: "first line second line third line",
    headingId: "setup", lineIndex: 2, lineText: "third line",
  } }));
  codeLayout(h); // fires the reflow observer, which re-places the bar
  expect(marked(h)).toEqual([]);
  expect(bar(h).hidden).toBe(false);
  expect(bar(h).style.top).toBe("272px"); // line box 270-300
});

test("an edit above the line: its text wins over the stale line index", () => {
  const h = boot();
  const edited = CODE_HTML.replace(
    "<pre><code>first line\n",
    "<pre><code>first line\ninserted line\n");
  h.send(mdDoc({ html: edited, mark: {
    blockIndex: 2, textPrefix: "first line second line third line",
    headingId: "setup", lineIndex: 1, lineText: "second line",
  } }));
  // Four 30px lines now: 210-240 / 240-270 / 270-300 / 300-330.
  codeLayout(h, { top: 210, bottom: 330 });
  expect(bar(h).style.top).toBe("272px"); // "second line" now sits at index 2
});

test("marked line gone entirely: the bar spans the whole block", () => {
  const h = boot();
  h.send(mdDoc({ html: CODE_HTML, mark: {
    blockIndex: 2, textPrefix: "first line second line third line",
    headingId: "setup", lineIndex: 1, lineText: "this line was deleted",
  } }));
  codeLayout(h);
  expect(bar(h).hidden).toBe(false);
  expect(bar(h).style.top).toBe("202px");    // pre box 200-320, 2px inset
  expect(bar(h).style.height).toBe("116px");
});

test("hovering the gutter beside a code line shows the ghost bar, not a class", () => {
  const h = boot();
  h.send(mdDoc({ html: CODE_HTML }));
  codeLayout(h);
  h.document.getElementById("scroll").dispatchEvent(
    new h.window.MouseEvent("mousemove",
      { bubbles: true, cancelable: true, clientX: 40, clientY: 245 }));
  expect(ghost(h).hidden).toBe(false);
  expect(ghost(h).style.top).toBe("242px");
  expect([...h.document.querySelectorAll(".md-gutter-hover")]).toEqual([]);
  expect(markMsgs(h)).toEqual([]);
});

test("scrollToMark on a line mark scrolls to the line, not the block top", () => {
  const h = boot();
  h.send(mdDoc({ html: CODE_HTML, mark: {
    blockIndex: 2, textPrefix: "first line second line third line",
    headingId: "setup", lineIndex: 2, lineText: "third line",
  } }));
  codeLayout(h);
  const scrollEl = h.document.getElementById("scroll");
  const calls = [];
  scrollEl.scrollTo = (opts) => calls.push(opts);
  h.send({ type: "scrollToMark", tabId: "t1" });
  expect(h.scrolledIntoView).toEqual([]); // not the block path
  expect(calls).toEqual([{ top: 190, behavior: "smooth" }]); // 270 - 80
});

test("a blank code line is markable and anchors by position", () => {
  const h = boot();
  const blank =
    "<pre><code>alpha\n\nbeta</code></pre>";
  h.send(mdDoc({ html: blank }));
  const page = h.document.getElementById("page");
  page.getBoundingClientRect = () =>
    ({ left: 200, right: 800, top: 0, bottom: 10000 });
  const pre = page.children[0];
  pre.getBoundingClientRect = () => ({ top: 0, bottom: 100, left: 200, right: 800 });
  pre.querySelector("code").getBoundingClientRect = () =>
    ({ top: 0, bottom: 90, left: 210, right: 790 });
  h.observers[0].cb();
  gutterClick(h, 45); // middle 30px band: the blank line
  expect(markMsgs(h)[0]).toMatchObject({ lineIndex: 1, lineText: "" });
});

test("gutter clicks on a non-scrollable kind (image) post nothing", () => {
  const h = boot();
  h.send({
    type: "setDoc", kind: "image", tabId: "t1", path: "C:\\docs\\x.png",
    url: "https://app.local/__vault/t1/x.png", modified: "",
  });
  const page = h.document.getElementById("page");
  page.getBoundingClientRect = () =>
    ({ left: 200, right: 800, top: 0, bottom: 10000 });
  gutterClick(h, 50);
  expect(markMsgs(h)).toEqual([]);
});
