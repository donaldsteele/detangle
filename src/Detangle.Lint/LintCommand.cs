using Detangle.Core.Diagnostics;
using Detangle.Core.Graph;
using Detangle.Core.History;
using Detangle.Core.Linking;
using Detangle.Core.Repair;
using Detangle.Core.Vault;

namespace Detangle.Lint;

/// <summary>How the command was asked to run.</summary>
/// <param name="VaultPath">The folder to examine.</param>
/// <param name="FailOn">The severity at or above which the exit code is 1, or null never to fail.</param>
/// <param name="Compact">True for one line of JSON rather than an indented document.</param>
/// <param name="OutputPath">Where to write the report, or null for standard output.</param>
/// <param name="Mark">True to record the vault as it stands as the baseline to measure against.</param>
/// <param name="Since">True to compare against the marked baseline and report what changed.</param>
/// <param name="FailOnRegression">True to exit 1 when a link broke or degraded, whatever the totals are.</param>
/// <param name="ChoicesPath">A settled-ambiguity file to read instead of the vault's own.</param>
/// <param name="PatchPath">Where to write the repair as a unified diff, or "-" for standard output.</param>
/// <param name="PatchPolicy">What counts as repairable when a patch is asked for.</param>
public sealed record LintOptions(
    string VaultPath,
    FindingSeverity? FailOn,
    bool Compact,
    string? OutputPath,
    bool Mark = false,
    bool Since = false,
    bool FailOnRegression = false,
    string? ChoicesPath = null,
    string? PatchPath = null,
    RepairPolicy? PatchPolicy = null);

/// <summary>
/// The `detangle lint` command (plan.md section 15.5): the same Link Doctor the panel
/// runs, pointed at a terminal.
/// <para>
/// Everything here is composition. <c>LinkDoctor.Examine</c> is already a pure function of
/// a graph and a content reader, and <c>Detangle.Core</c> has no UI dependency, which is
/// the property section 4 has been protecting since phase 1 precisely so this could be
/// written without moving anything.
/// </para>
/// </summary>
public static class LintCommand
{
    /// <summary>Exit code for a report that met the failure threshold.</summary>
    public const int FoundProblems = 1;

    /// <summary>Exit code for a vault that could not be read at all.</summary>
    public const int CouldNotRun = 2;

    /// <summary>Parses the command line, or returns the usage text to print.</summary>
    /// <param name="arguments">The arguments, without the executable name.</param>
    /// <returns>The options, or the error explaining why there are none.</returns>
    public static (LintOptions? Options, string? Error) Parse(IReadOnlyList<string> arguments)
    {
        string? vault = null;
        string? output = null;
        FindingSeverity? failOn = FindingSeverity.Error;
        bool compact = false;
        bool mark = false;
        bool since = false;
        bool failOnRegression = false;
        string? choices = null;
        string? patch = null;
        RepairPolicy? patchPolicy = null;

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            switch (argument)
            {
                case "--fail-on" when i + 1 < arguments.Count:
                    i++;

                    if (string.Equals(arguments[i], "never", StringComparison.OrdinalIgnoreCase))
                    {
                        failOn = null;
                        break;
                    }

                    if (!Enum.TryParse(arguments[i], ignoreCase: true, out FindingSeverity parsed))
                    {
                        return (null, $"'{arguments[i]}' is not a severity. Use error, warning, info or never.");
                    }

                    failOn = parsed;
                    break;

                case "--output" or "-o" when i + 1 < arguments.Count:
                    output = arguments[++i];
                    break;

                case "--choices" when i + 1 < arguments.Count:
                    choices = arguments[++i];
                    break;

                case "--emit-patch" when i + 1 < arguments.Count:
                    patch = arguments[++i];
                    break;

                case "--patch-policy" when i + 1 < arguments.Count:
                    i++;
                    patchPolicy = RepairPolicy.Parse(arguments[i]);

                    if (patchPolicy is null)
                    {
                        return (null, $"'{arguments[i]}' is not a repair policy. Use safeonly or all.");
                    }

                    break;

                case "--compact":
                    compact = true;
                    break;

                case "--mark":
                    mark = true;
                    break;

                case "--since":
                    since = true;
                    break;

                case "--fail-on-regression":
                    since = true;
                    failOnRegression = true;
                    break;

                case "--fail-on" or "--output" or "-o" or "--choices"
                    or "--emit-patch" or "--patch-policy":
                    return (null, $"{argument} needs a value.");

                default:
                    if (argument.StartsWith('-'))
                    {
                        return (null, $"Unknown option {argument}.");
                    }

                    if (vault is not null)
                    {
                        return (null, "Only one vault can be examined at a time.");
                    }

                    vault = argument;
                    break;
            }
        }

