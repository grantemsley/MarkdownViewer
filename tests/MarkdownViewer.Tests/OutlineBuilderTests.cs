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

    private static HeadingEntry E(int level, string text) =>
        new HeadingEntry { Level = level, Text = text, Id = text };

    [Fact]
    public void BuildTree_EmptyList_ReturnsEmpty()
    {
        var roots = OutlineBuilder.BuildTree(new List<HeadingEntry>());
        Assert.Empty(roots);
    }

    [Fact]
    public void BuildTree_SingleH1_OneRootAtDepthZero()
    {
        var roots = OutlineBuilder.BuildTree(new[] { E(1, "A") });
        var a = Assert.Single(roots);
        Assert.Equal("A", a.Text);
        Assert.Equal(1, a.Level);
        Assert.Equal(0, a.Depth);
        Assert.Empty(a.Children);
    }

    [Fact]
    public void BuildTree_H1H2H3Chain_NestsWithIncreasingDepth()
    {
        var roots = OutlineBuilder.BuildTree(new[] { E(1, "A"), E(2, "B"), E(3, "C") });
        var a = Assert.Single(roots);
        Assert.Equal(0, a.Depth);
        var b = Assert.Single(a.Children);
        Assert.Equal(1, b.Depth);
        var c = Assert.Single(b.Children);
        Assert.Equal(2, c.Depth);
    }

    [Fact]
    public void BuildTree_SiblingCollapse_H2PopsH3AndLandsUnderH1AtDepthOne()
    {
        // H1, H3, H2: H3 nests under H1 first; H2 then pops H3 off the stack
        // (3 >= 2) and lands as a second child of H1, not nested under H3.
        var roots = OutlineBuilder.BuildTree(new[] { E(1, "A"), E(3, "B"), E(2, "C") });
        var a = Assert.Single(roots);
        Assert.Equal(2, a.Children.Count);
        Assert.Equal("B", a.Children[0].Text);
        Assert.Equal("C", a.Children[1].Text);
        Assert.Equal(1, a.Children[0].Depth);
        Assert.Equal(1, a.Children[1].Depth);
        Assert.Empty(a.Children[0].Children);
    }

    [Fact]
    public void BuildTree_StartsAtH2ThenH1_H1BecomesNewRoot()
    {
        var roots = OutlineBuilder.BuildTree(new[] { E(2, "A"), E(1, "B") });
        Assert.Equal(2, roots.Count);
        Assert.Equal("A", roots[0].Text);
        Assert.Equal(0, roots[0].Depth);
        Assert.Equal("B", roots[1].Text);
        Assert.Equal(0, roots[1].Depth);
    }

    [Fact]
    public void BuildTree_EqualLevels_AreSiblings()
    {
        var roots = OutlineBuilder.BuildTree(new[] { E(1, "A"), E(2, "B"), E(2, "C") });
        var a = Assert.Single(roots);
        Assert.Equal(2, a.Children.Count);
        Assert.Equal(1, a.Children[0].Depth);
        Assert.Equal(1, a.Children[1].Depth);
        Assert.Empty(a.Children[0].Children);
    }

    [Fact]
    public void BuildTree_SkippedLevels_H1ThenH4_DepthIsOne()
    {
        var roots = OutlineBuilder.BuildTree(new[] { E(1, "A"), E(4, "B") });
        var a = Assert.Single(roots);
        var b = Assert.Single(a.Children);
        Assert.Equal(4, b.Level);
        Assert.Equal(1, b.Depth);
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
