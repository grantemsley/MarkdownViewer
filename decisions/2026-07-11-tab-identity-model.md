# Tab identity: runtime-scoped tokens through the bridge and vault URL space

**Date:** 2026-07-11 · **Context:** post-audit structural refactor
(`refactor/tab-identity-mainwindow-split`), Workstream A.

## Decision

Every tab's runtime carries a stable, process-lifetime identity token (`t1`,
`t2`, ...). The token rides in every doc-scoped bridge message (`setDoc`,
`scrollToHeading`, and inbound `scroll` reports) and is embedded in every vault
URL (`/__vault/<tabId>/<rel>`). Documents, scroll reports, and vault file
requests resolve to the tab that minted them - never to "whichever tab is
active right now" and never by path-string matching. Mismatched tokens are
dropped (messages) or 404 (URLs of closed tabs).

Two corollaries shipped as part of the same model:
- **The headings round-trip is gone.** C# populates the outline synchronously
  from the render result (`SetOutline`), and the tab's stashed outline is
  written at render time, not read back from the control on switch. There is
  no async outline path left, so the stale-outline race (audit 2.1) cannot
  exist rather than being filtered.
- **Bridge messages are typed C# records** (`BridgeMessages.cs`) with one
  generic `Send<T>` and a strict inbound parser that Trace-logs malformed or
  unknown messages. Adding a message kind (the planned folder-tree search) is
  one record + one dispatch arm per side.

## Why

The audit's unifying finding: almost every tab-switch race (wrong-tab render,
stale outline adoption, cross-tab scroll clobber, background-iframe vault
requests resolving against the wrong root) was a facet of *implicit* identity.
Carrying the token makes the whole class impossible by construction instead of
patching each symptom with heuristics.

## Alternatives rejected

- **Per-message path matching** (the pre-refactor approach): fails when two
  tabs show the same file (audit 2.2), and token-only matching fails on
  same-tab navigation (a trailing scroll report for the previous file), so the
  scroll gate requires BOTH token and path (`BridgeGates.ScrollApplies`).
- **Persisting tab ids across sessions:** unnecessary - vault URLs are
  regenerated per render, so ids are process-lifetime only and session
  save/restore (`TabState`/`TabSession`) is untouched.
- **Per-tab WebView2 instances** (would make identity implicit again): ruled
  out by the standing cold-start constraint - one shared WebView2 stays.

## New constraints / consequences

- A vault URL is only meaningful to the tab that minted it. Consequence: a
  HAND-AUTHORED absolute `https://app.local/__vault/...` link inside a
  document no longer resolves (its first segment reads as a tab id). All
  app-generated and relative links are unaffected. Accepted: that edge only
  ever worked when the path happened to match the active vault.
- Switching between two tabs viewing the same relative PDF path reloads the
  iframe (URLs now differ by tab) - the warm-iframe reuse is per-tab, which is
  the correctness point.
- `VaultPaths.ResolveWithinRoot` remains the single path-traversal gate; the
  tab id changes *which root* a request resolves against, not whether the gate
  runs.
