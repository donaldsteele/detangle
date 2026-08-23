using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.Search;
using Detangle.Core.Vault;

namespace Detangle.App;

/// <summary>
/// Right-click menus for the lists and trees in the shell.
/// <para>
/// One flyout per list rather than one per row template: every list here already drives
/// everything off its selection, so the menu can too, and the seven item templates would
/// otherwise carry seven copies of the same six commands. Right-pressing a row selects it
/// first — a menu acting on a row the reader cannot see highlighted is a menu that acts
/// somewhere else.
/// </para>
/// <para>
/// What is offered is bounded on purpose: six items and two separators. Nothing here
/// renames, moves or deletes a file, because there is no undo in the application yet and
/// a context menu is exactly where an accidental click lands.
/// </para>
/// </summary>
internal static class ItemMenus
{
    /// <summary>Attaches the menu for a list or tree whose items are pages.</summary>
    /// <param name="control">The list or tree to attach to.</param>
    /// <param name="shell">Where the commands go.</param>
    /// <param name="selected">The item currently selected, as the control understands it.</param>
    public static void Wire(Control control, ShellView shell, Func<object?> selected) =>
        Wire(control, shell, selected, item => shell.ViewModelForMenus is { } viewModel
            ? PageItems(DocumentOf(item), viewModel, shell)
            : []);

    /// <summary>Attaches a menu whose items are built by the caller.</summary>
    public static void Wire(
        Control control, ShellView shell, Func<object?> selected, Func<object?, IEnumerable<Control>> build)
    {
        var flyout = new MenuFlyout();

        control.ContextFlyout = flyout;

        // Tunnelling: the row has to be selected before the flyout reads the selection,
        // and a bubbling handler runs after the list has already decided what to do with
        // the press.
        control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);

        flyout.Opening += (_, _) =>
        {
            flyout.Items.Clear();

            foreach (Control item in build(selected()))
            {
                flyout.Items.Add(item);
            }
        };

        // A flyout with nothing in it still opens, as an empty grey rectangle beside the
        // pointer. Nothing selected means nothing to act on, so it should not open at all.
        control.ContextRequested += (_, args) =>
        {
            if (selected() is null)
            {
                args.Handled = true;
            }
        };

        void OnPointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (!args.GetCurrentPoint(control).Properties.IsRightButtonPressed
                || args.Source is not Visual source)
            {
                return;
            }

            switch (source.FindAncestorOfType<ListBoxItem>(includeSelf: true),
                source.FindAncestorOfType<TreeViewItem>(includeSelf: true))
            {
                case ({ } row, _) when control is SelectingItemsControl list:
                    list.SelectedItem = row.DataContext;
                    break;

                case (_, { } node) when control is TreeView tree:
                    tree.SelectedItem = node.DataContext;
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>The commands that apply to a page, wherever the page was selected.</summary>
    public static IEnumerable<Control> PageItems(VaultDocument? document, ShellViewModel shell, ShellView view)
    {
        if (document is null)
        {
            yield break;
        }

        yield return Item("Open", () => shell.Open(document));
        yield return new Separator();
        yield return Item("Copy path", () => view.CopyToClipboard(document.RelativePath));
        yield return Item("Copy as wikilink", () => view.CopyToClipboard($"[[{document.Stem}]]"));

        yield return Item(
            "Copy as markdown link",
            () => view.CopyToClipboard($"[{document.DisplayName}]({document.RelativePath})"));

        // The vault-relative path is the address space this product works in; the absolute
        // one names the reader's home directory, which is not what they meant to paste.
        if (shell.Capabilities.CanRevealInFileManager)
        {
            yield return new Separator();
            yield return Item("Reveal in file manager", () => view.Reveal(document.AbsolutePath));
        }
    }

    /// <summary>The commands for one open tab.</summary>
    public static IEnumerable<Control> TabItems(DocumentTab? tab, ShellViewModel shell, ShellView view)
    {
        if (tab is null)
        {
            yield break;
        }

        yield return Item("Close", () => shell.Close(tab));

        yield return Item("Close others", () =>
        {
            foreach (DocumentTab other in shell.Tabs.Where(t => t != tab).ToList())
            {
                shell.Close(other);
            }
        });

        yield return Item("Close all", () =>
        {
            foreach (DocumentTab open in shell.Tabs.ToList())
            {
                shell.Close(open);
            }
        });

        yield return new Separator();
        yield return Item("Copy path", () => view.CopyToClipboard(tab.Document.RelativePath));

        if (shell.Capabilities.CanRevealInFileManager)
        {
            yield return Item("Reveal in file manager", () => view.Reveal(tab.Document.AbsolutePath));
        }
    }

    /// <summary>The commands for one Link Doctor finding.</summary>
    public static IEnumerable<Control> FindingItems(Finding? finding, ShellViewModel shell, ShellView view)
    {
        if (finding is null)
        {
            yield break;
        }

        // The same test the action card applies: a fix is offered when there is a rewrite
        // to preview, so the menu never offers one the card would not.
        if (shell.PreviewFix(finding) is not null)
        {
            yield return Item("Fix", () => shell.ApplyFix(finding));
        }

        yield return Item("Ignore", () => shell.Ignore(finding));
        yield return new Separator();
        yield return Item("Open the page", () => shell.Open(finding.Document));
        yield return Item("Copy path", () => view.CopyToClipboard(finding.Document.RelativePath));

        yield return Item(
            "Copy the finding",
            () => view.CopyToClipboard($"{finding.Document.RelativePath}:{finding.Line}  {finding.Message}"));
    }

    /// <summary>The document behind whatever a list of this kind holds.</summary>
    public static VaultDocument? DocumentOf(object? item) => item switch
    {
        VaultDocument document => document,
        NavigationNode node => node.Document,
        SearchHit hit => hit.Document,
        Backlink backlink => backlink.Source,
        UnlinkedMention mention => mention.Source,
        GraphNode node => node.Document,
        DocumentTab tab => tab.Document,
        _ => null,
    };

    private static MenuItem Item(string header, Action invoke)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) => invoke();

        return item;
    }
}
