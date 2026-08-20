using System.Text;

namespace Detangle.Core.Parsing;

/// <summary>
/// Splits a document into its frontmatter and its body.
/// <para>
/// The body keeps the frontmatter's lines as blank ones rather than dropping them. Line
/// numbers are user-facing — they appear in Link Doctor findings and in editor jumps —
/// and a body whose lines are offset by the size of a block that varies per file would
/// make every one of those numbers wrong.
/// </para>
/// </summary>
public static class DocumentBody
{
    /// <summary>Reads the frontmatter and returns the body with those lines blanked.</summary>
    /// <param name="content">The full document text.</param>
    public static (DocumentFrontmatter Frontmatter, string Body) Split(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return (DocumentFrontmatter.Empty, string.Empty);
        }

        string text = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('﻿');
        DocumentFrontmatter frontmatter = FrontmatterReader.Read(text);

        return (frontmatter, Blank(text, frontmatter.LineCount));
    }

    /// <summary>Replaces the first <paramref name="lineCount"/> lines with empty ones.</summary>
    public static string Blank(string text, int lineCount)
    {
        if (lineCount <= 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int line = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (line >= lineCount)
            {
                builder.Append(text, i, text.Length - i);
                break;
            }

            if (text[i] == '\n')
            {
                line++;
            }

            builder.Append(text[i] == '\n' ? '\n' : ' ');
        }

        return builder.ToString();
    }
}
