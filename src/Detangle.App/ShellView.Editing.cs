using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Detangle.Core.Editing;
using Detangle.Rendering.Export;

namespace Detangle.App;

/// <summary>
/// The shell's editing and export wiring (plan.md sections 6.5 and 6.6).
/// <para>
/// Paths are asked for rather than guessed. An export writes a directory full of files
/// and normalization rewrites the vault in place, and neither is something to do to a
/// folder the reader did not name.
/// </para>
/// </summary>
public partial class ShellView
{
    private bool _syncingEditor;

    /// <summary>
    /// The file pickers, reached through the top level rather than held. A view does not
    /// know whether it is in a window or in a browser tab, and in a browser tab there is
    /// no window to ask.
    /// </summary>
    private Avalonia.Platform.Storage.IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

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
            if (FindingTree.SelectedItem is Core.Diagnostics.Finding finding)
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

        if (Storage is not { } storage)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Export the vault as a static site", AllowMultiple = false })
            .ConfigureAwait(true);

        if (folders is not [{ } folder])
        {
            return;
        }

        // A site is a directory of files, and this writes them by path. A browser hands
        // back a folder it will read and write on your behalf rather than a location on
        // disk, so there is nowhere to write to - and saying so is the whole fix here.
        // Choosing a folder and having nothing happen at all is what this did before, and
        // it is the third time that has been the answer to a question nobody was asked.
        if (folder.TryGetLocalPath() is not { Length: > 0 } path)
        {
            viewModel.Status =
                "A static site is a folder of files, which this build cannot write. "
                + "Use Export \u2192 Single HTML file instead, or run the desktop application.";

            return;
        }

        viewModel.ExportSite(path);
    }

    private async Task ExportSingleFile(bool currentPageOnly)
    {
        if (ViewModel is not { HasVault: true } viewModel)
        {
            return;
        }

        IStorageFile? file = await Save("HTML", "html", currentPageOnly);

        await WriteExport(file, path => viewModel.ExportSingleFile(path, currentPageOnly)).ConfigureAwait(true);
    }

    private async Task ExportPdf()
    {
        if (ViewModel is not { HasVault: true } viewModel)
        {
            return;
        }

        IStorageFile? file = await Save("PDF", "pdf", currentPageOnly: false);

        await WriteExport(file, path => viewModel.ExportPdf(path)).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs an exporter and makes sure the bytes reach the file the reader chose.
    /// <para>
    /// A desktop file has a path, and the exporter writes straight to it. A browser file
    /// does not: the save picker has already created an empty file, and the only way to
    /// fill it is the stream it hands back. Writing to <c>TryGetLocalPath</c> there wrote
    /// into the in-memory filesystem instead and left the reader with a downloaded PDF of
    /// nothing at all - which is what it did, and is why this exists.
    /// </para>
    /// </summary>
    private static async Task WriteExport(IStorageFile? file, Func<string, ExportReport?> export)
    {
        if (file is null)
        {
            return;
        }

        if (file.TryGetLocalPath() is { Length: > 0 } local)
        {
            export(local);

            return;
        }

        // Somewhere this platform can write, which for the browser build is its own
        // in-memory filesystem. The name matters only to the exporter, which uses the
        // extension to decide nothing - but a temp file per export keeps two of them from
        // colliding.
        string staging = Path.Combine(
            Path.GetTempPath(), $"detangle-export-{Guid.NewGuid():N}{Path.GetExtension(file.Name)}");

        try
        {
            if (export(staging) is null || !File.Exists(staging))
            {
                return;
            }

            await using Stream source = File.OpenRead(staging);
            await using Stream destination = await file.OpenWriteAsync().ConfigureAwait(true);

            await source.CopyToAsync(destination).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                File.Delete(staging);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A staging file that will not delete is litter in a filesystem that dies
                // with the tab. It is not worth interrupting a successful export for.
            }
        }
    }

    private Task<IStorageFile?> Save(string description, string extension, bool currentPageOnly)
    {
        string suggested = currentPageOnly && ViewModel?.ActiveTab?.Document is { } document
            ? document.Stem
            : Path.GetFileName(ViewModel?.VaultPath.TrimEnd(Path.DirectorySeparatorChar) ?? "vault");

        if (Storage is not { } storage)
        {
            return Task.FromResult<IStorageFile?>(null);
        }

        return storage.SaveFilePickerAsync(new FilePickerSaveOptions
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
