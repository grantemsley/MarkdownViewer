using System.Runtime.CompilerServices;
using System.Text;

namespace MarkdownViewer.Tests;

internal static class TestInit
{
    [ModuleInitializer]
    public static void Init()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
