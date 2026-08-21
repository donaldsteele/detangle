using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Detangle.Core.Editing;

namespace Detangle.App;

/// <summary>
/// The window's editing and export wiring (plan.md sections 6.5 and 6.6).
/// <para>
/// Paths are asked for rather than guessed. An export writes a directory full of files
/// and normalization rewrites the vault in place, and neither is something to do to a
/// folder the reader did not name.
/// </para>
/// </summary>
public partial class MainWindow
{
    private bool _syncingEditor;

    private void WireEditing()
    {
        SaveButton.Click += (_, _) => SaveWithConflictCheck();
        ReloadButton.Click += (_, _) =>
        {
            ConflictBanner.IsVisible = false;
            ViewModel?.ReloadDocumentCommand.Execute(null);
        };

        CreateNoteButton.Click += (_, _) =>
        {
            if (FindingList.SelectedItem is Core.Diagnostics.Finding finding)
            {
                ViewModel?.CreateMissingNote(finding);
            }
        };

        Editor.TextChanged += OnEditorTextChanged;

        ExportSiteItem.Click += async (_, _) => await ExportSite();
        ExportSingleItem.Click += async (_, _) => await ExportSingleFile(currentPageOnly: false);
        ExportPageItem.Click += async (_, _) => await ExportSingleFile(currentPageOnly: true);
        ExportPdfItem.Click += async (_, _) => await ExportPdf();
        NormalizeItem.Click += (_, _) => ViewModel?.NormalizeVault();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncingEditor || ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.EditorText = Editor.Text;
    }

    /// <summary>Pushes the shell's text into the editor without echoing it straight back.</summary>
    private void SyncEditor()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        _syncingEditor = true;

        try
        {
            if (!string.Equals(Editor.Text, viewModel.EditorText, StringComparison.Ordinal))
            {
                Editor.Text = viewModel.EditorText;
            }
        }
        finally
        {
            _syncingEditor = false;
        }

        if (viewModel.IsEditing)
        {
            Editor.Focus();
        }
    }

    /// <summary>
    /// Saves, and on a conflict tells the reader what happened rather than throwing their
    /// edit away or silently overwriting whatever else wrote the file.
    /// </summary>
    private void SaveWithConflictCheck()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.Save() == SaveOutcome.Conflict)
        {
            // The reader keeps their text; the status line explains, and saving again
            // after a reload is the way through. Nothing is destroyed either way.
            ConflictBanner.IsVisible = true;
            return;
        }

        ConflictBanner.IsVisible = false;
    }

    private async Task ExportSite()
    {
        if (ViewModel is not { HasVault: true } viewModel)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Export the vault as a static site", AllowMultiple = false });

        if (folders is [{ } folder] && folder.TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.ExportSite(path);
        }
    }

    private async Task ExportSingleFile(bool currentPageOnly)
    {
        if (ViewModel is not { HasVault: true } viewModel)
        {
            return;
        }

        IStorageFile? file = await Save("HTML", "html", currentPageOnly);

        if (file?.TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.ExportSingleFile(path, currentPageOnly);
        }
    }

    private async Task ExportPdf()
    {
        if (ViewModel is not { HasVault: true } viewModel)
        {
            return;
        }

        IStorageFile? file = await Save("PDF", "pdf", currentPageOnly: false);

        if (file?.TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.ExportPdf(path);
        }
    }

    private Task<IStorageFile?> Save(string description, string extension, bool currentPageOnly)
    {
        string suggested = currentPageOnly && ViewModel?.ActiveTab?.Document is { } document
            ? document.Stem
            : Path.GetFileName(ViewModel?.VaultPath.TrimEnd(Path.DirectorySeparatorChar) ?? "vault");

        return StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {description}",
            SuggestedFileName = $"{(suggested.Length == 0 ? "vault" : suggested)}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(description) { Patterns = [$"*.{extension}"] },
            ],
        });
    }
}
