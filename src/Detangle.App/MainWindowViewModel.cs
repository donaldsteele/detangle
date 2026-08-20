using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Detangle.Core.Vault;
using Detangle.Rendering;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;

namespace Detangle.App;

/// <summary>
/// The reader's state: a vault, its documents, and the one being read.
/// <para>
/// This is a phase 2 harness, not the shell. Phase 4 replaces it with the three-pane
/// layout, tabs, outline and backlinks; what it has to do today is prove that a real
/// vault opens and renders end to end.
/// </para>
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _vaultPath = string.Empty;

    [ObservableProperty]
    private VaultDocument? _selectedDocument;

    [ObservableProperty]
    private RenderDocument? _renderedDocument;

    [ObservableProperty]
    private string _status = "Open a vault to start reading.";

    private VaultSnapshot? _vault;
    private RenderModelBuilder? _builder;

    /// <summary>Creates the view model.</summary>
    /// <param name="diagramRenderer">
    /// The diagram backend. Defaults to Mermaider in process; the desktop head passes a
    /// WebView-backed one when the setting is on and the platform can host it.
    /// </param>
    /// <param name="theme">Which palette to render against.</param>
    public MainWindowViewModel(IDiagramRenderer? diagramRenderer = null, DiagramTheme theme = DiagramTheme.Light)
    {
        _diagramRenderer = diagramRenderer ?? new MermaiderDiagramRenderer();
        _diagramTheme = theme;
    }

    private readonly IDiagramRenderer _diagramRenderer;
    private readonly DiagramTheme _diagramTheme;

    /// <summary>The product name, shown in the window chrome.</summary>
    public string Title => "Detangle";

    /// <summary>The tagline, shown when no vault is open.</summary>
    public string Tagline => "Read the wiki the model actually wrote.";

    /// <summary>The informational version, without its build metadata.</summary>
    public string Version =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? "0.1.0";

    /// <summary>Markdown documents in the open vault, in path order.</summary>
    public ObservableCollection<VaultDocument> Documents { get; } = [];

    /// <summary>True once a vault has been scanned.</summary>
    public bool HasVault => _vault is not null;

    /// <summary>Scans a directory and selects the most index-like document in it.</summary>
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

        // Diagram SVG is cached per vault: the same fence rendered on every visit to a
        // page is the difference between instant navigation and a visible stall.
        var renderer = new CachingDiagramRenderer(
            _diagramRenderer, new FileDiagramCacheStore(_vault.RootPath));

        _builder = new RenderModelBuilder(
            _vault,
            options: new RenderOptions
            {
                DiagramRenderer = renderer,
                DiagramTheme = _diagramTheme,
            });
        VaultPath = _vault.RootPath;

        Documents.Clear();

        foreach (VaultDocument document in _vault.Documents
            .Where(d => d.IsMarkdown)
            .OrderBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            Documents.Add(document);
        }

        OnPropertyChanged(nameof(HasVault));

        SelectedDocument = Documents.FirstOrDefault(d => d.Stem.Equals("index", StringComparison.OrdinalIgnoreCase))
            ?? Documents.FirstOrDefault(d => d.Stem.Equals("README", StringComparison.OrdinalIgnoreCase))
            ?? Documents.FirstOrDefault();

        Status = $"{_vault.Profile.Flavor} · {Documents.Count} documents";
    }

    /// <summary>Navigates to a document by its vault-relative path.</summary>
    public void Navigate(string relativePath)
    {
        VaultDocument? target = Documents.FirstOrDefault(
            d => string.Equals(d.RelativePath, relativePath, StringComparison.Ordinal));

        if (target is not null)
        {
            SelectedDocument = target;
        }
    }

    partial void OnSelectedDocumentChanged(VaultDocument? value)
    {
        if (value is null || _builder is null)
        {
            RenderedDocument = null;
            return;
        }

        RenderDocument rendered = _builder.Build(value);
        RenderedDocument = rendered;

        int broken = rendered.BrokenLinks.Count();
        int ambiguous = rendered.AmbiguousLinks.Count();

        // The status line is the section 5.5 counter in miniature: how many links this
        // page has, and how many of them Detangle could not answer cleanly.
        Status = $"{value.RelativePath} · {rendered.Resolutions.Count} links · "
            + $"{broken} broken · {ambiguous} ambiguous";
    }
}
