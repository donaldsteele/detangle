using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Detangle.Rendering;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Export;

namespace Detangle.Desktop;

/// <summary>
/// Publishing a vault without opening a window.
/// <para>
/// The export path needs no UI — the render model, the Mermaid renderer and the HTML
/// emitter are all plain .NET — so the same executable that reads a wiki can also publish
/// one from a build script. That is how this project's own documentation site is built,
/// which means the exporter is exercised on every deploy rather than only in tests.
/// </para>
/// </summary>
internal static class HeadlessExport
{
    /// <summary>The usage text, printed for a malformed command line.</summary>
    public const string Usage =
        "usage: detangle --export-site <vault> <output-directory> [--title <title>]";

    /// <summary>
    /// Runs an export if the command line asked for one.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="exitCode">The code to exit with, when this handled the command line.</param>
    /// <returns>True when the command line was an export and the app should not start.</returns>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;

        if (args is not ["--export-site", ..])
        {
            return false;
        }

        if (args is not ["--export-site", { Length: > 0 } vaultPath, { Length: > 0 } outputPath, ..])
        {
            Console.Error.WriteLine(Usage);
            exitCode = 2;

            return true;
        }

        string title = TitleFrom(args) ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(vaultPath));

        try
        {
            VaultSnapshot vault = VaultScanner.Scan(vaultPath);

            var builder = new RenderModelBuilder(
                vault,
                options: new RenderOptions
                {
                    DiagramRenderer = new MermaiderDiagramRenderer(),
                    DiagramTheme = DiagramTheme.Dark,
                },
                // The settled ambiguities travel with the vault, so a published site
                // resolves the links the way the person who settled them decided, rather
                // than the way the chain would have guessed on its own.
                rememberedChoices: ChoiceStore.Open(vault.RootPath).ForResolver);

            ExportReport report = SiteExporter.ExportSite(
                vault,
                builder,
                new ExportOptions { OutputPath = outputPath, Title = title });

            Console.WriteLine($"{vault.Profile.Flavor} vault · {report}");

            foreach (string diagnostic in report.Diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            // A broken link is a fact about the vault, not a failure of the export, so it
            // is reported rather than made an error. A build that wants to fail on them
            // can read the count from this line.
            exitCode = 0;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            exitCode = 1;
        }

        return true;
    }

    private static string? TitleFrom(string[] args)
    {
        int index = Array.IndexOf(args, "--title");

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
