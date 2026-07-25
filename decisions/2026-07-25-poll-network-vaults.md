# Vaults on a network share are polled, not just watched

**Date:** 2026-07-25

## Decision

When the open folder is on a network location (a UNC path, or a mapped
network drive - `VaultService.IsNetworkPath`), the vault runs a **5s polling
fallback alongside** the FileSystemWatcher. Each tick fingerprints every
loaded folder (`DirectorySignature`, an FNV-1a hash over the entry names and
kinds) and stats the open file; anything that moved is fed into the same
dirty-folder / pending-changed path the watcher already uses, so a poll and a
watcher event produce identical reconciles.

Local vaults are unchanged: the watcher alone, no polling, no extra IO.

## Why

FileSystemWatcher over SMB depends on the server's change-notify reaching
us, and between two Windows 11 machines that is not dependable. Reported
symptom: files opened from a share never refreshed and new files never
appeared in the tree. Two failure modes behind it:

- Notifications simply go missing over SMB (the documented reason
  `PhysicalFileProvider` ships a `UsePollingFileWatcher` switch at all).
- A momentary disconnect - sleep/resume, Wi-Fi roam, server reboot - tears
  the watch handle down permanently. `Error` fires at most once and then the
  tree silently stops updating for the rest of the session. That handler
  previously only set `_reconcileAll`; it now rebuilds the watcher, and the
  poll re-creates one later if the share was still down at the time.

## Freshness floor we don't control

The Windows SMB client caches directory listings and file metadata for 10s
by default (`DirectoryCacheLifetime` / `FileInfoCacheLifetime`, plus a 5s
`FileNotFoundCacheLifetime`, all under `LanmanWorkstation`). Until that
expires, a change made on the other machine is invisible to *any*
enumeration - ours, Explorer's - so polling faster than 5s would only re-read
the client cache. Worst case is roughly cache lifetime + poll interval.
A user who wants shares to update instantly changes it on the client:
`Set-SmbClientConfiguration -DirectoryCacheLifetime 0 -FileInfoCacheLifetime 0 -FileNotFoundCacheLifetime 0`
(trades network round trips for freshness). Not something the app should do
machine-wide on its own.

## Choices inside the fallback

- **Fingerprint covers entry names + kind only, not sizes or timestamps.**
  The tree shows entries; a content-only save must not rebuild the folder.
  The open file is stat-checked separately, which is what a content change
  actually needs. This also keeps network behaviour at parity with local,
  where a `Changed` event reloads the document without touching the tree.
- **Seeded at scan time, not at the first poll.** `Register` stores the
  fingerprint from the children it just materialized (no extra enumeration).
  Seeding on the first poll instead would silently absorb anything created in
  the first 5s after opening a vault, and the tree would keep missing it
  until some later change moved the fingerprint.
- **Unreadable folder means "no information", never "empty".** A dropped link
  would otherwise read as every file being deleted at once. Same reasoning
  makes a network root miss `Directory.Exists` **twice** before the tree is
  cleared, so a blip no longer wipes the user's expansion state; a local root
  still clears on the first miss, where a missing folder really is deleted.
- **Ticks are skipped while a scan is in flight**, so a slow VPN link
  self-limits to one scan at a time instead of piling them up. No adaptive
  interval needed.

## How it was verified

Committed tests cover the pure pieces only (`DirectorySignatureTests`,
`IsNetworkPath`). The polling loop itself was verified with a throwaway
harness that opened a vault over a real SMB connection - `\\localhost\C$\…`
pointed at a temp folder, so the redirector is genuinely in the path - then
reflected in and set `_watcher.EnableRaisingEvents = false`, leaving polling
as the only mechanism that could notice anything. With change-notify dead: a
new file surfaced in the tree in 5.3s, an edit to the open file fired
`ActiveFileChanged` in 3.3s, a file created inside the first 5s (before any
poll) still surfaced, and a watcher nulled out to fake the network-error path
was rebuilt by the next tick. A local root started no poll timer. Not
committed: it needs a reachable `C$` admin share, so it would be flaky on CI.
Recreate it the same way if this needs re-testing.

## Not done

No preference for this. It is a correctness fallback, not a feature, and the
cost on a local vault is zero (never armed). If a huge tree over a slow link
ever proves expensive, the knob to add is the interval, not an on/off switch.
