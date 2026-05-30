using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class WindowsIntegrationServiceTests
{
    [Theory]
    [InlineData("md", ".md")]
    [InlineData(".md", ".md")]
    [InlineData("  .jsonl  ", ".jsonl")]
    [InlineData("jsonl", ".jsonl")]
    [InlineData(".markdown", ".markdown")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeExtension_AddsSingleLeadingDot(string input, string expected)
    {
        Assert.Equal(expected, WindowsIntegrationService.NormalizeExtension(input));
    }

    [Fact]
    public void NormalizeExtension_DoesNotDoubleTheDot()
    {
        Assert.Equal(".md", WindowsIntegrationService.NormalizeExtension(".md"));
    }

    [Fact]
    public void BuildCommand_QuotesExeAndToken()
    {
        var exe = @"C:\Program Files\MarkdownViewer\MarkdownViewer.exe";
        Assert.Equal($"\"{exe}\" \"%1\"", WindowsIntegrationService.BuildCommand(exe, "%1"));
        Assert.Equal($"\"{exe}\" \"%V\"", WindowsIntegrationService.BuildCommand(exe, "%V"));
    }

    [Fact]
    public void DefaultExtensions_AreMdAndJsonl()
    {
        Assert.Equal(new[] { ".md", ".jsonl" }, WindowsIntegrationService.DefaultExtensions);
    }
}
