// Characterization tests for src/WebAssets/bridge.js as it behaves TODAY.
// They pin current behaviour so the place-marker work (and anything after)
// shows a regression as a test failure, not a bug report. No production
// refactor: the harness drives bridge.js through its real seams.

import { test, expect } from "vitest";
import { boot, settle } from "./harness.js";

function mdDoc(over = {}) {
  return {
    type: "setDoc", kind: "markdown", tabId: "t1",
    path: "C:\\docs\\a.md", basePath: "C:\\docs",
    html: "<p>hello world</p>", scrollTop: 0, reloaded: false,
    modified: "2026-01-01 10:00", ...over,
  };
}

function textDoc(over = {}) {
  return {
    type: "setDoc", kind: "text", tabId: "t1",
    path: "C:\\docs\\notes.txt", lang: "", body: "line one\nline two",
    scrollTop: 0, reloaded: false, modified: "", ...over,
  };
}

function prefs(over = {}) {
  return {
    type: "setPrefs", theme: "light", accent: "#0078d4", typeface: "system",
    fontSize: 14, marginPct: 85, showLineNumbers: false, bodyStyle: "win11",
    ...over,
  };
}

const docRendered = (h) => h.sent.filter((m) => m.type === "docRendered");

// Replace #scroll's stored scrollTop with one that clamps at max.value,
// imitating a real browser where the doc isn't tall enough yet. Raising
// max.value later imitates content growth (images/mermaid finishing).
function clampScrollTop(scrollEl, max) {
  let v = 0;
  Object.defineProperty(scrollEl, "scrollTop", {
    configurable: true,
    get: () => v,
    set: (x) => { v = Math.min(x, max.value); },
  });
}

// ─── Load ────────────────────────────────────────────────────────────────

test("bridge announces ready on load", () => {
  const h = boot();
  expect(h.sent).toContainEqual({ type: "ready" });
});

// ─── setDoc rendering ────────────────────────────────────────────────────

test("markdown setDoc puts the HTML into #page and sets the body class", () => {
  const h = boot();
  h.send(mdDoc());
  const page = h.document.getElementById("page");
  expect(page.innerHTML).toBe("<p>hello world</p>");
  expect(h.document.body.classList.contains("kind-markdown")).toBe(true);
});

test("github body style wraps markdown in article.markdown-body", () => {
  const h = boot();
  h.send(prefs({ bodyStyle: "github" }));
  h.send(mdDoc());
  const article = h.document.querySelector("#page > article.markdown-body");
  expect(article).not.toBeNull();
  expect(article.innerHTML).toBe("<p>hello world</p>");
});

test("highlight.js theme follows body style and light/dark", () => {
  const h = boot();
  const href = () => h.document.getElementById("hl-theme").getAttribute("href");
  h.send(prefs());
  expect(href()).toBe("lib/highlight/styles/vs.min.css");
  h.send(prefs({ theme: "dark" }));
  expect(href()).toBe("lib/highlight/styles/vs2015.min.css");
  h.send(prefs({ bodyStyle: "github" }));
  expect(href()).toBe("lib/highlight/styles/github.min.css");
  h.send(prefs({ bodyStyle: "github", theme: "dark" }));
  expect(href()).toBe("lib/highlight/styles/github-dark.min.css");
});

test("a light/dark flip redraws mermaid diagrams from their source", async () => {
  const h = boot();
  const runs = [];
  // Fake mermaid: records the theme and source it drew, swaps in an svg.
  let theme = null;
  h.window.mermaid = {
    initialize: (cfg) => { theme = cfg.theme; },
    run: async ({ nodes }) => nodes.forEach((n) => {
      runs.push({ theme, src: n.textContent });
      n.innerHTML = "<svg></svg>";
    }),
  };
  h.send(prefs());
  h.send(mdDoc({ html: '<div class="mermaid">graph TD; A--&gt;B</div>' }));
  await settle();
  h.send(prefs({ fontSize: 15 }));          // no flip: no redraw
  await settle();
  h.send(prefs({ theme: "dark" }));         // flip: redraw in dark
  await settle();
  expect(runs).toEqual([
    { theme: "default", src: "graph TD; A-->B" },
    { theme: "dark", src: "graph TD; A-->B" },
  ]);
});

test("text setDoc renders the body as a code block", () => {
  const h = boot();
  h.send(textDoc());
  const code = h.document.querySelector("#page pre.textfile code");
  expect(code).not.toBeNull();
  expect(code.textContent).toBe("line one\nline two");
  expect(h.document.body.classList.contains("kind-text")).toBe(true);
});

