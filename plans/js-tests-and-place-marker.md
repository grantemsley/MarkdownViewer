# JS test harness + place marker

**Status:** ⏳ In progress · Last updated 2026-07-16

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Phase 1: JS harness scaffolding | vitest + jsdom under `tests/js/`; smoke test green; jsdom scrollTop stores values natively (no shim needed), scrollIntoView recorder added |
| ✅ Done | Phase 2: Characterize existing bridge.js | 19 tests; every one verified RED under mutation by a fresh-context agent |
| ✅ Done | Phase 3: Wire JS tests into CI | Node steps added to `build-test`; not pushed (public remote, Grant pushes) |
| ✅ Done | Phase 4: Mark model + bridge contract | .NET suite 490 -> 499 green (Release; running app locks Debug output). Baseline was 490, not the 484 in notes |
| ✅ Done | Phase 5: Gutter UI + anchoring in bridge.js | 14 tests written first (11 red pre-impl); bar is `::after` (line numbers own `::before`); copy-btn text stripped from prefixes |
| ⏳ In progress | Phase 6: Hotkey, jump, verification | Ctrl+G, `cancelRestoreWatch`, full-suite + interactive check |

## Goal

Give the reader a durable "I stopped here" marker in a document: click a gutter
strip left of the text, a bar + tint appears on that block, and it survives the
file being re-rendered by the watcher. One mark per file, in memory only (it
does not need to survive a restart), keyed by file path so the same file in two
tabs shows the same mark.

The anchoring logic that makes this survive an edit lives in JavaScript, and
this project has no JS test harness at all. So the harness comes first, proves
itself against the existing `bridge.js`, and lands as its own commit before any
feature code is written. That ordering is the point: a harness written
alongside the feature it is meant to test tends to be shaped to pass rather
than to catch.

## Design decisions already made (do not re-litigate)

- **Placement:** gutter click. **Count:** one per file, re-click clears.
  **Look:** left margin bar (`::before`) plus a faint background tint.
- **Anchor descriptor:** `{ blockIndex, textPrefix, headingId }` where
  `blockIndex` is the index among `#page`'s top-level element children,
  `textPrefix` is the first 60 chars of the block's normalized `textContent`,
  and `headingId` is the `id` of the nearest preceding heading. Resolution
  order on each render: index (only if the text still matches there), then a
  scan for the text, then the heading as a coarse fallback, then drop the mark.
  Dropping beats guessing: a mark on the wrong paragraph is worse than none.
  This is a scaled-down `TextQuoteSelector` + `TextPositionSelector` from the
  W3C Web Annotation model (what Hypothes.is uses) - quote plus a position
  hint, so an edit above the mark does not drift it.
- **Storage is C#-side, not a JS `Map`.** The JS context does outlive a
  markdown reload (the page is never navigated away from), but a RawBrowser
  (.pdf/.html) file changing on disk calls `CoreWebView2.Reload()`
  (`src/MainWindow.xaml.cs:1507-1508`), which discards the whole page and every
  tab's JS state with it. C# storage is immune and matches how `ScrollTop` is
  already handled.
- **Keyed by file path, not tab id.** Tab ids (`t1`, `t2`, ...) are
  process-lifetime and deliberately not persisted (see
  `decisions/2026-07-11-tab-identity-model.md`).
- **Scope:** markdown and text kinds only. Those are the kinds that get the
  `innerHTML` swap and post `docRendered` (`src/WebAssets/bridge.js:581-582`).
- **No production refactor of `bridge.js` to make it testable.** It is one
  IIFE with no exports; the harness drives it through its real seams
  (`window.chrome.webview` in, `postMessage` out) instead. Exporting internals
  purely for tests would change the shipping artifact to suit the test runner.

## ✅ Phase 1: JS harness scaffolding

Node 24.16 and npm 11.13 are already on this box; nothing to install globally.

1. Create `tests/js/package.json`. Keep JS tooling out of the repo root - this
   is a .NET solution and the root should keep reading like one.

