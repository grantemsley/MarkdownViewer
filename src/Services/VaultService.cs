using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>
/// Owns the open folder, its lazily-scanned tree, and a debounced file watcher.
///
/// The tree is scanned <b>one folder level at a time</b>: opening a folder scans
/// only its immediate children, and each folder's children are loaded on demand
/// when it's expanded or revealed. This keeps opening a huge/deep tree (e.g. a
/// Windows home dir with AppData) instant instead of walking the whole thing.
///
/// FileSystemWatcher fires on a thread-pool thread, so every event marshals onto
/// the UI dispatcher captured at construction. The debounce timer also runs on
/// the UI dispatcher — DispatcherTimer's default ctor uses CurrentDispatcher,
/// which on a thread-pool thread creates a dispatcher that's never pumped (timer
/// ticks never fire). Hence the explicit ctor.
///
/// On a change the watcher reconciles only the <i>affected loaded folder</i>
/// (one level), not the whole tree — events under unloaded/collapsed folders are
/// dropped (they'll be scanned fresh on expand). This is what stops AppData churn
/// from re-freezing the app after open.
/// </summary>
public class VaultService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private readonly HashSet<string> _pendingChanged = new(StringComparer.OrdinalIgnoreCase);
    // Directories whose child list may have changed (parent of a created/deleted/
    // renamed entry). Reconciled — one level each — on the next debounce tick.
    private readonly HashSet<string> _dirtyFolders = new(StringComparer.OrdinalIgnoreCase);
    // Set by a watcher buffer overflow (events were dropped): reconcile every
    // loaded folder since we can't know which ones changed.
    private bool _reconcileAll;
    private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;
    // Monotonically increasing counter bumped on every Open / OpenAsync. The
    // async path captures it and bails on its post-await mutations if a newer
    // open has run during the await — otherwise the continuation would stomp
    // the user's sync vault switch.
    private int _openGeneration;

    // Every loaded folder, keyed by full path. Lets a watcher event find the
    // affected folder node in O(1) and decide whether it's even loaded. Mutated
    // on the UI thread only (LoadChildren, reconcile, Open continuation).
    private readonly Dictionary<string, VaultNode> _loaded =
        new(StringComparer.OrdinalIgnoreCase);

    public string Root { get; private set; } = "";
    public VaultNode? RootNode { get; private set; }
    public string? ActiveFile { get; private set; }

    public event Action? TreeChanged;
    public event Action<string>? ActiveFileChanged;     // path of file that changed on disk
    /// <summary>
    /// Raised after a folder's children are (re)materialized — on lazy load and
    /// on watcher reconcile. The UI uses it to apply the visibility filter to the
    /// new children. Always raised on the UI thread.
    /// </summary>
    public event Action<VaultNode>? FolderChildrenChanged;

    public bool IsOpen => !string.IsNullOrEmpty(Root) && RootNode != null;

    public void Open(string folderPath)
    {
        _openGeneration++;
        DisposeWatcher();
        _loaded.Clear();

        if (!Directory.Exists(folderPath))
        {
            Root = "";
            RootNode = null;
            TreeChanged?.Invoke();
            return;
        }

        Root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        RootNode = ScanOneLevel(new DirectoryInfo(Root));
        if (RootNode != null) { RootNode.IsExpanded = true; Register(RootNode); }
        TreeChanged?.Invoke();
        StartWatcher();
    }

    /// <summary>
    /// Same as <see cref="Open"/> but performs the (one-level) directory scan on
    /// a worker thread. Used at startup so the scan can overlap with WebView2
    /// initialization. The TreeChanged event is raised back on the calling
    /// synchronization context. If a newer Open() or OpenAsync() runs during the
    /// await, this call's post-await mutations are skipped so the newer state
    /// stands. (With lazy loading the scan is cheap, but this is kept so startup
    /// stays fully off the UI thread.)
    /// </summary>
    public async Task OpenAsync(string folderPath)
    {
        var gen = ++_openGeneration;
        DisposeWatcher();
        _loaded.Clear();

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
        var built = await Task.Run(() => ScanOneLevel(new DirectoryInfo(root)));
        // If a synchronous Open() ran during the await, it already set Root /
        // RootNode and started a watcher for the newer folder. Don't trample
        // that state with our older scan.
        if (gen != _openGeneration) return;
        RootNode = built;
        if (RootNode != null) { RootNode.IsExpanded = true; Register(RootNode); }
        TreeChanged?.Invoke();
        StartWatcher();
    }

    /// <summary>True if no newer Open / OpenAsync has run since the given generation was captured.</summary>
    public bool IsCurrentGeneration(int generation) => generation == _openGeneration;

    /// <summary>Snapshot the current open generation; pair with <see cref="IsCurrentGeneration"/>.</summary>
    public int CaptureGeneration() => _openGeneration;

    private void Register(VaultNode folder) => _loaded[folder.FullPath] = folder;

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
                // of sync with disk. Recover by reconciling every loaded folder.
                _uiDispatcher.BeginInvoke(() => { _reconcileAll = true; EnsureDebounce(); });
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

    // ───────────────────────── scanning ─────────────────────────

    // Build a folder node and scan exactly its immediate children.
    private VaultNode? ScanOneLevel(DirectoryInfo dir, int depth = 0)
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
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        PopulateChildren(node);
        return node;
    }

    // Materialize a folder's immediate children. Pure w.r.t. service state
    // (no dictionary mutation) so it can run on a worker thread during open.
    private void PopulateChildren(VaultNode folder)
    {
        folder.Children.Clear();
        var dir = new DirectoryInfo(folder.FullPath);
        try
        {
            foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Skip reparse points (junctions/symlinks): following one that
                // points to an ancestor recurses forever; one pointing outside
                // silently pulls in external content (and the watcher would
                // follow it too).
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                folder.Children.Add(MakeFolderNode(sub, folder.Depth + 1));
            }
            foreach (var f in dir.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                folder.Children.Add(new VaultNode
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    Kind = VaultNodeKind.File,
                    Depth = folder.Depth + 1,
                });
            }
        }
        catch (UnauthorizedAccessException) { /* unreadable folder → no children */ }
        catch (IOException) { }
        folder.ChildrenLoaded = true;
    }

    private static VaultNode MakeFolderNode(DirectoryInfo sub, int depth)
    {
        var node = new VaultNode
        {
            Name = sub.Name,
            FullPath = sub.FullName,
            Kind = VaultNodeKind.Folder,
            Depth = depth,
        };
        SetHasChildren(node, HasAnyChildren(sub.FullName));
        return node;
    }

    // Cheap "does this folder have any entries" peek. EnumerateFileSystemEntries
    // is lazy — it stops at the first hit, vs GetDirectories()/GetFiles() which
    // materialize whole arrays. A folder whose only entry is a junction yields a
    // (rare) false arrow that expands to empty; an acceptable trade-off.
    private static bool HasAnyChildren(string path)
    {
        try
        {
            foreach (var _ in Directory.EnumerateFileSystemEntries(path)) return true;
            return false;
        }
        catch { return false; }
    }

    // Keep a folder's placeholder in sync with whether it has children. Loaded
    // folders carry their real children, so they never get a placeholder.
    private static void SetHasChildren(VaultNode folder, bool has)
    {
        folder.HasChildren = has;
        if (folder.ChildrenLoaded) return;
        folder.Children.Clear();
        if (has) folder.Children.Add(VaultNode.MakePlaceholder(folder.Depth + 1));
    }

    /// <summary>
    /// Load a folder's children on demand (no-op if already loaded). Called when
    /// the user expands a folder or when revealing a path. Raises
    /// <see cref="FolderChildrenChanged"/> so the UI can filter the new children.
    /// Must be called on the UI thread.
    /// </summary>
    public void LoadChildren(VaultNode folder)
    {
        if (folder.Kind != VaultNodeKind.Folder || folder.ChildrenLoaded) return;
        PopulateChildren(folder);
        Register(folder);
        FolderChildrenChanged?.Invoke(folder);
    }

    // ───────────────────────── reveal / expand-to-file ─────────────────────────

    /// <summary>
    /// Walk the tree to <paramref name="fullPath"/>, loading each folder along the
    /// way (lazy folders may not be materialized yet) and optionally expanding the
    /// ancestors so the target row is visible. Returns the target node, or null if
    /// the path is outside the vault or no longer exists.
    /// </summary>
    public VaultNode? RevealPath(string fullPath, bool expandAncestors)
    {
        if (RootNode == null || string.IsNullOrEmpty(Root)) return null;
        if (!fullPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) return null;

        var current = RootNode;
        if (expandAncestors) current.IsExpanded = true;
        LoadChildren(current);

        var rel = Path.GetRelativePath(Root, fullPath);
        if (rel == "." || string.IsNullOrEmpty(rel)) return current;

        var segments = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                 StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            LoadChildren(current);
            VaultNode? next = null;
            foreach (var c in current.Children)
            {
                if (c.IsPlaceholder) continue;
                if (string.Equals(c.Name, segments[i], StringComparison.OrdinalIgnoreCase)) { next = c; break; }
            }
            if (next == null) return null;
            // Expand every ancestor (i.e. every segment but the last).
            if (expandAncestors && i < segments.Length - 1) next.IsExpanded = true;
            current = next;
        }
        return current;
    }

    // ───────────────────────── file watch / reconcile ─────────────────────────

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        var path = e.FullPath;
        _uiDispatcher.BeginInvoke(() =>
        {
            MarkFolderDirty(Path.GetDirectoryName(path));
            // A create/delete of the open file (editors that save by replacing
            // the file fire delete+create) must reload it, not just refresh the
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
            MarkFolderDirty(Path.GetDirectoryName(oldPath));
            MarkFolderDirty(Path.GetDirectoryName(newPath));
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
        // Content/size/lastwrite change — doesn't alter the tree structure, only
        // matters for reloading the open file.
        var path = e.FullPath;
        _uiDispatcher.BeginInvoke(() => { _pendingChanged.Add(path); EnsureDebounce(); });
    }

    private void MarkFolderDirty(string? folder)
    {
        if (!string.IsNullOrEmpty(folder)) _dirtyFolders.Add(folder);
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
        var dirty = _dirtyFolders.ToArray();
        _dirtyFolders.Clear();
        var reconcileAll = _reconcileAll;
        _reconcileAll = false;

        if (!string.IsNullOrEmpty(Root) && !Directory.Exists(Root))
        {
            // The open folder itself vanished — clear the tree.
            RootNode = null;
            _loaded.Clear();
            TreeChanged?.Invoke();
            return;
        }

        if (reconcileAll)
        {
            // Snapshot — reconcile mutates _loaded.
            foreach (var folder in _loaded.Values.ToArray()) ReconcileFolder(folder);
        }
        else
        {
            foreach (var path in dirty)
                if (_loaded.TryGetValue(path, out var folder)) ReconcileFolder(folder);
            // Folders not in _loaded are unloaded/collapsed — skip; they'll be
            // scanned fresh when expanded.
        }

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

    // Re-scan one loaded folder's level and merge into its existing children,
    // preserving surviving nodes (and their expansion / loaded subtrees) so a
    // sibling change doesn't collapse or reload anything.
    private void ReconcileFolder(VaultNode folder)
    {
        var dir = new DirectoryInfo(folder.FullPath);
        if (!dir.Exists) return; // its own removal is handled by the parent's reconcile

        var target = new List<VaultNode>();
        try
        {
            foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var existing = folder.Children.FirstOrDefault(c =>
                    !c.IsPlaceholder && c.Kind == VaultNodeKind.Folder &&
                    string.Equals(c.Name, sub.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // Refresh the arrow; keep its identity, expansion and any
                    // loaded subtree untouched.
                    SetHasChildren(existing, HasAnyChildren(sub.FullName));
                    target.Add(existing);
                }
                else
                {
                    target.Add(MakeFolderNode(sub, folder.Depth + 1));
                }
            }
            foreach (var f in dir.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                var existing = folder.Children.FirstOrDefault(c =>
                    !c.IsPlaceholder && c.Kind == VaultNodeKind.File &&
                    string.Equals(c.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                target.Add(existing ?? new VaultNode
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    Kind = VaultNodeKind.File,
                    Depth = folder.Depth + 1,
                });
            }
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        // Un-register any loaded folders that are being dropped so stale entries
        // don't linger in the lookup (and their later events get ignored).
        foreach (var child in folder.Children)
            if (!target.Contains(child)) Unregister(child);

        TreeReconciler.Sync(folder.Children, target);
        FolderChildrenChanged?.Invoke(folder);
    }

    private void Unregister(VaultNode node)
    {
        if (node.Kind != VaultNodeKind.Folder) return;
        _loaded.Remove(node.FullPath);
        foreach (var c in node.Children) Unregister(c);
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
        _pendingChanged.Clear();
        _dirtyFolders.Clear();
        _reconcileAll = false;
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
