using System.Text.Json;
using Detangle.Core.Diagnostics;
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