```json
{
  "name": "markdownviewer-webassets-tests",
  "private": true,
  "type": "module",
  "scripts": {
    "test": "vitest run"
  },
  "devDependencies": {
    "jsdom": "^26.1.0",
    "vitest": "^3.2.4"
  }
}
```

2. Install and lock. Run from `tests/js/`:

```
npm install
```

Commit the generated `package-lock.json` - CI uses `npm ci`, which requires it.

3. Add `tests/js/vitest.config.js`:

```js
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["**/*.test.js"],
  },
});
```

The environment is `node`, not `jsdom`: each test constructs its own `JSDOM`
instance via the loader below, because `bridge.js` reads the DOM once at IIFE
time and must be re-executed against a fresh document per test.

4. Add `node_modules/` to `.gitignore` (it is not there yet).

5. Write the loader, `tests/js/harness.js`. This is the load-bearing piece -
   it boots the real `render.html` skeleton and the real `bridge.js` so the
   tests cannot drift from the shipping assets:

```js
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { JSDOM } from "jsdom";

const here = dirname(fileURLToPath(import.meta.url));
const assets = join(here, "..", "..", "src", "WebAssets");

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
  // growth watch. Stub it and expose the callback so tests can fire a resize.
  const observers = [];
  window.ResizeObserver = class {
    constructor(cb) { this.cb = cb; observers.push(this); }
    observe() {}
    disconnect() {}
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
  };
}
```

6. Prove the harness works before writing real tests. Add
   `tests/js/smoke.test.js`:

```js
import { test, expect } from "vitest";
import { boot } from "./harness.js";

test("bridge announces ready on load", () => {
  const h = boot();
  expect(h.sent).toContainEqual({ type: "ready" });
});
```

Run `npm test` from `tests/js/` and confirm it passes. If this one does not
go green, nothing downstream is trustworthy - fix the loader, do not work
around it in the test.

## ✅ Phase 2: Characterize existing bridge.js

Cover what `bridge.js` does **today**, before Phase 5 changes it. These are
characterization tests: they pin current behaviour so a regression in the mark
work shows up as a failure rather than as a bug report.

Write them in `tests/js/bridge.test.js`. Cover at minimum:

- `ready` is posted on load (move the smoke test here and delete `smoke.test.js`).
- `setDoc` with `kind: "markdown"` puts the HTML into `#page` and sets the body
  class; the `github` body style wraps it in `article.markdown-body`
  (`src/WebAssets/bridge.js:384-386`).
- `setDoc` posts `docRendered` carrying the same `tabId` and `path` it was
  given (`:553-555`), and posts it for markdown and text but **not** for
  image/binary/raw.
- Scrolling `#scroll` posts a `scroll` message with `tabId`, `top`, and `path`.
  The listener is rAF-throttled, so `await new Promise(r => setTimeout(r, 0))`
  after dispatching the event, or drive `window.requestAnimationFrame`
  directly.
- A `setDoc` carrying `scrollTop` restores that offset; one carrying
  `reloaded: true` keeps the pre-swap offset and ignores the host's
  `scrollTop` (`:417-422`). This is the trickiest existing behaviour and the
  one most likely to break.
- `scrollToHeading` with a stale `tabId` is dropped; with the current `tabId`
  it scrolls (assert via a spied `scrollIntoView`, which jsdom does not
  implement - assign a stub onto `window.Element.prototype`).
- A click on a `#page` link posts `openLink` with the right `href`/`base`.

Each test must fail if you break the line it guards. Verify that the same way
the junction work did: comment out the production line, watch the test go red,
restore it. A characterization test that passes against broken code is worse
than no test, because it certifies the breakage.

Do not chase coverage of the mermaid/highlight.js lazy-load paths - they pull
real network-ish resources and buy little. Note them as uncovered in the phase
write-up rather than faking the libraries.

