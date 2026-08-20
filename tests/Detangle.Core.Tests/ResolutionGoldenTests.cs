using System.Globalization;
using System.Text;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Golden tests: every link in every fixture vault, resolved, with the rule that fired
/// written down. These are the regression net for the chain — a change in step order or
/// in normalization shows up as a diff in a reviewable text file rather than as a
/// silently different answer.
/// <para>
/// Set DETANGLE_UPDATE_GOLDENS=1 to rewrite the expected files, then read the diff.
/// </para>
/// </summary>
public class ResolutionGoldenTests
{
    public static TheoryData<string> Vaults
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string vault in FixtureVaults.All)
            {
                data.Add(vault);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Vaults))]
    public void ResolutionMatchesGolden(string vaultName)
    {
        VaultSnapshot vault = FixtureVaults.Load(vaultName);
        // macOS stores filenames decomposed, so the same fixture yields NFD paths there
        // and NFC paths elsewhere. The golden is written composed on every platform.
        string actual = Render(vault).Normalize(NormalizationForm.FormC);

        string goldenPath = Path.Combine(FixtureVaults.FixturesRoot, "goldens", $"{vaultName}.golden");

        if (Environment.GetEnvironmentVariable("DETANGLE_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual);
            return;
        }

        Assert.True(File.Exists(goldenPath), $"Missing golden file {goldenPath}. Run with DETANGLE_UPDATE_GOLDENS=1.");

        string expected = File.ReadAllText(goldenPath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC);

        Assert.Equal(expected, actual);
    }

    private static string Render(VaultSnapshot vault)
    {
        LinkResolver resolver = vault.CreateResolver();
        var builder = new StringBuilder();

        builder.Append("flavor: ").Append(vault.Profile.Flavor).Append('\n');
        builder.Append("documents: ").Append(vault.Documents.Count).Append("\n\n");

        foreach (VaultDocument document in vault.Documents
            .Where(d => d.IsMarkdown)
            .OrderBy(d => d.RelativePath, StringComparer.Ordinal))
        {
            builder.Append(document.RelativePath).Append('\n');

            foreach (LinkResolution resolution in resolver.ResolveAll(document))
            {
                LinkReference link = resolution.Link;

                builder.Append("  ")
                    .Append(link.Syntax.ToString().ToLowerInvariant())
                    .Append(link.IsEmbed ? "!" : string.Empty)
                    .Append(" \"")
                    .Append(link)
                    .Append("\" -> ")
                    .Append(resolution.Rule)
                    .Append(' ')
                    .Append(resolution.Target?.RelativePath ?? "(none)");

                if (resolution.Anchor.Rule != AnchorRule.None)
                {
                    builder.Append(" [anchor: ").Append(resolution.Anchor.Rule);

                    if (resolution.Anchor.Line is int line)
                    {
                        builder.Append(" L").Append(line);
                    }

                    builder.Append(']');
                }

                if (resolution.IsAmbiguous)
                {
                    builder.Append(" [ambiguous: ")
                        .AppendJoin(", ", resolution.Candidates.Select(c => c.RelativePath))
                        .Append(']');
                }

                if (resolution.Suggestions.Count > 0)
                {
                    builder.Append(" [suggests: ")
                        .AppendJoin(", ", resolution.Suggestions.Select(c => c.RelativePath))
                        .Append(']');
                }

                builder.Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}
