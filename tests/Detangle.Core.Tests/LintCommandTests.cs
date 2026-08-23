using System.Text.Json;
using Detangle.Core.Diagnostics;
using Detangle.Core.History;
using Detangle.Lint;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for `detangle lint` (plan.md section 15.5) — the same Link Doctor the panel
/// runs, pointed at a terminal.
/// </summary>
public class LintCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-lint-" + Guid.NewGuid().ToString("N")[..8]);

    public LintCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "raw"));

        File.WriteAllText(
            Path.Combine(_root, "index.md"),
            "# Index\n\nSee [[My Target]] and [[my-targt]] and [[nowhere at all]].\n");

        File.WriteAllText(
            Path.Combine(_root, "raw", "notes.md"),
            "# Notes\n\nSee [[My Target]].\n");

        File.WriteAllText(Path.Combine(_root, "my-target.md"), "# My Target\n");
    }

    [Fact]
    public void TheReportNamesTheRuleThatResolvedEachFolder()
    {
        JsonElement report = Report();

        // The differentiating payload: not that links are broken, but which step of the
        // chain each folder's links needed. Nothing else in the category can say this.
        JsonElement rules = report.GetProperty("rules");

        Assert.True(rules.TryGetProperty("raw", out JsonElement raw));
        Assert.True(raw.TryGetProperty("normalizedName", out JsonElement count));
        Assert.Equal(1, count.GetInt32());
    }

    [Fact]
    public void TheReportIdentifiesItselfAndCountsWhatItFound()
    {
        JsonElement report = Report();

        Assert.Equal(FindingsReport.SchemaVersion, report.GetProperty("schema").GetInt32());
        Assert.Equal(3, report.GetProperty("documents").GetInt32());
        Assert.Equal(2, report.GetProperty("counts").GetProperty("error").GetInt32());
        Assert.Equal(2, report.GetProperty("counts").GetProperty("byKind").GetProperty("brokenLink").GetInt32());
    }

    [Fact]
    public void EveryFindingIsSuggestedBecauseNobodyIsGoingToOpenOneLater()
    {
        JsonElement report = Report();

        JsonElement broken = report.GetProperty("findings")
            .EnumerateArray()
            .First(f => f.GetProperty("target").GetString() == "my-targt");

        // The panel defers the edit-distance pass until a reader opens a finding. A report
        // has no reader who will, so a broken link that does not say what it probably
        // meant is one whose consumer has to do the search again.
        Assert.Equal("my-target", broken.GetProperty("suggestedRewrite").GetString());
    }

    [Fact]
    public void ExitCodeIsOneWhenSomethingIsAsSevereAsTheThreshold()
    {
        Assert.Equal(LintCommand.FoundProblems, Run(FindingSeverity.Error).Code);

        // Info is a lower bar, so errors still trip it.
        Assert.Equal(LintCommand.FoundProblems, Run(FindingSeverity.Info).Code);
    }

    [Fact]
    public void ExitCodeIsZeroWhenNothingReachesTheThreshold()
    {
        File.WriteAllText(Path.Combine(_root, "index.md"), "# Index\n\nSee [[my-target]].\n");
        File.Delete(Path.Combine(_root, "raw", "notes.md"));

        // Orphan pages remain, which are Info, so only "never" and a threshold below Info
        // pass. That is the point of --fail-on: only broken links are Errors, so gating on
        // the default would never notice the interesting findings.
        Assert.Equal(0, Run(FindingSeverity.Error).Code);
        Assert.Equal(0, Run(failOn: null).Code);
        Assert.Equal(LintCommand.FoundProblems, Run(FindingSeverity.Info).Code);
    }

    [Fact]
    public void AVaultThatCannotBeReadFailsWithoutAReport()
    {
        var options = new LintOptions(
            Path.Combine(_root, "no-such-folder"), FindingSeverity.Error, Compact: true, OutputPath: null);

        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(LintCommand.CouldNotRun, LintCommand.Run(options, output, error));
        Assert.Empty(output.ToString());
        Assert.Contains("cannot read", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportCanBeWrittenToAFile()
    {
        string path = Path.Combine(_root, "report.json");

        LintCommand.Run(
            new LintOptions(_root, FindingSeverity.Error, Compact: false, OutputPath: path),
            TextWriter.Null,
            TextWriter.Null);

        Assert.True(File.Exists(path));
        Assert.Equal(
            FindingsReport.SchemaVersion,
            JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("schema").GetInt32());
    }

    [Theory]
    [InlineData("--fail-on")]
    [InlineData("--fail-on", "sideways")]
    [InlineData("--nonsense")]
    [InlineData("one", "two")]
    public void ABadCommandLineIsRefusedWithAReason(params string[] arguments)
    {
        (LintOptions? options, string? error) = LintCommand.Parse(arguments);

        Assert.Null(options);
        Assert.NotNull(error);
    }

    [Fact]
    public void TheCommandLineDefaultsToFailingOnErrors()
    {
        (LintOptions? options, _) = LintCommand.Parse(["some-vault"]);

        Assert.NotNull(options);
        Assert.Equal("some-vault", options.VaultPath);
        Assert.Equal(FindingSeverity.Error, options.FailOn);
        Assert.False(options.Compact);
        Assert.Null(options.OutputPath);
    }

    [Fact]
    public void NeverMeansNever()
    {
        (LintOptions? options, _) = LintCommand.Parse(["v", "--fail-on", "never", "--compact", "-o", "out.json"]);

        Assert.NotNull(options);
        Assert.Null(options.FailOn);
        Assert.True(options.Compact);
        Assert.Equal("out.json", options.OutputPath);
    }

    [Fact]
    public void MarkingWritesTheBaselineWhereARepositoryWillSeeIt()
    {
        Assert.Equal(0, Run(mark: true, since: false, failOnRegression: false).Code);

        // At the vault root, not in .detangle/, which the FAQ calls a cache the reader may
        // delete. This is the one file a team commits.
        Assert.True(File.Exists(Path.Combine(_root, BaselineStore.FileName)));
        Assert.NotEmpty(BaselineStore.Load(_root).Links);
    }

    [Fact]
    public void WithNoBaselineThereIsNoDeltaBlockRatherThanAnEmptyOne()
    {
        // "Nothing regressed" and "nothing was compared" are different answers.
        Assert.False(Report().TryGetProperty("delta", out _));

        JsonElement compared = JsonDocument.Parse(
            Run(mark: false, since: true, failOnRegression: false).Report).RootElement;

        Assert.True(compared.TryGetProperty("delta", out JsonElement delta));
        Assert.False(delta.GetProperty("regressed").GetBoolean());
    }

    [Fact]
    public void ALinkThatBreaksAfterTheBaselineIsAReportedRegression()
    {
        Run(mark: true, since: false, failOnRegression: false);

        // The target goes away, so a link that resolved by an exact name now resolves to
        // nothing: the regeneration failure this gate exists to catch.
        File.Delete(Path.Combine(_root, "my-target.md"));

        (int code, string report) = Run(mark: false, since: true, failOnRegression: true);

        Assert.Equal(LintCommand.FoundProblems, code);

        JsonElement delta = JsonDocument.Parse(report).RootElement.GetProperty("delta");

        Assert.True(delta.GetProperty("regressed").GetBoolean());
        Assert.Equal(2, delta.GetProperty("links").GetProperty("broke").GetInt32());

        JsonElement regression = delta.GetProperty("regressions").EnumerateArray().First();

        Assert.Equal("broke", regression.GetProperty("change").GetString());
        Assert.Equal(string.Empty, regression.GetProperty("resolvedTo").GetString());
    }

    [Fact]
    public void AnAlreadyBrokenVaultThatDidNotGetWorsePassesTheRegressionGate()
    {
        // This is the whole point of the gate. The fixture already has two broken links,
        // so --fail-on error fails on every run and can never tell one run from another.
        Run(mark: true, since: false, failOnRegression: false);

        (int gated, _) = Run(mark: false, since: true, failOnRegression: true);
        (int absolute, _) = Run(FindingSeverity.Error);

        Assert.Equal(0, gated);
        Assert.Equal(LintCommand.FoundProblems, absolute);
    }

    [Fact]
    public void MarkingAndComparingInOneRunReportsTheRunBeingMarked()
    {
        Run(mark: true, since: false, failOnRegression: false);

        File.WriteAllText(Path.Combine(_root, "extra.md"), "# Extra\n");

        JsonElement delta = JsonDocument.Parse(
                Run(mark: true, since: true, failOnRegression: false).Report)
            .RootElement.GetProperty("delta");

        // Compared against the baseline as it was, then overwritten — not compared against
        // the state this very run just wrote, which would always be empty.
        Assert.Equal(1, delta.GetProperty("pages").GetProperty("added").GetInt32());
    }

    [Theory]
    [InlineData("--mark", true, false, false)]
    [InlineData("--since", false, true, false)]
    [InlineData("--fail-on-regression", false, true, true)]
    public void TheGateFlagsParse(string flag, bool mark, bool since, bool failOnRegression)
    {
        (LintOptions? options, string? error) = LintCommand.Parse([_root, flag]);

        Assert.Null(error);
        Assert.Equal(mark, options!.Mark);

        // --fail-on-regression implies --since: there is nothing to fail on without a
        // comparison, and making someone pass both would be a footgun with no upside.
        Assert.Equal(since, options.Since);
        Assert.Equal(failOnRegression, options.FailOnRegression);
    }

    private JsonElement Report() => JsonDocument.Parse(Run(FindingSeverity.Error).Report).RootElement;

    private (int Code, string Report) Run(FindingSeverity? failOn)
    {
        var output = new StringWriter();

        int code = LintCommand.Run(
            new LintOptions(_root, failOn, Compact: true, OutputPath: null),
            output,
            TextWriter.Null);

        return (code, output.ToString());
    }

    private (int Code, string Report) Run(bool mark, bool since, bool failOnRegression)
    {
        var output = new StringWriter();

        int code = LintCommand.Run(
            new LintOptions(
                _root,
                FailOn: null,
                Compact: true,
                OutputPath: null,
                Mark: mark,
                Since: since,
                FailOnRegression: failOnRegression),
            output,
            TextWriter.Null);

        return (code, output.ToString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not a failed test.
        }
    }
}
