using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Detangle.Core.Graph;
using Detangle.Core.Vault;
using Detangle.Rendering.Controls;
using Detangle.Rendering.Model;

namespace Detangle.App;

/// <summary>
/// The three-pane reader: navigation on the left, tabs and the document in the middle,
/// outline and backlinks on the right (plan.md section 6.1).
/// <para>
/// Rendering happens in code rather than through a data template because the output is a
/// control tree built per document, and because link activation has to reach the shell
/// with the resolution attached — that provenance is the product.
/// </para>
/// </summary>
public partial class ShellView : UserControl
{
    /// <summary>Where the search tab sits in the left rail, for handing it a query.</summary>
    private const int SearchTabIndex = 1;

    /// <summary>Where the Link Doctor sits, for the ledger's page filter.</summary>
    private const int DoctorTabIndex = 2;

    /// <summary>Where the tag tab sits, for a chip in the page that selected a tag.</summary>
    private const int TagTabIndex = 3;

    private DocumentRenderer _renderer;
    private GraphCanvas _graph;
    private string? _renderedPath;

    /// <summary>Creates the shell.</summary>
    public ShellView()
    {
        InitializeComponent();

        _renderer = CreateRenderer(isDark: ActualThemeVariant == ThemeVariant.Dark);

        OpenButton.Click += async (_, _) => await Guarded(ChooseVault).ConfigureAwait(true);

        // Typing a path and pressing Enter still opens it. The button is for people who do
        // not know the path by heart, which is most people opening a folder.
        VaultPathBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                _ = Guarded(() => ViewModel?.OpenVaultAsync(VaultPathBox.Text ?? string.Empty)
                    ?? Task.CompletedTask);

                args.Handled = true;
            }
        };
        NavigationTree.SelectionChanged += OnNavigationSelectionChanged;
        TagTree.SelectionChanged += OnTagSelectionChanged;
        TagDocumentList.SelectionChanged += OnTagDocumentSelectionChanged;

        TagSearchButton.Click += (_, _) =>
        {
            ViewModel?.SearchSelectedTag();
            LeftRail.SelectedIndex = SearchTabIndex;
        };
        OutlineList.SelectionChanged += OnOutlineSelectionChanged;
        BacklinkList.SelectionChanged += OnBacklinkSelectionChanged;
        MentionList.SelectionChanged += OnMentionSelectionChanged;
        ListKeyboard.Wire(PaletteBox, PaletteList, CommitPalette);
        ListKeyboard.Wire(SearchBox, SearchList, CommitSearch);
        FindingTree.SelectionChanged += OnFindingSelectionChanged;
        FixAllButton.Click += OnFixAllClick;
        ApplyFixButton.Click += OnApplyFixClick;
        IgnoreFindingButton.Click += OnIgnoreFindingClick;
        ShowIgnoredButton.Click += (_, _) => ViewModel?.ShowIgnored();
        MarkBaselineButton.Click += (_, _) => ViewModel?.MarkBaseline();
        ConfirmWriteButton.Click += OnConfirmWriteClick;
        CancelWriteButton.Click += (_, _) => ViewModel?.CancelPendingWrite();

        CopyStatusLogButton.Click += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                CopyToClipboard(viewModel.StatusLogText);
            }
        };
        ClearFindingFilterButton.Click += (_, _) => ViewModel?.FilterFindings(null);

        // The strip has always known how this page's links resolved; clicking a band asks
        // the Doctor about exactly those.
        Ledger.SegmentClicked += (_, confidence) =>
        {
            if (ViewModel?.ActiveTab is { } tab)
            {
                ViewModel.IsLeftPanelVisible = true;
                LeftRail.SelectedIndex = DoctorTabIndex;
                ViewModel.FilterFindings(tab.Document.RelativePath, confidence);
            }
        };
        _graph = CreateGraphCanvas(isDark: ActualThemeVariant == ThemeVariant.Dark);
        GraphHost.Children.Add(_graph);
        GraphFitButton.Click += (_, _) => _graph.FitToView();

        WireMenus();
        WireEditing();
        WireFind();
        WireDragAndDrop();

        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Listens for shortcuts at the window rather than at this control.
    /// <para>
    /// Key events go to whatever holds focus and bubble up from there, so a handler on this
    /// control only ever hears them while focus is inside it. Nothing in the reading pane
    /// takes focus — a rendered document is text blocks — so on a freshly opened window,
    /// and after clicking on the page itself, every shortcut in the application was silently
    /// doing nothing. Attaching at the top level catches them wherever focus is, or is not.
    /// </para>
    /// <para>
    /// Bubbling rather than tunnelling on purpose: a focused control gets first refusal, so
    /// Ctrl+A in the path box still selects text instead of being taken by the shell.
    /// </para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);

        base.OnDetachedFromVisualTree(e);
    }

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    /// <summary>
    /// Runs a click handler so that a failure is reported rather than lost.
    /// <para>
    /// An exception thrown out of an async event handler goes nowhere: the task is never
    /// awaited by anyone, so the application simply appears to ignore the click. That is
    /// exactly what opening a folder in the browser did - the reader granted permission,
    /// something threw, and there was neither a message nor a line in the console.
    /// </para>
    /// </summary>
    private async Task Guarded(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            string message = $"{ex.GetType().Name}: {ex.Message}";

            if (ViewModel is { } viewModel)
            {
                viewModel.Status = message;

                // The bar gets one line and trims it; the panel gets the whole thing,
                // stack trace included, because that is the part worth pasting into an
                // issue and the part the bar has never been able to show.
                viewModel.LogDetail(ex.ToString());
            }

            // The console is the only diagnostic surface the browser build has, and this
            // is the class of fault that leaves no other trace.
            Console.WriteLine($"detangle: {message}");
        }
    }

    /// <summary>
    /// Asks for a folder and opens it.
    /// <para>
    /// The button used to hand whatever was in the path box straight to the scanner, which
    /// meant the first thing a new user could do — press it with the box empty — threw out
    /// of <c>Path.GetFullPath</c> and took the window with it. It now asks, starting from
    /// the vault already open so opening a sibling folder is one step.
    /// </para>
    /// <para>
    /// Where there is no picker to ask with, the typed path is still honoured rather than
    /// leaving the button dead: the browser build has no local folders to offer, and says
    /// so instead of appearing to do nothing.
    /// </para>
    /// </summary>
    private async Task ChooseVault()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (Storage is not { CanPickFolder: true } storage)
        {
            if (VaultPathBox.Text is { Length: > 0 } typed)
            {
                await viewModel.OpenVaultAsync(typed).ConfigureAwait(true);

                return;
            }

            viewModel.Status = "This build cannot browse for folders. Type a path and press Enter.";

            return;
        }

        IStorageFolder? start = null;

        if (viewModel.VaultPath is { Length: > 0 } current)
        {
            try
            {
                start = await storage.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(current))).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // A remembered path that no longer resolves is not worth reporting: the
                // picker simply opens wherever it would have opened anyway.
                start = null;
            }
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open a vault",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            }).ConfigureAwait(true);

        if (folders is not [{ } folder])
        {
            return;
        }

        if (folder.TryGetLocalPath() is { Length: > 0 } path)
        {
            await viewModel.OpenVaultAsync(path).ConfigureAwait(true);

            return;
        }

        await ImportFolder(viewModel, folder).ConfigureAwait(true);
    }

    /// <summary>
    /// Copies a picked folder into memory and opens that, for platforms whose folders have
    /// no path. See <see cref="StorageVaultImporter"/> for why that is necessary and what
    /// it costs.
    /// </summary>
    private static async Task ImportFolder(ShellViewModel viewModel, IStorageFolder folder)
    {
        string name = string.Concat((folder.Name.Length == 0 ? "vault" : folder.Name)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

        string root = Path.Combine(Path.GetTempPath(), "detangle-vaults", name);

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.Status = $"That folder could not be opened: {ex.Message}";

            return;
        }

        viewModel.Status = $"Reading {folder.Name}…";

        StorageVaultImporter.Result result;

        try
        {
            result = await StorageVaultImporter.ImportAsync(() => Entries(folder), root).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.Status = $"That folder could not be read: {ex.Message}";

            return;
        }

        if (result.Files == 0)
        {
            viewModel.Status = result.Failure is { Length: > 0 } failure
                ? $"{folder.Name} could not be read: {failure}"
                : $"{folder.Name} holds nothing this reader can open.";

            return;
        }

        viewModel.OpenVault(root, isDetachedCopy: true);

        viewModel.Status =
            $"Opened a copy of {folder.Name} — {result.Files} files, held in this tab only."
            + " Nothing was uploaded, and edits cannot be written back."
            + (result.Truncated ? $" Stopped at {StorageVaultImporter.MaxFiles} files." : string.Empty);
    }

    /// <summary>
    /// Presents an Avalonia folder as entries the importer understands. The framework's
    /// storage interfaces cannot be implemented outside it, so this is the seam: thin
    /// enough to read in one go, with every decision on the other side of it.
    /// </summary>
    private static async IAsyncEnumerable<StorageVaultImporter.Entry> Entries(IStorageFolder folder)
    {
        await foreach (IStorageItem item in folder.GetItemsAsync().ConfigureAwait(true))
        {
            yield return item switch
            {
                IStorageFile file => new StorageVaultImporter.Entry(file.Name, file.OpenReadAsync, null),
                IStorageFolder child => new StorageVaultImporter.Entry(child.Name, null, () => Entries(child)),
                _ => new StorageVaultImporter.Entry(item.Name, null, null),
            };
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.AnchorRequested += (_, anchor) => ScrollToAnchor(anchor);
        viewModel.TagSelected += OnTagSelectedElsewhere;
        viewModel.FindRequested += (_, _) => OpenFind();

        // The shell can arrive already set to a palette — the WASM demo starts dark —
        // and that happens before there is a view to hear the change.
        ApplyTheme(viewModel.IsDarkTheme);

        // And the graph canvas is built with the palette that was current when this view
        // was constructed, which is the default one. Arriving already dark left it drawing
        // a white sheet inside a dark window, because only a *change* of palette replaced
        // it. Rebuilding it here costs nothing and is correct either way.
        ReplaceGraphCanvas();

        Render();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.ActiveTab):
                Render();
                break;

            case nameof(ShellViewModel.IsDarkTheme):
                ApplyTheme(ViewModel?.IsDarkTheme ?? false);
                _renderedPath = null;
                Render();
                ReplaceGraphCanvas();
                break;

            case nameof(ShellViewModel.GraphModel):
                _graph.Show(ViewModel?.GraphModel ?? GraphModel.Empty);
                break;

            case nameof(ShellViewModel.IsEditing):
            case nameof(ShellViewModel.EditorText):
                SyncEditor();
                break;

            case nameof(ShellViewModel.IsPaletteOpen) when ViewModel?.IsPaletteOpen == true:
                PaletteBox.Focus();
                break;
        }
    }

    /// <summary>
    /// Builds the graph canvas. It is one control rather than one per node: the picture
    /// is drawn, not laid out (plan.md section 6.4).
    /// </summary>
    private GraphCanvas CreateGraphCanvas(bool isDark)
    {
        var canvas = new GraphCanvas(isDark ? DocumentTheme.Dark : DocumentTheme.Light);

        canvas.NodeActivated += (_, args) => ViewModel?.OpenNode(args.Node);
        canvas.NodeHovered += OnGraphNodeHovered;

        return canvas;
    }

    /// <summary>Rebuilds the canvas in the other palette, keeping the current graph.</summary>
    private void ReplaceGraphCanvas()
    {
        GraphCanvas replacement = CreateGraphCanvas(ViewModel?.IsDarkTheme ?? false);

        GraphHost.Children.Clear();
        GraphHost.Children.Add(replacement);
        _graph = replacement;

        if (ViewModel?.GraphModel is { } model)
        {
            _graph.Show(model);
        }
    }

    /// <summary>
    /// Shows what the pointer is over. It is the only place a reader learns that a node is
    /// a page nothing links to, or one that does not exist — and it belongs to the graph
    /// pane, not to the status bar, which has a message of its own to keep showing.
    /// </summary>
    private void OnGraphNodeHovered(object? sender, GraphNode? node)
    {
        if (ViewModel is not { IsGraphVisible: true } viewModel)
        {
            return;
        }

        viewModel.GraphHover = node switch
        {
            null => string.Empty,
            { Kind: GraphNodeKind.MissingTarget } => $"{node.Label} · no such file · {node.InboundCount} links here",
            { Kind: GraphNodeKind.Cluster } => $"{node.Label} · {node.Weight:N0} pages · click to open the folder",
            _ => $"{node.Id} · {node.InboundCount} in · {node.OutboundCount} out"
                + (node.IsOrphan ? " · orphan" : string.Empty),
        };
    }

    /// <summary>Switches the control palette and the document renderer together.</summary>
    private void ApplyTheme(bool isDark)
    {
        _renderer = CreateRenderer(isDark);
        ThemeScope.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <summary>
    /// The body face, addressed by resource URI. A family name finds nothing in the
    /// WebAssembly build, which has no system fonts to search.
    /// </summary>
    private static readonly Avalonia.Media.FontFamily BodyFont =
        new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    private DocumentRenderer CreateRenderer(bool isDark)
    {
        DocumentTheme palette = (isDark ? DocumentTheme.Dark : DocumentTheme.Light)
            with
        { FontFamily = BodyFont };

        var renderer = new DocumentRenderer(palette);

        renderer.LinkActivated += OnLinkActivated;
        renderer.TagActivated += (_, tag) => ViewModel?.SelectTagNamed(tag);
        renderer.CopyRequested = CopyToClipboard;
        renderer.PreviewFactory = BuildPreview;
        renderer.ChoiceMade = (resolution, chosen) => ViewModel?.Settle(resolution, chosen);

        return renderer;
    }

    /// <summary>
    /// Renders the first screenful of a link's target as its hover preview. It is capped
    /// rather than complete: the point is to answer "is this the page I mean?" without
    /// leaving the one being read.
    /// </summary>
    private Control? BuildPreview(Detangle.Core.Linking.LinkResolution resolution)
    {
        if (resolution.Target is not { IsMarkdown: true } target
            || ViewModel?.Preview(target) is not { } rendered)
        {
            return null;
        }

        var trimmed = new RenderDocument(
            rendered.Document,
            [.. rendered.Blocks.Take(6)],
            rendered.Resolutions,
            rendered.Diagnostics);

        return new ScrollViewer
        {
            MaxHeight = 320,
            Content = _renderer.Render(trimmed),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private void Render()
    {
        if (ViewModel is not { ActiveTab: { Rendered: { } rendered } tab })
        {
            DocumentHost.Content = null;
            Ledger.Resolutions = null;

            // The host is empty now, so nothing is on screen to skip re-rendering. Leaving
            // the last path here meant closing every tab and reopening one of those same
            // documents matched the guard below, returned early, and left the reader
            // looking at a blank pane with a tab open above it.
            _renderedPath = null;

            return;
        }

        // The ledger reports on the page in front of the reader, so it is set even when
        // the document itself is already on screen and will not be rebuilt.
        Ledger.Resolutions = rendered.Resolutions;

        // Re-rendering the document already on screen would throw away the reader's
        // scroll position for nothing.
        if (string.Equals(_renderedPath, tab.Document.RelativePath, StringComparison.Ordinal))
        {
            return;
        }

        RememberScroll();

        DocumentHost.Content = _renderer.Render(rendered);
        _renderedPath = tab.Document.RelativePath;

        DocumentScroller.Offset = new Vector(0, ViewModel.PositionOf(tab.Document.RelativePath));

        if (tab.PendingAnchor is { Length: > 0 } anchor)
        {
            tab.PendingAnchor = null;
            ScrollToAnchor(anchor);
        }
    }

    private void RememberScroll()
    {
        if (_renderedPath is { Length: > 0 } path && ViewModel is { } viewModel)
        {
            viewModel.RememberPosition(path, (int)DocumentScroller.Offset.Y);
        }
    }

    /// <summary>
    /// Scrolls to a heading by its slug. Headings carry their slug on the control's Tag,
    /// which is what makes "#some-heading" in a link land in the right place.
    /// </summary>
    private void ScrollToAnchor(string anchor)
    {
        if (DocumentHost.Content is not Control root)
        {
            return;
        }

        Control? heading = root.GetLogicalDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Tag is string slug
                && string.Equals(slug, anchor, StringComparison.OrdinalIgnoreCase));

        if (heading is null || DocumentHost.Content is not Control content)
        {
            return;
        }

        // The offset is measured against the rendered document rather than the viewport,
        // so it stays correct no matter how far the reader has already scrolled.
        if (heading.TranslatePoint(default, content) is { } point)
        {
            DocumentScroller.Offset = new Vector(0, Math.Max(0, point.Y - 12));
        }
    }

    private void OnLinkActivated(object? sender, LinkActivatedEventArgs e)
    {
        if (e.ExternalUrl is { Length: > 0 } url)
        {
            OpenExternal(url);
            return;
        }

        RememberScroll();

        if (e.Resolution.Target is { IsMarkdown: true })
        {
            ViewModel?.Follow(e.Resolution);
            return;
        }

        if (e.Resolution.Target is { AbsolutePath: { Length: > 0 } attachment })
        {
            // An attachment is not a page; the platform knows what to do with it.
            OpenExternal(attachment);
        }
    }

    private void OnNavigationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NavigationTree.SelectedItem is NavigationNode { Document: { } document })
        {
            ViewModel?.Open(document);
        }
    }

    private void OnTagSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Listing what the tag names, rather than opening one of them: the tree's own
        // count is the promise, and picking one page out of fourteen is not keeping it.
        ViewModel?.SelectTag(TagTree.SelectedItem as TagNode);
    }

    /// <summary>
    /// Scrolls to the link a finding is about and pulses it once.
    /// <para>
    /// Nothing is invented when the line no longer matches: a finding from before a
    /// generation run points at a line that has moved, and quietly landing at the top of
    /// the page would be exactly the sort of silent guess this product exists to remove.
    /// It says so instead.
    /// </para>
    /// </summary>
    /// <param name="line">The 1-based line the finding names.</param>
    /// <returns>True when the link was found and shown.</returns>
    private bool RevealLink(int line)
    {
        if (line <= 0 || DocumentHost.Content is not Control content)
        {
            return false;
        }

        Control? link = content.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => c.Tag is LinkOrigin origin && origin.Line == line)
            .OrderBy(c => (c.Tag as LinkOrigin)!.Column)
            .FirstOrDefault();

        if (link is null || link.TranslatePoint(default, content) is not { } point)
        {
            return false;
        }

        // A third of the way down rather than at the very top: a link with no context
        // above it reads as the start of the page rather than as a place in it.
        DocumentScroller.Offset = new Vector(
            0, Math.Max(0, point.Y - (DocumentScroller.Bounds.Height / 3)));

        Pulse(link);

        return true;
    }

    /// <summary>
    /// Marks a control once and lets the mark fade. No new colour: the selection brush is
    /// what "this is the thing you asked for" already means everywhere else in the shell.
    /// </summary>
    private void Pulse(Control control)
    {
        if (control is not Button button || Resource("Selection") is not { } brush)
        {
            return;
        }

        IBrush? original = button.Background;

        button.Background = brush;

        DispatcherTimer.RunOnce(
            () => button.Background = original,
            TimeSpan.FromMilliseconds(900));
    }

    private void OnRecentVaultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RecentVault vault } && ViewModel is { } viewModel)
        {
            _ = Guarded(() => viewModel.OpenRecentAsync(vault));
        }
    }

    /// <summary>
    /// Lets a folder dragged onto the window be opened as a vault.
    /// <para>
    /// Gated on the head rather than tried and caught: a browser tab has no folder on a
    /// disk to be dropped, so it never offers the highlight in the first place.
    /// </para>
    /// </summary>
    private void WireDragAndDrop()
    {
        DragDrop.SetAllowDrop(this, true);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropTarget.IsVisible = false);
        AddHandler(DragDrop.DropEvent, OnDrop);

        void OnDragOver(object? sender, DragEventArgs args)
        {
            bool accepts = ViewModel?.Capabilities.CanDropFolders == true && PathIn(args) is not null;

            args.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
            DropTarget.IsVisible = accepts;
            args.Handled = true;
        }

        void OnDrop(object? sender, DragEventArgs args)
        {
            DropTarget.IsVisible = false;

            if (ViewModel is not { } viewModel || PathIn(args) is not { } path)
            {
                return;
            }

            args.Handled = true;

            // A dropped page opens its folder and then that page, which is what somebody
            // dragging one file out of a wiki meant by it.
            string? page = File.Exists(path) ? Path.GetFileName(path) : null;
            string folder = page is null ? path : Path.GetDirectoryName(path) ?? path;

            _ = Guarded(async () =>
            {
                await viewModel.OpenVaultAsync(folder).ConfigureAwait(true);

                if (page is not null)
                {
                    viewModel.OpenPath(page);
                }
            });
        }

        static string? PathIn(DragEventArgs args)
        {
            if (args.DataTransfer?.TryGetValue(DataFormat.File) is not { } item
                || item.TryGetLocalPath() is not { Length: > 0 } path)
            {
                return null;
            }

            // A folder, or a markdown file inside one. Anything else is a drop this
            // application has no answer for, and refusing it in the highlight is kinder
            // than accepting it and reporting a failure afterwards.
            return Directory.Exists(path)
                || (File.Exists(path) && Detangle.Core.Linking.LinkNormalizer.HasMarkdownExtension(path))
                    ? path
                    : null;
        }
    }

    private void OnRevokeChoiceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Detangle.Core.Linking.SettledChoice choice })
        {
            ViewModel?.Revoke(choice);
        }
    }

    private void OnTagDocumentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TagDocumentList.SelectedItem is VaultDocument document)
        {
            ViewModel?.Open(document);
        }
    }

    private void OnTagSelectedElsewhere(object? sender, TagNode tag)
    {
        // A chip in the page selected the tag; the tree follows so the rail is not showing
        // a list whose heading names a tag nothing in the tree appears to have picked.
        LeftRail.SelectedIndex = TagTabIndex;

        var ancestors = new List<TagNode>();

        if (!Path(ViewModel?.Tags ?? []))
        {
            return;
        }

        // Root first: a nested node has no container until its parent is expanded and the
        // tree has laid out, so expanding from the bottom up would be reaching for
        // containers that do not exist yet.
        foreach (TagNode ancestor in ancestors)
        {
            if (TagTree.TreeContainerFromItem(ancestor) is not TreeViewItem container)
            {
                break;
            }

            container.IsExpanded = true;
            TagTree.UpdateLayout();
        }

        TagTree.SelectedItem = tag;

        // Only the ancestors of the selected tag: expanding the whole tree to reveal one
        // node would leave the reader scrolling a rail they did not open.
        bool Path(IEnumerable<TagNode> nodes)
        {
            foreach (TagNode node in nodes)
            {
                if (ReferenceEquals(node, tag))
                {
                    return true;
                }

                ancestors.Add(node);

                if (Path(node.Children))
                {
                    return true;
                }

                ancestors.RemoveAt(ancestors.Count - 1);
            }

            return false;
        }
    }

    private void OnOutlineSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is HeadingRenderBlock heading)
        {
            ScrollToAnchor(heading.Slug);
        }
    }

    private void OnBacklinkSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BacklinkList.SelectedItem is Backlink backlink)
        {
            ViewModel?.Open(backlink.Source);
        }
    }

    private void OnMentionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MentionList.SelectedItem is UnlinkedMention mention)
        {
            ViewModel?.Open(mention.Source);
        }
    }

    private void CommitPalette()
    {
        if (PaletteList.SelectedItem is PaletteEntry entry)
        {
            PaletteList.SelectedItem = null;
            entry.Invoke();
        }
    }

    private void CommitSearch()
    {
        if (SearchList.SelectedItem is Detangle.Core.Search.SearchHit hit)
        {
            // A hit knows the heading it was found under, so opening it lands on the
            // section rather than at the top of a long page.
            ViewModel?.Open(hit.Document);

            if (hit.Heading is { Length: > 0 } heading)
            {
                ScrollToAnchor(Detangle.Core.Linking.HeadingSlugger.SlugCore(heading));
            }
        }
    }

    /// <summary>
    /// Shows what the selected finding is and what acting on it would do (plan.md section
    /// 15.3). Selecting a group heading rather than a finding clears the card, because a
    /// group is a count rather than something to act on.
    /// </summary>
    private void OnFindingSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedFinding is not { } finding || ViewModel is not { } viewModel)
        {
            FindingActions.IsVisible = false;
            return;
        }

        // Suggestions for a broken link are worked out when one is opened, not for every
        // finding in the vault; the search behind them is not cheap. What comes back is
        // the finding to draw: replacing it in the collection clears the tree's selection,
        // so re-reading the selection here would find nothing.
        finding = viewModel.Suggest(finding);

        viewModel.Open(finding.Document);

        FindingActions.IsVisible = true;
        FindingDetail.Text = finding.SuggestedAnchor is { Length: > 0 } heading
            ? $"{finding.Message} Probably \"{heading}\"."
            : finding.Message;

        (string Before, string After)? preview = viewModel.PreviewFix(finding);

        FindingDiff.IsVisible = preview is not null;
        ApplyFixButton.IsVisible = preview is not null;
        CreateNoteButton.IsVisible = finding.Kind == Detangle.Core.Diagnostics.FindingKind.BrokenLink;

        if (preview is { } lines)
        {
            FindingBefore.Text = "- " + lines.Before.Trim();
            FindingAfter.Text = "+ " + lines.After.Trim();
        }

        // After the page is open and laid out, not before: the control the finding points
        // at does not exist until the render has happened.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (finding.Line > 0 && !RevealLink(finding.Line))
                {
                    // The file changed since the scan, or this finding came from a stale
                    // baseline. Saying so beats landing at the top of the page and letting
                    // the reader assume the link is up there somewhere.
                    viewModel.Status =
                        $"{finding.Document.RelativePath} no longer has that link on line {finding.Line}; "
                        + "rescan to refresh the findings.";
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>The finding the tree has selected, or null when a group heading is.</summary>
    private Detangle.Core.Diagnostics.Finding? SelectedFinding =>
        FindingTree.SelectedItem as Detangle.Core.Diagnostics.Finding;

    private void OnApplyFixClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedFinding is not { } finding || ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Status = viewModel.ApplyFix(finding)
            ? $"Rewrote one link in {finding.Document.RelativePath}."
            : "That link is no longer where the finding says it is; the file was not touched.";

        FindingActions.IsVisible = false;
    }

    private void OnIgnoreFindingClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedFinding is { } finding)
        {
            ViewModel?.Ignore(finding);
            FindingActions.IsVisible = false;
        }
    }

    private void OnFixAllClick(object? sender, RoutedEventArgs e) => ViewModel?.ProposeFixAllSafe();

    private void OnConfirmWriteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { PendingWrite: { } pending } viewModel)
        {
            return;
        }

        string title = pending.Title;
        int written = viewModel.ConfirmPendingWrite();

        // The writers set their own status while they run; this replaces it with the one
        // that names what the reader actually agreed to.
        viewModel.Status = written == 0
            ? $"{title} changed nothing: the files had moved on since the list was made."
            : $"{title}: rewrote {written} file{(written == 1 ? string.Empty : "s")}.";
    }

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DocumentTab tab })
        {
            ViewModel?.Close(tab);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // A control that already acted on the key keeps it: this runs after the focused
        // element has had its turn, which is the point of listening where we do.
        if (e.Handled)
        {
            return;
        }

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        switch (e.Key)
        {
            case Key.E when control:
                ViewModel?.ToggleEditCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S when control && ViewModel?.IsEditing == true:
                SaveWithConflictCheck();
                e.Handled = true;
                break;

            case Key.G when control:
                ViewModel?.ToggleGraphCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.B when control && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                ViewModel?.ToggleRightPanelCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.B when control:
                ViewModel?.ToggleLeftPanelCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F when control:
                OpenFind();
                e.Handled = true;
                break;

            case Key.K when control:
            case Key.P when control && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                ViewModel?.TogglePaletteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape when ViewModel?.IsPaletteOpen == true:
                ViewModel.TogglePaletteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape when FindBar.IsVisible:
                CloseFind();
                e.Handled = true;
                break;

            // Escape is the way out of a confirm everywhere else in computing, and this
            // one is guarding the only irreversible thing the application does.
            case Key.Escape when ViewModel?.PendingWrite is not null:
                ViewModel.CancelPendingWrite();
                e.Handled = true;
                break;

            case Key.Escape when ViewModel?.IsStatusLogVisible == true:
                ViewModel.ToggleStatusLog();
                e.Handled = true;
                break;

            case Key.Left when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                ViewModel?.GoBackCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Right when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                ViewModel?.GoForwardCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.W when control && ViewModel?.ActiveTab is { } active:
                ViewModel.Close(active);
                e.Handled = true;
                break;
        }
    }

    private void OpenExternal(string target)
    {
        // Asked rather than tried: a head that cannot hand a target to a platform says so
        // once, at startup, and the commands that would need it are never offered. What is
        // left here is a target the platform accepted and then had no handler for.
        if (ViewModel?.Capabilities.CanOpenExternalLinks != true)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or PlatformNotSupportedException)
        {
            // A desktop with no handler for this target is not worth interrupting reading.
        }
    }

    /// <summary>The shell's view model, for the menu factory.</summary>
    internal ShellViewModel? ViewModelForMenus => ViewModel;

    /// <summary>
    /// Attaches the right-click menus. Every list of pages gets the same six commands, so
    /// "copy this as a wikilink" means the same thing in the file tree, in search results,
    /// in backlinks and in the tag list.
    /// </summary>
    private void WireMenus()
    {
        ItemMenus.Wire(NavigationTree, this, () => NavigationTree.SelectedItem);
        ItemMenus.Wire(SearchList, this, () => SearchList.SelectedItem);
        ItemMenus.Wire(BacklinkList, this, () => BacklinkList.SelectedItem);
        ItemMenus.Wire(MentionList, this, () => MentionList.SelectedItem);
        ItemMenus.Wire(TagDocumentList, this, () => TagDocumentList.SelectedItem);

        ItemMenus.Wire(
            TabStrip,
            this,
            () => TabStrip.SelectedItem,
            item => ViewModel is { } viewModel
                ? ItemMenus.TabItems(item as DocumentTab, viewModel, this)
                : []);

        ItemMenus.Wire(
            FindingTree,
            this,
            () => FindingTree.SelectedItem as Detangle.Core.Diagnostics.Finding,
            item => ViewModel is { } viewModel
                ? ItemMenus.FindingItems(item as Detangle.Core.Diagnostics.Finding, viewModel, this)
                : []);
    }

    /// <summary>Puts text on the clipboard, and says so where the reader is looking.</summary>
    internal void CopyToClipboard(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        // Fire and forget with the failure reported: nothing downstream waits on a copy,
        // and a clipboard the platform refused is worth one line rather than a dialog.
        _ = CopyAsync();

        async Task CopyAsync()
        {
            try
            {
                await clipboard.SetValueAsync(DataFormat.Text, text).ConfigureAwait(true);

                if (ViewModel is { } viewModel)
                {
                    viewModel.Status = $"Copied {text}";
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                if (ViewModel is { } viewModel)
                {
                    viewModel.Status = $"The clipboard would not take that: {ex.Message}";
                }
            }
        }
    }

    /// <summary>Shows a file in the platform's file manager, with the file selected.</summary>
    internal void Reveal(string absolutePath)
    {
        if (ViewModel?.Capabilities.CanRevealInFileManager != true)
        {
            return;
        }

        try
        {
            // Selecting the file rather than opening its folder: the point of revealing a
            // path is the file, and a folder of two hundred pages does not answer it.
            ProcessStartInfo start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{absolutePath}\"")
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open", $"-R \"{absolutePath}\"")
                    : new ProcessStartInfo("xdg-open", $"\"{Path.GetDirectoryName(absolutePath)}\"");

            Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or PlatformNotSupportedException or IOException)
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.Status = $"That folder could not be shown: {ex.Message}";
            }
        }
    }
}
