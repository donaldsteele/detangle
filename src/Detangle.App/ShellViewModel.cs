using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Linking;
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

    [ObservableProperty]
    private bool _isGraphVisible;

    [ObservableProperty]
    private GraphModel _graphModel = GraphModel.Empty;

    [ObservableProperty]
    private string _graphSummary = string.Empty;

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

    /// <summary>Raised when the reader should scroll to an anchor in the active document.</summary>
    public event EventHandler<string>? AnchorRequested;

    /// <summary>The navigation tree for the left rail.</summary>
    public ObservableCollection<NavigationNode> Navigation { get; } = [];

    /// <summary>The tag hierarchy for the tag browser.</summary>
    public ObservableCollection<TagNode> Tags { get; } = [];

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

    /// <summary>Link Doctor findings over the whole vault.</summary>
    public ObservableCollection<Finding> Findings { get; } = [];

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

    /// <summary>Scans a vault and opens its most index-like page.</summary>
    public void OpenVault(string path)
    {
        try
        {
            _vault = VaultScanner.Scan(path);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
            return;
        }

        var renderer = new CachingDiagramRenderer(
            _diagramRenderer, new FileDiagramCacheStore(_vault.RootPath));

        _builder = new RenderModelBuilder(
            _vault,
            options: new RenderOptions
            {
                DiagramRenderer = renderer,
                DiagramTheme = IsDarkTheme ? DiagramTheme.Dark : DiagramTheme.Light,
            });

        _graph = LinkGraph.Build(_vault);
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

        RefreshFindings();

        NavigationTreeBuilder.Result navigation = NavigationTreeBuilder.Build(_vault, ReadContent);
        NavigationSourceName = navigation.Source.ToString();

        Navigation.Clear();

        foreach (NavigationNode node in navigation.Roots)
        {
            Navigation.Add(node);
        }

        Tags.Clear();

        foreach (TagNode tag in TagTree.Build(_vault))
        {
            Tags.Add(tag);
        }

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

        VaultDocument? first = FindStartPage();

        if (first is not null)
        {
            Open(first);
        }
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

    /// <summary>Switches between the light and dark palettes and re-renders every tab.</summary>
    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;

        if (_vault is null)
        {
            return;
        }

        // Diagrams are themed, so a theme switch has to rebuild them; the cache keys on
        // theme, so the previous palette's renders survive for the switch back.
        _builder = new RenderModelBuilder(
            _vault,
            options: new RenderOptions
            {
                DiagramRenderer = new CachingDiagramRenderer(
                    _diagramRenderer, new FileDiagramCacheStore(_vault.RootPath)),
                DiagramTheme = IsDarkTheme ? DiagramTheme.Dark : DiagramTheme.Light,
            });

        foreach (DocumentTab tab in Tabs)
        {
            tab.Rendered = _builder.Build(tab.Document);
        }
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
        _graph = LinkGraph.Build(_vault);
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

        foreach (Finding finding in LinkDoctor.Examine(_graph, ReadContent)
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Document.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            Findings.Add(finding);
        }
    }

    /// <summary>
    /// Fills in what a broken link probably meant, in place, the first time a reader
    /// opens that finding.
    /// </summary>
    public void Suggest(Finding finding)
    {
        if (finding.Kind != FindingKind.BrokenLink || finding.SuggestedRewrite is not null)
        {
            return;
        }

        int index = Findings.IndexOf(finding);

        if (index >= 0)
        {
            Findings[index] = LinkDoctor.SuggestFix(finding);
        }
    }

    /// <summary>
    /// Applies every safe fix — the links that resolved through steps 4 to 8 and have
    /// exactly one canonical form — and returns how many files were rewritten.
    /// </summary>
    public int FixAllSafe()
    {
        var byDocument = LinkDoctor.SafeToFix(Findings)
            .GroupBy(f => f.Document.RelativePath, StringComparer.Ordinal);

        int written = 0;

        foreach (IGrouping<string, Finding> group in byDocument)
        {
            VaultDocument document = group.First().Document;
            string? content = ReadContent(document);

            if (content is null)
            {
                continue;
            }

            // Later lines first: rewriting from the bottom up keeps every remaining
            // finding's line number valid.
            foreach (Finding finding in group.OrderByDescending(f => f.Line))
            {
                content = LinkDoctor.ApplyRewrite(content, finding) ?? content;
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

    /// <summary>Shows or hides the graph view.</summary>
    [RelayCommand]
    public void ToggleGraph()
    {
        IsGraphVisible = !IsGraphVisible;

        if (IsGraphVisible)
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

        if (_vault is null)
        {
            return;
        }

        string query = PaletteQuery.Trim();

        foreach (PaletteEntry action in Actions().Where(
            a => query.Length == 0 || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            PaletteResults.Add(action);
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

        yield return new PaletteEntry("Normalize links in the vault", "rewrites every link to its canonical target", () =>
        {
            IsPaletteOpen = false;
            NormalizeVault();
        });

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
