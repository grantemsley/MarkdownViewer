using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MarkdownViewer.Services;

/// <summary>
/// Looks up the small system shell icon associated with a file extension or
/// "is a folder", and caches the result so the tree doesn't hit shell32 per
/// row. Uses SHGFI_USEFILEATTRIBUTES so the lookup doesn't depend on the
/// path actually existing — extension drives everything.
/// </summary>
public static class FileIconProvider
{
    private static readonly Dictionary<string, ImageSource?> _byExt = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _folderIcon;
    private static readonly object _lock = new();

    public static ImageSource? Get(string path, bool isFolder)
    {
        if (isFolder)
        {
            lock (_lock)
            {
                if (_folderIcon != null) return _folderIcon;
                _folderIcon = LoadFolderIcon();
                return _folderIcon;
            }
        }

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) ext = "(none)";

        lock (_lock)
        {
            if (_byExt.TryGetValue(ext, out var cached)) return cached;
            var icon = LoadFileIcon(ext);
            _byExt[ext] = icon;
            return icon;
        }
    }

    private static ImageSource? LoadFileIcon(string ext)
    {
        // "(none)" sentinel back to "" so SHGetFileInfo gets a real filename.
        var stub = "file" + (ext.StartsWith('.') ? ext : "");
        return ShellIcon(stub, FILE_ATTRIBUTE_NORMAL);
    }

    private static ImageSource? LoadFolderIcon()
    {
        // The path doesn't need to exist; SHGFI_USEFILEATTRIBUTES means we're
        // just hinting "this is a directory" via the attribute flag.
        return ShellIcon("Folder", FILE_ATTRIBUTE_DIRECTORY);
    }

    private static ImageSource? ShellIcon(string path, uint attrs)
    {
        var info = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        IntPtr result;
        try
        {
            result = SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        }
        catch
        {
            return null;
        }
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    // ─── P/Invoke ─────────────────────────────────────────────────────────

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
