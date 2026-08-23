using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Detangle.Core.Diagnostics;
using Detangle.Core.Editing;
using Detangle.Core.Graph;
using Detangle.Core.History;
using Detangle.Core.Linking;
using Detangle.Core.Repair;
using Detangle.Core.Search;
using Detangle.Core.Vault;
using Detangle.Rendering;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;

namespace Detangle.App;

/// <summary>One open tab.</summary>
public sealed partial class DocumentTab : ObservableObject
{
    [ObservableProperty]
    private RenderDocument? _rendered;

    [ObservableProperty]
    private string? _pendingAnchor;

    /// <summary>Creates a tab for a document.</summary>
    public DocumentTab(VaultDocument document)
    {
        Document = document;
    }

    /// <summary>The document this tab shows.</summary>
    public VaultDocument Document { get; }

    /// <summary>The tab's label.</summary>
    public string Title => Document.DisplayName;

    /// <summary>The tab's tooltip.</summary>
    public string Path => Document.RelativePath;
}

/// <summary>An entry in the command palette.</summary>
/// <param name="Title">What the reader sees.</param>
/// <param name="Subtitle">Where it leads, or what it does.</param>
/// <param name="Invoke">What running it does.</param>
public sealed record PaletteEntry(string Title, string Subtitle, Action Invoke);