**Phase write-up:** 19 tests in `tests/js/bridge.test.js` (the smoke test moved
in as "bridge announces ready on load"; `smoke.test.js` deleted). Beyond the
minimum list: the rAF throttle on scroll reports, the no-doc scroll guard, the
clamped-restore growth watch (via a test-side clamping `scrollTop` override),
and `scrollToHeading` cancelling a pending growth watch (`bridge.js:596` - the
line Phase 6's `scrollToMark` must mirror). Uncovered by design: the
mermaid/highlight.js lazy-load paths (they fetch real resources; jsdom never
loads them) and `addCopyButton`'s clipboard interaction.

## ✅ Phase 3: Wire JS tests into CI

Add a step to `.github/workflows/ci.yml` in the existing `build-test` job,
after the .NET `Test` step (the runner is `windows-2025`; Node is preinstalled
but pin it anyway so the version is not ambient):

```yaml
      - uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: 'npm'
          cache-dependency-path: tests/js/package-lock.json

      - name: Install JS test deps
        working-directory: tests/js
        run: npm ci

      - name: Test JS
        working-directory: tests/js
        run: npm test
```

Leave `.github/workflows/release.yml` alone. It runs the .NET tests as a
release gate; the JS suite guards renderer behaviour that CI on `main` already
covers, and adding it to the tag path only lengthens the release.

**Commit here, before any feature code.** Message along the lines of
`test(webassets): jsdom harness + characterization tests for bridge.js`. Push
to `origin`. The harness has to stand on its own in history: if the mark
feature is later reverted, the tests for the rest of the renderer stay.

## ✅ Phase 4: Mark model + bridge contract

C# side. Follow the existing conventions in `src/Services/BridgeMessages.cs` -
that one file is the whole contract, records with expression-bodied `Type`
props, camelCase, nulls omitted.

1. `src/Models/MarkAnchor.cs`: `public sealed record MarkAnchor(int BlockIndex,
   string TextPrefix, string? HeadingId);`

2. In `src/MainWindow.xaml.cs`, beside `_runtimes` (`:34`):
   `private readonly Dictionary<string, MarkAnchor> _marks = new(StringComparer.OrdinalIgnoreCase);`
   Path-keyed, process-lifetime, no `AppSettings` involvement and no
   `SettingsSchema` bump.

3. Inbound records in `BridgeMessages.cs`: `MarkSetMsg(string TabId, string
   Path, int BlockIndex, string TextPrefix, string? HeadingId)` and
   `MarkClearedMsg(string TabId, string Path)`. Add both to
   `BridgeInbound.Parse` (`:105-156`).

4. Gate: `BridgeGates.MarkApplies`, mirroring `ScrollApplies` (`:188-191`) -
   token match **and** path match. Unit-test it in
   `tests/MarkdownViewer.Tests/BridgeMessagesTests.cs` alongside the
   `ScrollApplies` tests, including the stale-tab and wrong-path cases.

5. Outbound: add an optional `mark` field to `MarkdownDocMsg` and `TextDocMsg`
   rather than sending a separate message. It rides along with `setDoc` exactly
   as `scrollTop` already does, which removes any question of whether the doc
   is in the DOM when the mark arrives. Populate it in `RenderMarkdown`
   (`:1343-1382`) and `ShowText` by looking up `_marks` on the file path.

6. Outbound: `ScrollToMarkMsg(string TabId)` for the Ctrl+G jump.

7. Handle the inbound messages next to the existing `ScrollMsg` handling
   (`:1598-1599`): on `MarkSetMsg` store, on `MarkClearedMsg` remove.

Run `.\test.ps1` and confirm the suite is still green (484 + the new gate
tests) before moving on.

## ✅ Phase 5: Gutter UI + anchoring in bridge.js

**Write the tests first this time** - the harness exists now, and this is the
logic it was built for.

Test cases that define the behaviour (`tests/js/mark.test.js`):

- A click in the gutter zone posts `markSet` with the right `blockIndex`,
  `textPrefix`, and `headingId`.
- A click in the gutter zone on the already-marked block posts `markCleared`.
- A click inside the text (not the gutter zone) posts neither.
- `setDoc` carrying a `mark` whose `blockIndex` and `textPrefix` agree marks
  that block.
- `setDoc` carrying a `mark` whose `textPrefix` has moved to a different index
  (simulating an edit above it) marks the block that still has the text, not
  the one at the stale index. **This is the case the whole design exists for.**
- `setDoc` carrying a `mark` whose text is gone entirely falls back to the
  heading, and with no heading either, drops the mark and marks nothing.
- The mark class survives a `reloaded: true` `setDoc`.

Then the implementation, in `src/WebAssets/bridge.js`:

- Anchor helpers: `describeMark(el)` -> descriptor, `resolveMark(descriptor)`
  -> element or `null`, applying the resolution order from the decisions
  section above.
- `applyMark()` called at the end of the `setDoc` path, after the `innerHTML`
  swap, next to where `addCopyButton` is re-applied (`:345-370`). That function
  is the model here: idempotent, re-run on every render.
- Delegated `mousemove` and `click` on `#scroll` (never inline handlers - the
  CSP at `render.html:22` has `script-src` without `'unsafe-inline'`, and this
  is deliberate). Gutter hit-test: `clientX` left of `#page`'s content box.
- New `mark` / `markCleared` arms in the dispatch `switch` (`:564-603`).

And in `src/WebAssets/reader.css`: extra `padding-left` on `#page`, a
`.md-mark::before` margin bar, a faint `.md-mark` background tint, and a
`.md-gutter-hover::before` ghost bar. Both themes - `reader.css` already has
the light/dark variables to hang this on; use them rather than hard-coding two
colours.

## ⏳ Phase 6: Hotkey, jump, verification

1. Ctrl+G in `MainWindow_KeyDown` (`:1965-1996`) sends `ScrollToMarkMsg` for
   the active tab. Ctrl+G is free in the current ladder (Ctrl+B is taken by
   the sidebar toggle). Follow the arm shape already there: `e.Handled = true;
   return;`.

2. The `scrollToMark` arm in `bridge.js` **must** call `cancelRestoreWatch()`
   before scrolling, for the same reason `scrollToHeading` does (`:596`): the
   wheel/pointer/key cancellers cannot see input from the WPF chrome, so
   without it the growth-watch yanks the view back to the restore offset on the
   next content resize. Then `scrollIntoView({ behavior: "smooth", block:
   "start" })`. Add a JS test asserting the cancel happens.

3. Full suite: `.\test.ps1` and `npm test` from `tests/js/`. Both green.

4. Interactive check via the `/run` skill or `.\smoke.ps1` - the parts no unit
   test covers:
   - Gutter hover shows the ghost bar; click sets the mark; re-click clears it.
   - Edit and save text **above** the mark; the watcher re-render leaves the
     mark on the same paragraph.
   - Edit the marked paragraph's own text away; the mark drops rather than
     landing somewhere wrong.
   - Ctrl+G from the bottom of a long doc jumps to the mark and stays there
     while a Mermaid diagram below finishes rendering (this is the
     `cancelRestoreWatch` case).
   - Same file in two tabs shows the same mark.
   - Switch to a tab with a PDF, touch the PDF on disk to force the
     `CoreWebView2.Reload()`, switch back: the mark is still there. This is
     the case that justifies C#-side storage over a JS `Map`.

5. Close-out per `~/.claude/docs/plan-format.md`: file the load-bearing
   decisions in `decisions/` (the anchoring model and why storage is C#-side
   rather than JS; the no-refactor-for-testability call), move any deferred
   follow-ons to `todo.md` `## Proposed`, promote the now-shipped
   "UI smoke test / JS unit tests" item out of `todo.md` `## Proposed`, then
   move this file to `plans/finished/`.
