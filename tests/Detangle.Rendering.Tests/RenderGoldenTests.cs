using System.Globalization;
using System.Text;
using Detangle.Core.Vault;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Renders every document of every fixture vault and compares the shape of the result
/// against a checked-in file. This is the phase 2 exit criterion made testable: the
/// torture vault renders end to end, and any change to the translation shows up as a
/// reviewable diff rather than as a subtly different page.
/// <para>
/// Set DETANGLE_UPDATE_GOLDENS=1 to rewrite the expected files.
/// </para>
/// </summary>
public class RenderGoldenTests
{
    private static readonly string[] VaultNames =
    [
        "llm-wiki", "obsidian", "foam", "dendron", "logseq", "quartz", "zettelkasten",
        "mkdocs", "docusaurus", "gitbook", "docsify", "mdbook", "deepwiki", "torture",
    ];

    public static TheoryData<string> Vaults
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (string vault in VaultNames)
            {
                data.Add(vault);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Vaults))]
    public void RenderedShapeMatchesGolden(string vaultName)
    {
        VaultSnapshot vault = VaultScanner.Scan(Path.Combine(FixturesRoot, "vaults", vaultName));
        var builder = new RenderModelBuilder(vault);

        var output = new StringBuilder();

        foreach (VaultDocument document in vault.Documents
            .Where(d => d.IsMarkdown)
            .OrderBy(d => d.RelativePath, StringComparer.Ordinal))
        {
            RenderDocument rendered = builder.Build(document);

            output.Append(document.RelativePath).Append('\n');

            foreach (RenderBlock block in rendered.Blocks)
            {
                Describe(output, block, 1);
            }

            foreach (string diagnostic in rendered.Diagnostics)
            {
                output.Append("  ! ").Append(diagnostic).Append('\n');
            }

            output.Append('\n');
        }

        string actual = output.ToString().Normalize(NormalizationForm.FormC);
        string goldenPath = Path.Combine(FixturesRoot, "goldens", $"{vaultName}.render.golden");

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

    private static void Describe(StringBuilder output, RenderBlock block, int depth)
    {
        string indent = new(' ', depth * 2);

        switch (block)
        {
            case ParagraphRenderBlock paragraph:
                output.Append(indent).Append("paragraph: ")
                    .Append(Summarize(paragraph.Inlines)).Append('\n');
                break;

            case HeadingRenderBlock heading:
                output.Append(indent).Append("h").Append(heading.Level).Append(" #")
                    .Append(heading.Slug).Append(": ").Append(heading.Text).Append('\n');
                break;

            case CodeRenderBlock code:
                output.Append(indent).Append("code")
                    .Append(code.IsDiagram ? "(diagram)" : string.Empty)
                    .Append(" lang=").Append(code.Language.Length == 0 ? "-" : code.Language)
                    .Append(" lines=")
                    .Append(code.Source.TrimEnd('\n').Split('\n').Length.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
                break;

            case CalloutRenderBlock callout:
                output.Append(indent).Append("callout ").Append(callout.Dialect.ToString().ToLowerInvariant())
                    .Append('/').Append(callout.Kind)
                    .Append(callout.IsCollapsible ? callout.StartsCollapsed ? " [closed]" : " [open]" : string.Empty)
                    .Append(": ").Append(callout.Title).Append('\n');
                DescribeAll(output, callout.Blocks, depth + 1);
                break;

            case QuoteRenderBlock quote:
                output.Append(indent).Append("quote\n");
                DescribeAll(output, quote.Blocks, depth + 1);
                break;

            case ListRenderBlock list:
                output.Append(indent).Append(list.IsOrdered ? "ordered-list" : "list")
                    .Append(" items=").Append(list.Items.Count).Append('\n');
                DescribeAll(output, list.Items, depth + 1);
                break;

            case ListItemRenderBlock item:
                output.Append(indent).Append("item")
                    .Append(item.Task == TaskState.None ? string.Empty : $" [{item.Task.ToString().ToLowerInvariant()}]")
                    .Append('\n');
                DescribeAll(output, item.Blocks, depth + 1);
                break;

            case TableRenderBlock table:
                output.Append(indent).Append("table rows=").Append(table.Rows.Count)
                    .Append(" columns=").Append(table.Alignments.Count).Append('\n');
                break;

            case ThematicBreakRenderBlock:
                output.Append(indent).Append("rule\n");
                break;

            case MathRenderBlock math:
                output.Append(indent).Append("math: ").Append(Clip(math.Source)).Append('\n');
                break;

            case DefinitionListRenderBlock definitions:
                output.Append(indent).Append("definition-list items=")
                    .Append(definitions.Items.Count).Append('\n');
                DescribeAll(output, definitions.Items, depth + 1);
                break;

            case DefinitionRenderBlock definition:
                output.Append(indent).Append("definition: ").Append(Summarize(definition.Term)).Append('\n');
                DescribeAll(output, definition.Definitions.SelectMany(d => d), depth + 1);
                break;

            case FootnotesRenderBlock footnotes:
                output.Append(indent).Append("footnotes: ")
                    .AppendJoin(", ", footnotes.Notes.Select(n => n.Label)).Append('\n');
                break;

            case TransclusionRenderBlock transclusion:
                output.Append(indent).Append("embed ")
                    .Append(transclusion.Resolution.Target?.RelativePath ?? "(unresolved)");

                if (transclusion.Error is { Length: > 0 } error)
                {
                    output.Append(" error=").Append(Clip(error));
                }

                output.Append('\n');
                DescribeAll(output, transclusion.Blocks, depth + 1);
                break;

            case PropertiesRenderBlock properties:
                output.Append(indent).Append("properties ")
                    .Append(properties.Frontmatter.Kind.ToString().ToLowerInvariant())
                    .Append(" references=").Append(properties.References.Count).Append('\n');
                break;

            default:
                output.Append(indent).Append(block.GetType().Name).Append('\n');
                break;
        }
    }

    private static void DescribeAll(StringBuilder output, IEnumerable<RenderBlock> blocks, int depth)
    {
        foreach (RenderBlock block in blocks)
        {
            Describe(output, block, depth);
        }
    }

    private static string Summarize(IReadOnlyList<RenderInline> inlines)
    {
        var parts = new List<string>();

        foreach (RenderInline inline in inlines)
        {
            switch (inline)
            {
                case LinkRun link:
                    parts.Add($"[link {link.Resolution.Rule} -> {link.Resolution.Target?.RelativePath ?? "(none)"}]");
                    break;
                case ImageRun image:
                    parts.Add($"[image -> {image.Resolution.Target?.RelativePath ?? "(none)"}]");
                    break;
                case MathRun math:
                    parts.Add($"[math {Clip(math.Source)}]");
                    break;
                case FootnoteReferenceRun footnote:
                    parts.Add($"[footnote {footnote.Label}]");
                    break;
                case TagRun tag:
                    parts.Add($"[tag {tag.Tag}]");
                    break;
                case CodeRun code:
                    parts.Add($"`{Clip(code.Code)}`");
                    break;
                case StyleRun style:
                    parts.Add($"<{style.Style.ToString().ToLowerInvariant()}>{Summarize(style.Children)}</>");
                    break;
                case TextRun text when text.Text.Trim().Length > 0:
                    parts.Add(Clip(text.Text.Trim()));
                    break;
            }
        }

        return Clip(string.Join(' ', parts), 160);
    }

    private static string Clip(string value, int limit = 60)
    {
        string collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= limit ? collapsed : collapsed[..limit] + "…";
    }

    private static string FixturesRoot { get; } = FindFixturesRoot();

    private static string FindFixturesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"tests/fixtures was not found above {AppContext.BaseDirectory}.");
    }
}