/// <summary>
/// The reader shell: a vault, a navigation tree, open tabs, and the panes around them
/// (plan.md section 6.1).
/// <para>
/// Everything expensive happens once per vault — the scan, the link graph, the tag tree,
/// the navigation source — and everything per-page is derived from those. Opening a tab
/// is then a render, not a re-analysis, which is what keeps navigation feeling instant
/// on a vault of any size.
/// </para>
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IDiagramRenderer _diagramRenderer;
    private readonly Dictionary<string, int> _readingPositions = new(StringComparer.Ordinal);
    private readonly List<VaultDocument> _back = [];
    private readonly List<VaultDocument> _forward = [];

    private VaultState _state = VaultState.Empty;
    private VaultSnapshotRecord _baseline = VaultSnapshotRecord.Empty;
    private VaultSnapshot? _vault;
    private RenderModelBuilder? _builder;
    private LinkGraph? _graph;
    private SearchIndex? _search;
    private VaultWatcher? _watcher;
    private bool _navigating;

    [ObservableProperty]
    private string _vaultPath = string.Empty;

    [ObservableProperty]
    private DocumentTab? _activeTab;

    [ObservableProperty]
    private string _status = "Open a vault to start reading.";

    [ObservableProperty]
    private string _linkSummary = string.Empty;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private string _paletteQuery = string.Empty;

    [ObservableProperty]
    private string _navigationSourceName = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    /// <summary>The selected tag, as the search box would spell it, or empty for none.</summary>
    [ObservableProperty]
    private string _selectedTag = string.Empty;

    /// <summary>The header over the tag's page list: which tag, and how many pages.</summary>
    [ObservableProperty]
    private string _tagSummary = string.Empty;

    [ObservableProperty]
    private bool _isGraphVisible;

    /// <summary>
    /// Whether the navigation rail is showing. Independent of the right-hand panel: a
    /// reader following backlinks wants the outline and not the file tree, and someone
    /// reading a long page wants neither.
    /// </summary>
    [ObservableProperty]
    private bool _isLeftPanelVisible = true;

    /// <summary>Whether the outline and backlinks panel is showing.</summary>
    [ObservableProperty]
    private bool _isRightPanelVisible = true;

    [ObservableProperty]
    private GraphModel _graphModel = GraphModel.Empty;

    [ObservableProperty]
    private string _graphSummary = string.Empty;

    /// <summary>
    /// What the pointer is over in the graph.
    /// <para>
    /// Its own line, in the graph's own header. It used to be written to the status bar,
    /// which meant moving the mouse across the picture erased whatever the status bar was
    /// saying — including the one-line report of an export that had just failed.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private string _graphHover = string.Empty;

    [ObservableProperty]
    private string _graphTypeFilter = string.Empty;

    [ObservableProperty]
    private string _graphTagFilter = string.Empty;

    [ObservableProperty]
    private string _graphFolderFilter = string.Empty;

    [ObservableProperty]
    private bool _isGraphLocal;

    [ObservableProperty]
    private int _graphHops = 2;

    [ObservableProperty]
    private bool _graphShowsOrphans = true;

    [ObservableProperty]
    private bool _graphShowsMissingTargets = true;

    /// <summary>Creates the shell.</summary>
    /// <param name="diagramRenderer">The diagram backend; defaults to Mermaider.</param>
    public ShellViewModel(IDiagramRenderer? diagramRenderer = null)
    {
        _diagramRenderer = diagramRenderer ?? new MermaiderDiagramRenderer();
    }

    /// <summary>
    /// What the head this shell is running in can do. Set by the head at startup; the
    /// default is the desktop's, which is what a test constructing a bare shell is.
    /// </summary>
    public HeadCapabilities Capabilities { get; init; } = HeadCapabilities.Desktop;

    /// <summary>
    /// This reader's own settings, which live in their configuration directory rather than
    /// in any vault. A head with nowhere to write leaves this as the empty one.
    /// </summary>
    public AppSettings Settings { get; init; } = AppSettings.None;

    /// <summary>The vaults opened before, most recent first, for the start screen.</summary>
    public ObservableCollection<RecentVault> RecentVaults { get; } = [];

    /// <summary>Raised when the reader should scroll to an anchor in the active document.</summary>
    public event EventHandler<string>? AnchorRequested;

    /// <summary>
    /// Raised when find-in-page should open. The bar belongs to the view — it searches a
    /// control tree — so the palette asks rather than opening it.
    /// </summary>
    public event EventHandler? FindRequested;

    /// <summary>
    /// Raised when something other than the tag tree picked a tag — a chip in the page,
    /// say — so the tree can move its selection to match.
    /// </summary>
    public event EventHandler<TagNode>? TagSelected;

    /// <summary>The navigation tree for the left rail.</summary>
    public ObservableCollection<NavigationNode> Navigation { get; } = [];

    /// <summary>The tag hierarchy for the tag browser.</summary>
    public ObservableCollection<TagNode> Tags { get; } = [];

    /// <summary>
    /// The pages carrying the selected tag, or any tag under it.
    /// <para>
    /// The tree shows a count; this is the count. Selecting a tag used to open one
    /// arbitrary page out of the several it named, which is the one interaction in the
    /// shell that did something other than what it appeared to do.
    /// </para>
    /// </summary>
    public ObservableCollection<VaultDocument> TagDocuments { get; } = [];

    /// <summary>Open tabs.</summary>
    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    /// <summary>Headings of the active document.</summary>
    public ObservableCollection<HeadingRenderBlock> Outline { get; } = [];

    /// <summary>Pages linking to the active document.</summary>
    public ObservableCollection<Backlink> Backlinks { get; } = [];

    /// <summary>Pages naming the active document without linking it.</summary>
    public ObservableCollection<UnlinkedMention> Mentions { get; } = [];

    /// <summary>Palette entries matching <see cref="PaletteQuery"/>.</summary>
    public ObservableCollection<PaletteEntry> PaletteResults { get; } = [];

    /// <summary>Hits for <see cref="SearchQuery"/>.</summary>
    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    /// <summary>
    /// The ambiguities somebody settled in this vault, so they can be reviewed and undone.
    /// <para>
    /// Rung 14 is the only one a person writes, and until this list existed there was no
    /// way to see what had been decided or to change your mind — the decision was
    /// invisible from the moment it was made.
    /// </para>
    /// </summary>
    public ObservableCollection<SettledChoice> SettledChoices { get; } = [];

    /// <summary>Link Doctor findings over the whole vault.</summary>
    public ObservableCollection<Finding> Findings { get; } = [];

    /// <summary>The same findings, grouped by kind, for the triage tree.</summary>
    public ObservableCollection<FindingGroup> FindingGroups { get; } = [];

    /// <summary>The vault, once one is open.</summary>
    public VaultSnapshot? Vault => _vault;

    /// <summary>The link graph over the open vault.</summary>
    public LinkGraph? Graph => _graph;

    /// <summary>True when a vault is open.</summary>
    public bool HasVault => _vault is not null;

    /// <summary>True when going back is possible.</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>True when going forward is possible.</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// True when the open vault is a copy rather than the reader's own folder.
    /// <para>
    /// The browser build cannot hand a scanner a folder on someone's disk, so a folder
    /// opened there is copied into memory first. Everything reads normally; what must not
    /// happen is a save, which would write into a filesystem that disappears with the tab
    /// and lose the reader's work while telling them it had been saved.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _isDetachedCopy;

    /// <summary>Scans a vault and opens its most index-like page.</summary>
    public void OpenVault(string path) => OpenVault(path, isDetachedCopy: false);

    /// <summary>Scans a vault and opens its most index-like page.</summary>
    /// <param name="path">The folder to scan.</param>
    /// <param name="isDetachedCopy">True when the folder is a copy that cannot be saved to.</param>
    public void OpenVault(string path, bool isDetachedCopy)
    {
        if (!Prepare(path, isDetachedCopy))
        {
            return;
        }

        if (Scan(path) is not { } scanned)
        {
            return;
        }

        Adopt(scanned, isDetachedCopy);
    }

    /// <summary>
    /// Scans a vault off the UI thread and opens its most index-like page.
    /// <para>
    /// The scan reads every markdown file in the folder and parses each one, which on a
    /// vault of a few thousand pages is long enough to freeze a window — and the start
    /// screen's recent list makes that freeze the first thing a reader meets, every
    /// launch. Only the scan moves: everything after it touches observable collections
    /// and belongs on the thread that owns them.
    /// </para>
    /// </summary>
    /// <param name="path">The folder to scan.</param>
    /// <param name="isDetachedCopy">True when the folder is a copy that cannot be saved to.</param>
    public async Task OpenVaultAsync(string path, bool isDetachedCopy = false)
    {
        if (!Prepare(path, isDetachedCopy))
        {
            return;
        }

        IsOpening = true;
        Status = $"Reading {path}…";

        try
        {
            // ConfigureAwait(true): what comes back has to be applied where the collections
            // live, and this method is only ever called from a head that has a UI thread.
            if (await Task.Run(() => Scan(path)).ConfigureAwait(true) is { } scanned)
            {
                Adopt(scanned, isDetachedCopy);
            }
        }
        finally
        {
            IsOpening = false;
        }
    }

    /// <summary>True while a vault is being read.</summary>
    [ObservableProperty]
    private bool _isOpening;

    /// <summary>Settles what is true before a scan starts, or reports why there will not be one.</summary>
    private bool Prepare(string path, bool isDetachedCopy)
    {
        IsDetachedCopy = isDetachedCopy;

        if (!string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        Status = "Choose a folder to open.";

        return false;
    }

    /// <summary>
    /// Reads the folder, or reports why it could not be read. Runs off the UI thread in
    /// the async path, so it touches nothing but its arguments and the status line.
    /// </summary>
    private VaultSnapshot? Scan(string path)
    {
        try
        {
            return VaultScanner.Scan(path);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;

            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Text a person typed, not a path the application chose, so the ways it can be
            // malformed are open-ended: illegal characters, a device name, a path longer
            // than the platform allows. Path.GetFullPath rejects all of those before the
            // scan begins, and none of them are the filesystem exceptions above.
            Status = $"That is not a usable folder path: {ex.Message}";

            return null;
        }
    }

    /// <summary>Takes a scanned vault as the open one and builds everything derived from it.</summary>
    private void Adopt(VaultSnapshot scanned, bool isDetachedCopy)
    {
        _vault = scanned;

        var renderer = new CachingDiagramRenderer(
            _diagramRenderer, new FileDiagramCacheStore(_vault.RootPath));

        // A detached copy is a folder the browser handed over, not a place on disk, so
        // its choices can only last as long as the tab. Opening the store with no path
        // is what makes that true rather than pretending otherwise.
        _state = VaultState.Open(isDetachedCopy ? null : _vault.RootPath);

        _builder = new RenderModelBuilder(
            _vault,
            options: new RenderOptions
            {
                DiagramRenderer = renderer,
                DiagramTheme = IsDarkTheme ? DiagramTheme.Dark : DiagramTheme.Light,
            },
            rememberedChoices: _state.Choices.ForResolver);

        _graph = LinkGraph.Build(_vault, _state.Choices.ForResolver);
        _baseline = isDetachedCopy ? VaultSnapshotRecord.Empty : BaselineStore.Load(_vault.RootPath);
        VaultPath = _vault.RootPath;

        // Search and the file watcher are conveniences, not the product. Both need
        // platform facilities — SQLite's native library, and OS file notifications — that
        // the WASM demo does not have, and a reader losing the search box is a far better
        // outcome there than a vault that refuses to open.
        _search?.Dispose();
        _search = null;

        try
        {
            _search = SearchIndex.Open(_vault.RootPath);
            _search.Rebuild(_vault, ReadContent);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _search?.Dispose();
            _search = null;
            SearchSummary = "Search is unavailable in this build.";
        }

        _watcher?.Dispose();
        _watcher = null;

        try
        {
            _watcher = new VaultWatcher(_vault);
            _watcher.Changed += OnVaultChanged;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or NotSupportedException
            or UnauthorizedAccessException or ArgumentException)
        {
            // Without a watcher the vault is still read; it just will not notice a file
            // changing underneath it until something asks for a rescan.
            _watcher = null;
        }

        // A filter naming a page in the vault that was open before would hide every
        // finding in the one being opened now.
        FilterFindings(null);

        RefreshDelta();
        RefreshFindings();
        RefreshSettledChoices();

        NavigationTreeBuilder.Result navigation = NavigationTreeBuilder.Build(_vault, ReadContent);
        NavigationSourceName = navigation.Source.ToString();

        Navigation.Clear();

        foreach (NavigationNode node in navigation.Roots)
        {
            Navigation.Add(node);
        }

        Tags.Clear();
        SelectTag(null);

        foreach (TagNode tag in TagTree.Build(_vault))
        {
            Tags.Add(tag);
        }

        OnPropertyChanged(nameof(HasTags));

        Tabs.Clear();
        _back.Clear();
        _forward.Clear();

        int broken = _graph.Resolutions.Count(r => r.IsUnresolved);
        int ambiguous = _graph.Resolutions.Count(r => r.IsAmbiguous);

        // The section 5.5 status counter: how big this vault is, and how much of it
        // Detangle could not answer cleanly.
        LinkSummary = $"{_vault.Documents.Count(d => d.IsMarkdown):N0} files · "
            + $"{broken:N0} broken · {ambiguous:N0} ambiguous";

        Status = $"{_vault.Profile.Flavor} · navigation from {NavigationSourceName}";

        OnPropertyChanged(nameof(HasVault));

        if (!isDetachedCopy && Capabilities.CanPersistAcrossSessions)
        {
            Settings.Remember(_vault, DateTimeOffset.UtcNow);
        }

        RefreshRecentVaults();

        VaultDocument? first = FindStartPage();

        if (first is not null)
        {
            Open(first);
        }
    }

    /// <summary>Rereads the recent list, which the start screen shows when no vault is open.</summary>
    public void RefreshRecentVaults()
    {
        RecentVaults.Clear();

        foreach (RecentVault vault in Settings.Recent)
        {
            RecentVaults.Add(vault);
        }

        OnPropertyChanged(nameof(HasRecentVaults));
    }

    /// <summary>True when there is a recent list worth showing.</summary>
    public bool HasRecentVaults => RecentVaults.Count > 0;

    /// <summary>
    /// True when a vault is open, has been examined, and nothing came back. The five empty
    /// states below say what an empty pane means, because a blank panel is the one thing a
    /// reader cannot tell apart from a panel that has not loaded.
    /// </summary>
    public bool HasCleanBillOfHealth => HasVault && FindingGroups.Count == 0;

    /// <summary>True when the open vault has no tags anywhere.</summary>
    public bool HasTags => Tags.Count > 0;

    /// <summary>True when a page is open and nothing links to it.</summary>
    public bool HasNoBacklinks => ActiveTab is not null && Backlinks.Count == 0;

    /// <summary>True when a page is open and nothing names it without linking it.</summary>
    public bool HasNoMentions => ActiveTab is not null && Mentions.Count == 0;

    /// <summary>
    /// Opens a vault from the recent list, or explains why it is not there any more.
    /// <para>
    /// A folder that has been moved or deleted is reported rather than thrown over, and
    /// stays on the list marked missing: silently dropping it would leave a reader
    /// wondering whether they had imagined opening it.
    /// </para>
    /// </summary>
    /// <param name="vault">The entry that was chosen.</param>
    public async Task OpenRecentAsync(RecentVault vault)
    {
        if (!vault.Exists)
        {
            Status = $"{vault.Path} is not there any more.";

            return;
        }

        await OpenVaultAsync(vault.Path).ConfigureAwait(true);
    }

    /// <summary>Takes one vault off the recent list.</summary>
    /// <param name="vault">The entry to forget.</param>
    public void ForgetRecent(RecentVault vault)
    {
        Settings.Forget(vault);
        RefreshRecentVaults();
    }

    /// <summary>Opens a document in a tab, reusing one that already shows it.</summary>
    public void Open(VaultDocument document, string? anchor = null)
    {
        if (_builder is null)
        {
            return;
        }

        if (ActiveTab is { } current && !_navigating
            && !string.Equals(current.Document.RelativePath, document.RelativePath, StringComparison.Ordinal))
        {
            _back.Add(current.Document);
            _forward.Clear();
            RaiseHistoryChanged();
        }

        DocumentTab? existing = Tabs.FirstOrDefault(
            t => string.Equals(t.Document.RelativePath, document.RelativePath, StringComparison.Ordinal));

        if (existing is null)
        {
            existing = new DocumentTab(document) { Rendered = _builder.Build(document) };
            Tabs.Add(existing);
        }

        existing.PendingAnchor = anchor;
        ActiveTab = existing;

        if (anchor is { Length: > 0 })
        {
            AnchorRequested?.Invoke(this, anchor);
        }
    }

    /// <summary>
    /// Opens a page by vault-relative path, resolving it the same way a link would so
    /// that "wiki/schema" finds wiki/schema.md.
    /// </summary>
    /// <param name="relativePath">The path to open.</param>
    /// <returns>True when a page was found.</returns>
    public bool OpenPath(string relativePath)
    {
        if (_vault is null || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        VaultDocument? document = _vault.Index.ByRelativePath(relativePath).FirstOrDefault()
            ?? _vault.Documents.FirstOrDefault(d => d.IsMarkdown
                && LinkNormalizer.Normalize(d.RelativePath) == LinkNormalizer.Normalize(relativePath));

        if (document is null)
        {
            return false;
        }

        Open(document);

        return true;
    }

    /// <summary>Closes a tab, activating its neighbour.</summary>
    public void Close(DocumentTab tab)
    {
        int index = Tabs.IndexOf(tab);

        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    /// <summary>Goes back to the previously read document.</summary>
    [RelayCommand]
    public void GoBack()
    {
        if (_back.Count == 0 || ActiveTab is not { } current)
        {
            return;
        }

        VaultDocument target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(current.Document);

        NavigateWithoutHistory(target);
    }

    /// <summary>Goes forward again after going back.</summary>
    [RelayCommand]
    public void GoForward()
    {
        if (_forward.Count == 0 || ActiveTab is not { } current)
        {
            return;
        }

        VaultDocument target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(current.Document);

        NavigateWithoutHistory(target);
    }

    /// <summary>Opens or closes the command palette.</summary>
    [RelayCommand]
    public void TogglePalette()
    {
        IsPaletteOpen = !IsPaletteOpen;

        if (IsPaletteOpen)
        {
            PaletteQuery = string.Empty;
            RefreshPalette();
        }
    }

    /// <summary>
    /// True until the reader picks a palette by hand. While it holds, the shell follows
    /// the operating system; choosing one is a decision the system should not overrule.
    /// </summary>
    public bool FollowsSystemTheme { get; private set; } = true;

    /// <summary>Applies the operating system's palette, unless the reader has chosen one.</summary>
    public void FollowSystemTheme(bool isDark)
    {
        if (FollowsSystemTheme && isDark != IsDarkTheme)
        {
            ApplyTheme(isDark);
        }
    }

    /// <summary>Switches between the light and dark palettes and re-renders every tab.</summary>
    [RelayCommand]
    public void ToggleTheme()
    {
        FollowsSystemTheme = false;
        ApplyTheme(!IsDarkTheme);
    }

    private void ApplyTheme(bool isDark)
    {
        IsDarkTheme = isDark;

        if (_vault is null)
        {
            return;
        }

        // Diagrams are themed, so a theme switch has to rebuild them; the cache keys on
        // theme, so the previous palette's renders survive for the switch back.
        RebuildBuilder();

        foreach (DocumentTab tab in Tabs)
        {
            tab.Rendered = _builder!.Build(tab.Document);
        }
    }

    /// <summary>
    /// Rebuilds the render model builder over the current vault, theme and remembered
    /// choices. One copy, because a builder rebuilt without the choices silently forgets
    /// every decision the reader has made.
    /// </summary>
    private void RebuildBuilder()
    {
        if (_vault is null)
        {
            return;
        }

        _builder = new RenderModelBuilder(
            _vault,
            options: new RenderOptions
            {
                DiagramRenderer = new CachingDiagramRenderer(
                    _diagramRenderer, new FileDiagramCacheStore(_vault.RootPath)),
                DiagramTheme = IsDarkTheme ? DiagramTheme.Dark : DiagramTheme.Light,
            },
            rememberedChoices: _state.Choices.ForResolver);
    }

    /// <summary>Reindexes and re-examines after files changed outside the app.</summary>
    private void OnVaultChanged(object? sender, IReadOnlyList<VaultChange> changes)
    {
        if (_vault is null)
        {
            return;
        }

        // A rescan is what keeps the resolver honest: a renamed file changes what every
        // link in the vault resolves to, so the indexes are rebuilt rather than patched.
        VaultSnapshot rescanned;

        try
        {
            rescanned = VaultScanner.Scan(_vault.RootPath);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        _vault = rescanned;
        _graph = LinkGraph.Build(_vault, _state.Choices.ForResolver);
        _search?.Rebuild(_vault, ReadContent);

        foreach (VaultChange change in changes)
        {
            DocumentTab? tab = Tabs.FirstOrDefault(
                t => string.Equals(t.Document.RelativePath, change.RelativePath, StringComparison.Ordinal));

            if (tab is null)
            {
                continue;
            }

            VaultDocument? updated = _vault.Index.ByRelativePath(change.RelativePath).FirstOrDefault();

            if (updated is not null && _builder is not null)
            {
                tab.Rendered = _builder.Build(updated);
            }
        }

        RefreshDelta();
        RefreshFindings();
        RunSearch();
        RebuildGraphIfShown();
    }

    /// <summary>
    /// Rescans the vault now, rather than waiting for the watcher's next sweep. The
    /// watcher calls this on its own schedule; callers use it when they know something
    /// changed and want the answer immediately.
    /// </summary>
    public void Reconcile() => OnVaultChanged(this, []);

    /// <summary>Re-examines the vault for Link Doctor findings.</summary>
    public void RefreshFindings()
    {
        Findings.Clear();

        if (_graph is null)
        {
            return;
        }

        IReadOnlySet<string> changed = Delta.TouchedDocuments;

        foreach (Finding finding in LinkDoctor.Examine(_graph, ReadContent)
            .Where(f => !_state.IsIgnored(f))
            .Where(f => !ShowOnlyChanged || changed.Contains(f.Document.RelativePath))
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Document.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            Findings.Add(finding);
        }

        RegroupFindings();
    }

    /// <summary>Rebuilds the triage tree from the flat list.</summary>
    private void RegroupFindings()
    {
        FindingGroups.Clear();

        IEnumerable<Finding> shown = Findings;

        // The ledger asked for one page. Filtering here rather than replacing the
        // collection keeps every other reader of Findings — the fix-all pass, the report —
        // looking at the whole vault, which is what they are for.
        if (FindingPageFilter is { Length: > 0 } path)
        {
            shown = shown.Where(f => string.Equals(f.Document.RelativePath, path, StringComparison.Ordinal));

            if (FindingConfidenceFilter is { } confidence)
            {
                shown = shown.Where(f => f.Resolution?.Confidence == confidence);
            }
        }

        foreach (FindingGroup group in shown
            .GroupBy(f => f.Kind)
            .Select(g => new FindingGroup(g.Key, g))
            .OrderBy(g => g.Severity)
            .ThenBy(g => g.Kind))
        {
            FindingGroups.Add(group);
        }

        IgnoredCount = _state.IgnoredCount;

        OnPropertyChanged(nameof(HasCleanBillOfHealth));
    }

    /// <summary>
    /// Narrows the Doctor to one page, and optionally to one band of the ledger.
    /// <para>
    /// The strip has always known how the open page's links resolved. This is what turns
    /// that from a picture into a question the panel can answer.
    /// </para>
    /// </summary>
    /// <param name="relativePath">The page to narrow to, or null to show the whole vault.</param>
    /// <param name="confidence">The resolution class to narrow to, or null for all of them.</param>
    public void FilterFindings(string? relativePath, ResolutionConfidence? confidence = null)
    {
        FindingPageFilter = relativePath;
        FindingConfidenceFilter = confidence;

        RegroupFindings();

        FindingFilterSummary = relativePath is { Length: > 0 } path
            ? confidence is { } band
                ? $"{band} links in {path}"
                : $"this page only · {path}"
            : null;
    }

    /// <summary>The page the Doctor is narrowed to, or null for the whole vault.</summary>
    public string? FindingPageFilter { get; private set; }

    /// <summary>The resolution class the Doctor is narrowed to, or null for all of them.</summary>
    public ResolutionConfidence? FindingConfidenceFilter { get; private set; }

    /// <summary>The chip over the finding tree, or null when nothing is narrowed.</summary>
    [ObservableProperty]
    private string? _findingFilterSummary;

    /// <summary>How many findings the reader has dismissed in this vault.</summary>
    [ObservableProperty]
    public partial int IgnoredCount { get; set; }

    /// <summary>What changed since the reader last marked the vault, in one line.</summary>
    [ObservableProperty]
    public partial string? ChangeSummary { get; set; }

    /// <summary>True when the triage list is restricted to what changed since the baseline.</summary>
    [ObservableProperty]
    public partial bool ShowOnlyChanged { get; set; }

    /// <summary>The difference between the marked baseline and the vault as it stands.</summary>
    public VaultDelta Delta { get; private set; } = VaultDelta.None;

    /// <summary>
    /// Marks the vault's current state as the one to compare against from now on.
    /// <para>
    /// Explicit, and deliberately not automatic. Nothing tells Detangle that a generation
    /// run has finished: the watcher debounces at 250 ms and fires dozens of times while
    /// one is under way, so a baseline taken on rescan would as often as not be a
    /// half-written corpus compared against itself.
    /// </para>
    /// </summary>
    public void MarkBaseline()
    {
        if (_graph is null)
        {
            return;
        }

        _baseline = VaultSnapshotRecord.Take(_graph, ReadContent);

        // The same file detangle-lint --mark writes, at the vault root. A gate in
        // continuous integration and this button have to mean the same thing.
        bool persisted = !IsDetachedCopy
            && Capabilities.CanPersistAcrossSessions
            && BaselineStore.Save(_vault!.RootPath, _baseline);

        RefreshDelta();

        Status = persisted
            ? $"Marked in {BaselineStore.FileName}. Changes are measured against the vault as it stands."
            : "Marked for this session; this copy cannot be written to.";
    }

    /// <summary>Recomputes what has changed since the baseline.</summary>
    private void RefreshDelta()
    {
        Delta = _graph is null
            ? VaultDelta.None
            : VaultDelta.Compare(_baseline, VaultSnapshotRecord.Take(_graph, ReadContent));

        ChangeSummary = Delta.Summary();

        if (Delta.IsEmpty)
        {
            ShowOnlyChanged = false;
        }
    }

    partial void OnShowOnlyChangedChanged(bool value) => RefreshFindings();

    /// <summary>
    /// What applying a finding's fix would do to its line: the text as it stands and the
    /// text as it would be written.
    /// <para>
    /// Computed on demand rather than kept on the finding, because it means reading the
    /// document, and a panel that read four hundred files to draw a list would be a panel
    /// nobody opened twice. <see cref="LinkDoctor.ApplyRewrite"/> is pure — content in,
    /// content out — so the preview is the fix, not a guess at it.
    /// </para>
    /// </summary>
    /// <param name="finding">The finding to preview.</param>
    /// <returns>The before and after lines, or null when there is nothing to apply.</returns>
    public (string Before, string After)? PreviewFix(Finding finding)
    {
        if (finding.SuggestedRewrite is not { Length: > 0 }
            || ReadContent(finding.Document) is not { } content)
        {
            return null;
        }

        if (LinkDoctor.ApplyRewrite(content, finding) is not { } rewritten)
        {
            return null;
        }

        string[] before = Lines(content);
        string[] after = Lines(rewritten);
        int index = finding.Line - 1;

        return index >= 0 && index < before.Length && index < after.Length
            ? (before[index], after[index])
            : null;

        static string[] Lines(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    /// <summary>
    /// Applies one finding's fix to the file it is in, and returns true when the file was
    /// written.
    /// <para>
    /// Separate from <see cref="FixAllSafe"/> on purpose: that one is restricted to the
    /// links with exactly one correct answer, and this is the reader saying they have
    /// looked at this one. It still refuses anything with no suggested rewrite, so a
    /// near-miss guess cannot be applied by accident.
    /// </para>
    /// </summary>
    public bool ApplyFix(Finding finding)
    {
        if (finding.SuggestedRewrite is not { Length: > 0 }
            || ReadContent(finding.Document) is not { } content
            || LinkDoctor.ApplyRewrite(content, finding) is not { } rewritten
            || !WriteAtomically(finding.Document.AbsolutePath, content: rewritten))
        {
            return false;
        }

        Reconcile();

        return true;
    }

    /// <summary>
    /// Stops reporting one finding. The key is the document, the kind and the link — never
    /// the line number, which moves every time the generator runs.
    /// </summary>
    public void Ignore(Finding finding)
    {
        bool persisted = _state.Ignore(finding);

        Findings.Remove(finding);
        RegroupFindings();

        Status = persisted
            ? $"Ignoring that {finding.Kind} in {finding.Document.RelativePath}."
            : $"Ignoring that {finding.Kind} until this tab is closed; this copy cannot be written to.";
    }

    /// <summary>Reports every dismissed finding again.</summary>
    public void ShowIgnored()
    {
        _state.UnignoreAll();
        RefreshFindings();

        Status = "Showing every finding again.";
    }

    /// <summary>
    /// Fills in what a broken link or a broken fragment probably meant, in place, the
    /// first time a reader opens that finding, and returns the finding to draw.
    /// <para>
    /// It returns rather than only replacing because replacing the selected item in an
    /// observable collection clears the tree's selection: a caller that re-read the
    /// selection afterwards would draw the finding as it was before the search ran, which
    /// is to say without the answer the search was for.
    /// </para>
    /// </summary>
    public Finding Suggest(Finding finding)
    {
        bool wanted = finding.Kind switch
        {
            FindingKind.BrokenLink => finding.SuggestedRewrite is null,
            FindingKind.BrokenAnchor => finding.SuggestedAnchor is null,
            _ => false,
        };

        if (!wanted)
        {
            return finding;
        }

        int index = Findings.IndexOf(finding);

        if (index < 0)
        {
            return finding;
        }

        Finding suggested = LinkDoctor.SuggestFix(finding);

        Findings[index] = suggested;

        // The tree binds to the groups, which hold their own list. Replacing only in the
        // flat one leaves the panel showing the finding as it was before the search ran,
        // which is to say without the answer the search was for.
        foreach (FindingGroup group in FindingGroups.Where(g => g.Kind == finding.Kind))
        {
            int position = group.Findings.IndexOf(finding);

            if (position >= 0)
            {
                group.Findings[position] = suggested;
            }
        }

        return suggested;
    }

    /// <summary>
    /// Records which document an ambiguous link was meant to reach, and re-resolves the
    /// vault so every copy of that link follows the reader's choice from now on.
    /// <para>
    /// This is the only step in the chain the reader writes. Every other rule is
    /// deterministic and applies everywhere; this one applies to one target written in
    /// one folder, which is the narrowest thing that can be said about an ambiguity
    /// without guessing again (plan.md section 5.4).
    /// </para>
    /// </summary>
    /// <param name="resolution">The ambiguous link the reader settled.</param>
    /// <param name="chosen">The document they picked.</param>
    public void Settle(LinkResolution resolution, VaultDocument chosen)
    {
        if (_vault is null || _graph is null)
        {
            return;
        }

        VaultDocument? source = _vault.Index
            .ByRelativePath(resolution.Link.SourcePath)
            .FirstOrDefault();

        if (source is null)
        {
            return;
        }

        bool persisted = _state.Choices.Settle(
            source, resolution.Link.RawTarget, chosen, resolution.Candidates.Count);

        // Everything downstream reads the chain's answers, so they all have to be asked
        // again: the graph the Doctor and the graph view are built from, and every open
        // tab, whose links carry the rule that resolved them.
        _graph = LinkGraph.Build(_vault, _state.Choices.ForResolver);
        RebuildBuilder();

        foreach (DocumentTab tab in Tabs)
        {
            tab.Rendered = _builder!.Build(tab.Document);
        }

        RefreshFindings();
        RefreshSettledChoices();
        RebuildGraphIfShown();

        Status = persisted
            ? $"\"{resolution.Link.RawTarget}\" now means {chosen.RelativePath} in this vault."
            : $"\"{resolution.Link.RawTarget}\" now means {chosen.RelativePath} "
                + "until this tab is closed; this copy cannot be written to.";
    }

    /// <summary>
    /// The last few things the shell said, in full.
    /// <para>
    /// The status bar is one line with a character ellipsis on it, and it is the only
    /// place a failure is ever reported. A long export error was therefore trimmed to
    /// something unactionable and then replaced by the next message — so the panel keeps
    /// what was said, untrimmed and selectable, and an exception's full text goes here
    /// even when only its first line goes to the bar.
    /// </para>
    /// </summary>
    public ObservableCollection<string> StatusLog { get; } = [];

    /// <summary>How many messages the panel keeps.</summary>
    public const int StatusLogLimit = 50;

    /// <summary>Whether the status panel is open.</summary>
    [ObservableProperty]
    private bool _isStatusLogVisible;

    /// <summary>Opens or closes the status panel.</summary>
    [RelayCommand]
    public void ToggleStatusLog() => IsStatusLogVisible = !IsStatusLogVisible;

    /// <summary>The whole log as one block of text, for the Copy button.</summary>
    public string StatusLogText => string.Join(Environment.NewLine, StatusLog);

    /// <summary>
    /// Records detail the status bar has no room for — an exception's full text, a stack
    /// trace — without putting it on the one line a reader is meant to read.
    /// </summary>
    /// <param name="detail">The text to keep.</param>
    public void LogDetail(string detail) => Record(detail);

    partial void OnStatusChanged(string value) => Record(value);

    private void Record(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StatusLog.Add(message);

        // Oldest first out. A panel that grew without bound would be a memory leak whose
        // symptom is a scrollbar nobody reaches the top of.
        while (StatusLog.Count > StatusLogLimit)
        {
            StatusLog.RemoveAt(0);
        }
    }

    /// <summary>
    /// The rewrite waiting for the reader to say yes, or null when none is.
    /// </summary>
    [ObservableProperty]
    private PendingWrite? _pendingWrite;

    /// <summary>
    /// Works out what "fix all safe" would change and holds it, rather than doing it.
    /// <para>
    /// The plan comes from the same planner the CLI emits patches with, so what the card
    /// lists is exactly what the write will do.
    /// </para>
    /// </summary>
    public void ProposeFixAllSafe()
    {
        PatchSet plan = VaultRepair.Plan(Findings, ReadContent, RepairPolicy.Safe);

        if (plan.IsEmpty)
        {
            Status = "Nothing here has a single canonical rewrite.";
            PendingWrite = null;

            return;
        }

        PendingWrite = new PendingWrite(
            "Fix all safe",
            [.. plan.Patches.Select(p => new PendingWriteFile(p.RelativePath, p.LinkCount))],
            FixAllSafe);
    }

    /// <summary>
    /// Works out what normalizing every link would change and holds it, rather than doing
    /// it. The dry run is the same pass as the write with nothing written, so the count on
    /// the card is the count the write produces.
    /// </summary>
    /// <param name="form">Which canonical form to rewrite to.</param>
    public void ProposeNormalizeVault(LinkForm form = LinkForm.VaultRelative)
    {
        if (_vault is null || _graph is null)
        {
            return;
        }

        var files = new List<PendingWriteFile>();

        foreach (VaultDocument document in _vault.Documents.Where(d => d.IsMarkdown))
        {
            if (ReadContent(document) is not { } content)
            {
                continue;
            }

            NormalizeResult result = MarkdownNormalizer.Normalize(
                content, document, _graph.OutboundFrom(document), form);

            if (result.Rewritten > 0)
            {
                files.Add(new PendingWriteFile(document.RelativePath, result.Rewritten));
            }
        }

        if (files.Count == 0)
        {
            Status = "Every link in this vault is already in its canonical form.";
            PendingWrite = null;

            return;
        }

        PendingWrite = new PendingWrite(
            "Normalize links in place",
            [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)],
            () => NormalizeVault(form));
    }

    /// <summary>Performs the proposed write.</summary>
    public int ConfirmPendingWrite()
    {
        if (PendingWrite is not { } pending)
        {
            return 0;
        }

        // Cleared before the write rather than after: the write rescans, which rebuilds
        // the findings the plan was made from, and a card still on screen would then be
        // describing a vault that no longer exists.
        PendingWrite = null;

        return pending.Apply();
    }

    /// <summary>Abandons the proposed write. Nothing was touched.</summary>
    public void CancelPendingWrite()
    {
        if (PendingWrite is { } pending)
        {
            PendingWrite = null;
            Status = $"{pending.Title} cancelled. No file was changed.";
        }
    }

    /// <summary>
    /// Applies every safe fix — the links that resolved through steps 4 to 8 and have
    /// exactly one canonical form — and returns how many files were rewritten.
    /// </summary>
    public int FixAllSafe()
    {
        if (_vault is null)
        {
            return 0;
        }

        // Planned by the same code the CLI plans with, so "safe" cannot come to mean two
        // things. What is different here is that the plan is then applied.
        PatchSet plan = VaultRepair.Plan(Findings, ReadContent, RepairPolicy.Safe);
        int written = 0;

        foreach (FilePatch patch in plan.Patches)
        {
            if (_vault.Index.ByRelativePath(patch.RelativePath).FirstOrDefault() is not { } document
                || ReadContent(document) is not { } content)
            {
                continue;
            }

            foreach (Hunk hunk in patch.Hunks.OrderByDescending(h => h.Line))
            {
                content = ReplaceLine(content, hunk);
            }

            if (WriteAtomically(document.AbsolutePath, content))
            {
                written++;
            }
        }

        if (written > 0)
        {
            OnVaultChanged(this, []);
        }

        return written;
    }

    /// <summary>
    /// Puts one planned line back into a file, when the file still says what the plan was
    /// made against. A line that has moved or changed since is left alone: the plan was
    /// computed a moment ago, but "a moment ago" is long enough for a generator to have
    /// rewritten the page underneath it.
    /// </summary>
    private static string ReplaceLine(string content, Hunk hunk)
    {
        bool crlf = content.Contains("\r\n", StringComparison.Ordinal);
        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int index = hunk.Line - 1;

        if (index < 0 || index >= lines.Length
            || !string.Equals(lines[index], hunk.Before, StringComparison.Ordinal))
        {
            return content;
        }

        lines[index] = hunk.After;

        return string.Join(crlf ? "\r\n" : "\n", lines);
    }

    /// <summary>Shows or hides the navigation rail.</summary>
    [RelayCommand]
    public void ToggleLeftPanel() => IsLeftPanelVisible = !IsLeftPanelVisible;

    /// <summary>Shows or hides the outline and backlinks panel.</summary>
    [RelayCommand]
    public void ToggleRightPanel() => IsRightPanelVisible = !IsRightPanelVisible;

    /// <summary>Shows or hides the graph view.</summary>
    [RelayCommand]
    public void ToggleGraph() => IsGraphVisible = !IsGraphVisible;

    /// <summary>
    /// Builds the graph the moment it is asked for, however it was asked for.
    /// <para>
    /// The rebuild used to live in <see cref="ToggleGraph"/>, which the toolbar button
    /// never calls: it binds IsChecked straight to the property, so pressing it showed an
    /// empty canvas while Ctrl+G showed a graph. Hanging the work on the property means
    /// every route into it - button, shortcut, command palette, a future one - gets the
    /// same behaviour.
    /// </para>
    /// </summary>
    partial void OnIsGraphVisibleChanged(bool value)
    {
        if (value)
        {
            RebuildGraph();
        }
    }

    /// <summary>
    /// Rebuilds the graph view's model from the current filters.
    /// <para>
    /// Above the level-of-detail threshold the model collapses to one node per folder.
    /// A five thousand node hairball is not a picture of anything, and drawing it at ten
    /// frames a second would be worse than useless (plan.md section 6.4).
    /// </para>
    /// </summary>
    public void RebuildGraph()
    {
        if (_graph is null)
        {
            GraphModel = GraphModel.Empty;
            GraphSummary = string.Empty;

            return;
        }

        var options = new GraphOptions
        {
            Types = Split(GraphTypeFilter),
            Tags = Split(GraphTagFilter),
            Folder = GraphFolderFilter.Trim() is { Length: > 0 } folder ? folder : null,
            IncludeOrphans = GraphShowsOrphans,
            IncludeMissingTargets = GraphShowsMissingTargets,
            Focus = IsGraphLocal ? ActiveTab?.Document : null,
            Hops = Math.Max(1, GraphHops),
        };

        GraphModel full = GraphModel.Build(_graph, options);
        GraphModel folded = full.WithLevelOfDetail();

        GraphModel = folded;

        int missing = folded.Nodes.Count(n => n.Kind == GraphNodeKind.MissingTarget);

        GraphSummary = ReferenceEquals(full, folded)
            ? $"{folded.Nodes.Count:N0} nodes · {folded.Edges.Count:N0} links · {missing:N0} missing"
            : $"{full.Nodes.Count:N0} nodes folded into {folded.Nodes.Count:N0} folders";
    }

    /// <summary>Opens the page a graph node stands for, or drills into a folder cluster.</summary>
    public void OpenNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node.Kind)
        {
            case GraphNodeKind.Page when node.Document is { } document:
                IsGraphVisible = false;
                Open(document);
                break;

            case GraphNodeKind.Cluster:
                // Clicking a folder is how the reader gets past the level-of-detail fold:
                // filter to that folder and the pages inside it come back individually.
                GraphFolderFilter = node.Folder;
                RebuildGraph();
                break;

            default:
                // A missing target has nothing to open. Phase 7 offers to create it.
                Status = $"\"{node.Label}\" matches no file in this vault";
                break;
        }
    }

    private static IReadOnlyList<string> Split(string value) =>
        [.. value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Rereads the settled ambiguities into the reviewable list.</summary>
    public void RefreshSettledChoices()
    {
        SettledChoices.Clear();

        foreach (SettledChoice choice in _state.Choices.All)
        {
            SettledChoices.Add(choice);
        }

        OnPropertyChanged(nameof(HasSettledChoices));
        OnPropertyChanged(nameof(SettledSummary));
    }

    /// <summary>True when somebody has settled at least one ambiguity in this vault.</summary>
    public bool HasSettledChoices => SettledChoices.Count > 0;

    /// <summary>The heading over the settled list.</summary>
    public string SettledSummary => SettledChoices.Count == 1
        ? "1 settled link"
        : $"{SettledChoices.Count} settled links";

    /// <summary>
    /// Undoes one settled ambiguity, putting the link back where the chain would leave it.
    /// </summary>
    /// <param name="choice">The decision to revoke.</param>
    public void Revoke(SettledChoice choice)
    {
        if (_vault is null)
        {
            return;
        }

        bool persisted = _state.Choices.Forget(choice);

        // The same rebuild a settled link triggers, for the same reason: every downstream
        // answer was computed with this decision in it.
        _graph = LinkGraph.Build(_vault, _state.Choices.ForResolver);
        RebuildBuilder();

        foreach (DocumentTab tab in Tabs)
        {
            tab.Rendered = _builder!.Build(tab.Document);
        }

        RefreshFindings();
        RefreshSettledChoices();
        RebuildGraphIfShown();

        Status = persisted
            ? $"\"{choice.RawTarget}\" is ambiguous again."
            : $"\"{choice.RawTarget}\" is ambiguous again until this tab is closed.";
    }

    /// <summary>Lists the pages a tag names, including every tag nested under it.</summary>
    /// <param name="tag">The selected node, or null to clear the list.</param>
    public void SelectTag(TagNode? tag)
    {
        TagDocuments.Clear();

        if (tag is null)
        {
            SelectedTag = string.Empty;
            TagSummary = string.Empty;
            return;
        }

        foreach (VaultDocument document in tag.AllDocuments)
        {
            TagDocuments.Add(document);
        }

        SelectedTag = tag.FullTag;

        TagSummary = TagDocuments.Count == 1
            ? $"1 PAGE TAGGED {tag.FullTag.ToUpperInvariant()}"
            : $"{TagDocuments.Count} PAGES TAGGED {tag.FullTag.ToUpperInvariant()}";
    }

    /// <summary>Selects a tag by name, opening the rail if it is closed.</summary>
    /// <param name="tag">The tag as written, with or without its leading hash.</param>
    /// <returns>True when the vault has that tag.</returns>
    public bool SelectTagNamed(string tag)
    {
        string wanted = tag.Trim().Trim('#', '/');

        if (wanted.Length == 0 || Find(Tags) is not { } node)
        {
            return false;
        }

        IsLeftPanelVisible = true;
        SelectTag(node);
        TagSelected?.Invoke(this, node);

        return true;

        TagNode? Find(IEnumerable<TagNode> nodes)
        {
            foreach (TagNode candidate in nodes)
            {
                if (string.Equals(candidate.FullTag, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (Find(candidate.Children) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }
    }

    /// <summary>Hands the selected tag to the search box, as the query that means the same thing.</summary>
    public void SearchSelectedTag()
    {
        if (SelectedTag is { Length: > 0 } tag)
        {
            SearchQuery = $"tag:{tag}";
        }
    }

    /// <summary>Runs the current search query.</summary>
    public void RunSearch()
    {
        SearchResults.Clear();

        if (_search is null || _vault is null || SearchQuery.Trim().Length == 0)
        {
            SearchSummary = string.Empty;
            return;
        }

        foreach (SearchHit hit in _search.Search(Core.Search.SearchQuery.Parse(SearchQuery), _vault))
        {
            SearchResults.Add(hit);
        }

        SearchSummary = $"{SearchResults.Count} result{(SearchResults.Count == 1 ? string.Empty : "s")}";
    }

    /// <summary>
    /// Writes a file by replacing it, so a crash mid-write cannot leave a vault holding
    /// half a document.
    /// </summary>
    private static bool WriteAtomically(string path, string content)
    {
        string temporary = path + ".detangle-tmp";

        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do; the original file is untouched either way.
            }

            return false;
        }
    }

    /// <summary>Renders a document for a hover preview, without opening it.</summary>
    public RenderDocument? Preview(VaultDocument document) => _builder?.Build(document);

    /// <summary>Remembers how far down a document the reader had scrolled.</summary>
    public void RememberPosition(string relativePath, int offset) =>
        _readingPositions[relativePath] = offset;

    /// <summary>The remembered scroll offset for a document, or zero.</summary>
    public int PositionOf(string relativePath) =>
        _readingPositions.TryGetValue(relativePath, out int offset) ? offset : 0;

    /// <summary>Reads a document's text from disk.</summary>
    public static string? ReadContent(VaultDocument document)
    {
        try
        {
            return File.Exists(document.AbsolutePath) ? File.ReadAllText(document.AbsolutePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Follows a resolved link, honouring its anchor.</summary>
    public void Follow(LinkResolution resolution)
    {
        if (resolution.Target is { IsMarkdown: true } target)
        {
            Open(target, resolution.Link.Anchor);
        }
    }

    partial void OnActiveTabChanged(DocumentTab? value)
    {
        // The editor belongs to one document. Switching tabs with it open would show one
        // page's text over another page's preview.
        if (_session is not null
            && !string.Equals(_session.Document.RelativePath, value?.Document.RelativePath, StringComparison.Ordinal))
        {
            CloseEditor();
        }

        Outline.Clear();
        Backlinks.Clear();
        Mentions.Clear();

        if (value?.Rendered is not { } rendered || _graph is null)
        {
            Status = _vault is null ? "Open a vault to start reading." : "No document selected.";
            return;
        }

        foreach (HeadingRenderBlock heading in rendered.Outline)
        {
            Outline.Add(heading);
        }

        foreach (Backlink backlink in _graph.BacklinksTo(value.Document))
        {
            Backlinks.Add(backlink);
        }

        foreach (UnlinkedMention mention in _graph.UnlinkedMentions(value.Document, ReadContent, limit: 20))
        {
            Mentions.Add(mention);
        }

        OnPropertyChanged(nameof(HasNoBacklinks));
        OnPropertyChanged(nameof(HasNoMentions));

        int broken = rendered.BrokenLinks.Count();
        int ambiguous = rendered.AmbiguousLinks.Count();

        Status = $"{value.Document.RelativePath} · {rendered.Resolutions.Count} links · "
            + $"{broken} broken · {ambiguous} ambiguous · {Backlinks.Count} backlinks";
    }

    partial void OnPaletteQueryChanged(string value) => RefreshPalette();

    partial void OnSearchQueryChanged(string value) => RunSearch();

    partial void OnGraphTypeFilterChanged(string value) => RebuildGraphIfShown();

    partial void OnGraphTagFilterChanged(string value) => RebuildGraphIfShown();

    partial void OnGraphFolderFilterChanged(string value) => RebuildGraphIfShown();

    partial void OnIsGraphLocalChanged(bool value) => RebuildGraphIfShown();

    partial void OnGraphHopsChanged(int value) => RebuildGraphIfShown();

    partial void OnGraphShowsOrphansChanged(bool value) => RebuildGraphIfShown();

    partial void OnGraphShowsMissingTargetsChanged(bool value) => RebuildGraphIfShown();

    private void RebuildGraphIfShown()
    {
        if (IsGraphVisible)
        {
            RebuildGraph();
        }
    }

    /// <summary>
    /// Rebuilds the palette. Documents match on path and display name, and headings of
    /// the active document match too, so "go to heading" needs no second palette.
    /// </summary>
    private void RefreshPalette()
    {
        PaletteResults.Clear();

        string query = PaletteQuery.Trim();

        foreach (PaletteEntry action in Actions().Where(
            a => query.Length == 0 || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            PaletteResults.Add(action);
        }

        // With no vault open there are no pages and no headings to offer, but the actions
        // above still are — reopening a recent vault is exactly what a palette is for at
        // the moment the wrong vault, or none, is in front of the reader.
        if (_vault is null)
        {
            return;
        }

        IEnumerable<VaultDocument> documents = _vault.Documents.Where(d => d.IsMarkdown);

        if (query.Length > 0)
        {
            documents = documents.Where(d =>
                d.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (VaultDocument document in documents
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(30))
        {
            VaultDocument captured = document;

            PaletteResults.Add(new PaletteEntry(
                captured.DisplayName,
                captured.RelativePath,
                () =>
                {
                    IsPaletteOpen = false;
                    Open(captured);
                }));
        }

        if (ActiveTab?.Rendered is not { } rendered)
        {
            return;
        }

        foreach (HeadingRenderBlock heading in rendered.Outline
            .Where(h => query.Length == 0 || h.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(10))
        {
            HeadingRenderBlock captured = heading;

            PaletteResults.Add(new PaletteEntry(
                $"# {captured.Text}",
                "heading in this document",
                () =>
                {
                    IsPaletteOpen = false;
                    AnchorRequested?.Invoke(this, captured.Slug);
                }));
        }
    }

    /// <summary>
    /// The things the palette can do rather than open. Kept short on purpose: a palette
    /// that lists every command in the app is a menu with worse discoverability.
    /// </summary>
    private IEnumerable<PaletteEntry> Actions()
    {
        // Vault-scoped commands are hidden when there is no vault: a palette offering to
        // edit a page that is not open is a list of things that will not happen.
        if (HasVault)
        {
            yield return new PaletteEntry("Graph view", "Ctrl+G", () =>
            {
                IsPaletteOpen = false;
                ToggleGraph();
            });

            yield return new PaletteEntry("Edit this page", "Ctrl+E", () =>
            {
                IsPaletteOpen = false;
                ToggleEdit();
            });

            yield return new PaletteEntry("Find in this page", "Ctrl+F", () =>
            {
                IsPaletteOpen = false;
                FindRequested?.Invoke(this, EventArgs.Empty);
            });

            yield return new PaletteEntry(
                "Normalize links in the vault",
                "rewrites every link to its canonical target",
                () =>
                {
                    IsPaletteOpen = false;
                    IsLeftPanelVisible = true;
                    ProposeNormalizeVault();
                });
        }

        // Vault-scoped commands stay above; this is the one that is useful precisely when
        // the vault in front of you is the wrong one.
        foreach (RecentVault recent in RecentVaults.Where(r => r.Exists).Take(3))
        {
            RecentVault captured = recent;

            yield return new PaletteEntry($"Open {captured.Name}", captured.Path, () =>
            {
                IsPaletteOpen = false;
                _ = OpenRecentAsync(captured);
            });
        }

        if (UpdateService is { IsInstalled: true })
        {
            yield return new PaletteEntry("Check for updates", "asks the release feed", () =>
            {
                IsPaletteOpen = false;
                _ = CheckForUpdatesAsync();
            });
        }
    }

    private void NavigateWithoutHistory(VaultDocument target)
    {
        _navigating = true;

        try
        {
            Open(target);
        }
        finally
        {
            _navigating = false;
            RaiseHistoryChanged();
        }
    }

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>
    /// Picks the page to open a vault on: whatever the navigation names first, else the
    /// most index-like file, else anything at all.
    /// </summary>
    private VaultDocument? FindStartPage()
    {
        IEnumerable<VaultDocument> markdown = _vault!.Documents.Where(d => d.IsMarkdown);

        // An index page is the front door, and it is usually also the page the navigation
        // was read from — opening the first thing that navigation *lists* would skip past
        // the overview the author wrote.
        VaultDocument? index =
            markdown.FirstOrDefault(d => d.Stem.Equals("index", StringComparison.OrdinalIgnoreCase))
            ?? markdown.FirstOrDefault(d => d.Stem.Equals("README", StringComparison.OrdinalIgnoreCase));

        if (index is not null)
        {
            return index;
        }

        NavigationNode? first = Navigation.FirstOrDefault(n => n.Document is not null)
            ?? Navigation.SelectMany(n => n.Children).FirstOrDefault(n => n.Document is not null);

        return first?.Document ?? markdown.FirstOrDefault();
    }
}
