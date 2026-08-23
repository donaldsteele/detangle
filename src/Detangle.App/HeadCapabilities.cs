namespace Detangle.App;

/// <summary>
/// What the head this shell is running in can actually do.
/// <para>
/// The desktop and the browser share one control tree, and some of what the shell offers
/// is only true in one of them: a browser tab has no file manager to reveal a path in and
/// no folder to drop. The difference used to be handled where it bit — calling the
/// platform and swallowing the <c>PlatformNotSupportedException</c> — which shows a
/// reader a command, takes their click, and does nothing.
/// </para>
/// <para>
/// This is the other way round: each head states what it can do, and the shell binds
/// visibility to that, so a command that cannot work is never offered. It is set once at
/// startup rather than probed, because a probe answers "what platform is this" and the
/// question is "what did this head wire up" — which only the head knows.
/// </para>
/// </summary>
public sealed record HeadCapabilities
{
    /// <summary>A desktop head: a real filesystem, a shell, and a window that reopens.</summary>
    public static HeadCapabilities Desktop { get; } = new()
    {
        CanRevealInFileManager = true,
        CanOpenExternalLinks = true,
        CanDropFolders = true,
        CanPersistAcrossSessions = true,
    };

    /// <summary>
    /// A browser head. Links still open — that is a navigation, not a process — but there
    /// is no file manager, no folder to drop, and nothing that outlives the tab.
    /// </summary>
    public static HeadCapabilities Browser { get; } = new()
    {
        CanRevealInFileManager = false,
        CanOpenExternalLinks = true,
        CanDropFolders = false,
        CanPersistAcrossSessions = false,
    };

    /// <summary>Whether a path can be shown in the platform's file manager.</summary>
    public bool CanRevealInFileManager { get; init; }

    /// <summary>Whether a link out of the vault can be handed to the platform.</summary>
    public bool CanOpenExternalLinks { get; init; }

    /// <summary>Whether a folder dragged onto the window can be opened as a vault.</summary>
    public bool CanDropFolders { get; init; }

    /// <summary>Whether anything written beside the vault will still be there next time.</summary>
    public bool CanPersistAcrossSessions { get; init; }
}
