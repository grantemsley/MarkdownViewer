# bridge.js is tested through its real seams, not refactored for the runner

**Date:** 2026-07-16
**Status:** Adopted (built with `plans/js-tests-and-place-marker.md`)

## Decision

`src/WebAssets/bridge.js` stays one IIFE with no exports. The JS test harness
(`tests/js/harness.js`, vitest + jsdom) boots the real `render.html` skeleton
and evaluates the real `bridge.js` source in a fresh JSDOM per test, stubbing
only the host seam (`window.chrome.webview`) and DOM APIs jsdom lacks
(`ResizeObserver`, `scrollIntoView`). Tests drive it exactly as the WPF shell
does: messages in through the webview listener, assertions on what it posts
back.

The harness and characterization tests landed as their own commits **before**
any feature code existed, and each characterization test was verified by a
fresh-context agent to go red when the production line it guards is broken
(19/19 mutations caught).

## Why

- Exporting internals purely for tests changes the shipping artifact to suit
  the test runner, and unit-testing extracted helpers would miss the wiring
  (listener registration order, module-level state like `currentTabId` /
  `scrollPath`) where renderer bugs actually live.
- A harness written alongside the feature it is meant to test gets shaped to
  pass rather than to catch; proving it against pre-existing behaviour first
  is what makes its later green runs meaningful.
- A test that passes against broken code certifies the breakage - hence the
  mutation pass, same technique as the junction work.

## Constraints this creates

- Each test pays a full JSDOM boot (~10-20 ms); fine at this scale.
- `vitest` runs with `environment: "node"`, not `"jsdom"`: bridge.js reads
  the DOM once at IIFE time, so every test must re-execute it against its own
  fresh document via `boot()`.
- jsdom has no layout: `scrollTop` stores whatever is assigned (so restores
  always "stick" unless a test installs a clamping override), and geometry-
  dependent logic (the gutter hit-test) is tested by stubbing
  `getBoundingClientRect`.
- The mermaid / highlight.js lazy-load paths are uncovered by design - they
  fetch real resources jsdom never loads.

## Notes

CI runs the suite in the existing `build-test` job (pinned Node 24,
`npm ci` + `npm test` from `tests/js/`); deliberately absent from
`release.yml`, which gates releases on the .NET suite only.
