# Per-tab scroll restore: live-tracking over synchronous capture at switch

*Decided 2026-06-14.*

**Decision.** Per-tab scroll position is preserved by live-tracking rather than capturing at
switch time. `bridge.js` reports `#scroll`'s offset (rAF-throttled, tagged with the doc
path) and the shell stores the latest on the active `TabRuntime.ScrollTop`; on switch-back
it is passed into `setDoc` and restored after layout settles (the same double-rAF the reload
path uses). Only a re-show of the tab's current doc restores (`isNavigation == false`); a
genuine navigation resets to 0.

Scroll reports are path-matched against the active file so a stale report from a just-left
doc cannot clobber the new tab.

**Why.** An `await ExecuteScriptAsync` at switch time to capture the offset synchronously
would add a round trip and make rapid switching re-entrant. Live-tracking keeps the switch
path synchronous and avoids that class of bug entirely.

**Consequences / caveats.**
- Two tabs open to the same file can momentarily share a restored offset on fast A->B->A
  switching (path-match cannot tell them apart). Self-corrects on the next scroll. Filed
  in `todo.md` as a known limitation.