// ─── docRendered ─────────────────────────────────────────────────────────

test("markdown setDoc posts docRendered with the same tabId and path", () => {
  const h = boot();
  h.send(mdDoc({ tabId: "t7", path: "C:\\docs\\b.md" }));
  expect(docRendered(h)).toEqual([
    { type: "docRendered", tabId: "t7", path: "C:\\docs\\b.md" },
  ]);
});

test("text setDoc posts docRendered", () => {
  const h = boot();
  h.send(textDoc({ tabId: "t2" }));
  expect(docRendered(h)).toEqual([
    { type: "docRendered", tabId: "t2", path: "C:\\docs\\notes.txt" },
  ]);
});

test("image, binary, and raw setDoc do not post docRendered", () => {
  const h = boot();
  h.send({
    type: "setDoc", kind: "image", tabId: "t1", path: "C:\\docs\\x.png",
    url: "https://app.local/__vault/t1/x.png", modified: "",
  });
  h.send({ type: "setDoc", kind: "binary", tabId: "t1", path: "C:\\docs\\x.bin", modified: "" });
  h.send({
    type: "setDoc", kind: "raw", tabId: "t1", path: "C:\\docs\\x.pdf",
    url: "https://app.local/__vault/t1/x.pdf", modified: "",
  });
  expect(docRendered(h)).toEqual([]);
});

// ─── Scroll reporting ────────────────────────────────────────────────────

test("scrolling #scroll posts a scroll message with tabId, top, and path", async () => {
  const h = boot();
  h.send(mdDoc({ tabId: "t3", path: "C:\\docs\\c.md" }));
  const scrollEl = h.document.getElementById("scroll");
  scrollEl.scrollTop = 123;
  scrollEl.dispatchEvent(new h.window.Event("scroll"));
  await settle();
  expect(h.sent).toContainEqual({
    type: "scroll", tabId: "t3", top: 123, path: "C:\\docs\\c.md",
  });
});

test("scroll reports are rAF-throttled: two rapid events post once", async () => {
  const h = boot();
  h.send(mdDoc());
  const scrollEl = h.document.getElementById("scroll");
  scrollEl.scrollTop = 50;
  scrollEl.dispatchEvent(new h.window.Event("scroll"));
  scrollEl.scrollTop = 80;
  scrollEl.dispatchEvent(new h.window.Event("scroll"));
  await settle();
  expect(h.sent.filter((m) => m.type === "scroll")).toHaveLength(1);
});

test("scrolling with no scrollable doc posts nothing", async () => {
  const h = boot(); // still on the empty state: scrollPath is ""
  const scrollEl = h.document.getElementById("scroll");
  scrollEl.scrollTop = 40;
  scrollEl.dispatchEvent(new h.window.Event("scroll"));
  await settle();
  expect(h.sent.filter((m) => m.type === "scroll")).toEqual([]);
});

// ─── Scroll restore (the trickiest existing behaviour) ───────────────────

test("setDoc carrying scrollTop restores that offset", async () => {
  const h = boot();
  h.send(mdDoc({ scrollTop: 500 }));
  await settle();
  expect(h.document.getElementById("scroll").scrollTop).toBe(500);
});

test("reloaded setDoc keeps the pre-swap offset and ignores the host scrollTop", async () => {
  const h = boot();
  h.send(mdDoc());
  await settle();
  const scrollEl = h.document.getElementById("scroll");
  scrollEl.scrollTop = 250; // reader's live position
  h.send(mdDoc({ reloaded: true, scrollTop: 999 }));
  await settle();
  expect(scrollEl.scrollTop).toBe(250);
});

test("reloaded text setDoc also keeps the pre-swap offset", async () => {
  const h = boot();
  h.send(textDoc());
  await settle();
  const scrollEl = h.document.getElementById("scroll");
  scrollEl.scrollTop = 77;
  h.send(textDoc({ reloaded: true, scrollTop: 999 }));
  await settle();
  expect(scrollEl.scrollTop).toBe(77);
});

