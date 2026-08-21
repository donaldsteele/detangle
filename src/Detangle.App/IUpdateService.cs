namespace Detangle.App;

/// <summary>An update that is available to install.</summary>
/// <param name="Version">The version on the other end.</param>
/// <param name="IsDelta">True when only the changed files will be downloaded.</param>
public sealed record AvailableUpdate(string Version, bool IsDelta);

/// <summary>
/// In-app update, as the shell sees it (plan.md section 8, phase 8).
/// <para>
/// An interface rather than a direct call into Velopack because the updater is a
/// packaging concern that only the desktop entry point knows how to do: the WASM build
/// has no such thing, and a shell that referenced the updater directly could not be
/// tested without installing one.
/// </para>
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// True when this copy was installed by the updater. A build run from a source tree
    /// has no update feed to ask, and pretending otherwise means a checkbox that never
    /// does anything.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>Asks the feed whether there is a newer version.</summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an update and applies it. The app restarts into the new version, so
    /// nothing after this call runs.
    /// </summary>
    /// <param name="update">The update returned by <see cref="CheckAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    Task ApplyAsync(AvailableUpdate update, CancellationToken cancellationToken = default);
}
