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
///
/// A vault on a network share (UNC or mapped drive) additionally gets a polling
/// fallback: FileSystemWatcher rides SMB change-notify, which between two Windows
/// boxes drops notifications, and after a connection blip the watcher's handle is
/// torn down for good (Error fires at most once, then silence). See the polling
/// section at the bottom of this file.
/// </summary>
public class VaultService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private volatile bool _disposed;
    private readonly HashSet<string> _pendingChanged = new(StringComparer.OrdinalIgnoreCase);
    // Directories whose child list may have changed (parent of a created/deleted/
    // renamed entry). Reconciled — one level each — on the next debounce tick.
    private readonly HashSet<string> _dirtyFolders = new(StringComparer.OrdinalIgnoreCase);
    // Set by a watcher buffer overflow (events were dropped): reconcile every
    // loaded folder since we can't know which ones changed.
    private bool _reconcileAll;
    private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;

    // ── polling fallback state (network roots; see the polling section below) ──
    private DispatcherTimer? _poll;
    private bool _isNetworkRoot;
    // True while a poll's IO is in flight on a worker thread. Ticks that land
    // during a scan are skipped, so a slow link self-limits to one scan at a
    // time instead of piling up.
    private bool _pollBusy;
    // Last seen fingerprint per loaded folder. A folder is only reconciled when
    // its fingerprint moves; a folder absent here is seeded silently.
    private readonly Dictionary<string, ulong> _pollSignatures =
        new(StringComparer.OrdinalIgnoreCase);
    // Last seen (path, write time, size) of the open file, so a remote edit that
    // produced no watcher event still triggers a reload.
    private (string Path, DateTime Write, long Length)? _activeStat;
    // Consecutive Flush passes that found the root missing. A network root has
    // to miss twice before the tree is cleared (see Flush).
    private int _rootMissCount;
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

    // Current folder/file sort preferences. Applied when a folder's children are
    // materialized (lazy load, open, watcher reconcile). MainWindow keeps this in
    // sync with AppSettings and calls ResortAll when the user changes it.
    private SortPrefs _sort = new();

    public string Root { get; private set; } = "";
    public VaultNode? RootNode { get; private set; }
    public string? ActiveFile { get; private set; }

    /// <summary>
    /// Update the active sort preferences. Does not re-sort already-loaded
    /// folders — call <see cref="ResortAll"/> for that. Safe to call before Open.
    /// The prefs are cloned: this vault must not see later edits to the caller's
    /// (shared, live) settings object until they are re-applied here explicitly,
    /// otherwise tabs that never get a ResortAll drift into mixed ordering.
    /// </summary>
    public void SetSort(SortPrefs sort) => _sort = sort?.Clone() ?? new SortPrefs();

    private List<VaultNode> SortFolders(IEnumerable<VaultNode> folders) =>
        TreeSorter.Sort(folders, _sort.FolderKey, _sort.FolderDir == "desc");

    private List<VaultNode> SortFiles(IEnumerable<VaultNode> files) =>
        TreeSorter.Sort(files, _sort.FileKey, _sort.FileDir == "desc");

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
        _isNetworkRoot = IsNetworkPath(Root);
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
            if (_disposed || gen != _openGeneration) return;
            Root = "";
            RootNode = null;
            TreeChanged?.Invoke();
            return;
        }

        Root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        _isNetworkRoot = IsNetworkPath(Root);
        var root = Root;
        var built = await Task.Run(() => ScanOneLevel(new DirectoryInfo(root)));
        // If a synchronous Open() ran during the await, it already set Root /
        // RootNode and started a watcher for the newer folder. Don't trample
        // that state with our older scan. _disposed guards the case where the
        // tab was closed mid-scan: without it this continuation would call
        // StartWatcher() and leave a live FileSystemWatcher on a dead service.
        if (_disposed || gen != _openGeneration) return;
        RootNode = built;
        if (RootNode != null) { RootNode.IsExpanded = true; Register(RootNode); }
        TreeChanged?.Invoke();
        StartWatcher();
    }

    /// <summary>True if no newer Open / OpenAsync has run since the given generation was captured.</summary>
    public bool IsCurrentGeneration(int generation) => generation == _openGeneration;

    /// <summary>Snapshot the current open generation; pair with <see cref="IsCurrentGeneration"/>.</summary>
    public int CaptureGeneration() => _openGeneration;

    private void Register(VaultNode folder)
    {
        _loaded[folder.FullPath] = folder;
        // Seed the poll fingerprint from the scan we just did, not from the
        // first poll: seeding later would silently absorb anything created
        // between the scan and that poll, and the tree would keep missing it
        // until some *later* change happened to move the fingerprint.
        _pollSignatures[folder.FullPath] = SignatureOf(folder);
    }

    private static ulong SignatureOf(VaultNode folder) =>
        DirectorySignature.OfEntries(folder.Children
            .Where(c => !c.IsPlaceholder)
            .Select(c => (c.Name, c.Kind == VaultNodeKind.Folder)));

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
            _watcher.Error += (_, e) =>
            {
                // Two very different failures arrive here, and only one of them
                // leaves a usable watcher behind.
                var fatal = e.GetException() is not InternalBufferOverflowException;
                _uiDispatcher.BeginInvoke(() =>
                {
                    if (_disposed) return;
                    // Either way the tree is now out of sync with disk, since
                    // events were lost: re-scan every loaded folder.
                    _reconcileAll = true;
                    if (fatal)
                    {
                        // A network error (share offline, server rebooted,
                        // sleep/resume) kills the watch handle for good: the
                        // watcher is done, and without this the tree silently
                        // stops updating for the rest of the session. Drop it
                        // and hand over to polling, which rebuilds it once the
                        // root is reachable again. Rebuilding inline instead
                        // would spin if the replacement errors immediately;
                        // going through the poll caps retries at one per tick.
                        DisposeWatcherObject();
                        EnsurePolling();
                    }
                    // A buffer overflow (a burst of changes outran the
                    // watcher's internal buffer) only drops events - the
                    // watcher itself is still live, so it is left alone.
                    EnsureDebounce();
                });
            };
        }
        catch
        {
            // Watcher is best-effort — polling below is the safety net.
            _watcher = null;
        }

        // Poll whenever the watcher can't be trusted: any network root (SMB
        // change-notify goes missing between Windows boxes) or a watcher that
        // couldn't be created at all. A healthy local watcher needs no polling.
        if (_isNetworkRoot || _watcher == null) EnsurePolling();
        else StopPolling();
    }

    public void SetActiveFile(string? filePath)
    {
        ActiveFile = filePath;
        // Re-seed on the next poll rather than comparing the new file against
        // the previous file's size/timestamp, which would fire a spurious
        // "changed" the moment a different file is opened.
        _activeStat = null;
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
        var folders = new List<VaultNode>();
        var files = new List<VaultNode>();
        try
        {
            foreach (var sub in dir.GetDirectories())
            {
                // Junctions/symlinks are shown like any other folder — matching
                // how /__vault/ already serves through them. Loading is one level
                // per expand, so a junction pointing at an ancestor can't recurse:
                // it just costs another click. The watcher does not report changes
                // inside a junction target, so a junctioned subtree is browsable
                // but not live-updated.
                folders.Add(MakeFolderNode(sub, folder.Depth + 1));
            }
            foreach (var f in dir.GetFiles())
                files.Add(MakeFileNode(f, folder.Depth + 1));
        }
        catch (UnauthorizedAccessException) { /* unreadable folder → no children */ }
        catch (IOException) { }
        // Folders always group above files; each group sorted by its own pref.
        foreach (var n in SortFolders(folders)) folder.Children.Add(n);
        foreach (var n in SortFiles(files)) folder.Children.Add(n);
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
            CreatedUtc = SafeCreationUtc(sub),
            ModifiedUtc = SafeWriteUtc(sub),
        };
        SetHasChildren(node, HasAnyChildren(sub.FullName));
        return node;
    }

    private static VaultNode MakeFileNode(FileInfo f, int depth) => new()
    {
        Name = f.Name,
        FullPath = f.FullName,
        Kind = VaultNodeKind.File,
        Depth = depth,
        CreatedUtc = SafeCreationUtc(f),
        ModifiedUtc = SafeWriteUtc(f),
    };

    // The timestamps are already populated on the FileSystemInfo by the
    // GetDirectories()/GetFiles() enumeration, so these are no extra syscalls.
    // A torn/unreadable entry can still throw — fall back to a stable value so
    // sorting never crashes the scan.
    private static DateTime SafeCreationUtc(FileSystemInfo fsi)
    {
        try { return fsi.CreationTimeUtc; } catch { return DateTime.MinValue; }
    }

    private static DateTime SafeWriteUtc(FileSystemInfo fsi)
    {
        try { return fsi.LastWriteTimeUtc; } catch { return DateTime.MinValue; }
    }

    // Cheap "does this folder have any entries" peek. EnumerateFileSystemEntries
    // is lazy — it stops at the first hit, vs GetDirectories()/GetFiles() which
    // materialize whole arrays.
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

    /// <summary>
    /// Re-order every loaded folder's children to match the current sort
    /// preferences, in place (existing node instances keep their identity,
    /// expansion, selection and loaded subtrees). Call after <see cref="SetSort"/>
    /// when the user changes the sort. Unloaded folders need no work — they sort
    /// on their next lazy load. Must run on the UI thread (mutates bound
    /// collections).
    /// </summary>
    public void ResortAll()
    {
        // Snapshot: we only read _loaded here, but stay consistent with other
        // call sites that iterate it.
        foreach (var folder in _loaded.Values.ToArray())
        {
            if (!folder.ChildrenLoaded) continue;
            var folders = folder.Children.Where(c => !c.IsPlaceholder && c.Kind == VaultNodeKind.Folder);
            var files = folder.Children.Where(c => !c.IsPlaceholder && c.Kind == VaultNodeKind.File);
            var target = new List<VaultNode>();
            target.AddRange(SortFolders(folders));
            target.AddRange(SortFiles(files));
            TreeReconciler.Sync(folder.Children, target);
        }
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
        // A FSW callback queued before Dispose() can still land here afterward;
        // don't resurrect the debounce timer on a disposed instance.
        if (_disposed) return;
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
        if (_disposed) return;
        _debounce?.Stop();
        var changed = _pendingChanged.ToArray();
        _pendingChanged.Clear();
        var dirty = _dirtyFolders.ToArray();
        _dirtyFolders.Clear();
        var reconcileAll = _reconcileAll;
        _reconcileAll = false;

        if (!string.IsNullOrEmpty(Root) && !Directory.Exists(Root))
        {
            // The open folder itself vanished — clear the tree. On a network
            // root "vanished" is ambiguous: a dropped Wi-Fi link, a rebooting
            // server or a resumed laptop all make the root unreachable for a
            // few seconds, and wiping the tree (plus every expansion the user
            // built up) for a blip is worse than showing it stale. So a
            // network root has to miss twice in a row; a local root, where a
            // missing folder really is deleted, still clears immediately.
            if (_isNetworkRoot && ++_rootMissCount < 2) return;
            RootNode = null;
            _loaded.Clear();
            _pollSignatures.Clear();
            TreeChanged?.Invoke();
            return;
        }
        _rootMissCount = 0;

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

        var folders = new List<VaultNode>();
        var files = new List<VaultNode>();
        try
        {
            foreach (var sub in dir.GetDirectories())
            {
                var existing = folder.Children.FirstOrDefault(c =>
                    !c.IsPlaceholder && c.Kind == VaultNodeKind.Folder &&
                    string.Equals(c.Name, sub.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // Refresh the arrow; keep its identity, expansion and any
                    // loaded subtree untouched.
                    SetHasChildren(existing, HasAnyChildren(sub.FullName));
                    folders.Add(existing);
                }
                else
                {
                    folders.Add(MakeFolderNode(sub, folder.Depth + 1));
                }
            }
            foreach (var f in dir.GetFiles())
            {
                var existing = folder.Children.FirstOrDefault(c =>
                    !c.IsPlaceholder && c.Kind == VaultNodeKind.File &&
                    string.Equals(c.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                files.Add(existing ?? MakeFileNode(f, folder.Depth + 1));
            }
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        // Same grouping/order as a fresh scan so a reconcile doesn't reshuffle
        // into a different order than the lazy load would produce.
        var target = new List<VaultNode>();
        target.AddRange(SortFolders(folders));
        target.AddRange(SortFiles(files));

        // Un-register any loaded folders that are being dropped so stale entries
        // don't linger in the lookup (and their later events get ignored).
        foreach (var child in folder.Children)
            if (!target.Contains(child)) Unregister(child);

        TreeReconciler.Sync(folder.Children, target);
        // Re-seed from what we just scanned so the next poll compares against
        // the state the tree is actually showing.
        _pollSignatures[folder.FullPath] = SignatureOf(folder);
        FolderChildrenChanged?.Invoke(folder);
    }

    private void Unregister(VaultNode node)
    {
        if (node.Kind != VaultNodeKind.Folder) return;
        _loaded.Remove(node.FullPath);
        _pollSignatures.Remove(node.FullPath);
        foreach (var c in node.Children) Unregister(c);
    }

    // ───────────────────────── network polling fallback ─────────────────────────

    // Why this exists: FileSystemWatcher over SMB depends on the server's
    // change-notify reaching us, and between two Windows machines that is not
    // dependable — notifications go missing under load, and a momentary
    // disconnect (sleep/resume, Wi-Fi roam, server reboot) tears the watch
    // handle down permanently, after which the tree silently stops updating for
    // the rest of the session. So a network vault is *also* polled: each tick
    // fingerprints every loaded folder and stats the open file, then feeds
    // whatever moved into the same dirty-folder / pending-changed path the
    // watcher already uses. Belt and braces: when change-notify does work, the
    // watcher still gives the instant update and the poll finds nothing.
    //
    // Freshness has a floor we don't control. The Windows SMB client caches
    // directory listings and file metadata for 10s by default
    // (DirectoryCacheLifetime / FileInfoCacheLifetime on LanmanWorkstation), so
    // a change made on the other machine is invisible to *any* enumeration —
    // ours, Explorer's — until that cache expires. Polling faster than the
    // cache would just re-read it, so 5s keeps worst-case latency around the
    // cache lifetime without spraying round trips.
    private const int PollIntervalMs = 5000;

    private void EnsurePolling()
    {
        if (_disposed || _poll != null || string.IsNullOrEmpty(Root)) return;
        // Same dispatcher reasoning as the debounce timer: the explicit ctor is
        // required so ticks fire on the UI thread rather than on a dispatcher
        // nobody pumps. Background priority keeps polls behind rendering.
        _poll = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMs),
        };
        _poll.Tick += (_, _) => PollOnce();
        _poll.Start();
    }

    // Stops the timer only. The fingerprints stay: they are seeded by every
    // scan whether or not polling is running, so if the watcher later dies and
    // polling takes over it starts from the state the tree is showing rather
    // than having to re-seed (and swallow whatever changed in between).
    private void StopPolling()
    {
        _poll?.Stop();
        _poll = null;
    }

    // One poll pass. The IO runs on a worker thread (a stalled share must never
    // block the UI); only the comparison and the marking happen back here.
    private async void PollOnce()
    {
        if (_disposed || _pollBusy) return;
        _pollBusy = true;
        try
        {
            var gen = _openGeneration;
            var folders = _loaded.Keys.ToArray();
            var active = ActiveFile;
            var root = Root;
            var scan = await Task.Run(() => ScanForChanges(root, folders, active));
            // A vault switch (or a closed tab) during the scan makes these
            // results describe a folder we no longer show.
            if (_disposed || gen != _openGeneration) return;

            var touched = false;
            foreach (var (path, sig) in scan.Signatures)
            {
                // A folder we have no fingerprint for is being seeded, not
                // changed — otherwise the first poll after open would reconcile
                // the whole tree for nothing.
                if (_pollSignatures.TryGetValue(path, out var prev) && prev != sig)
                {
                    _dirtyFolders.Add(path);
                    touched = true;
                }
                _pollSignatures[path] = sig;
            }
            // Forget folders that are no longer loaded (collapsed away by a
            // reconcile) so a later re-expand seeds fresh instead of comparing
            // against a fingerprint from minutes ago.
            foreach (var stale in _pollSignatures.Keys.Where(k => !_loaded.ContainsKey(k)).ToArray())
                _pollSignatures.Remove(stale);

            if (ActiveFile != null && string.Equals(active, ActiveFile, StringComparison.OrdinalIgnoreCase))
            {
                var known = _activeStat is { } p &&
                            string.Equals(p.Path, ActiveFile, StringComparison.OrdinalIgnoreCase);
                var cur = scan.ActiveStat;
                // Deleted (cur is null) counts as a change too: that's what
                // turns the view into "this file no longer exists", same as the
                // watcher's Deleted event does locally.
                if (known && (cur == null ||
                              cur.Value.Write != _activeStat!.Value.Write ||
                              cur.Value.Length != _activeStat!.Value.Length))
                {
                    _pendingChanged.Add(ActiveFile);
                    touched = true;
                }
                _activeStat = cur is { } c ? (ActiveFile, c.Write, c.Length) : null;
            }

            // Self-heal a watcher that a network error killed, once the root is
            // reachable again. The reachability check rode along with the scan:
            // testing it here would block the UI thread for the SMB timeout
            // whenever the share is actually down.
            if (_watcher == null && scan.RootExists) StartWatcher();

            if (touched) EnsureDebounce();
        }
        catch
        {
            // A poll is best-effort; the next tick tries again.
        }
        finally
        {
            _pollBusy = false;
        }
    }

    // Worker-thread half of a poll: pure IO over paths only (never touches the
    // node tree, which belongs to the UI thread).
    private static (Dictionary<string, ulong> Signatures, (DateTime Write, long Length)? ActiveStat, bool RootExists)
        ScanForChanges(string root, string[] folders, string? activeFile)
    {
        var rootExists = false;
        try { rootExists = !string.IsNullOrEmpty(root) && Directory.Exists(root); }
        catch { }

        var signatures = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            // Unreadable (offline, denied, deleted) yields null: omit it rather
            // than record a value, so an unreachable share reads as "no news"
            // instead of "everything was deleted".
            var sig = DirectorySignature.Compute(folder);
            if (sig.HasValue) signatures[folder] = sig.Value;
        }

        (DateTime Write, long Length)? stat = null;
        if (!string.IsNullOrEmpty(activeFile))
        {
            try
            {
                var fi = new FileInfo(activeFile);
                if (fi.Exists) stat = (fi.LastWriteTimeUtc, fi.Length);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return (signatures, stat, rootExists);
    }

    /// <summary>
    /// True when <paramref name="path"/> lives on a network location: a UNC path
    /// (<c>\\server\share\…</c>) or a mapped network drive. Network vaults get the
    /// polling fallback because SMB change-notify can't be relied on.
    /// </summary>
    public static bool IsNetworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            // Extended-length forms: \\?\UNC\server\share is a share, while
            // plain \\?\C:\… is local despite the leading backslashes.
            if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(@"\\.\UNC\", StringComparison.OrdinalIgnoreCase))
                return true;
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                full.StartsWith(@"\\.\", StringComparison.Ordinal))
                full = full.Substring(4);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;

            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            // Unmapped drive letter, malformed path, etc. Treat as local: the
            // watcher still runs, we just don't add polling.
            return false;
        }
    }

    // ───────────────────────── teardown ─────────────────────────

    // Kill just the watcher object, leaving pending/dirty state alone. Used by
    // the Error handler, which disposes a dead watcher and immediately builds a
    // replacement without losing the events already queued for the next flush.
    private void DisposeWatcherObject()
    {
        if (_watcher == null) return;
        try { _watcher.EnableRaisingEvents = false; } catch { }
        _watcher.Dispose();
        _watcher = null;
    }

    private void DisposeWatcher()
    {
        DisposeWatcherObject();
        StopPolling();
        _pollSignatures.Clear();
        _activeStat = null;
        _pollBusy = false;
        _debounce?.Stop();
        _debounce = null;
        _pendingChanged.Clear();
        _dirtyFolders.Clear();
        _reconcileAll = false;
        _rootMissCount = 0;
        _isNetworkRoot = false;
    }

    public void Dispose()
    {
        // Set before disposing the watcher so any in-flight OpenAsync
        // continuation or already-queued FSW callback sees it and bails out
        // instead of re-arming a watcher/timer on this dead instance.
        _disposed = true;
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
