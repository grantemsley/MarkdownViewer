using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Appearance;
using WpfUiControls = Wpf.Ui.Controls;

namespace MarkdownViewer;

public partial class MainWindow : WpfUiControls.FluentWindow
{
    private readonly AppSettings _settings;
    // Each tab owns its own VaultService (independent tree/watcher per the tabs
    // design). _vault always refers to the ACTIVE tab's vault, so the rest of the
    // window keeps using `_vault` unchanged; only the active runtime changes under
    // it on a tab switch. With a single tab this is behaviour-identical to before.
    private VaultService _vault => _active.Vault;
    private readonly TabManager _tabs = new();
    private readonly Dictionary<TabState, TabRuntime> _runtimes = new();
    private TabRuntime _active = null!;   // seeded in the constructor
    // Strip items, kept parallel to _tabs.Tabs (same order). Bound to the tab strip.
    private readonly ObservableCollection<TabVM> _tabStripItems = new();
    // Guards the ListBox SelectionChanged ⇄ SwitchToTab loop while we sync selection.
    private bool _switchingTabs;
    private bool TabsEnabled => _settings.Tabs.Enabled;
    private readonly string? _initialArg;
    private bool _webViewReady;
    // True once bridge.js has loaded and posted its first "ready" message.
    // Before this, any Send() / file-render is wasted: the JS listener
    // doesn't exist yet, and the "ready" handler re-establishes state.
    private bool _bridgeReady;
    // Precomputed first-doc payload, produced in parallel with WebView2 init.
    // When the WebView reports "ready", we can post it directly without
    // paying Markdig's first-use cost or the file read on the UI thread.
    private InitialRender? _initialRender;
    private Task? _initialRenderTask;

    private sealed record InitialRender(
        string TabId,
        string FilePath,
        RenderedDoc Doc,
        bool ShowLineNumbers);
    // URL currently loaded into the raw-browser iframe, used to distinguish
    // "our initial navigation" and "anchor change" from "user clicked a link"
    // inside the iframe.
    private string? _currentIframeUrl;
    private string? _currentMdFile;
    private readonly ObservableCollection<FolderRow> _pinnedRows = new();
    private readonly ObservableCollection<FolderRow> _currentRows = new();
    private readonly ObservableCollection<FolderRow> _recentRows = new();
    private CoreWebView2Find? _find;
    private DispatcherTimer? _settingsSaveTimer;
    // The update the startup check surfaced in the banner (release page URL +
    // its version string), held so the Download/Dismiss handlers can act on it.
    private string? _pendingUpdateUrl;
    private string _pendingUpdateVersion = "";
    // WebView2 environment creation (runtime discovery + user-data-folder setup)
    // is kicked off as a field initializer — i.e. during construction, before the
    // window is even shown — so it overlaps window layout/first paint instead of
    // waiting for the Loaded handler. EnsureCoreWebView2Async still runs after
    // Loaded (it needs the control in the visual tree). The async helper captures
    // any error into the task so it surfaces in InitializeAsync's try/catch.
    private readonly Task<CoreWebView2Environment> _envTask = CreateWebViewEnvAsync();

