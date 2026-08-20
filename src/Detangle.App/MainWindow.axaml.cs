using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
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
public partial class MainWindow : Window
{
    private DocumentRenderer _renderer;
    private string? _renderedPath;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _renderer = CreateRenderer(isDark: ActualThemeVariant == ThemeVariant.Dark);

        OpenButton.Click += (_, _) => ViewModel?.OpenVault(VaultPathBox.Text ?? string.Empty);
        NavigationTree.SelectionChanged += OnNavigationSelectionChanged;
        TagTree.SelectionChanged += OnTagSelectionChanged;
        OutlineList.SelectionChanged += OnOutlineSelectionChanged;
        BacklinkList.SelectionChanged += OnBacklinkSelectionChanged;
        MentionList.SelectionChanged += OnMentionSelectionChanged;
        PaletteList.SelectionChanged += OnPaletteSelectionChanged;
        SearchList.SelectionChanged += OnSearchSelectionChanged;
        FindingList.SelectionChanged += OnFindingSelectionChanged;
        FixAllButton.Click += OnFixAllClick;

        DataContextChanged += OnDataContextChanged;
        KeyDown += OnWindowKeyDown;
    }

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.AnchorRequested += (_, anchor) => ScrollToAnchor(anchor);

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
                _renderer = CreateRenderer(ViewModel?.IsDarkTheme ?? false);
                RequestedThemeVariant = ViewModel?.IsDarkTheme == true ? ThemeVariant.Dark : ThemeVariant.Light;
                _renderedPath = null;
                Render();
                break;

            case nameof(ShellViewModel.IsPaletteOpen) when ViewModel?.IsPaletteOpen == true:
                PaletteBox.Focus();
                break;
        }
    }

    private DocumentRenderer CreateRenderer(bool isDark)
    {
        var renderer = new DocumentRenderer(isDark ? DocumentTheme.Dark : DocumentTheme.Light);

        renderer.LinkActivated += OnLinkActivated;
        renderer.PreviewFactory = BuildPreview;

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
            return;
        }

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
        if (TagTree.SelectedItem is TagNode { Documents.Count: > 0 } tag)
        {
            ViewModel?.Open(tag.Documents[0]);
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

    private void OnPaletteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is PaletteEntry entry)
        {
            PaletteList.SelectedItem = null;
            entry.Invoke();
        }
    }

    private void OnSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
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

    private void OnFindingSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FindingList.SelectedItem is Detangle.Core.Diagnostics.Finding finding)
        {
            ViewModel?.Open(finding.Document);
        }
    }

    private void OnFixAllClick(object? sender, RoutedEventArgs e)
    {
        int written = ViewModel?.FixAllSafe() ?? 0;

        if (ViewModel is { } viewModel)
        {
            viewModel.Status = written == 0
                ? "Nothing to fix: every link is already canonical."
                : $"Rewrote links in {written} file{(written == 1 ? string.Empty : "s")}.";
        }
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
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        switch (e.Key)
        {
            case Key.K when control:
            case Key.P when control && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                ViewModel?.TogglePaletteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape when ViewModel?.IsPaletteOpen == true:
                ViewModel.TogglePaletteCommand.Execute(null);
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

    private static void OpenExternal(string target)
    {
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
}
