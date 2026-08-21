using System.Globalization;
using System.Text;
using Detangle.Core.Dbml;
using Detangle.Core.Linking;
using Detangle.Rendering.Model;

namespace Detangle.Rendering.Export;

/// <summary>
/// Turns a render model into HTML.
/// <para>
/// The export goes through the same model the reader draws from, which is what makes the
/// exported site agree with the app: a link that resolved by an alias match is an anchor
/// in the HTML, a diagram is the SVG that was already rendered, and a transclusion is the
/// borrowed text inlined exactly where the reader saw it. Re-parsing the markdown with a
/// stock HTML renderer would throw all of that away and produce the same broken links
/// every other tool produces.
/// </para>
/// <para>
/// Nothing here emits script, and vault text is escaped on the way out, so a page in the
/// exported site cannot execute anything a note claimed to be HTML.
/// </para>
/// </summary>
public sealed class HtmlEmitter
{
    private readonly StringBuilder _builder = new();
    private readonly Func<LinkResolution, string?> _href;

    /// <summary>Creates an emitter.</summary>
    /// <param name="href">
    /// Turns a resolution into a URL, or returns null for a link that should be rendered
    /// as plain text because its target is not in the export.
    /// </param>
    public HtmlEmitter(Func<LinkResolution, string?> href)
    {
        ArgumentNullException.ThrowIfNull(href);

        _href = href;
    }

    /// <summary>Emits the body of one document.</summary>
    public string Emit(RenderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _builder.Clear();

        foreach (RenderBlock block in document.Blocks)
        {
            Write(block);
        }

        return _builder.ToString();
    }

    /// <summary>Escapes text for an HTML text node or attribute.</summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 16);

        foreach (char c in text)
        {
            _ = c switch
            {
                '&' => builder.Append("&amp;"),
                '<' => builder.Append("&lt;"),
                '>' => builder.Append("&gt;"),
                '"' => builder.Append("&quot;"),
                '\'' => builder.Append("&#39;"),
                _ => builder.Append(c),
            };
        }