    private static async Task<CoreWebView2Environment> CreateWebViewEnvAsync()
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownViewer", "WebView2Cache");
        Directory.CreateDirectory(dataFolder);
        return await CoreWebView2Environment.CreateAsync(null, dataFolder);
    }

    public MainWindow(string? initialArg)
    {
        // Register 1252 (Windows-Western) code page so it's available as a
        // fallback for text decoding on non-UTF-8 files.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        InitializeComponent();
        _settings = SettingsService.Load();
        _initialArg = initialArg;
        // Seed the first tab + its runtime. CreateRuntime wires the vault's events
        // (gated so only the active tab drives the UI) and applies the sort, so
        // _vault/_active resolve from here on.
        var firstTab = _tabs.OpenBlankTab();
        _runtimes[firstTab] = _active = CreateRuntime();
        _tabStripItems.Add(new TabVM(firstTab));
        TabStrip.ItemsSource = _tabStripItems;
        TabStrip.SelectedIndex = 0;
        // The strip row only shows when tabs are enabled; otherwise it's a single
        // implicit tab and the window looks/behaves exactly like before.
        TabStripRow.Visibility = TabsEnabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyWindowState();
        SyncUiPrefs();
        ApplyTheme();

        // Re-push prefs (incl. accent + effective theme) to the WebView when
        // the system or app theme changes after launch. Named method (not a
        // lambda) so MainWindow_Closed can unsubscribe it — this is a static
        // event, which would otherwise pin the window instance.
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;

        PinnedList.ItemsSource = _pinnedRows;
        CurrentList.ItemsSource = _currentRows;
        RecentList.ItemsSource = _recentRows;

        ApplySidebarSplitRatio(_settings.Window.SidebarFolderRatio);

        // (Vault events are wired per-runtime in CreateRuntime, gated to the
        // active tab, rather than once here.)
        // Load a folder's children the first time it's expanded. A class handler
        // on the TreeView catches the routed Expanded from any TreeViewItem.
        FolderTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FolderTree_ItemExpanded));
        // Middle-click a tree row → open that file/folder in a new tab.
        FolderTree.PreviewMouseDown += FolderTree_PreviewMouseDown;

        // Publish the tree's measured width; DepthAdjustedWidth subtracts the
        // per-row indent and a mode-aware chrome buffer (wrap vs ellipsis) so
        // labels wrap/ellipsize inside the sidebar column without clipping.
        FolderTree.SizeChanged += (_, _) =>
            UiPrefs.Instance.SidebarRowMaxWidth = FolderTree.ActualWidth;
        OutlineTree.SizeChanged += (_, _) =>
            UiPrefs.Instance.SidebarRowMaxWidth = OutlineTree.ActualWidth;

        Drop += MainWindow_Drop;
        DragEnter += MainWindow_DragEnter;
        Closed += MainWindow_Closed;
        SizeChanged += (_, _) => ScheduleSave();
        LocationChanged += (_, _) => ScheduleSave();
        KeyDown += MainWindow_KeyDown;

        Loaded += async (_, _) =>
        {
            // SystemThemeWatcher needs a valid HWND, so re-apply theme once
            // the window is loaded — re-hooks the watcher if pref is "system".
            ApplyTheme();
            await InitializeAsync();
            // Fire-and-forget: a notify-only GitHub Releases check that surfaces
            // a banner if a newer version exists. Never blocks startup or throws.
            _ = CheckForUpdatesAsync();
        };
    }

    // ─── Initialization ──────────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        try
        {
            // Resolve the initial folder up front so the vault scan can run
            // in parallel with WebView2 init (WebView2 cold-start dominates,
            // so the scan ends up free under its shadow).
            var (folder, file) = VaultService.ResolveInput(_initialArg)
                                  .Pipe(x => (x.folder, x.file));
            if (string.IsNullOrEmpty(folder))
            {
                // No file/folder arg → restore the previous tab session (all tabs;
                // the active one is opened below, the rest lazily on activation).
                // Falls back to the last single folder when there's nothing to restore.
                if (RestoreTabsFromSession() is { } restored)
                {
                    folder = restored.folder;
                    file = restored.file;
                }
                else
                {
                    folder = _settings.Vaults.Current;
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                        folder = "";
                }
            }

            Task? scanTask = null;
            Task? prerenderTask = null;
            int scanGeneration = 0;
            TabRuntime? scanRuntime = null;
            VaultService? scanVault = null;
            if (!string.IsNullOrEmpty(folder))
            {
                scanTask = _vault.OpenAsync(folder);
                // Snapshot the generation OpenAsync just bumped to. If a user-
                // triggered sync OpenVault() runs during the long awaits below
                // (WebView2 init can take 2+ seconds), the generation will
                // change and we'll defer to that newer state instead of
                // stomping it with our scan's continuation.
                scanGeneration = _vault.CaptureGeneration();
                // Also capture *which* runtime/vault this scan belongs to. The
                // generation alone is not enough: a different tab's VaultService
                // is a separate instance with its own counter, so a stale gen
                // from tab A can spuriously match tab B (both at generation 1).
                scanRuntime = _active;
                scanVault = _active.Vault;

                // Resolve which file the user will see and start its render
                // on a background thread. By the time the WebView is ready,
                // the HTML is already built — the "ready" handler just posts.
                var initialFile = file;
                if (string.IsNullOrEmpty(initialFile) &&
                    _settings.Vaults.LastFile.TryGetValue(folder, out var last))
                {
                    var lastPath = Path.IsPathRooted(last) ? last : Path.Combine(folder, last);
                    if (File.Exists(lastPath)) initialFile = lastPath;
                }
                if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile) &&
                    ContentRouter.Route(initialFile, out _) == ViewerKind.Markdown)
                {
                    var vaultRoot = folder;
                    var path = initialFile;
                    var tabId = _active.Id;
                    var showLineNumbers = _settings.Reading.ShowLineNumbers;
                    var highlightCustomTags = _settings.Reading.HighlightCustomTags;
                    prerenderTask = Task.Run(() =>
                    {
                        try
                        {
                            var doc = DocumentRenderer.RenderMarkdownFile(
                                path, vaultRoot, tabId, showLineNumbers, highlightCustomTags);
                            _initialRender = new InitialRender(tabId, path, doc, showLineNumbers);
                        }
                        catch { /* fall back to UI-thread render in OpenFile */ }
                    });
                    _initialRenderTask = prerenderTask;
                }
            }

            // Started in the constructor (see _envTask); awaiting it here just
            // collects the result that's been warming since before window paint.
            var env = await _envTask;
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;
            // Block default browser context menu items that aren't useful here.
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            // Serve the bundled web assets from resources embedded in the exe
            // (so a published build needs no WebAssets\ folder). The filter must
            // be registered before the first navigation to app.local below.
            core.AddWebResourceRequestedFilter("https://app.local/*",
                CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                // Vault files are served same-origin under /__vault/<tabId>/<rel>
                // so that subresources (images, PDF) aren't cross-origin to the
                // app.local document — a cross-origin <img> from app.local to
                // vault.local simply fails to load. The tab id in the URL names
                // the runtime whose vault the request resolves against, so a
                // late request from a background/hidden document reads from the
                // vault that owns it (never the currently-active tab's), and a
                // closed tab's URLs just 404. VaultPaths stays the single
                // traversal gate for the on-disk resolution.
                if (VaultUrlScheme.TryVaultRel(e.Request.Uri, out var tabId, out var vaultRel))
                {
                    var owner = FindRuntimeByTabId(tabId);
                    var disk = owner is null
                        ? null
                        : VaultPaths.ResolveWithinRoot(owner.Vault.Root, vaultRel);
                    if (disk is null || !File.Exists(disk)) return;   // 404
                    FileStream fs;
                    try { fs = File.OpenRead(disk); }
                    catch { return; }
                    e.Response = env.CreateWebResourceResponse(
                        fs, 200, "OK", "Content-Type: " + WebAssetProvider.ContentType(disk));
                    return;
                }

                string rel;
                try { rel = new Uri(e.Request.Uri).AbsolutePath; }
                catch { return; }
                rel = Uri.UnescapeDataString(rel).TrimStart('/');
                if (rel.Length == 0) rel = "render.html";   // app.local/ → shell
                var stream = WebAssetProvider.Open(rel);
                if (stream is null) return;                  // 404: let WebView2 handle it
                e.Response = env.CreateWebResourceResponse(
                    stream, 200, "OK", "Content-Type: " + WebAssetProvider.ContentType(rel));
            };

            core.WebMessageReceived += WebView_WebMessageReceived;
            core.NavigationStarting += WebView_NavigationStarting;
            core.ContextMenuRequested += Core_ContextMenuRequested;
            // Subscribe to iframe nav so link clicks inside a raw HTML doc still
            // route the right way (external → OS browser; cross-vault → app).
            core.FrameCreated += (_, args) =>
            {
                args.Frame.NavigationStarting += Frame_NavigationStarting;
            };
            // Open new windows (e.g. target="_blank") in the OS browser.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                TryOpenExternal(e.Uri);
            };

            _webViewReady = true;
            WebView.CoreWebView2.Navigate("https://app.local/render.html");

            if (scanTask == null)
            {
                ShowEmpty("Open a folder to get started.");
                RefreshOpenPopup();
                return;
            }

            await scanTask;
            // If the user switched tabs or opened a different vault while
            // WebView2 was warming up, this scan's continuation no longer owns
            // the active view — defer to their choice (their OpenVault already
            // wired up the new vault, OpenFile, Recents, etc.). Gate on runtime
            // identity AND generation so a stale scan can't stomp another tab.
            if (scanRuntime != _active || !scanVault!.IsCurrentGeneration(scanGeneration))
                return;
            // Scan finished and we're still the active vault — but the scan
            // itself may have hit an IO error (BuildNode returned null).
            if (!_vault.IsOpen)
            {
                ShowEmpty("Open a folder to get started.");
                RefreshOpenPopup();
                return;
            }
            // Bookkeeping happens here, not before the scan, so a failed open
            // doesn't leave Recents pointing at an unusable folder.
            UpdateRecentsBookkeeping(folder);
            FinishOpenVault(folder, file);
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 init failed: " + ex.Message + "\n\n" +
                "The Edge WebView2 Runtime must be installed (it ships preinstalled on Windows 11).",
                "MarkdownViewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateRecentsBookkeeping(string folder)
    {
        if (string.Equals(_settings.Vaults.Current, folder, StringComparison.OrdinalIgnoreCase))
            return;
        var prev = _settings.Vaults.Current;
        if (!string.IsNullOrEmpty(prev) && Directory.Exists(prev))
        {
            _settings.Vaults.Recents.RemoveAll(r =>
                string.Equals(r, prev, StringComparison.OrdinalIgnoreCase));
            _settings.Vaults.Recents.Insert(0, prev);
            if (_settings.Vaults.Recents.Count > 10)
                _settings.Vaults.Recents.RemoveRange(10, _settings.Vaults.Recents.Count - 10);
        }
        _settings.Vaults.Current = folder;
    }

    // The post-scan tail of OpenVault: title, last-file resolution,
    // OpenFile/ShowEmpty, persistence. Shared by the parallel startup path and
    // the user-triggered sync path. Vault files are served same-origin by the
    // WebResourceRequested handler (which reads _vault.Root live), so there's no
    // per-vault virtual-host mapping to (re)register here.
    private void FinishOpenVault(string folder, string? selectFile)
    {
        Title = $"{Path.GetFileName(folder)} — MarkdownViewer";

        if (string.IsNullOrEmpty(selectFile) &&
            _settings.Vaults.LastFile.TryGetValue(folder, out var last))
        {
            var lastPath = Path.IsPathRooted(last) ? last : Path.Combine(folder, last);
            if (File.Exists(lastPath)) selectFile = lastPath;
        }

        if (!string.IsNullOrEmpty(selectFile) && File.Exists(selectFile))
            OpenFile(selectFile);
        else
            ShowEmpty("Pick a file from the sidebar.");

        SettingsService.Save(_settings);
        RefreshOpenPopup();
    }

    private void ApplyWindowState()
    {
        var w = _settings.Window;
        // Clamp to current virtual screen so a saved off-screen pos doesn't
        // disappear the window.
        var virt = SystemParameters.VirtualScreenWidth;
        var virtH = SystemParameters.VirtualScreenHeight;
        Left = Math.Min(Math.Max(0, w.X), Math.Max(0, virt - 200));
        Top = Math.Min(Math.Max(0, w.Y), Math.Max(0, virtH - 100));
        Width = Math.Max(600, w.Width);
        Height = Math.Max(400, w.Height);
        SidebarCol.Width = new GridLength(Math.Max(160, Math.Min(420, w.SidebarWidth)));
    }

    private void ScheduleSave()
    {
        if (_settingsSaveTimer == null)
        {
            _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            // Subscribe exactly once. ScheduleSave is called repeatedly (every
            // SizeChanged/LocationChanged during a drag), so subscribing here
            // rather than on each call avoids accumulating duplicate handlers
            // that would each fire — and re-save — on a single tick.
            _settingsSaveTimer.Tick += (_, _) =>
            {
                _settingsSaveTimer!.Stop();
                FlushWindowState();
                SettingsService.Save(_settings);
            };
        }
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void FlushWindowState()
    {
        var w = _settings.Window;
        if (WindowState == WindowState.Normal)
        {
            w.X = Left; w.Y = Top; w.Width = Width; w.Height = Height;
        }
        w.SidebarWidth = SidebarCol.Width.Value;
        w.SidebarFolderRatio = CurrentSidebarSplitRatio();
    }

    private void ApplySidebarSplitRatio(double ratio)
    {
        ratio = Math.Clamp(double.IsFinite(ratio) ? ratio : 0.5, 0.1, 0.9);
        FolderRow.Height = new GridLength(ratio, GridUnitType.Star);
        OutlineRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private double CurrentSidebarSplitRatio()
    {
        var top = FolderRow.ActualHeight;
        var bot = OutlineRow.ActualHeight;
        var sum = top + bot;
        if (sum <= 0) return 0.5;
        return Math.Clamp(top / sum, 0.1, 0.9);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        FlushWindowState();
        PersistTabs();
        SettingsService.Save(_settings);
        UnhookSystemWatcher();
        // Unsubscribe from the static theme event (it would otherwise root this
        // window) and stop the pending save timer so it can't tick after close.
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        _settingsSaveTimer?.Stop();
        foreach (var rt in _runtimes.Values) rt.Vault.Dispose();
    }

    private void OnApplicationThemeChanged(ApplicationTheme currentTheme, System.Windows.Media.Color accent)
        => SendPrefs();

    // ─── Update check (notify-only) ──────────────────────────────────────

    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.Updates.CheckForUpdates) return;
        // Throttle: at most one completed check per day.
        if (!UpdateService.IsCheckDue(_settings.Updates.LastCheckUtc, DateTime.UtcNow,
                UpdateService.CheckInterval))
            return;

        var outcome = await UpdateService.CheckAsync(UpdateService.CurrentVersion());
        // Stamp the daily timer only when GitHub was actually reached, so an
        // offline launch doesn't burn the day's check.
        if (outcome.Completed)
        {
            _settings.Updates.LastCheckUtc = DateTime.UtcNow;
            SettingsService.Save(_settings);
        }

        var result = outcome.Update;
        if (result == null) return;
        // Respect a prior dismissal of this exact version.
        if (string.Equals(result.LatestVersion, _settings.Updates.DismissedVersion,
                StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            _pendingUpdateUrl = result.ReleaseUrl;
            _pendingUpdateVersion = result.LatestVersion;
            UpdateBannerText.Text =
                $"MarkdownViewer {result.LatestVersion} is available (you have {UpdateService.CurrentVersion().ToString(3)}).";
            UpdateBanner.Visibility = Visibility.Visible;
        }
        catch { /* window may be closing — banner is best-effort */ }
    }

    private void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingUpdateUrl)) TryOpenExternal(_pendingUpdateUrl);
        // They've acted on it — don't re-announce this version next launch.
        DismissUpdate();
    }

    private void UpdateDismiss_Click(object sender, RoutedEventArgs e) => DismissUpdate();

    private void DismissUpdate()
    {
        _settings.Updates.DismissedVersion = _pendingUpdateVersion;
        SettingsService.Save(_settings);
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // ─── Vault open / tree ───────────────────────────────────────────────

    // User-intent open: scan + wire the folder AND record the choice in the
    // global Recents/Current bookkeeping. Anything that merely re-materializes
    // a tab the user already had (lazy activation of a restored tab) must call
    // OpenVaultCore instead, so clicking through restored tabs doesn't
    // reshuffle the Recents list.
    private void OpenVault(string folder, string? selectFile)
        => OpenVaultImpl(folder, selectFile, updateRecents: true);

    // Scan + wire only: no Recents/Current writes.
    private void OpenVaultCore(string folder, string? selectFile)
        => OpenVaultImpl(folder, selectFile, updateRecents: false);

    private void OpenVaultImpl(string folder, string? selectFile, bool updateRecents)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            ShowEmpty("Open a folder to get started.");
            RefreshOpenPopup();
            return;
        }

        // Clear the active file before re-scanning. Otherwise re-opening the SAME
        // folder makes the intermediate tree rebuild re-select the previously-open
        // file; with a virtualized tree that selection event fires *deferred* —
        // after we've advanced to the new file — and re-opens the old one,
        // clobbering the file we're navigating to. FinishOpenVault re-selects the
        // real target below.
        _currentMdFile = null;
        _vault.Open(folder);
        // Open() can silently fail (permissions, race with deletion) — bail
        // out before committing the folder to Recents/Current, otherwise an
        // inaccessible folder gets persisted and reopened on next launch.
        if (!_vault.IsOpen)
        {
            ShowEmpty("Open a folder to get started.");
            RefreshOpenPopup();
            return;
        }
        if (updateRecents) UpdateRecentsBookkeeping(folder);
        FinishOpenVault(folder, selectFile);
    }

    // Per-tab runtime: the live objects backing one tab (its vault plus the
    // view state stashed while the tab is inactive).
    private sealed class TabRuntime
    {
        private static int _nextId;
        // Stable identity token for this runtime, unique for the process
        // lifetime. Carried in every bridge message and embedded in every
        // /__vault/<tabId>/<rel> URL this tab mints, so documents, scroll
        // reports, and vault file requests resolve to the tab that owns them
        // instead of "whichever tab is active right now". Stable across the
        // tab's life so a re-shown raw doc keeps the same iframe URL (the
        // warm-iframe comparison in bridge.js depends on that).
        public readonly string Id = "t" + System.Threading.Interlocked.Increment(ref _nextId);
        public readonly VaultService Vault = new();
        // View state stashed when this tab is deactivated, restored on return.
        public string? CurrentFile;
        public string? CurrentIframeUrl;
        public System.Collections.IEnumerable? OutlineSource;
        // Last scroll offset of this tab's doc, tracked live while the tab is
        // active and re-applied on switch-back so a switch doesn't jump to top.
        public double ScrollTop;
    }

    // Resolve a tab id from a vault URL back to its live runtime; null when
    // the tab has been closed (its outstanding URLs then just 404).
    private TabRuntime? FindRuntimeByTabId(string tabId)
    {
        foreach (var rt in _runtimes.Values)
            if (rt.Id == tabId) return rt;
        return null;
    }

    // One strip item per tab. Wraps the (pure) TabState so the ListBox can bind a
    // Title that refreshes when the tab's open file changes.
    private sealed class TabVM : System.ComponentModel.INotifyPropertyChanged
    {
        public TabState State { get; }
        public TabVM(TabState state) => State = state;
        public string Title => State.Title;
        public string TopLabel => State.TopLabel;
        public string MainLabel => State.MainLabel;
        public void RefreshTitle()
        {
            foreach (var p in new[] { nameof(Title), nameof(TopLabel), nameof(MainLabel) })
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(p));
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    // Build a tab's runtime and wire its vault's events, gated so only the ACTIVE
    // tab drives the shared sidebar/content. An inactive tab's on-disk change is
    // ignored here: switching back to it re-reads the file from disk anyway.
    private TabRuntime CreateRuntime()
    {
        var rt = new TabRuntime();
        rt.Vault.SetSort(_settings.Sorting);
        rt.Vault.TreeChanged += () => { if (rt == _active) OnVaultTreeChanged(); };
        rt.Vault.FolderChildrenChanged += folder => { if (rt == _active) OnFolderChildrenChanged(folder); };
        rt.Vault.ActiveFileChanged += path => { if (rt == _active) OnActiveFileChanged(path); };
        return rt;
    }

    // ─── Tab operations & strip ──────────────────────────────────────────

    // Stash the active tab's view state into its runtime before switching away.
    private void SaveActiveViewState()
    {
        _active.CurrentFile = _currentMdFile;
        _active.CurrentIframeUrl = _currentIframeUrl;
        // OutlineSource is NOT read back from the control here: SetOutline
        // stashes it on the runtime at render time, so a stale binding can
        // never be adopted as the tab's own outline.
    }

    // Restore the active tab's view: rebind the sidebar to its vault and render
    // its doc (or the empty state). Setting _currentMdFile first keeps OpenFile
    // from force-expanding (it's a switch, not a navigation), so the tab's tree
    // expansion is preserved.
    private void LoadActiveViewState()
    {
        _currentIframeUrl = _active.CurrentIframeUrl;
        FolderTree.ItemsSource = _vault.RootNode != null
            ? new[] { _vault.RootNode } : Array.Empty<VaultNode>();
        ApplyFilter();
        OutlineTree.ItemsSource = _active.OutlineSource;

        var file = _active.CurrentFile;
        if (!string.IsNullOrEmpty(file) && File.Exists(file))
        {
            _currentMdFile = file;
            OpenFile(file);
        }
        else
        {
            ShowEmpty(_vault.IsOpen ? "Pick a file from the sidebar." : "Open a folder to get started.");
        }
    }

    // Point the strip's selection at the active tab without re-triggering a switch.
    private void SyncStripSelection()
    {
        _switchingTabs = true;
        TabStrip.SelectedIndex = _tabs.ActiveIndex;
        _switchingTabs = false;
    }

    // Keep the active tab's TabState (root + file) and its strip label current with
    // what's actually open. Called whenever the active doc/folder changes.
    private void SyncActiveTabState()
    {
        if (_tabs.Active is not { } state) return;
        state.VaultRoot = string.IsNullOrEmpty(_vault.Root) ? null : _vault.Root;
        state.File = _currentMdFile;
        var idx = _tabs.ActiveIndex;
        if (idx >= 0 && idx < _tabStripItems.Count) _tabStripItems[idx].RefreshTitle();
        PersistTabs();
    }

    // The one tab-switch ritual, shared by every transition: stash the active
    // tab's view state, apply the tab-list mutation, swap the active runtime,
    // re-materialize the new active tab, then sync strip selection and persist.
    // `mutate` returns the runtime that should become active, or null to keep
    // the current one (e.g. closing a background tab). `save` is false when the
    // outgoing tab no longer exists (it was just closed). `activate` is false
    // when the caller drives the render itself right after (a user-intent
    // vault open into the fresh tab).
    private void TransitionTo(Func<TabRuntime?> mutate, bool save = true, bool activate = true)
    {
        if (save) SaveActiveViewState();
        var next = mutate();
        if (next != null && next != _active)
        {
            _active = next;
            if (activate) ActivateCurrentTab();
        }
        SyncStripSelection();
        PersistTabs();
    }

    private void NewBlankTab()
    {
        if (!TabsEnabled) return;
        TransitionTo(() =>
        {
            var state = _tabs.OpenBlankTab();
            var rt = CreateRuntime();
            _runtimes[state] = rt;
            _tabStripItems.Add(new TabVM(state));
            return rt;   // blank → empty sidebar + "open a folder"
        });
    }

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Tabs.Count) return;
        var state = _tabs.Tabs[index];
        if (!_runtimes.TryGetValue(state, out var rt) || rt == _active) return;
        TransitionTo(() =>
        {
            _tabs.Activate(index);
            return rt;
        });
    }

    private void CloseTabAt(int index)
    {
        if (index < 0 || index >= _tabs.Tabs.Count) return;
        var closing = _tabs.Tabs[index];
        var wasActive = _runtimes.TryGetValue(closing, out var rt) && rt == _active;
        if (rt != null) { rt.Vault.Dispose(); _runtimes.Remove(closing); }
        if (index < _tabStripItems.Count) _tabStripItems.RemoveAt(index);

        if (!_tabs.CloseTab(index)) { Close(); return; }   // last tab closed → close window

        // The closed tab's stash died with it — nothing to save; a background
        // close keeps the current active runtime (mutate returns null).
        TransitionTo(
            () => wasActive ? _runtimes[_tabs.Tabs[_tabs.ActiveIndex]] : null,
            save: false);
    }

    // Show the active tab. If its vault hasn't been opened yet (a restored tab
    // visited for the first time), open it now; otherwise just rebind + re-render
    // from the runtime's saved state. Lazy activation is NOT a user "open" —
    // OpenVaultCore skips the Recents/Current bookkeeping so clicking through
    // restored tabs doesn't reshuffle the Recents list.
    private void ActivateCurrentTab()
    {
        var state = _tabs.Active;
        if (state != null && !_active.Vault.IsOpen &&
            !string.IsNullOrEmpty(state.VaultRoot) && Directory.Exists(state.VaultRoot))
            OpenVaultCore(state.VaultRoot, state.File);
        else
            LoadActiveViewState();
    }

    // Snapshot the open tabs into settings so a plain launch can reopen them.
    private void PersistTabs()
    {
        _settings.Tabs.Sessions = _tabs.Serialize().ToList();
        _settings.Tabs.ActiveIndex = _tabs.ActiveIndex;
    }

    // Rebuild the tab set from the saved session (dropping tabs whose folder is
    // gone) and return the active tab's (folder, file) for the eager open in
    // InitializeAsync. Returns null when there's nothing to restore (caller falls
    // back to the last single folder). Non-active tabs open lazily on activation.
    private (string folder, string? file)? RestoreTabsFromSession()
    {
        if (!TabsEnabled || _settings.Tabs.Sessions.Count == 0) return null;
        _tabs.Restore(_settings.Tabs.Sessions, _settings.Tabs.ActiveIndex, Directory.Exists);
        if (_tabs.Tabs.Count == 0) return null;

        // Replace the seeded blank tab's runtime/strip with one per restored tab.
        foreach (var r in _runtimes.Values) r.Vault.Dispose();
        _runtimes.Clear();
        _tabStripItems.Clear();
        foreach (var state in _tabs.Tabs)
        {
            _runtimes[state] = CreateRuntime();
            _tabStripItems.Add(new TabVM(state));
        }
        _active = _runtimes[_tabs.Tabs[_tabs.ActiveIndex]];
        SyncStripSelection();

        var act = _tabs.Active!;
        return (act.VaultRoot ?? "", act.File);
    }

    private void TabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingTabs) return;
        if (TabStrip.SelectedIndex >= 0) SwitchToTab(TabStrip.SelectedIndex);
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TabVM vm)
        {
            var idx = _tabStripItems.IndexOf(vm);
            if (idx >= 0) CloseTabAt(idx);
        }
    }

    private void TabItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            (sender as FrameworkElement)?.DataContext is TabVM vm)
        {
            var idx = _tabStripItems.IndexOf(vm);
            if (idx >= 0) { CloseTabAt(idx); e.Handled = true; }
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewBlankTab();

    // Middle-click a sidebar row → open it in a new tab.
    private void FolderTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!TabsEnabled || e.ChangedButton != MouseButton.Middle) return;
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is VaultNode node && !node.IsPlaceholder)
        {
            OpenNodeInNewTab(node);
            e.Handled = true;
        }
    }

    private void VaultNode_OpenInNewTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is VaultNode node) OpenNodeInNewTab(node);
    }

    // A file opens in a new tab rooted at the current tab's folder (same tree,
    // new file); a folder opens in a new tab rooted at that folder, no file.
    private void OpenNodeInNewTab(VaultNode node)
    {
        if (!TabsEnabled) return;
        if (node.Kind == VaultNodeKind.File)
            OpenRouted(_vault.Root, node.FullPath, OpenMode.NewTab);
        else
            OpenRouted(node.FullPath, null, OpenMode.NewTab);
    }

    // Open (root, file) per the TabManager routing policy: TabManager decides
    // where the open lands (a new TabState, or mutating the active one) and is
    // the unit-tested owner of that decision; this method materializes the
    // outcome (runtime + strip item for a fresh tab) and then drives the
    // user-intent vault open, which records Recents and renders the file.
    private void OpenRouted(string? root, string? file, OpenMode mode)
    {
        if (!TabsEnabled)
        {
            OpenVault(root ?? "", file);
            return;
        }
        var before = _tabs.Active;
        var state = _tabs.OpenFile(root, file, mode);
        if (!ReferenceEquals(state, before))
        {
            // A new tab was created and activated: give it a runtime, but skip
            // ActivateCurrentTab — the OpenVault below renders the target
            // directly (with Recents bookkeeping, unlike a lazy activation).
            TransitionTo(() =>
            {
                var rt = CreateRuntime();
                _runtimes[state] = rt;
                _tabStripItems.Add(new TabVM(state));
                return rt;
            }, activate: false);
        }
        OpenVault(root ?? "", file);
    }

    // Walk up the visual/logical tree to the nearest ancestor of type T.
    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    // A file handed to this (already-running) window by a second launch under
    // single-instance. Brings the window to the front and opens the file per the
    // incoming-file preference (a new tab, or replacing the current tab).
    public void HandleIncomingFile(string? path)
    {
        // The second process granted us foreground rights (AllowSetForegroundWindow),
        // so a plain Activate() takes the foreground without the Topmost hack that
        // was leaving other windows' focus stuck.
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();

        if (string.IsNullOrEmpty(path)) return;   // bare focus signal
        var (folder, file) = VaultService.ResolveInput(path);
        if (string.IsNullOrEmpty(folder)) return;

        // The incoming-file preference picks the mode; TabManager routes it.
        var mode = TabsEnabled && _settings.Tabs.OpenIncomingInNewTab
            ? OpenMode.NewTab : OpenMode.ReplaceCurrent;
        OpenRouted(folder, file, mode);
    }

    private void OnVaultTreeChanged()
    {
        FolderTree.ItemsSource = _vault.RootNode != null
            ? new[] { _vault.RootNode }
            : Array.Empty<VaultNode>();
        ApplyFilter();

        // If the open file was deleted or renamed away by an external editor,
        // fall back to the empty state. (Otherwise we keep showing a stale
        // doc that the user can no longer find on disk.)
        if (_currentMdFile != null && !File.Exists(_currentMdFile))
        {
            ShowEmpty("This file no longer exists.");
            return;
        }

        // Fresh vault tree — reveal and expand to the active file.
        SelectActiveInTree(expandAncestors: true);
    }

    private void ApplyFilter()
    {
        if (_vault.RootNode == null) return;
        TreeFilter.Apply(_vault.RootNode, _settings.Files);
        // The vault root was explicitly opened by the user — always show it,
        // even if every child is filtered out. The user sees an empty folder
        // rather than a vanished tree.
        _vault.RootNode.IsVisible = true;
    }

    // Select (and reveal) the node for the currently open file, loading folders
    // along the way (lazy folders may not be materialized yet). Ancestors are
    // expanded only when <paramref name="expandAncestors"/> is set — callers pass
    // false to re-select without overriding a folder the user manually collapsed.
    private void SelectActiveInTree(bool expandAncestors)
    {
        if (_currentMdFile == null || _vault.RootNode == null) return;
        var node = _vault.RevealPath(_currentMdFile, expandAncestors);
        if (node != null) node.IsSelected = true;
    }

    // A folder was expanded by the user: materialize its children on demand.
    private void FolderTree_ItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: VaultNode node }
            && node.Kind == VaultNodeKind.Folder)
            _vault.LoadChildren(node);
    }

    // A folder's children were (re)materialized (lazy load or watcher reconcile):
    // filter the new children. Selection survives a reconcile on its own — Sync
    // preserves surviving node instances, which keep their IsSelected — so this
    // must not re-run SelectActiveInTree (that would re-enter via RevealPath).
    private void OnFolderChildrenChanged(VaultNode folder)
    {
        TreeFilter.ApplyToChildren(folder, _settings.Files);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not VaultNode n || n.Kind != VaultNodeKind.File) return;
        // Re-selection of the already-open file (typically after a tree
        // rebuild from a file-watcher event) — do nothing. Re-rendering here
        // would reset the user's scroll position. Use Ctrl+R / F5 for an
        // explicit reload.
        if (string.Equals(n.FullPath, _currentMdFile, StringComparison.OrdinalIgnoreCase))
            return;
        OpenFile(n.FullPath);
    }

    // ─── Folder tree context menu ────────────────────────────────────────

    private void VaultNode_OpenDefault_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not VaultNode n) return;
        if (n.Kind != VaultNodeKind.File) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = n.FullPath,
                UseShellExecute = true, // resolves the registered default app
            });
        }
        catch { /* shell launch is best-effort */ }
    }

    private void VaultNode_OpenWith_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not VaultNode n) return;
        if (n.Kind != VaultNodeKind.File) return;
        try
        {
            // SHOpenWithDialog raises the "How do you want to open this file?"
            // chooser for ANY file. The "openas" shell verb only worked for
            // types with no registered default; this works regardless.
            var info = new OpenAsInfo
            {
                FilePath = n.FullPath,
                FileClass = null,
                InFlags = OAIF_EXEC | OAIF_ALLOW_REGISTRATION | OAIF_REGISTER_EXT,
            };
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SHOpenWithDialog(hwnd, ref info);
        }
        catch { /* shell dialog is best-effort */ }
    }

    // SHOpenWithDialog interop for the tree "Open with…" command.
    private const int OAIF_ALLOW_REGISTRATION = 0x01;
    private const int OAIF_REGISTER_EXT = 0x02;
    private const int OAIF_EXEC = 0x04;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        [MarshalAs(UnmanagedType.LPWStr)] public string? FileClass;
        public int InFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo info);

    private void VaultNode_OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not VaultNode n) return;
        try
        {
            // For a file: open Explorer with the file selected.
            // For a folder: open the folder itself.
            var args = n.Kind == VaultNodeKind.File
                ? $"/select,\"{n.FullPath}\""
                : $"\"{n.FullPath}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = args,
                UseShellExecute = true,
            });
        }
        catch { /* shell launch is best-effort */ }
    }

    private void VaultNode_MakeRoot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not VaultNode n) return;
        if (n.Kind != VaultNodeKind.Folder) return;
        OpenVault(n.FullPath, selectFile: null);
    }

    private void VaultNode_OpenParent_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vault.Root)) return;
        var parent = Path.GetDirectoryName(_vault.Root.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
        OpenVault(parent, selectFile: null);
    }

    // ─── WebView right-click menu ────────────────────────────────────────

    // Normalize a WebView2 menu label for matching: drop the '&' mnemonic
    // marker and a trailing ellipsis ("…" or "..."), trim, lowercase.
    private static string NormalizeMenuLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        var s = label.Replace("&", "").Trim();
        s = s.TrimEnd('.', '…').Trim();
        return s.ToLowerInvariant();
    }

    private void Core_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (WebView?.CoreWebView2 == null) return;

        // Strip items the user explicitly didn't want. We match on both the
        // WebView2 item Name and a normalized Label, because the internal
        // names vary by runtime build (e.g. Save As is "saveAs" or
        // "savePageAs"; "More tools" is the "other" submenu). Navigation
        // (back/forward), Save As, Print, Share, Web capture and the More
        // tools submenu are all user-hostile in a single-document reader.
        // Iterate backwards so removal during enumeration is safe.
        var dropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "back", "forward", "saveAs", "savePageAs", "print", "share",
              "webCapture", "webSelect", "other" };
        var dropLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "back", "forward", "save as", "more tools" };
        for (int i = e.MenuItems.Count - 1; i >= 0; i--)
        {
            var item = e.MenuItems[i];
            if (dropNames.Contains(item.Name) || dropLabels.Contains(NormalizeMenuLabel(item.Label)))
                e.MenuItems.RemoveAt(i);
        }

        if (string.IsNullOrEmpty(_currentMdFile)) return;

        var env = WebView.CoreWebView2.Environment;
        var sep = env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);
        e.MenuItems.Add(sep);

        var openExt = env.CreateContextMenuItem(
            "Open with default app", null, CoreWebView2ContextMenuItemKind.Command);
        openExt.CustomItemSelected += (_, _) => OpenSourceInDefaultApp(_currentMdFile);
        e.MenuItems.Add(openExt);

        if (IsRenderable(_currentMdFile))
        {
            var openBrowser = env.CreateContextMenuItem(
                "Open rendered in default browser", null, CoreWebView2ContextMenuItemKind.Command);
            openBrowser.CustomItemSelected += (_, _) => OpenRenderedInBrowser(_currentMdFile);
            e.MenuItems.Add(openBrowser);

            var export = env.CreateContextMenuItem(
                "Export rendered HTML…", null, CoreWebView2ContextMenuItemKind.Command);
            export.CustomItemSelected += (_, _) => ExportRenderedHtml(_currentMdFile);
            e.MenuItems.Add(export);
        }
    }

    private static bool IsRenderable(string path)
    {
        var kind = ContentRouter.Route(path, out _);
        return kind == ViewerKind.Markdown || kind == ViewerKind.JsonlTranscript;
    }

    private static void OpenSourceInDefaultApp(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
        }
        catch { /* best-effort */ }
    }

    private void OpenRenderedInBrowser(string filePath)
    {
        var doc = HtmlExporter.BuildStandaloneHtml(filePath,
            _settings.Transcripts.VisibleCategories, _settings.Reading.HighlightCustomTags);
        if (doc == null) return;
        SweepOldRenderedTempFiles();
        var temp = Path.Combine(Path.GetTempPath(),
            "MarkdownViewer-" + Path.GetFileNameWithoutExtension(filePath)
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".html");
        try { File.WriteAllText(temp, doc); } catch { return; }

        var browser = GetDefaultBrowserExe();
        try
        {
            if (!string.IsNullOrEmpty(browser) && File.Exists(browser))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = browser,
                    Arguments = "\"" + temp + "\"",
                    UseShellExecute = false,
                });
            }
            else
            {
                // Fallback: let shell pick (typically the browser anyway for .html).
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = temp,
                    UseShellExecute = true,
                });
            }
        }
        catch { /* best-effort */ }
    }

    // "Open rendered in browser" drops a temp .html in %TEMP% each time; the OS
    // never reclaims them. Best-effort sweep of our own prior exports older than a
    // day so they don't accumulate indefinitely (skip any still open in a browser,
    // which stays locked and throws).
    private static void SweepOldRenderedTempFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), "MarkdownViewer-*.html"))
            {
                try { if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); }
                catch { /* in use or gone — skip */ }
            }
        }
        catch { /* temp dir unreadable — nothing to do */ }
    }

    private void ExportRenderedHtml(string filePath)
    {
        var doc = HtmlExporter.BuildStandaloneHtml(filePath,
            _settings.Transcripts.VisibleCategories, _settings.Reading.HighlightCustomTags);
        if (doc == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(filePath) + ".html",
            Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*",
            DefaultExt = ".html",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, doc); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message,
                "MarkdownViewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? GetDefaultBrowserExe()
    {
        try
        {
            // The user's HTTP UserChoice points at a ProgID; that ProgID
            // resolves to a shell\open\command whose first token is the
            // browser exe.
            using var uc = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\URLAssociations\http\UserChoice");
            var progId = uc?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return null;

            using var cmdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            var cmdStr = cmdKey?.GetValue("") as string;
            if (string.IsNullOrEmpty(cmdStr)) return null;

            // Typical form: "C:\path\app.exe" --some-args "%1"
            if (cmdStr.StartsWith("\""))
            {
                var end = cmdStr.IndexOf('"', 1);
                if (end > 1) return cmdStr.Substring(1, end - 1);
            }
            var space = cmdStr.IndexOf(' ');
            return space > 0 ? cmdStr.Substring(0, space) : cmdStr;
        }
        catch { return null; }
    }

    // ─── Opening files ───────────────────────────────────────────────────

    private void OpenFile(string filePath)
    {
        // Auto-expand the file's folders only when navigating to a *different*
        // file (incl. the cold-start restore, where the previous file is null).
        // A reload (F5) or the bridge-ready re-open of the already-open file must
        // NOT re-expand — that would fight a folder the user manually collapsed.
        var isNavigation = !string.Equals(filePath, _currentMdFile, StringComparison.OrdinalIgnoreCase);
        _currentMdFile = filePath;
        // A real navigation to a different file starts at the top; only a
        // re-show of the tab's *current* doc (tab switch, bridge-ready re-open)
        // keeps its scroll. Reset here so a tab's stored offset never carries
        // over to the next file opened in it.
        if (isNavigation) _active.ScrollTop = 0;
        _vault.SetActiveFile(filePath);
        // Reveal the file in the sidebar tree: load + (on navigation) expand its
        // parent folders (lazy folders may not be materialized yet) and select the
        // row. Matters most for the cold-start case where the last-opened file is
        // restored from settings and would otherwise be buried under collapsed folders.
        SelectActiveInTree(expandAncestors: isNavigation);
        SyncActiveTabState();   // keep the tab's root/file + strip label current
        if (!string.IsNullOrEmpty(_vault.Root))
            _settings.Vaults.LastFile[_vault.Root] = filePath;
        ScheduleSave();

        // During cold boot, bridge.js hasn't yet posted "ready" — the JS
        // listener isn't bound, so any rendered HTML would be discarded.
        // The "ready" handler re-invokes OpenFile, which will then render.
        if (!_bridgeReady) return;

        // Offset to restore: the tab's saved scroll on a switch-back (isNavigation
        // false), or 0 for a fresh open (where it was just reset above).
        var restoreScrollTop = _active.ScrollTop;

        var kind = ContentRouter.Route(filePath, out var lang);

        switch (kind)
        {
            case ViewerKind.Markdown:
                RenderMarkdown(filePath, reloaded: false, restoreScrollTop);
                break;
            case ViewerKind.RawBrowser:
                NavigateRaw(filePath);
                break;
            case ViewerKind.Image:
                ShowImage(filePath);
                break;
            case ViewerKind.Text:
                ShowText(filePath, lang, restoreScrollTop);
                break;
            case ViewerKind.JsonlTranscript:
                RenderTranscript(filePath, restoreScrollTop);
                break;
            case ViewerKind.Binary:
                SetOutline(Array.Empty<HeadingEntry>());
                Send(new BinaryDocMsg(_active.Id, filePath, FileModified(filePath)));
                break;
            default:
                ShowEmpty("This file no longer exists.");
                break;
        }
    }

    private void RenderMarkdown(string filePath, bool reloaded, double restoreScrollTop = 0)
    {
        try
        {
            // First doc on cold start may have been rendered on a worker
            // thread in InitializeAsync — reuse that result instead of doing
            // the Markdig parse again on the UI thread. Skip the cache on
            // reload (disk may have changed) or if prefs that affect output
            // have moved since the prerender ran.
            RenderedDoc doc;
            if (!reloaded &&
                _initialRender is { } pre &&
                pre.TabId == _active.Id &&
                string.Equals(pre.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
                pre.ShowLineNumbers == _settings.Reading.ShowLineNumbers)
            {
                doc = pre.Doc;
                _initialRender = null; // one-shot
            }
            else
            {
                doc = DocumentRenderer.RenderMarkdownFile(filePath, _vault.Root, _active.Id,
                    _settings.Reading.ShowLineNumbers, _settings.Reading.HighlightCustomTags);
            }

            SetOutline(doc.Headings);
            Send(new MarkdownDocMsg(_active.Id, filePath, doc.BasePath, doc.Html,
                reloaded, restoreScrollTop, FileModified(filePath)));
        }
        catch (FileNotFoundException)
        {
            ShowEmpty("This file no longer exists.");
        }
        catch (Exception ex)
        {
            SetOutline(Array.Empty<HeadingEntry>());
            Send(new TextDocMsg(_active.Id, filePath, "", "Render error: " + ex.Message, 0,
                FileModified(filePath)));
        }
    }


    private void RenderTranscript(string filePath, double restoreScrollTop = 0)
    {
        try
        {
            var doc = DocumentRenderer.RenderTranscriptFile(filePath, _active.Id,
                _settings.Transcripts.VisibleCategories, _settings.Reading.HighlightCustomTags);
            SetOutline(doc.Headings);
            Send(new MarkdownDocMsg(_active.Id, filePath, doc.BasePath, doc.Html,
                Reloaded: false, restoreScrollTop, FileModified(filePath)));
        }
        catch (FileNotFoundException)
        {
            ShowEmpty("This file no longer exists.");
        }
        catch (Exception ex)
        {
            SetOutline(Array.Empty<HeadingEntry>());
            Send(new TextDocMsg(_active.Id, filePath, "", "Transcript render error: " + ex.Message, 0,
                FileModified(filePath)));
        }
    }

    private void NavigateRaw(string filePath)
    {
        if (string.IsNullOrEmpty(_vault.Root)) return;
        var rel = Path.GetRelativePath(_vault.Root, filePath).Replace('\\', '/');
        var url = VaultUrlScheme.FileUrl(_active.Id, rel);

        // HTML is rendered inline via srcdoc — bridge.js puts it in a sandboxed,
        // null-origin iframe (no scripts) so an opened HTML file can't run script
        // or reach the host. <base> injection keeps the file's relative URLs
        // resolvable (they point back at /__vault/).
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".html" || ext == ".htm" || ext == ".xhtml")
        {
            try
            {
                var html = ContentRouter.ReadTextFile(filePath);
                var baseHref = url.Substring(0, url.LastIndexOf('/') + 1);
                html = VaultUrlScheme.InjectBaseTag(html, baseHref);
                _currentIframeUrl = null; // srcdoc has no URL; disable URL-match path
                SetOutline(Array.Empty<HeadingEntry>());
                Send(new RawDocMsg(_active.Id, filePath, Html: html, Url: null, FileModified(filePath)));
                return;
            }
            catch
            {
                // Fall through to URL approach if read fails.
            }
        }

        // PDF and any other raw file: navigate the iframe to the same-origin
        // app.local/__vault URL. (PDF used to ship base64->blob to dodge the
        // vault.local cross-origin penalty; serving same-origin makes that moot.)
        _currentIframeUrl = url;
        SetOutline(Array.Empty<HeadingEntry>());
        Send(new RawDocMsg(_active.Id, filePath, Html: null, Url: url, FileModified(filePath)));
    }

    private void ShowText(string filePath, string lang, double restoreScrollTop = 0)
    {
        try
        {
            var body = ContentRouter.ReadTextFile(filePath);
            SetOutline(Array.Empty<HeadingEntry>());
            Send(new TextDocMsg(_active.Id, filePath, lang, body, restoreScrollTop, FileModified(filePath)));
        }
        catch (Exception ex)
        {
            SetOutline(Array.Empty<HeadingEntry>());
            Send(new TextDocMsg(_active.Id, filePath, "", "Could not read file: " + ex.Message, 0,
                FileModified(filePath)));
        }
    }

    // Last-modified timestamp shown on the right of the breadcrumb bar.
    // Short date + short time in the current culture (e.g. "6/1/2026 2:32 PM").
    // Returns "" if the file is gone or unreadable so the bar just omits it.
    private static string FileModified(string filePath)
    {
        try { return File.GetLastWriteTime(filePath).ToString("g"); }
        catch { return ""; }
    }

    private void ShowImage(string filePath)
    {
        if (string.IsNullOrEmpty(_vault.Root)) return;
        var rel = Path.GetRelativePath(_vault.Root, filePath).Replace('\\', '/');
        SetOutline(Array.Empty<HeadingEntry>());
        Send(new ImageDocMsg(_active.Id, filePath, VaultUrlScheme.FileUrl(_active.Id, rel), FileModified(filePath)));
    }

    private void ShowEmpty(string message)
    {
        _currentMdFile = null;
        SetOutline(null);
        _currentIframeUrl = null;
        Send(new EmptyDocMsg(_active.Id, message));
        SyncActiveTabState();
    }

    private void OnActiveFileChanged(string path)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_currentMdFile == null) return;
            // Follow a rename of the open file (atomic-save replaced it, or it
            // was renamed on disk) so the view keeps tracking the right path.
            if (!string.IsNullOrEmpty(path)) _currentMdFile = path;
            if (!File.Exists(_currentMdFile))
            {
                ShowEmpty("This file no longer exists.");
                return;
            }
            var kind = ContentRouter.Route(_currentMdFile, out var lang);
            if (kind == ViewerKind.Markdown) RenderMarkdown(_currentMdFile, reloaded: true);
            else if (kind == ViewerKind.Text) ShowText(_currentMdFile, lang);
            else if (kind == ViewerKind.RawBrowser && _webViewReady && WebView.CoreWebView2 != null)
                WebView.CoreWebView2.Reload();
        });
    }

    // ─── Send / receive bridge ───────────────────────────────────────────

    private void Send<T>(T message)
    {
        if (!_webViewReady) return;
        // Before bridge.js posts "ready", no JS listener is bound — messages
        // are silently dropped. The "ready" handler resends the appropriate
        // initial state, so we skip the JSON-serialize + post entirely here.
        if (!_bridgeReady) return;
        var json = BridgeJson.Serialize(message);
        try { WebView.CoreWebView2.PostWebMessageAsJson(json); } catch { }
    }

    private void SendPrefs()
    {
        if (!_webViewReady) return;
        var accent = ApplicationAccentColorManager.SystemAccent;
        Send(new PrefsMsg(
            Theme: ResolveEffectiveTheme(),
            Accent: $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}",
            Typeface: _settings.Reading.Typeface,
            FontSize: _settings.Reading.FontSize,
            MarginPct: _settings.Reading.MarginPct,
            ShowLineNumbers: _settings.Reading.ShowLineNumbers,
            BodyStyle: _settings.Reading.BodyStyle));
    }

    private string ResolveEffectiveTheme()
        => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? "dark" : "light";

    private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.WebMessageAsJson; } catch { return; }

        var msg = BridgeInbound.Parse(json, out var parseError);
        if (msg is null)
        {
            // A malformed or unknown message is a protocol bug (renamed field,
            // typo'd kind) — surface it instead of rendering a silent blank pane.
            System.Diagnostics.Trace.TraceWarning("Bridge message dropped: " + parseError);
            return;
        }

        try
        {
            switch (msg)
            {
                case ReadyMsg:
                    _bridgeReady = true;
                    // If the background prerender is still running, wait
                    // briefly so OpenFile can pick up the precomputed HTML
                    // instead of redoing Markdig on the UI thread. The task
                    // body is pure CPU+IO with no UI dispatch, so .Wait is
                    // safe from a deadlock standpoint.
                    if (_initialRenderTask is { IsCompleted: false })
                    {
                        try { _initialRenderTask.Wait(2000); } catch { }
                    }
                    SendPrefs();
                    // Resend whatever should be on screen.
                    if (_currentMdFile != null) OpenFile(_currentMdFile);
                    else if (!_vault.IsOpen) Send(new EmptyDocMsg(_active.Id, "Open a folder to get started."));
                    else Send(new EmptyDocMsg(_active.Id, "Pick a file from the sidebar."));
                    // Drop the precomputed initial render after the single
                    // ready-triggered open — even if RenderMarkdown didn't
                    // match it (different file picked, or a reload came in
                    // first). Holding it could serve stale content if the
                    // user revisits the initial file later.
                    _initialRender = null;
                    _initialRenderTask = null;
                    break;
                case OpenLinkMsg m:
                    HandleInVaultLink(m.Href, m.Base);
                    break;
                case RequestExternalMsg m:
                    TryOpenExternal(m.Url);
                    break;
                case ScrollMsg m:
                    // The active renderer reports its scroll offset as the user
                    // scrolls; remember it on the active tab so a switch-back
                    // restores the position. Gated on the tab token AND the doc
                    // path (see BridgeGates.ScrollApplies): the token drops a
                    // stale report from a backgrounded tab even when both tabs
                    // show the same file; the path drops a trailing report for
                    // the previous doc after a same-tab navigation.
                    if (BridgeGates.ScrollApplies(m, _active.Id, _currentMdFile))
                        _active.ScrollTop = m.Top;
                    break;
                case TranscriptFilterMsg m:
                    if (!string.IsNullOrEmpty(m.Category))
                    {
                        _settings.Transcripts.VisibleCategories[m.Category] = m.Checked;
                        ScheduleSave();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            // Handler failures were previously swallowed by a blanket catch;
            // keep the no-crash behavior but leave a trace of what broke.
            System.Diagnostics.Trace.TraceWarning(
                $"Bridge handler for {msg.GetType().Name} failed: {ex}");
        }
    }

    // Populate the outline pane directly from the render result — synchronous
    // with the render, so a stale outline can never arrive from a previous doc
    // or tab (the old flow round-tripped headings through bridge.js and bound
    // whatever came back last). Null clears the pane (empty-state); an empty
    // list is a doc with no headings. The tab runtime's stash is updated here,
    // at the moment of truth, rather than read back from the control on switch.
    private void SetOutline(IEnumerable<HeadingEntry>? headings)
    {
        if (headings is null)
        {
            OutlineTree.ItemsSource = null;
            _active.OutlineSource = null;
            return;
        }
        var roots = OutlineBuilder.BuildTree(headings);
        var threshold = _settings.Outline.CollapseBelow;
        var needle = (_settings.Outline.CollapseContaining ?? "").Trim();
        OutlineBuilder.ApplyCollapse(roots, threshold, needle);
        OutlineTree.ItemsSource = roots;
        _active.OutlineSource = roots;
    }

    private void OutlineTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is HeadingViewModel hv)
            Send(new ScrollToHeadingMsg(_active.Id, hv.Id));
    }

    private void HandleInVaultLink(string href, string basePath)
    {
        if (string.IsNullOrEmpty(_vault.Root)) return;
        if (!LinkRouter.TryResolveVaultHref(href, basePath, out var tabId, out var rel, out var anchor)) return;
        TryOpenRelative(tabId, rel, anchor);
    }

    private void TryOpenRelative(string tabId, string rel, string anchor)
    {
        // Identity gate: a vault URL names the tab that minted it. Only the
        // active tab's links may drive the shared view; a stale or foreign
        // tab id (queued message from before a switch, hand-authored URL) is
        // dropped rather than resolved against the wrong vault.
        if (tabId != _active.Id) return;
        if (string.IsNullOrEmpty(_vault.Root)) return;
        var combined = Path.GetFullPath(Path.Combine(_vault.Root, rel));
        // Refuse paths that escape the vault.
        if (!combined.StartsWith(_vault.Root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            !combined.Equals(_vault.Root, StringComparison.OrdinalIgnoreCase))
            return;
        if (!File.Exists(combined)) return;
        OpenFile(combined);
        if (!string.IsNullOrEmpty(anchor))
            Send(new ScrollToHeadingMsg(_active.Id, anchor));
    }

    private static void TryOpenExternal(string url)
    {
        // Only hand web/mail URLs to the shell. Without a scheme check, a
        // crafted link or web message could pass ShellExecute a path to an
        // executable, a file:// URL, or a custom protocol handler.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeMailto))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // ─── NavigationStarting interception (raw browser only) ─────────────

    private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri ?? "";
        var route = LinkRouter.RouteTopLevel(uri);
        switch (route.Action)
        {
            case LinkAction.OpenInVault:
                e.Cancel = true;
                TryOpenRelative(route.TabId, route.VaultRel, anchor: "");
                break;
            case LinkAction.OpenExternal:
                e.Cancel = true;
                TryOpenExternal(uri);
                break;
            case LinkAction.Cancel:
                e.Cancel = true;
                break;
        }
    }

    private void Frame_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri ?? "";
        var route = LinkRouter.RouteFrame(uri, _currentIframeUrl, e.IsUserInitiated);
        switch (route.Action)
        {
            case LinkAction.OpenInVault:
                e.Cancel = true;
                TryOpenRelative(route.TabId, route.VaultRel, anchor: "");
                break;
            case LinkAction.OpenExternal:
                e.Cancel = true;
                TryOpenExternal(uri);
                break;
            case LinkAction.Cancel:
                e.Cancel = true;
                break;
        }
    }

    // ─── Open-folder popup ───────────────────────────────────────────────

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshOpenPopup();
        OpenPopup.IsOpen = !OpenPopup.IsOpen;
    }

    private void RefreshOpenPopup()
    {
        _pinnedRows.Clear();
        _currentRows.Clear();
        _recentRows.Clear();

        var pinned = _settings.Vaults.Pinned;
        var current = _settings.Vaults.Current ?? "";
        var recents = _settings.Vaults.Recents;

        foreach (var p in pinned)
        {
            if (!Directory.Exists(p)) continue;
            _pinnedRows.Add(new FolderRow
            {
                Path = p,
                DisplayName = p,
                IsCurrent = p.Equals(current, StringComparison.OrdinalIgnoreCase),
                IsPinned = true,
            });
        }
        PinnedSection.Visibility = _pinnedRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(current))
        {
            _currentRows.Add(new FolderRow
            {
                Path = current,
                DisplayName = current,
                IsCurrent = true,
                IsPinned = pinned.Any(p => p.Equals(current, StringComparison.OrdinalIgnoreCase)),
            });
        }

        var shown = 0;
        foreach (var r in recents)
        {
            if (shown >= 3) break;
            if (string.Equals(r, current, StringComparison.OrdinalIgnoreCase)) continue;
            if (pinned.Any(p => p.Equals(r, StringComparison.OrdinalIgnoreCase))) continue;
            if (!Directory.Exists(r)) continue;
            _recentRows.Add(new FolderRow
            {
                Path = r,
                DisplayName = r,
                IsCurrent = false,
                IsPinned = false,
            });
            shown++;
        }
        RecentsSection.Visibility = _recentRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenFolderRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string path)
        {
            OpenPopup.IsOpen = false;
            OpenVault(path, null);
        }
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string path) return;
        var list = _settings.Vaults.Pinned;
        if (list.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)) == 0)
            list.Insert(0, path);
        SettingsService.Save(_settings);
        RefreshOpenPopup();
    }

    private void PickFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenPopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Open folder",
            InitialDirectory = !string.IsNullOrEmpty(_vault.Root) ? _vault.Root : null,
        };
        if (dlg.ShowDialog(this) == true)
            OpenVault(dlg.FolderName, null);
    }

    // ─── Preferences ─────────────────────────────────────────────────────

    private void PrefsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.PreferencesWindow(_settings) { Owner = this };
        dlg.ShowDialog();
        SettingsService.Save(_settings);
        SyncUiPrefs();
        // Sort prefs apply to EVERY tab's vault, not just the active one —
        // each vault holds its own cloned SortPrefs, so an un-resorted
        // background tab would otherwise keep the old ordering forever.
        foreach (var rt in _runtimes.Values)
        {
            rt.Vault.SetSort(_settings.Sorting);
            rt.Vault.ResortAll();
        }
        ApplyTheme();
        ApplyFilter();
        _vault.RootNode?.RefreshDisplay();
        // Push prefs BEFORE re-rendering — setMarkdown reads the JS-side
        // `bodyStyle` variable to decide whether to wrap the content in
        // <article class="markdown-body">, so the new value has to land
        // before the new document does.
        SendPrefs();
        if (_currentMdFile != null)
        {
            var kind = ContentRouter.Route(_currentMdFile, out _);
            if (kind == ViewerKind.Markdown)
                RenderMarkdown(_currentMdFile, reloaded: true);
            else if (kind == ViewerKind.JsonlTranscript)
                RenderTranscript(_currentMdFile);
        }
    }

    private void SyncUiPrefs()
    {
        UiPrefs.Instance.ShowExtensions = _settings.Files.ShowExtensions;
        UiPrefs.Instance.Wrap = _settings.Files.WrapSidebar;
        UiPrefs.Instance.TabsEnabled = _settings.Tabs.Enabled;

        // Wrap only does something if the row's available width is finite.
        // When horizontal scrolling is on, the TreeView measures rows with
        // infinite width and TextWrapping is a no-op. Disable horizontal
        // scrolling in wrap mode; restore Auto when not wrapping.
        var h = _settings.Files.WrapSidebar
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        ScrollViewer.SetHorizontalScrollBarVisibility(FolderTree, h);
        ScrollViewer.SetHorizontalScrollBarVisibility(OutlineTree, h);
    }

    private bool _systemWatcherActive;

    /// <summary>
    /// Resolves the user's chosen theme into a concrete WPF-UI theme and
    /// applies it. When the user picks "system", we hand control to
    /// SystemThemeWatcher so live OS theme/accent changes flow through.
    /// </summary>
    private void ApplyTheme()
    {
        var pref = _settings.Theme;
        if (pref == "light")
        {
            UnhookSystemWatcher();
            ApplicationThemeManager.Apply(ApplicationTheme.Light, WpfUiControls.WindowBackdropType.Mica);
            // Apply() can reset the accent in some flows; restore from the OS.
            ApplicationAccentColorManager.ApplySystemAccent();
        }
        else if (pref == "dark")
        {
            UnhookSystemWatcher();
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WpfUiControls.WindowBackdropType.Mica);
            ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            // "system" / "auto" — follow Windows.
            ApplicationThemeManager.ApplySystemTheme();
            ApplicationAccentColorManager.ApplySystemAccent();
            HookSystemWatcher();
        }
    }

    private void HookSystemWatcher()
    {
        if (_systemWatcherActive) return;
        try
        {
            SystemThemeWatcher.Watch(this, WpfUiControls.WindowBackdropType.Mica, updateAccents: true);
            _systemWatcherActive = true;
        }
        catch { /* watcher requires HWND; safe to no-op if not ready yet */ }
    }

    private void UnhookSystemWatcher()
    {
        if (!_systemWatcherActive) return;
        try { SystemThemeWatcher.UnWatch(this); } catch { }
        _systemWatcherActive = false;
    }

    // ─── Drag/drop ───────────────────────────────────────────────────────

    private void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        // GetData can return null for delayed-rendering sources (e.g. an Outlook
        // attachment) even when GetDataPresent reports FileDrop — guard the cast
        // so a drop from such a source doesn't crash the UI thread.
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items || items.Length == 0) return;
        var first = items[0];
        if (Directory.Exists(first)) OpenVault(first, null);
        else if (File.Exists(first)) OpenVault(Path.GetDirectoryName(first) ?? "", first);
    }

    // ─── Keyboard shortcuts ──────────────────────────────────────────────

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        // Tab shortcuts (Ctrl+1..9 are taken by the tree-focus binds below).
        if (TabsEnabled && ctrl && e.Key == Key.T) { NewBlankTab(); e.Handled = true; return; }
        if (TabsEnabled && ctrl && e.Key == Key.W) { CloseTabAt(_tabs.ActiveIndex); e.Handled = true; return; }
        if (TabsEnabled && ctrl && e.Key == Key.Tab && _tabs.Tabs.Count > 1)
        {
            int n = _tabs.Tabs.Count;
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            SwitchToTab(((_tabs.ActiveIndex + step) % n + n) % n);
            e.Handled = true; return;
        }
        if (ctrl && e.Key == Key.O) { PickFolderButton_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (ctrl && e.Key == Key.F) { OpenFindBar(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.OemComma) { PrefsButton_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (ctrl && e.Key == Key.B) { ToggleSidebar(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D1) { FolderTree.Focus(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D2) { OutlineTree.Focus(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.R || e.Key == Key.F5)
        {
            if (_currentMdFile != null) OpenFile(_currentMdFile);
            e.Handled = true; return;
        }
        if (e.Key == Key.Escape && FindBar.IsOpen) { CloseFindBar(); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { AdjustFontSize(+1); e.Handled = true; return; }
        if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { AdjustFontSize(-1); e.Handled = true; return; }
        if (ctrl && e.Key == Key.D0) { _settings.Reading.FontSize = 14; SendPrefs(); ScheduleSave(); e.Handled = true; return; }
    }

    private void AdjustFontSize(int delta)
    {
        var s = _settings.Reading.FontSize + delta;
        _settings.Reading.FontSize = Math.Max(11, Math.Min(22, s));
        SendPrefs();
        ScheduleSave();
    }

    private double _savedSidebarWidth = 240;
    private void ToggleSidebar()
    {
        if (SidebarCol.Width.Value > 0)
        {
            _savedSidebarWidth = SidebarCol.Width.Value;
            SidebarCol.Width = new GridLength(0);
        }
        else
        {
            SidebarCol.Width = new GridLength(_savedSidebarWidth);
        }
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e) => ScheduleSave();

    private void SidebarSplit_DragCompleted(object sender, DragCompletedEventArgs e) => ScheduleSave();

    // ─── Find bar ────────────────────────────────────────────────────────

    private void OpenFindBar()
    {
        // Float at the WebView's top-right corner (bar is ≈410px wide).
        FindBar.HorizontalOffset = Math.Max(0, WebView.ActualWidth - 410);
        FindBar.IsOpen = true;   // focus handled in FindBar_Opened
    }

    private void CloseFindBar()
    {
        FindBar.IsOpen = false;  // FindBar_Closed does the cleanup
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    private void FindBar_Opened(object? sender, EventArgs e)
    {
        // The popup is its own HWND (WS_EX_NOACTIVATE). The WebView2 child HWND
        // keeps OS keyboard focus, and a WPF .Focus() only sets WPF focus — so
        // we must hand the popup's HWND real Win32 focus before the text box
        // can receive keystrokes. Both HWNDs are on this UI thread, so SetFocus
        // is allowed without activating the window.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PresentationSource.FromVisual(FindBox) is System.Windows.Interop.HwndSource src)
                SetFocus(src.Handle);
            FindBox.Focus();
            Keyboard.Focus(FindBox);
            FindBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void FindBar_Closed(object? sender, EventArgs e)
    {
        // Runs both for explicit close and StaysOpen=False auto-close (click
        // outside), so find highlights are always cleared.
        try { _find?.Stop(); } catch { }
        WebView.Focus();
    }

    private async void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = FindBox.Text ?? "";
        if (string.IsNullOrEmpty(q))
        {
            FindCount.Text = "";
            try { _find?.Stop(); } catch { }
            return;
        }
        try
        {
            _find ??= WebView.CoreWebView2.Find;
            var opts = WebView.CoreWebView2.Environment.CreateFindOptions();
            opts.FindTerm = q;
            opts.SuppressDefaultFindDialog = true;
            await _find.StartAsync(opts);
            UpdateFindCount();
        }
        catch (Exception ex)
        {
            FindCount.Text = "n/a";
            System.Diagnostics.Debug.WriteLine("Find error: " + ex.Message);
        }
    }

    private void UpdateFindCount()
    {
        try
        {
            if (_find == null) { FindCount.Text = ""; return; }
            FindCount.Text = _find.MatchCount > 0
                ? $"{_find.ActiveMatchIndex}/{_find.MatchCount}"
                : "0";
        }
        catch { FindCount.Text = ""; }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        try { _find?.FindNext(); UpdateFindCount(); } catch { }
    }
    private void FindPrev_Click(object sender, RoutedEventArgs e)
    {
        try { _find?.FindPrevious(); UpdateFindCount(); } catch { }
    }
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            try
            {
                if (_find != null)
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _find.FindPrevious();
                    else _find.FindNext();
                    UpdateFindCount();
                }
            }
            catch { }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            e.Handled = true;
        }
    }
}

internal static class TupleExt
{
    public static TR Pipe<T, TR>(this T self, Func<T, TR> f) => f(self);
}
