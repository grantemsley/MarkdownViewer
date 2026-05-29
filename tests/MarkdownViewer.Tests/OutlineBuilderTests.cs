using System.Collections.Generic;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class OutlineBuilderTests
{
    private static HeadingViewModel H(int level, string text, params HeadingViewModel[] children)
    {
        var n = new HeadingViewModel { Level = level, Text = text, Id = text, IsExpanded = true };
        foreach (var c in children) n.Children.Add(c);
        return n;
    }

    [Fact]
    public void ApplyCollapse_LevelBelowThreshold_IsExpanded()
    {
        var h = H(2, "Intro");
        OutlineBuilder.ApplyCollapse(new[] { h }, threshold: 4, needle: "");
        Assert.True(h.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_LevelAtThreshold_IsCollapsed()
    {
        var h = H(4, "Deep");
        OutlineBuilder.ApplyCollapse(new[] { h }, threshold: 4, needle: "");
        Assert.False(h.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_LevelAboveThreshold_IsCollapsed()
    {
        var h = H(5, "Deeper");
        OutlineBuilder.ApplyCollapse(new[] { h }, threshold: 4, needle: "");
        Assert.False(h.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_Threshold7_NeverCollapsesByLevel()
    {
        // Max markdown heading is H6; threshold 7 means "never".
        var nodes = new List<HeadingViewModel>
        {
            H(1, "A"), H(2, "B"), H(3, "C"), H(4, "D"), H(5, "E"), H(6, "F"),
        };
        OutlineBuilder.ApplyCollapse(nodes, threshold: 7, needle: "");
        foreach (var n in nodes) Assert.True(n.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_ContainingNeedle_IsCollapsed()
    {
        var match = H(2, "Appendix A");
        var miss = H(2, "Introduction");
        OutlineBuilder.ApplyCollapse(new[] { match, miss }, threshold: 7, needle: "Appendix");
        Assert.False(match.IsExpanded);
        Assert.True(miss.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_ContainingNeedle_CaseInsensitive()
    {
        var h = H(2, "Appendix A");
        OutlineBuilder.ApplyCollapse(new[] { h }, threshold: 7, needle: "APPENDIX");
        Assert.False(h.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_EmptyNeedle_DoesNotFilter()
    {
        var h = H(2, "Anything");
        OutlineBuilder.ApplyCollapse(new[] { h }, threshold: 7, needle: "");
        Assert.True(h.IsExpanded);
    }

    [Fact]
    public void ApplyCollapse_RecursesIntoChildren()
    {
        var child = H(4, "Deep");
        var root = H(1, "Top", child);
        OutlineBuilder.ApplyCollapse(new[] { root }, threshold: 4, needle: "");
        Assert.True(root.IsExpanded);
        Assert.False(child.IsExpanded);
    }
}