        return builder.ToString();
    }

    private void Write(RenderBlock block)
    {
        switch (block)
        {
            case ParagraphRenderBlock paragraph:
                _builder.Append("<p>");
                Write(paragraph.Inlines);
                _builder.Append("</p>\n");
                break;

            case HeadingRenderBlock heading:
                _builder.Append(CultureInfo.InvariantCulture, $"<h{heading.Level} id=\"{Escape(heading.Slug)}\">");
                Write(heading.Inlines);
                _builder.Append(CultureInfo.InvariantCulture, $"</h{heading.Level}>\n");
                break;

            // A diagram is checked before code: a mermaid fence is both, and the picture
            // is the point.
            case DiagramRenderBlock diagram:
                WriteDiagram(diagram);
                break;

            case CodeRenderBlock code:
                _builder.Append(
                    code.Language.Length > 0
                        ? $"<pre><code class=\"language-{Escape(code.Language)}\">"
                        : "<pre><code>");
                _builder.Append(Escape(code.Source));
                _builder.Append("</code></pre>\n");
                break;

            case QuoteRenderBlock quote:
                _builder.Append("<blockquote>\n");
                WriteAll(quote.Blocks);
                _builder.Append("</blockquote>\n");
                break;

            case CalloutRenderBlock callout:
                WriteCallout(callout);
                break;

            case ListRenderBlock list:
                WriteList(list);
                break;

            case ListItemRenderBlock item:
                _builder.Append("<li>");
                WriteAll(item.Blocks);
                _builder.Append("</li>\n");
                break;

            case TableRenderBlock table:
                WriteTable(table);
                break;

            case ThematicBreakRenderBlock:
                _builder.Append("<hr />\n");
                break;

            case MathRenderBlock math:
                // KaTeX is not bundled into the export, so block math ships as its source
                // in a marked element: readable, copyable, and honest about what it is.
                _builder.Append("<div class=\"math math-block\">");
                _builder.Append(Escape(math.Source));
                _builder.Append("</div>\n");
                break;

            case DefinitionListRenderBlock definitions:
                WriteDefinitions(definitions);
                break;

            case FootnotesRenderBlock footnotes:
                WriteFootnotes(footnotes);
                break;

            case TransclusionRenderBlock transclusion:
                WriteTransclusion(transclusion);
                break;

            case PropertiesRenderBlock properties:
                WriteProperties(properties);
                break;

            default:
                break;
        }
    }

    private void WriteAll(IEnumerable<RenderBlock> blocks)
    {
        foreach (RenderBlock block in blocks)
        {
            Write(block);
        }
    }

    private void WriteDiagram(DiagramRenderBlock diagram)
    {
        _builder.Append(CultureInfo.InvariantCulture, $"<figure class=\"diagram diagram-{Lower(diagram.Kind.ToString())}\">\n");

        if (diagram.IsRendered)
        {
            // The SVG is inlined rather than linked so the exported page is one file that
            // needs no network and no asset directory.
            _builder.Append(diagram.Svg);
            _builder.Append('\n');
        }
        else
        {
            _builder.Append("<pre><code>");
            _builder.Append(Escape(diagram.Source));
            _builder.Append("</code></pre>\n");
        }

        if (diagram.Schema is { } schema)
        {
            WriteSchema(schema);
        }

        foreach (string diagnostic in diagram.Diagnostics)
        {
            _builder.Append("<p class=\"diagnostic\">");
            _builder.Append(Escape(diagnostic));
            _builder.Append("</p>\n");
        }

        _builder.Append("</figure>\n");
    }

    /// <summary>
    /// The table detail an erDiagram cannot say: defaults, notes and the settings the
    /// Mermaid emitter drops (plan.md section 4.2).
    /// </summary>
    private void WriteSchema(DbmlSchema schema)
    {
        _builder.Append("<details class=\"schema\"><summary>Schema detail</summary>\n<table>\n");
        _builder.Append("<tr><th>Table</th><th>Column</th><th>Type</th><th>Settings</th><th>Note</th></tr>\n");

        foreach (DbmlTable table in schema.Tables)
        {
            foreach (DbmlColumn column in table.Columns)
            {
                var settings = new List<string>();

                if (column.IsPrimaryKey) { settings.Add("pk"); }
                if (column.IsUnique) { settings.Add("unique"); }
                if (column.IsNotNull) { settings.Add("not null"); }
                if (column.IsIncrement) { settings.Add("increment"); }
                if (column.Default is { Length: > 0 } value) { settings.Add($"default {value}"); }

                _builder.Append(CultureInfo.InvariantCulture, $"<tr><td>{Escape(table.Name)}</td>");
                _builder.Append(CultureInfo.InvariantCulture, $"<td>{Escape(column.Name)}</td>");
                _builder.Append(CultureInfo.InvariantCulture, $"<td>{Escape(column.Type)}</td>");
                _builder.Append(CultureInfo.InvariantCulture, $"<td>{Escape(string.Join(", ", settings))}</td>");
                _builder.Append(CultureInfo.InvariantCulture, $"<td>{Escape(column.Note)}</td></tr>\n");
            }
        }

        _builder.Append("</table>\n</details>\n");
    }

    private void WriteCallout(CalloutRenderBlock callout)
    {
        string tag = callout.IsCollapsible ? "details" : "div";

        _builder.Append(CultureInfo.InvariantCulture, $"<{tag} class=\"callout callout-{Escape(Lower(callout.Kind))}\"");

        if (callout.IsCollapsible && !callout.StartsCollapsed)
        {
            _builder.Append(" open");
        }

        _builder.Append(">\n");
        _builder.Append(callout.IsCollapsible ? "<summary>" : "<p class=\"callout-title\">");
        _builder.Append(Escape(callout.Title));
        _builder.Append(callout.IsCollapsible ? "</summary>\n" : "</p>\n");

        WriteAll(callout.Blocks);

        _builder.Append(CultureInfo.InvariantCulture, $"</{tag}>\n");
    }

    private void WriteList(ListRenderBlock list)
    {
        if (list.IsOrdered)
        {
            _builder.Append(CultureInfo.InvariantCulture, $"<ol start=\"{list.Start}\">\n");
        }
        else
        {
            _builder.Append("<ul>\n");
        }

        foreach (ListItemRenderBlock item in list.Items)
        {
            _builder.Append(item.Task == TaskState.None ? "<li>" : "<li class=\"task\">");

            if (item.Task != TaskState.None)
            {
                _builder.Append(
                    item.Task == TaskState.Checked
                        ? "<input type=\"checkbox\" checked disabled /> "
                        : "<input type=\"checkbox\" disabled /> ");
            }

            WriteAll(item.Blocks);
            _builder.Append("</li>\n");
        }

        _builder.Append(list.IsOrdered ? "</ol>\n" : "</ul>\n");
    }

    private void WriteTable(TableRenderBlock table)
    {
        _builder.Append("<table>\n");

        foreach (TableRowRenderBlock row in table.Rows)
        {
            _builder.Append("<tr>");

            for (int i = 0; i < row.Cells.Count; i++)
            {
                TableCellRenderBlock cell = row.Cells[i];
                string tag = row.IsHeader ? "th" : "td";
                string alignment = i < table.Alignments.Count
                    ? AlignmentOf(table.Alignments[i])
                    : string.Empty;

                _builder.Append(CultureInfo.InvariantCulture, $"<{tag}{alignment}");

                if (cell.ColumnSpan > 1)
                {
                    _builder.Append(CultureInfo.InvariantCulture, $" colspan=\"{cell.ColumnSpan}\"");
                }

                _builder.Append('>');
                WriteCell(cell);
                _builder.Append(CultureInfo.InvariantCulture, $"</{tag}>");
            }

            _builder.Append("</tr>\n");
        }

        _builder.Append("</table>\n");
    }

    /// <summary>
    /// A table cell holding one paragraph is written without the paragraph tag, which is
    /// what keeps an exported table from being twice as tall as the one on screen.
    /// </summary>
    private void WriteCell(TableCellRenderBlock cell)
    {
        if (cell.Blocks is [ParagraphRenderBlock only])
        {
            Write(only.Inlines);
            return;
        }

        WriteAll(cell.Blocks);
    }

    private void WriteDefinitions(DefinitionListRenderBlock definitions)
    {
        _builder.Append("<dl>\n");

        foreach (DefinitionRenderBlock definition in definitions.Items)
        {
            _builder.Append("<dt>");
            Write(definition.Term);
            _builder.Append("</dt>\n");

            foreach (IReadOnlyList<RenderBlock> blocks in definition.Definitions)
            {
                _builder.Append("<dd>");
                WriteAll(blocks);
                _builder.Append("</dd>\n");
            }
        }

        _builder.Append("</dl>\n");
    }

    private void WriteFootnotes(FootnotesRenderBlock footnotes)
    {
        _builder.Append("<section class=\"footnotes\">\n<ol>\n");

        foreach ((string label, IReadOnlyList<RenderBlock> blocks) in footnotes.Notes)
        {
            _builder.Append(CultureInfo.InvariantCulture, $"<li id=\"fn-{Escape(label)}\">");
            WriteAll(blocks);
            _builder.Append("</li>\n");
        }

        _builder.Append("</ol>\n</section>\n");
    }

    private void WriteTransclusion(TransclusionRenderBlock transclusion)
    {
        _builder.Append("<blockquote class=\"transclusion\">\n");

        string? href = _href(transclusion.Resolution);
        string label = transclusion.Resolution.Target?.DisplayName
            ?? transclusion.Resolution.Link.RawTarget;

        _builder.Append("<p class=\"transclusion-source\">");

        if (href is { Length: > 0 })
        {
            _builder.Append(CultureInfo.InvariantCulture, $"<a href=\"{Escape(href)}\">{Escape(label)}</a>");
        }
        else
        {
            _builder.Append(Escape(label));
        }

        _builder.Append("</p>\n");

        if (transclusion.Error is { Length: > 0 } error)
        {
            _builder.Append("<p class=\"diagnostic\">");
            _builder.Append(Escape(error));
            _builder.Append("</p>\n");
        }

        WriteAll(transclusion.Blocks);
        _builder.Append("</blockquote>\n");
    }

    private void WriteProperties(PropertiesRenderBlock properties)
    {
        _builder.Append("<aside class=\"properties\">\n<dl>\n");

        Row("Title", properties.Frontmatter.Title);
        Row("Type", properties.Frontmatter.Type);
        Row("Status", properties.Frontmatter.Status);
        Row("Created", properties.Frontmatter.Created?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Row("Updated", properties.Frontmatter.Updated?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Row("Tags", string.Join(", ", properties.Frontmatter.Tags));
        Row("Aliases", string.Join(", ", properties.Frontmatter.Aliases));
        Row("Authors", string.Join(", ", properties.Frontmatter.Authors));

        foreach ((string key, string value) in properties.Frontmatter.Extra)
        {
            Row(key, value);
        }

        if (properties.References.Count > 0)
        {
            _builder.Append("<dt>References</dt><dd>");

            for (int i = 0; i < properties.References.Count; i++)
            {
                if (i > 0)
                {
                    _builder.Append(", ");
                }

                WriteLink(properties.References[i], []);
            }

            _builder.Append("</dd>\n");
        }

        _builder.Append("</dl>\n</aside>\n");

        void Row(string key, string? value)
        {
            if (value is { Length: > 0 })
            {
                _builder.Append(CultureInfo.InvariantCulture, $"<dt>{Escape(key)}</dt><dd>{Escape(value)}</dd>\n");
            }
        }
    }

    private void Write(IEnumerable<RenderInline> inlines)
    {
        foreach (RenderInline inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    _builder.Append(Escape(text.Text));
                    break;

                case StyleRun style:
                    WriteStyled(style);
                    break;

                case CodeRun code:
                    _builder.Append("<code>");
                    _builder.Append(Escape(code.Code));
                    _builder.Append("</code>");
                    break;

                case BreakRun { IsHard: true }:
                    _builder.Append("<br />");
                    break;

                case BreakRun:
                    _builder.Append('\n');
                    break;

                case LinkRun link:
                    WriteLink(link.Resolution, link.Children, link.Url);
                    break;

                case ImageRun image:
                    WriteImage(image);
                    break;

                case MathRun math:
                    _builder.Append("<span class=\"math math-inline\">");
                    _builder.Append(Escape(math.Source));
                    _builder.Append("</span>");
                    break;

                case TagRun tag:
                    _builder.Append(CultureInfo.InvariantCulture, $"<span class=\"tag\">#{Escape(tag.Tag)}</span>");
                    break;

                case FootnoteReferenceRun footnote:
                    _builder.Append(CultureInfo.InvariantCulture,
                        $"<sup><a href=\"#fn-{Escape(footnote.Label)}\">{footnote.Order}</a></sup>");
                    break;

                default:
                    break;
            }
        }
    }

    private void WriteStyled(StyleRun style)
    {
        string[] tags = TagsFor(style.Style);

        foreach (string tag in tags)
        {
            _builder.Append(CultureInfo.InvariantCulture, $"<{tag}>");
        }

        Write(style.Children);

        for (int i = tags.Length - 1; i >= 0; i--)
        {
            _builder.Append(CultureInfo.InvariantCulture, $"</{tags[i]}>");
        }
    }

    private void WriteLink(LinkResolution resolution, IReadOnlyList<RenderInline> children, string? externalUrl = null)
    {
        string label = children.Count > 0
            ? null!
            : resolution.Link.Label ?? resolution.Link.RawTarget;

        string? href = externalUrl ?? _href(resolution);

        if (href is not { Length: > 0 })
        {
            // A link with no target in the export is written as text with the class the
            // stylesheet marks as broken. Silently dropping it would hide exactly what
            // this product exists to show.
            _builder.Append("<span class=\"broken-link\">");
            WriteLabel(children, label);
            _builder.Append("</span>");

            return;
        }

        _builder.Append(CultureInfo.InvariantCulture, $"<a href=\"{Escape(href)}\"");

        if (externalUrl is { Length: > 0 })
        {
            // Anything leaving the exported site opens without handing the opener a
            // window reference, and without leaking the page it came from.
            _builder.Append(" rel=\"noopener noreferrer\"");
        }
        else if (resolution.Confidence != ResolutionConfidence.Exact)
        {
            _builder.Append(CultureInfo.InvariantCulture, $" class=\"resolved-{Lower(resolution.Confidence.ToString())}\"");
            _builder.Append(CultureInfo.InvariantCulture, $" title=\"{Escape(resolution.Explain())}\"");
        }

        _builder.Append('>');
        WriteLabel(children, label);
        _builder.Append("</a>");
    }

    private void WriteLabel(IReadOnlyList<RenderInline> children, string? fallback)
    {
        if (children.Count > 0)
        {
            Write(children);
        }
        else
        {
            _builder.Append(Escape(fallback));
        }
    }

    private void WriteImage(ImageRun image)
    {
        string? href = _href(image.Resolution);
        string alternate = image.AlternateText ?? image.Resolution.Link.RawTarget;

        if (href is not { Length: > 0 })
        {
            _builder.Append(CultureInfo.InvariantCulture, $"<span class=\"broken-link\">{Escape(alternate)}</span>");
            return;
        }

        _builder.Append(CultureInfo.InvariantCulture, $"<img src=\"{Escape(href)}\" alt=\"{Escape(alternate)}\"");

        if (image.Width is { } width)
        {
            _builder.Append(CultureInfo.InvariantCulture, $" width=\"{(int)width}\"");
        }

        if (image.Height is { } height)
        {
            _builder.Append(CultureInfo.InvariantCulture, $" height=\"{(int)height}\"");
        }

        _builder.Append(" />");
    }

    private static string[] TagsFor(TextStyle style)
    {
        var tags = new List<string>(3);

        if (style.HasFlag(TextStyle.Bold)) { tags.Add("strong"); }
        if (style.HasFlag(TextStyle.Italic)) { tags.Add("em"); }
        if (style.HasFlag(TextStyle.Strikethrough)) { tags.Add("del"); }
        if (style.HasFlag(TextStyle.Highlight)) { tags.Add("mark"); }
        if (style.HasFlag(TextStyle.Superscript)) { tags.Add("sup"); }
        if (style.HasFlag(TextStyle.Subscript)) { tags.Add("sub"); }

        return [.. tags];
    }

    private static string AlignmentOf(ColumnAlignment alignment) => alignment switch
    {
        ColumnAlignment.Left => " style=\"text-align:left\"",
        ColumnAlignment.Center => " style=\"text-align:center\"",
        ColumnAlignment.Right => " style=\"text-align:right\"",
        _ => string.Empty,
    };

    private static string Lower(string value) => value.ToLowerInvariant();
}
