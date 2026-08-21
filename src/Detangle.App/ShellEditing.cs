using CommunityToolkit.Mvvm.Input;
using Detangle.Core.Diagnostics;
using Detangle.Core.Editing;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Detangle.Rendering.Export;
using Detangle.Rendering.Model;

namespace Detangle.App;

/// <summary>
/// The shell's editing and export half (plan.md sections 6.5 and 6.6).
/// <para>
/// Kept beside the reader rather than inside it: reading is the product, and everything
/// here is something the reader does <em>to</em> a vault. Every write in this file goes
/// through the same atomic path, and every one of them refuses a file that changed
/// underneath it.
/// </para>
/// </summary>
public sealed partial class ShellViewModel
{
    private EditSession? _session;

    /// <summary>True when the split editor is open on the active document.</summary>
    public bool IsEditing => _session is not null;

    /// <summary>The text in the editor.</summary>
    public string EditorText
    {
        get => _editorText;
        set
        {
            if (SetProperty(ref _editorText, value))
            {
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }
    }

    private string _editorText = string.Empty;

    /// <summary>True when the editor holds something the file on disk does not.</summary>
    public bool HasUnsavedChanges =>
        _session is not null && !string.Equals(_session.Content, EditorText, StringComparison.Ordinal);

    /// <summary>Opens or closes the split editor on the active document.</summary>
    [RelayCommand]
    public void ToggleEdit()
    {
        if (_session is not null)
        {
            CloseEditor();
            return;
        }

        if (ActiveTab?.Document is not { } document)
        {
            return;
        }

        _session = DocumentEditor.Open(document);

        if (_session is null)
        {
            Status = $"{document.RelativePath} could not be read.";
            return;
        }

        EditorText = _session.Content;
        Status = $"Editing {document.RelativePath}. Ctrl+S saves.";

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>
    /// Writes the editor's text back to disk and re-renders the page from what was
    /// actually written.
    /// </summary>
    /// <param name="overwriteExternalChanges">Save over a file something else changed.</param>
    /// <returns>What happened, so the window can offer to force a conflicted save.</returns>
    public SaveOutcome Save(bool overwriteExternalChanges = false)
    {
        if (_session is not { } session)
        {
            return SaveOutcome.Failed;
        }

        SaveResult result = DocumentEditor.Save(session, EditorText, overwriteExternalChanges);

        Status = result.Outcome switch
        {
            SaveOutcome.Saved => $"Saved {session.Document.RelativePath}.",
            SaveOutcome.Unchanged => $"{session.Document.RelativePath} was already up to date.",
            SaveOutcome.Conflict => result.Message ?? "The file changed on disk.",
            _ => result.Message ?? "The file could not be written.",
        };

        if (!result.IsSuccess)
        {
            return result.Outcome;
        }

        _session = result.Session;

        OnPropertyChanged(nameof(HasUnsavedChanges));

        // A save changes what every link in the vault might resolve to, so the whole
        // snapshot is rebuilt rather than the one page patched.
        Reconcile();

        return result.Outcome;
    }

    /// <summary>Saves the current document.</summary>
    [RelayCommand]
    public void SaveDocument() => Save();

    /// <summary>Throws away the editor's text and rereads the file.</summary>
    [RelayCommand]
    public void ReloadDocument()
    {
        if (_session is not { } session || DocumentEditor.Reload(session) is not { } reloaded)
        {
            return;
        }

        _session = reloaded;
        EditorText = reloaded.Content;
        Status = $"Reloaded {reloaded.Document.RelativePath} from disk.";

        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>True when something else has changed the file being edited.</summary>
    public bool HasExternalChange => _session is not null && DocumentEditor.HasChangedOnDisk(_session);

    /// <summary>
    /// Creates the note a broken link was pointing at, and opens it.
    /// </summary>
    /// <param name="finding">A broken-link finding.</param>
    /// <returns>The created document, or null when nothing was created.</returns>
    public VaultDocument? CreateMissingNote(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        if (_vault is null || finding.Resolution is not { } resolution)
        {
            return null;
        }

        NoteDraft? draft = NoteFactory.Draft(_vault, resolution);

        if (draft is null)
        {
            return null;
        }

        if (NoteFactory.Create(draft) is { } failure)
        {
            Status = failure;
            return null;
        }

        Status = draft.TemplatePath is { } template
            ? $"Created {draft.RelativePath} from {template}."
            : $"Created {draft.RelativePath}.";

        Reconcile();

        VaultDocument? created = _vault.Index.ByRelativePath(draft.RelativePath).FirstOrDefault();

        if (created is not null)
        {
            Open(created);
        }

        return created;
    }

    /// <summary>
    /// Rewrites every link in the vault to its canonical target, and returns how many
    /// files changed (plan.md section 6.6).
    /// </summary>
    public int NormalizeVault(LinkForm form = LinkForm.VaultRelative)
    {
        if (_vault is null || _graph is null)
        {
            return 0;
        }

        int changed = 0;
        int rewritten = 0;

        foreach (VaultDocument document in _vault.Documents.Where(d => d.IsMarkdown))
        {
            if (ReadContent(document) is not { } content)
            {
                continue;
            }

            NormalizeResult result = MarkdownNormalizer.Normalize(
                content, document, _graph.OutboundFrom(document), form);

            if (result.Rewritten == 0)
            {
                continue;
            }

            if (AtomicFile.Write(document.AbsolutePath, result.Content) is null)
            {
                changed++;
                rewritten += result.Rewritten;
            }
        }

        Status = $"Rewrote {rewritten:N0} links in {changed:N0} files.";

        if (changed > 0)
        {
            Reconcile();
        }

        return changed;
    }

    /// <summary>Exports the vault as a static HTML site.</summary>
    /// <param name="outputPath">The directory to write into.</param>
    public ExportReport? ExportSite(string outputPath)
    {
        if (_vault is null || _builder is null)
        {
            return null;
        }

        ExportReport report = SiteExporter.ExportSite(_vault, _builder, ExportOptionsFor(outputPath));

        Status = $"Exported {report}";

        return report;
    }

    /// <summary>Exports the vault, or one page, as a single self-contained HTML file.</summary>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="currentPageOnly">Export only the page being read.</param>
    public ExportReport? ExportSingleFile(string outputPath, bool currentPageOnly = false)
    {
        if (_vault is null || _builder is null)
        {
            return null;
        }

        ExportReport report = SiteExporter.ExportSingleFile(
            _vault,
            _builder,
            ExportOptionsFor(outputPath),
            currentPageOnly ? ActiveTab?.Document : null);

        Status = $"Exported {report}";

        return report;
    }

    /// <summary>Exports the vault, or one page, as a PDF.</summary>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="currentPageOnly">Export only the page being read.</param>
    public ExportReport? ExportPdf(string outputPath, bool currentPageOnly = false)
    {
        if (_vault is null || _builder is null)
        {
            return null;
        }

        IEnumerable<VaultDocument> documents = currentPageOnly && ActiveTab?.Document is { } current
            ? [current]
            : _vault.Documents
                .Where(d => d.IsMarkdown && !d.Frontmatter.IsDraft)
                .OrderBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase);

        List<RenderDocument> rendered = [.. documents.Select(_builder.Build)];

        ExportReport report = PdfExporter.Export(
            rendered,
            new PdfOptions
            {
                OutputPath = outputPath,
                Title = _vault.RootPath.Length == 0 ? "Wiki" : Path.GetFileName(_vault.RootPath.TrimEnd(Path.DirectorySeparatorChar)),
            });

        Status = $"Exported {report}";

        return report;
    }

    private ExportOptions ExportOptionsFor(string outputPath) => new()
    {
        OutputPath = outputPath,
        Title = _vault is null || _vault.RootPath.Length == 0
            ? "Wiki"
            : Path.GetFileName(_vault.RootPath.TrimEnd(Path.DirectorySeparatorChar)),
    };

    private void CloseEditor()
    {
        _session = null;
        EditorText = string.Empty;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
