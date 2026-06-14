using System.Collections.Generic;
using System.Linq;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class TabManagerTests
{
    private static TabManager WithTabs(params string[] files)
    {
        var m = new TabManager();
        foreach (var f in files) m.OpenInNewTab(@"C:\vault", f);
        return m;
    }

    [Fact]
    public void Empty_HasNoActiveTab()
    {
        var m = new TabManager();
        Assert.Empty(m.Tabs);
        Assert.Equal(-1, m.ActiveIndex);
        Assert.Null(m.Active);
    }

    [Fact]
    public void OpenInNewTab_AppendsAndActivatesLast()
    {
        var m = WithTabs("a.md", "b.md");
        Assert.Equal(2, m.Tabs.Count);
        Assert.Equal(1, m.ActiveIndex);
        Assert.Equal("b.md", m.Active!.Title);
    }

    [Fact]
    public void OpenBlankTab_AddsEmptyTabTitledNewTab()
    {
        var m = new TabManager();
        var tab = m.OpenBlankTab();
        Assert.Null(tab.VaultRoot);
        Assert.Null(tab.File);
        Assert.Equal("New tab", tab.Title);
        Assert.Same(tab, m.Active);
    }

    [Fact]
    public void OpenFile_NewTab_AddsTab()
    {
        var m = WithTabs("a.md");
        m.OpenFile(@"C:\vault", @"C:\vault\b.md", OpenMode.NewTab);
        Assert.Equal(2, m.Tabs.Count);
        Assert.Equal("b.md", m.Active!.Title);
    }

    [Fact]
    public void OpenFile_ReplaceCurrent_MutatesActiveTabInPlace()
    {
        var m = WithTabs("a.md");
        var before = m.Active;
        m.OpenFile(@"C:\vault", @"C:\vault\b.md", OpenMode.ReplaceCurrent);
        Assert.Single(m.Tabs);
        Assert.Same(before, m.Active);           // same tab instance...
        Assert.Equal(@"C:\vault\b.md", m.Active!.File);  // ...new file
    }

    [Fact]
    public void OpenFile_ReplaceCurrent_WithNoTabs_CreatesOne()
    {
        var m = new TabManager();
        m.OpenFile(@"C:\vault", @"C:\vault\a.md", OpenMode.ReplaceCurrent);
        Assert.Single(m.Tabs);
        Assert.Equal("a.md", m.Active!.Title);
    }

    [Fact]
    public void OpenInCurrent_NavigatesWithinActiveTab()
    {
        var m = WithTabs("a.md", "b.md");
        m.Activate(0);
        m.OpenInCurrent(@"C:\vault", @"C:\vault\c.md");
        Assert.Equal(2, m.Tabs.Count);           // no new tab
        Assert.Equal("c.md", m.Tabs[0].Title);   // tab 0 navigated
    }

    [Fact]
    public void CloseTab_NonActiveBefore_ShiftsActiveIndexLeft()
    {
        var m = WithTabs("a.md", "b.md", "c.md"); // active = 2 (c)
        Assert.True(m.CloseTab(0));               // remove a
        Assert.Equal(1, m.ActiveIndex);           // c is now at index 1
        Assert.Equal("c.md", m.Active!.Title);
    }

    [Fact]
    public void CloseTab_Active_SelectsNeighbor()
    {
        var m = WithTabs("a.md", "b.md", "c.md");
        m.Activate(1);                            // active = b
        Assert.True(m.CloseTab(1));               // remove b
        Assert.Equal(1, m.ActiveIndex);           // neighbor (c, now index 1)
        Assert.Equal("c.md", m.Active!.Title);
    }

    [Fact]
    public void CloseTab_LastActive_ClampsToEnd()
    {
        var m = WithTabs("a.md", "b.md");         // active = 1 (b)
        Assert.True(m.CloseTab(1));               // remove the last/active
        Assert.Equal(0, m.ActiveIndex);
        Assert.Equal("a.md", m.Active!.Title);
    }

    [Fact]
    public void CloseTab_OnlyTab_ReturnsFalseToSignalWindowClose()
    {
        var m = WithTabs("a.md");
        Assert.False(m.CloseTab(0));              // false → caller closes window
        Assert.Empty(m.Tabs);
        Assert.Equal(-1, m.ActiveIndex);
    }

    [Fact]
    public void CloseTab_OutOfRange_IsNoOp()
    {
        var m = WithTabs("a.md");
        Assert.True(m.CloseTab(5));
        Assert.Single(m.Tabs);
        Assert.Equal(0, m.ActiveIndex);
    }

    [Fact]
    public void SerializeRestore_RoundTripsTabsAndActive()
    {
        var m = WithTabs("a.md", "b.md", "c.md");
        m.Activate(1);
        var sessions = m.Serialize();
        var active = m.ActiveIndex;

        var restored = new TabManager();
        restored.Restore(sessions, active, _ => true);

        Assert.Equal(3, restored.Tabs.Count);
        Assert.Equal(1, restored.ActiveIndex);
        Assert.Equal(new[] { "a.md", "b.md", "c.md" },
            restored.Tabs.Select(t => t.Title).ToArray());
    }

    [Fact]
    public void Restore_DropsTabsWhoseRootIsGone_AndRemapsActive()
    {
        // Three tabs in three folders; the middle folder no longer exists and was active.
        var sessions = new List<TabSession>
        {
            new() { VaultRoot = @"C:\one",  File = @"C:\one\a.md" },
            new() { VaultRoot = @"C:\gone", File = @"C:\gone\b.md" },
            new() { VaultRoot = @"C:\three", File = @"C:\three\c.md" },
        };
        var m = new TabManager();
        m.Restore(sessions, activeIndex: 1, rootExists: root => root != @"C:\gone");

        Assert.Equal(2, m.Tabs.Count);
        Assert.Equal(new[] { "a.md", "c.md" }, m.Tabs.Select(t => t.Title).ToArray());
        // The dropped tab was active → clamp into the surviving set rather than dangle.
        Assert.InRange(m.ActiveIndex, 0, 1);
    }

    [Fact]
    public void Restore_AllRootsGone_LeavesNoTabs()
    {
        var sessions = new List<TabSession> { new() { VaultRoot = @"C:\gone", File = @"C:\gone\a.md" } };
        var m = new TabManager();
        m.Restore(sessions, 0, _ => false);
        Assert.Empty(m.Tabs);
        Assert.Equal(-1, m.ActiveIndex);
    }

    [Fact]
    public void Restore_KeepsBlankRootTab()
    {
        var sessions = new List<TabSession> { new() { VaultRoot = null, File = null } };
        var m = new TabManager();
        m.Restore(sessions, 0, _ => false);       // rootExists never consulted for null root
        Assert.Single(m.Tabs);
        Assert.Equal("New tab", m.Tabs[0].Title);
    }
}