        return vault is null
            ? (null, "No vault given.")
            : (new LintOptions(
                vault, failOn, compact, output, mark, since, failOnRegression, choices, patch, patchPolicy),
                null);
    }

    /// <summary>The usage text, printed for --help and for a bad command line.</summary>
    public static string Usage =>
        """
        detangle-lint — the Link Doctor, as a report

        Usage:
          detangle-lint <vault> [options]

        Options:
          --fail-on <severity>  Exit 1 when a finding is this severe or worse.
                                error (default), warning, info, or never.
          --output, -o <path>   Write the report to a file instead of standard output.
          --compact             One line of JSON rather than an indented document.
          --mark                Record the vault as it stands as the baseline, in
                                .detangle-baseline.json at the vault root. Commit it.
          --since               Report what changed since the baseline was marked, as a
                                "delta" block: what broke, healed, degraded or improved.
          --fail-on-regression  Exit 1 when a link broke or now needs a later rule than it
                                did, whatever the absolute counts are. Implies --since.
          --choices <path>      Read settled ambiguities from this file rather than from
                                .detangle-choices at the vault root.
          --emit-patch <path>   Write the repair as a unified diff, or "-" for standard
                                output. The vault itself is never touched.
          --patch-policy <what> safeonly (default) for the links with exactly one canonical
                                form, or all to include the guesses at broken links.
          --help, -h            This text.

        The report's "rules" section is the part worth reading: how many links in each
        folder needed which step of the resolution chain. A folder whose links all needed
        a late rule is a folder whose naming does not match the vault it was written into.

        For a wiki a generator rewrites wholesale, --fail-on-regression is the gate worth
        having. "Are there errors" fails on every run once anything is broken, so it cannot
        tell the run that made things worse from the twenty that did not.
        """;

    /// <summary>Runs the command and returns the process exit code.</summary>
    /// <param name="options">What to examine.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where failures go.</param>
    public static int Run(LintOptions options, TextWriter output, TextWriter error)
    {
        VaultSnapshot vault;

        try
        {
            vault = VaultScanner.Scan(options.VaultPath);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            error.WriteLine($"detangle-lint: cannot read {options.VaultPath}: {ex.Message}");

            return CouldNotRun;
        }

        // The one rung of the chain a person writes. Without it the report describes a
        // vault nobody is looking at: the application that settled an ambiguity resolves
        // that link one way and this would resolve it another.
        LinkGraph graph = LinkGraph.Build(
            vault, ChoiceStore.Open(vault.RootPath, options.ChoicesPath).ForResolver);
        IReadOnlyList<Finding> findings = LinkDoctor.Examine(graph, ReadContent);

        // Every finding is suggested, unlike in the panel, where the edit-distance pass is
        // deferred until a reader opens one. Nothing here is going to open one later, and
        // a report that says a link is broken without saying what it probably meant is a
        // report whose reader has to do the search again.
        findings = [.. findings.Select(LinkDoctor.SuggestFix)];

        // Taken before the baseline is overwritten, so "--mark --since" reports the run
        // that is being marked rather than an empty comparison against itself.
        VaultDelta? delta = options.Since
            ? VaultDelta.Compare(
                BaselineStore.Load(vault.RootPath),
                VaultSnapshotRecord.Take(graph, ReadContent))
            : null;

        if (options.Mark && !BaselineStore.Save(vault.RootPath, VaultSnapshotRecord.Take(graph, ReadContent)))
        {
            error.WriteLine(
                $"detangle-lint: cannot write {BaselineStore.PathFor(vault.RootPath)}; the baseline was not marked.");

            return CouldNotRun;
        }

        if (options.PatchPath is { Length: > 0 } patchPath)
        {
            PatchSet plan = VaultRepair.Plan(findings, ReadContent, options.PatchPolicy);

            // Planned, never applied. The documentation promises this command does not
            // rewrite your markdown, and a repair is a decision that wants the before and
            // after in front of a person — which is what the diff is for.
            try
            {
                if (patchPath == "-")
                {
                    output.Write(plan.ToUnifiedDiff());
                }
                else
                {
                    File.WriteAllText(patchPath, plan.ToUnifiedDiff());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException or PathTooLongException)
            {
                error.WriteLine($"detangle-lint: cannot write the patch: {ex.Message}");

                return CouldNotRun;
            }
        }

        string report = FindingsReport.Write(
            graph, findings, vault.RootPath, indented: !options.Compact, delta: delta);

        try
        {
            if (options.OutputPath is { Length: > 0 } path)
            {
                File.WriteAllText(path, report);
            }
            else
            {
                output.WriteLine(report);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"detangle-lint: cannot write the report: {ex.Message}");

            return CouldNotRun;
        }

        // A regression fails on its own terms, whatever the absolute counts are: that is
        // the whole point of the gate, and an already-broken wiki that got worse looks
        // identical to one that did not under --fail-on alone.
        if (options.FailOnRegression && delta is { HasRegression: true })
        {
            return FoundProblems;
        }

        // Severity ascends from Error, so "at least as severe as the threshold" is <=.
        return options.FailOn is { } threshold
            && FindingsReport.WorstSeverity(findings) is { } worst
            && worst <= threshold
                ? FoundProblems
                : 0;
    }

    private static string? ReadContent(VaultDocument document)
    {
        try
        {
            return File.ReadAllText(document.AbsolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that vanished between the scan and the read is one fewer finding,
            // not a failed run.
            return null;
        }
    }
}