test("a clamped restore starts a growth watch that re-applies until it sticks", async () => {
  const h = boot();
  const scrollEl = h.document.getElementById("scroll");
  const max = { value: 100 }; // doc is still short: offsets clamp at 100
  clampScrollTop(scrollEl, max);
  h.send(mdDoc({ scrollTop: 500 }));
  await settle();
  expect(scrollEl.scrollTop).toBe(100); // clamped for now
  const watch = h.observers.at(-1);
  expect(watch).toBeDefined();
  expect(watch.disconnected).toBe(false);

  max.value = 1000; // content grew (image/diagram finished)
  watch.cb([]);     // ResizeObserver fires
  expect(scrollEl.scrollTop).toBe(500);
  expect(watch.disconnected).toBe(true); // target reached: watch stops
});

// ─── scrollToHeading ─────────────────────────────────────────────────────

test("scrollToHeading with a stale tabId is dropped", () => {
  const h = boot();
  h.send(mdDoc({ tabId: "t1", html: '<h2 id="target">Section</h2><p>body</p>' }));
  h.send({ type: "scrollToHeading", tabId: "t9", id: "target" });
  expect(h.scrolledIntoView).toEqual([]);
});

test("scrollToHeading with the current tabId scrolls to the element", () => {
  const h = boot();
  h.send(mdDoc({ tabId: "t1", html: '<h2 id="target">Section</h2><p>body</p>' }));
  h.send({ type: "scrollToHeading", tabId: "t1", id: "target" });
  expect(h.scrolledIntoView).toHaveLength(1);
  expect(h.scrolledIntoView[0].el.id).toBe("target");
  expect(h.scrolledIntoView[0].opts).toEqual({ behavior: "smooth", block: "start" });
});

test("scrollToHeading cancels a pending growth watch", async () => {
  const h = boot();
  const scrollEl = h.document.getElementById("scroll");
  clampScrollTop(scrollEl, { value: 100 });
  h.send(mdDoc({ tabId: "t1", html: '<h2 id="target">Section</h2>', scrollTop: 500 }));
  await settle();
  const watch = h.observers.at(-1);
  expect(watch.disconnected).toBe(false);
  h.send({ type: "scrollToHeading", tabId: "t1", id: "target" });
  expect(watch.disconnected).toBe(true);
});

// ─── Link interception ───────────────────────────────────────────────────

test("clicking an in-vault link posts openLink with href and base", () => {
  const h = boot();
  h.send(mdDoc({ html: '<a href="sub/other.md">link</a>', basePath: "C:\\docs" }));
  h.document.querySelector("#page a").dispatchEvent(
    new h.window.MouseEvent("click", { bubbles: true, cancelable: true }));
  expect(h.sent).toContainEqual({
    type: "openLink", href: "sub/other.md", base: "C:\\docs",
  });
});

test("clicking an external https link posts requestExternal", () => {
  const h = boot();
  h.send(mdDoc({ html: '<a href="https://example.com/page">out</a>' }));
  h.document.querySelector("#page a").dispatchEvent(
    new h.window.MouseEvent("click", { bubbles: true, cancelable: true }));
  expect(h.sent).toContainEqual({
    type: "requestExternal", url: "https://example.com/page",
  });
  expect(h.sent.filter((m) => m.type === "openLink")).toEqual([]);
});

// ─── Print ───────────────────────────────────────────────────────────────

test("print switches a dark view to light for the paper and restores it after", async () => {
  const h = boot();
  const seen = [];
  h.window.print = () => seen.push({
    dark: h.document.body.classList.contains("theme-dark"),
    hl: h.document.getElementById("hl-theme").getAttribute("href"),
    accent: h.document.documentElement.style.getPropertyValue("--accent"),
  });
  h.send(prefs({ theme: "dark", accent: "#4CC2FF" }));
  h.send(mdDoc());
  h.send({ type: "print", tabId: "t1" });
  await new Promise((r) => setTimeout(r, 1700)); // jsdom never loads the sheets
  expect(seen).toEqual([{ dark: false, hl: "lib/highlight/styles/vs.min.css", accent: "" }]);
  h.window.dispatchEvent(new h.window.Event("afterprint"));
  expect(h.document.body.classList.contains("theme-dark")).toBe(true);
  expect(h.document.documentElement.style.getPropertyValue("--accent")).toBe("#4CC2FF");
});

test("print is a no-op for raw views", async () => {
  const h = boot();
  let printed = 0;
  h.window.print = () => { printed++; };
  h.send(prefs());
  h.send({ type: "setDoc", kind: "raw", tabId: "t1", path: "C:\docs\a.pdf",
    url: "https://app.local/__vault/t1/a.pdf", modified: "" });
  h.send({ type: "print", tabId: "t1" });
  await settle();
  expect(printed).toBe(0);
});
