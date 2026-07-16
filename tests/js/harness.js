import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { JSDOM } from "jsdom";

const here = dirname(fileURLToPath(import.meta.url));
const assets = join(here, "..", "..", "src", "WebAssets");

// Boot the real render.html skeleton plus the real bridge.js in a fresh JSDOM
// instance, stubbing only the host seam (window.chrome.webview) and the DOM
// APIs jsdom lacks. Tests drive bridge.js through the same channels the WPF
// shell uses: `send` delivers a host->bridge message, `sent` collects what
// bridge.js posted back.
export function boot() {
  // Strip the <script src="bridge.js"> tag: jsdom would try to fetch it, and
  // we want to run the source ourselves after the stubs are installed.
  const html = readFileSync(join(assets, "render.html"), "utf8")
    .replace('<script defer src="bridge.js"></script>', "");

  const dom = new JSDOM(html, {
    runScripts: "outside-only",
    pretendToBeVisual: true, // supplies requestAnimationFrame
    url: "https://app.local/render.html",
  });
  const { window } = dom;

  // jsdom has no ResizeObserver; bridge.js uses one for the scroll-restore
  // growth watch. Stub it and expose the instances so tests can fire a resize.
  const observers = [];
  window.ResizeObserver = class {
    constructor(cb) {
      this.cb = cb;
      this.observed = [];
      this.disconnected = false;
      observers.push(this);
    }
    observe(el) { this.observed.push(el); }
    disconnect() { this.disconnected = true; }
  };

  // Note on scrollTop: jsdom has no layout engine, so it never clamps —
  // assignments to #scroll.scrollTop store and read back verbatim. Restores
  // therefore always "stick" on the first try; a test that needs the clamped
  // path (growth watch) must redefine the property on the element itself.

  // jsdom has no scrollIntoView at all; record calls for assertions.
  const scrolledIntoView = [];
  window.Element.prototype.scrollIntoView = function (opts) {
    scrolledIntoView.push({ el: this, opts });
  };

  // The host seam. bridge.js calls window.chrome.webview.addEventListener at
  // load time, so this must exist before the source runs.
  const sent = [];
  let listener = null;
  window.chrome = {
    webview: {
      postMessage: (obj) => sent.push(obj),
      addEventListener: (_type, fn) => { listener = fn; },
    },
  };

  const src = readFileSync(join(assets, "bridge.js"), "utf8");
  window.eval(src);

  return {
    window,
    document: window.document,
    sent,                                   // messages bridge.js posted to the host
    send: (msg) => listener({ data: msg }), // host -> bridge
    observers,
    scrolledIntoView,
  };
}

// bridge.js queues work through requestAnimationFrame (scroll reporting,
// restoreScroll's double rAF). jsdom's pretendToBeVisual clock ticks every
// ~16ms, so give it a couple of frames' worth of real time.
export function settle(ms = 60) {
  return new Promise((r) => setTimeout(r, ms));
}
