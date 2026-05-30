using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>
/// Owns the open folder, its scanned tree, and a debounced file watcher.
/// FileSystemWatcher fires on a thread-pool thread, so every event marshals
/// onto the UI dispatcher captured at construction. The debounce timer also
/// runs on the UI dispatcher — DispatcherTimer's default ctor uses
/// CurrentDispatcher, which on a thread-pool thread creates a dispatcher
/// that's never pumped (timer ticks never fire). Hence the explicit ctor.
/// </summary>
public class VaultService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private readonly HashSet<string> _pendingChanged = new(StringComparer.OrdinalIgnoreCase);
    private bool _treeDirty;
    // Bumped per rescan so an out-of-order or superseded background walk can
    // be discarded when it completes.
    private int _rescanToken;
    private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;
    // Monotonically increasing counter bumped on every Open / OpenAsync. The
    // async path captures it and bails on its post-await mutations if a newer
    // open has run during the await — otherwise the continuation would stomp
    // the user's sync vault switch.
    private int _openGeneration;

    public string Root { get; private set; } = "";
    public VaultNode? RootNode { get; private set; }
    public string? ActiveFile { get; private set; }

    public event Action? TreeChanged;
    public event Action<string>? ActiveFileChanged; // path of file that changed on disk

    public bool IsOpen => !string.IsNullOrEmpty(Root) && RootNode != null;

    public void Open(string folderPath)
    {
        _openGeneration++;
        DisposeWatcher();

        if (!Directory.Exists(folderPath))
        {
            Root = "";
            RootNode = null;
            TreeChanged?.Invoke();
            return;
        }

        Root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        RootNode = BuildNode(new DirectoryInfo(Root));
        if (RootNode != null) RootNode.IsExpanded = true;
        TreeChanged?.Invoke();
        StartWatcher();
    }

    /// <summary>
    /// Same as <see cref="Open"/> but performs the recursive directory scan on
    /// a worker thread. Used at startup so the scan can overlap with WebView2
    /// initialization instead of blocking the UI thread. The TreeChanged
    /// event is raised back on the calling synchronization context. If a
    /// newer Open() or OpenAsync() runs during the await, this call's
    /// post-await mutations are skipped so the newer state stands.
    /// </summary>
    public async Task OpenAsync(string folderPath)
    {
        var gen = ++_openGeneration;
        DisposeWatcher();

        if (!Directory.Exists(folderPath))
        {
            if (gen != _openGeneration) return;
            Root = "";
            RootNode = null;
            TreeChanged?.Invoke();
            return;
        }

        Root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        var root = Root;
        var built = await Task.Run(() => BuildNode(new DirectoryInfo(root)));
        // If a synchronous Open() ran during the await, it already set Root /
        // RootNode and started a watcher for the newer folder. Don't trample
        // that state with our older scan.
        if (gen != _openGeneration) return;
        RootNode = built;
        if (RootNode != null) RootNode.IsExpanded = true;
        TreeChanged?.Invoke();
        StartWatcher();
    }

    /// <summary>True if no newer Open / OpenAsync has run since the given generation was captured.</summary>
    public bool IsCurrentGeneration(int generation) => generation == _openGeneration;

    /// <summary>Snapshot the current open generation; pair with <see cref="IsCurrentGeneration"/>.</summary>
    public int CaptureGeneration() => _openGeneration;

    private void StartWatcher()
    {
        if (string.IsNullOrEmpty(Root)) return;
        try
        {
            _watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnFsRenamed;
            _watcher.Changed += OnFsChanged;
            _watcher.Error += (_, _) =>
            {
                // A buffer overflow (a burst of changes outran the watcher's
                // internal buffer) silently drops events, leaving the tree out
                // of sync with disk. Recover by scheduling a full rescan.
                _uiDispatcher.BeginInvoke(() => { _treeDirty = true; EnsureDebounce(); });
            };
        }
        catch
        {
            // Watcher is best-effort.
        }
    }

    public void SetActiveFile(string? filePath)
    {
        ActiveFile = filePath;
    }

    /// <summary>
    /// Expand every ancestor folder in the tree so the given file's row is
    /// visible. Each folder's IsExpanded notifies the bound TreeView, which
    /// re-renders. The user can still collapse a folder manually afterward —
    /// we only re-expand when a file inside it gets opened.
    /// </summary>
    public void ExpandToFile(string filePath)
    {
        if (RootNode == null || string.IsNullOrEmpty(Root)) return;
        if (!filePath.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) return;

        var rel = Path.GetRelativePath(Root, filePath);
        if (rel == "." || string.IsNullOrEmpty(rel)) return;

        var segments = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                 StringSplitOptions.RemoveEmptyEntries);

        var current = RootNode;
        current.IsExpanded = true;
        // Walk every segment except the file name itself.
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            VaultNode? next = null;
            foreach (var c in current.Children)
            {
                if (c.Kind == VaultNodeKind.Folder
                    && string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase))
                { next = c; break; }
            }
            if (next == null) return;
            next.IsExpanded = true;
            current = next;
        }
    }

    // Backstop against pathological nesting. The reparse-point skip below is
    // what actually prevents junction/symlink cycles — an uncatchable
    // StackOverflowException — since on NTFS a directory cycle can only occur
    // through a reparse point; this cap just bounds stack use as a belt-and-
    // braces measure.
    private const int MaxScanDepth = 64;

    private VaultNode? BuildNode(DirectoryInfo dir, int depth = 0)
    {
        VaultNode node;
        try
        {
            node = new VaultNode
            {
                Name = dir.Name,
                FullPath = dir.FullName,
                Kind = VaultNodeKind.Folder,
                Depth = depth,
            };
            if (depth < MaxScanDepth)
            {
                foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    // Skip reparse points (junctions/symlinks): following one
                    // that points to an ancestor recurses forever and crashes
                    // the process; one pointing outside silently pulls in
                    // external content (and the watcher would follow it too).
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var child = BuildNode(sub, depth + 1);
                    if (child != null) node.Children.Add(child);
                }
            }
            foreach (var f in dir.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Children.Add(new VaultNode
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    Kind = VaultNodeKind.File,
                    Depth = depth + 1,
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        return node;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        var path = e.FullPath;
        _uiDispatcher.BeginInvoke(() =>
        {
            _treeDirty = true;
            // A create/delete of the open file (editors that save by replacing
            // the file fire delete+create) must reload it, not just rebuild the
            // tree — Flush only reloads paths recorded in _pendingChanged.
            _pendingChanged.Add(path);
            EnsureDebounce();
        });
    }

    private void OnFsRenamed(object sender, RenamedEventArgs e)
    {
        var oldPath = e.OldFullPath;
        var newPath = e.FullPath;
        _uiDispatcher.BeginInvoke(() =>
        {
            _treeDirty = true;
            // Atomic-save (write temp, rename it over the target) renames *to*
            // the open file's path; a plain rename moves the open file *away*.
            // Follow the latter so the view keeps tracking it, and signal a
            // reload either way via _pendingChanged.
            if (ActiveFile != null &&
                oldPath.Equals(ActiveFile, StringComparison.OrdinalIgnoreCase))
            {
                ActiveFile = newPath;
            }
            _pendingChanged.Add(newPath);
            EnsureDebounce();
        });
    }

    private void OnFsChanged(object sender, FileSystemEventArgs e)
    {
        var path = e.FullPath;
        _uiDispatcher.BeginInvoke(() => { _pendingChanged.Add(path); EnsureDebounce(); });
    }

    private void EnsureDebounce()
    {
        // Runs on the UI dispatcher (callers marshal). Lazily build the timer
        // with the captured UI dispatcher so its ticks fire here.
        if (_debounce == null)
        {
            _debounce = new DispatcherTimer(DispatcherPriority.Normal, _uiDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _debounce.Tick += (_, _) => Flush();
        }
        _debounce.Stop();
        _debounce.Start();
    }

    private void Flush()
    {
        _debounce?.Stop();
        var changed = _pendingChanged.ToArray();
        _pendingChanged.Clear();
        var rebuild = _treeDirty;
        _treeDirty = false;

        if (rebuild) Rescan();

        if (ActiveFile != null)
        {
            foreach (var p in changed)
            {
                if (p.Equals(ActiveFile, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveFileChanged?.Invoke(ActiveFile);
                    break;
                }
            }
        }
    }

    private void Rescan()
    {
        if (string.IsNullOrEmpty(Root)) return;
        var root = Root;
        if (!Directory.Exists(root))
        {
            RootNode = null;
            TreeChanged?.Invoke();
            return;
        }

        // Snapshot which folders are currently expanded so we can restore that
        // state on the freshly-built tree. Without this, every file watcher
        // event (e.g. a file deletion) collapses the whole tree.
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (RootNode != null) CollectExpanded(RootNode, expanded);

        // Walk the directory off the UI thread — on a large/deep vault this is
        // slow, and Rescan runs inside the debounce tick on the UI thread, so
        // an inline walk would freeze the window on every change burst. Apply
        // the result back on the dispatcher, discarding it if a newer open or
        // rescan has superseded this one.
        var gen = _openGeneration;
        var token = ++_rescanToken;
        Task.Run(() =>
        {
            var newRoot = BuildNode(new DirectoryInfo(root));
            _uiDispatcher.BeginInvoke(() =>
            {
                if (gen != _openGeneration || token != _rescanToken) return;
                if (newRoot != null)
                {
                    newRoot.IsExpanded = true;
                    RestoreExpanded(newRoot, expanded);
                }
                RootNode = newRoot;
                TreeChanged?.Invoke();
            });
        });
    }

    private static void CollectExpanded(VaultNode node, HashSet<string> set)
    {
        if (node.Kind == VaultNodeKind.Folder && node.IsExpanded)
            set.Add(node.FullPath);
        foreach (var c in node.Children) CollectExpanded(c, set);
    }

    private static void RestoreExpanded(VaultNode node, HashSet<string> set)
    {
        if (node.Kind == VaultNodeKind.Folder && set.Contains(node.FullPath))
            node.IsExpanded = true;
        foreach (var c in node.Children) RestoreExpanded(c, set);
    }

    private void DisposeWatcher()
    {
        if (_watcher != null)
        {
            try { _watcher.EnableRaisingEvents = false; } catch { }
            _watcher.Dispose();
            _watcher = null;
        }
        _debounce?.Stop();
        _debounce = null;
        lock (_pendingChanged) { _pendingChanged.Clear(); _treeDirty = false; }
    }

    public void Dispose()
    {
        DisposeWatcher();
    }

    public static (string folder, string? file) ResolveInput(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return ("", null);
        try
        {
            var full = Path.GetFullPath(arg);
            if (Directory.Exists(full)) return (full, null);
            if (File.Exists(full)) return (Path.GetDirectoryName(full) ?? "", full);
        }
        catch { }
        return ("", null);
    }
}
