using System.Globalization;
using System.Text;

namespace Detangle.Core.Tests;

/// <summary>
/// Generates a large vault on disk, for the performance budgets in plan.md section 6.2.
/// <para>
/// The shape matters as much as the size: pages carry frontmatter, headings, prose,
/// code fences and links written the way a generated wiki writes them — a mix of exact
/// paths, drifted titles and deliberate breakage — so the numbers measured over it
/// reflect the work Detangle actually does rather than the work a directory of lorem
/// ipsum would ask for.
/// </para>
/// </summary>
public static class SyntheticVault
{
    private static readonly string[] Types = ["concept", "entity", "source", "synthesis"];

    private static readonly string[] Topics =
    [
        "attention", "embedding", "tokenizer", "transformer", "retrieval", "grounding",
        "evaluation", "alignment", "distillation", "quantization", "inference", "prompting",
    ];

    private static readonly string[] Words =
    [
        "model", "context", "window", "vector", "index", "corpus", "signal", "weight",
        "gradient", "objective", "sample", "latent", "encoder", "decoder", "prior",
        "residual", "batch", "epoch", "checkpoint", "benchmark",
    ];

    /// <summary>Creates the vault if it is not already there, and returns its path.</summary>
    /// <param name="fileCount">How many markdown pages to write.</param>
    /// <param name="name">Directory name under the system temp directory.</param>
    public static string Create(int fileCount, string name = "detangle-perf")
    {
        string root = Path.Combine(Path.GetTempPath(), $"{name}-{fileCount}");
        string marker = Path.Combine(root, ".generated");

        if (File.Exists(marker) && File.ReadAllText(marker) == fileCount.ToString(CultureInfo.InvariantCulture))
        {
            return root;
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);

        // A deterministic seed keeps one run comparable with the next.
        var random = new Random(20260820);

        for (int i = 0; i < fileCount; i++)
        {
            string topic = Topics[i % Topics.Length];
            string folder = Path.Combine(root, topic);

            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, $"{topic}-{i:D5}.md"), Page(i, fileCount, random));
        }

        File.WriteAllText(
            Path.Combine(root, "index.md"),
            "# Index\n\n" + string.Join(
                '\n',
                Enumerable.Range(0, Math.Min(200, fileCount))
                    .Select(i => $"- [[{Topics[i % Topics.Length]}-{i:D5}]]")));

        File.WriteAllText(marker, fileCount.ToString(CultureInfo.InvariantCulture));

        return root;
    }

    private static string Page(int index, int fileCount, Random random)
    {
        string topic = Topics[index % Topics.Length];
        var page = new StringBuilder();

        page.Append("---\n")
            .Append("title: ").Append(Title(topic, index)).Append('\n')
            .Append("type: ").Append(Types[index % Types.Length]).Append('\n')
            .Append("tags: [").Append(topic).Append(", llm/").Append(topic).Append("]\n")
            .Append("updated: 2026-0").Append((index % 8) + 1).Append("-15\n");

        if (index % 5 == 0)
        {
            page.Append("related:\n  - ").Append(Topics[(index + 3) % Topics.Length])
                .Append('-').Append((index + 7) % fileCount).Append('\n');
        }

        page.Append("---\n\n");
        page.Append("# ").Append(Title(topic, index)).Append("\n\n");

        for (int section = 0; section < 4; section++)
        {
            page.Append("## Section ").Append(section + 1).Append("\n\n");

            for (int paragraph = 0; paragraph < 3; paragraph++)
            {
                page.Append(Prose(random, 28)).Append("\n\n");
            }

            // Links in the shapes the resolver has to handle: an exact path, a drifted
            // title, and one that matches nothing.
            int neighbour = (index + section + 1) % fileCount;
            string neighbourTopic = Topics[neighbour % Topics.Length];

            page.Append("See [[").Append(neighbourTopic).Append('/').Append(neighbourTopic)
                .Append('-').Append(neighbour.ToString("D5", CultureInfo.InvariantCulture)).Append("]]");
            page.Append(" and [[").Append(Title(neighbourTopic, neighbour)).Append("]]");

            if (section == 3 && index % 11 == 0)
            {
                page.Append(" and [[").Append(topic).Append("-missing-").Append(index).Append("]]");
            }

            page.Append(".\n\n");
        }

        if (index % 7 == 0)
        {
            page.Append("```csharp\nvar model = Load(\"").Append(topic).Append("\");\nmodel.Run();\n```\n\n");
        }

        return page.ToString();
    }

    private static string Title(string topic, int index) =>
        $"{char.ToUpperInvariant(topic[0])}{topic[1..]} {index:D5}";

    private static string Prose(Random random, int wordCount)
    {
        var sentence = new StringBuilder();

        for (int i = 0; i < wordCount; i++)
        {
            sentence.Append(Words[random.Next(Words.Length)]).Append(' ');
        }

        return sentence.ToString().TrimEnd();
    }
}
