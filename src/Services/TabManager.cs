using System;
using System.Collections.Generic;
using System.Linq;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>
/// The tab feature's decision logic, kept UI-agnostic (no WPF / WebView2 /
/// VaultService) so it can be unit-tested in full. Owns the ordered tab list and
/// which tab is active, and answers the load-bearing questions: where does an
/// opened file land (new tab vs. replace), what happens when a tab closes
/// (including the last one), and how the open set serializes/restores across
/// launches. The view layer observes this and drives the actual vault + WebView.
/// </summary>
public sealed class TabManager
{
    private readonly List<TabState> _tabs = new();

    public IReadOnlyList<TabState> Tabs => _tabs;

    /// <summary>Index of the active tab, or -1 when there are no tabs.</summary>
    public int ActiveIndex { get; private set; } = -1;

    public TabState? Active =>
        ActiveIndex >= 0 && ActiveIndex < _tabs.Count ? _tabs[ActiveIndex] : null;

    /// <summary>Open a brand-new blank tab ("open a folder" state) and activate it.</summary>
    public TabState OpenBlankTab() => AddAndActivate(new TabState());

    /// <summary>Open <paramref name="file"/> (in <paramref name="root"/>) in a new
    /// active tab. Used by middle-click / "Open in new tab" — always a new tab,
    /// regardless of the replace/new-tab preference.</summary>
    public TabState OpenInNewTab(string? root, string? file) =>
        AddAndActivate(new TabState { VaultRoot = root, File = file });

    /// <summary>Open into the active tab, mutating it (sidebar single-click —
    /// navigate within the current tab). Creates a first tab if none exists.</summary>
    public TabState OpenInCurrent(string? root, string? file)
    {
        if (Active is not { } tab) return AddAndActivate(new TabState { VaultRoot = root, File = file });
        tab.VaultRoot = root;
        tab.File = file;
        return tab;
    }

    /// <summary>Route an opened file per <paramref name="mode"/> — the
    /// new-tab-vs-replace preference applied to an incoming/opened file.</summary>
    public TabState OpenFile(string? root, string? file, OpenMode mode) =>
        mode == OpenMode.NewTab ? OpenInNewTab(root, file) : OpenInCurrent(root, file);

    /// <summary>
    /// Close the tab at <paramref name="index"/>. Returns true if at least one tab
    /// remains (and <see cref="ActiveIndex"/> has been re-pointed at a survivor),
    /// false if that was the last tab (the caller should close the window). A no-op
    /// returning the current open/empty state for an out-of-range index.
    /// </summary>
    public bool CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return _tabs.Count > 0;

        _tabs.RemoveAt(index);
        if (_tabs.Count == 0) { ActiveIndex = -1; return false; }

        // Re-point the active index: a tab before the active one shifts it left;
        // closing the active tab selects its neighbor (clamped to the new range).
        if (index < ActiveIndex) ActiveIndex--;
        else if (index == ActiveIndex) ActiveIndex = Math.Min(index, _tabs.Count - 1);
        return true;
    }

    /// <summary>Make the tab at <paramref name="index"/> active (no-op if out of range).</summary>
    public void Activate(int index)
    {
        if (index >= 0 && index < _tabs.Count) ActiveIndex = index;
    }

    // ── session persistence ────────────────────────────────────────────────

    /// <summary>Snapshot the open tabs (root + file each) for settings.</summary>
    public IReadOnlyList<TabSession> Serialize() =>
        _tabs.Select(t => new TabSession { VaultRoot = t.VaultRoot, File = t.File }).ToList();

    /// <summary>
    /// Rebuild the tab set from a saved session. Sessions whose root no longer
    /// exists (per <paramref name="rootExists"/>) are dropped; a blank-root tab is
    /// always kept. The active index is clamped to the surviving set; an empty
    /// result leaves no tabs (ActiveIndex -1) for the caller to seed a blank tab.
    /// </summary>
    public void Restore(IEnumerable<TabSession> sessions, int activeIndex, Func<string, bool> rootExists)
    {
        _tabs.Clear();
        ActiveIndex = -1;

        // Track how the original indices map to surviving ones so the saved active
        // index still points at the same tab after drops.
        int newActive = -1, originalIndex = -1;
        foreach (var s in sessions)
        {
            originalIndex++;
            if (!string.IsNullOrEmpty(s.VaultRoot) && !rootExists(s.VaultRoot!)) continue;
            _tabs.Add(new TabState { VaultRoot = s.VaultRoot, File = s.File });
            if (originalIndex == activeIndex) newActive = _tabs.Count - 1;
        }

        if (_tabs.Count == 0) return;
        ActiveIndex = newActive >= 0 ? newActive : Math.Clamp(activeIndex, 0, _tabs.Count - 1);
    }

    private TabState AddAndActivate(TabState tab)
    {
        _tabs.Add(tab);
        ActiveIndex = _tabs.Count - 1;
        return tab;
    }
}
