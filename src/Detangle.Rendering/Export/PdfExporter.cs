using System.Globalization;
using System.Text;
using Detangle.Core.Linking;
using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;
using SkiaSharp;
using Svg.Skia;

namespace Detangle.Rendering.Export;

/// <summary>
/// Writes a PDF of one page or a whole subtree (plan.md section 6.6).
/// <para>
/// The PDF is drawn rather than converted: Skia's PDF backend takes the same drawing
/// calls the screen does, so there is no browser, no headless Chromium and no PDF
/// library in the dependency list. Diagrams go in as vectors through the same SVG the
/// reader saw, links become real PDF annotations, and a link between two exported pages
/// becomes an internal jump.
/// </para>
/// <para>
/// What it does not do, deliberately: math is written as its source rather than
/// typeset — KaTeX is a browser thing and there is no browser here — and code is not
/// syntax coloured, because a printed page reads better without it. Both are reported in
/// the export's diagnostics rather than left for the reader to discover.
/// </para>
/// </summary>
public static class PdfExporter
{
    /// <summary>Writes documents into one PDF, in the order given.</summary>
    /// <param name="documents">The rendered pages to write.</param>
    /// <param name="options">Page geometry and title.</param>
    public static ExportReport Export(IEnumerable<RenderDocument> documents, PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);

        List<RenderDocument> pages = [.. documents];
        var diagnostics = new List<string>();

        var included = new HashSet<string>(
            pages.Select(p => p.Document.RelativePath), StringComparer.Ordinal);

        string? directory = Path.GetDirectoryName(options.OutputPath);

        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new SKFileWStream(options.OutputPath);
        using var pdf = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Title = options.Title,
            Producer = "Detangle",
            Creation = null,
        });

        using var typography = new PdfTypography();
        var writer = new PageWriter(pdf, options, typography, included, diagnostics);

        if (options.IncludeContents && pages.Count > 1)
        {
            writer.WriteContents(pages);
        }

        foreach (RenderDocument page in pages)
        {
            writer.WriteDocument(page);
        }

        writer.Finish();
        pdf.Close();

        return new ExportReport(pages.Count, 0, writer.Links, writer.BrokenLinks, diagnostics);
    }

    /// <summary>
    /// Lays out and draws pages one after another, starting a new sheet whenever the
    /// cursor runs off the bottom of the current one.
    /// </summary>
    private sealed class PageWriter(
        SKDocument pdf,
        PdfOptions options,
        PdfTypography typography,
        HashSet<string> included,
        List<string> diagnostics)
    {
        private SKCanvas? _canvas;
        private float _y;

        /// <summary>How many pages have been begun, so a block can tell it crossed one.</summary>
        private int _pages;

        /// <summary>
        /// True while the writer is working out how tall something is. The cursor advances
        /// as it would when drawing, but nothing is drawn and no page is turned, so a block
        /// can be measured before the decision to place it is made.
        /// </summary>
        private bool _measuring;

        /// <summary>The canvas to draw on, or null while measuring.</summary>
        private SKCanvas? Ink => _measuring ? null : _canvas;

        /// <summary>The space a paragraph opens with, shared so a list marker can match it.</summary>
        private const float ParagraphLead = 6f;
        private bool _mathReported;

        /// <summary>Vault links written into the PDF.</summary>
        public int Links { get; private set; }

        /// <summary>Links with nothing in the export to point at.</summary>
        public int BrokenLinks { get; private set; }

        /// <summary>Writes a contents page listing every document in the export.</summary>
        public void WriteContents(List<RenderDocument> pages)
        {
            Begin();
            Heading(options.Title, level: 1);

            foreach (RenderDocument page in pages)
            {
                Space(2);

                float top = _y;
                var runs = new List<Run>
                {
                    new(page.Document.DisplayName, Bold: false, Italic: false, Mono: false, options.FontSize),
                };

                DrawWrapped(runs, options.Margin, options.ContentWidth);

                // The entry jumps to the page's own destination, which is what makes a
                // contents page worth having rather than decorative.
                Link(
                    new SKRect(options.Margin, top - options.FontSize, options.PageWidth - options.Margin, _y),
                    DestinationOf(page.Document.RelativePath),
                    internalDestination: true);
            }

            End();
        }

        /// <summary>Writes one document, starting it on a fresh sheet.</summary>
        public void WriteDocument(RenderDocument document)
        {
            End();
            Begin();

            // A named destination is anchored at the top of the document's first page so
            // links between exported pages land on the page rather than near it.
            if (_measuring)
            {
                return;
            }

            _canvas!.DrawNamedDestinationAnnotation(
                new SKPoint(options.Margin, _y - options.FontSize),
                SKData.CreateCopy(Encoding.UTF8.GetBytes(DestinationOf(document.Document.RelativePath))));

            foreach (RenderBlock block in document.Blocks)
            {
                Write(block, options.Margin, options.ContentWidth);
            }

            foreach (string diagnostic in document.Diagnostics)
            {
                diagnostics.Add($"{document.Document.RelativePath}: {diagnostic}");
            }
        }

        /// <summary>Closes the last sheet.</summary>
        public void Finish() => End();

        private void Write(RenderBlock block, float left, float width)
        {
            switch (block)
            {
                case HeadingRenderBlock heading:
                    Space(heading.Level == 1 ? 14 : 10);
                    Heading(RenderModelBuilder.ToPlainText(heading.Inlines), heading.Level, left, width);
                    break;

                case ParagraphRenderBlock paragraph:
                    Space(ParagraphLead);
                    DrawWrapped(RunsOf(paragraph.Inlines, options.FontSize), left, width);
                    break;

                case DiagramRenderBlock diagram:
                    Space(10);
                    WriteDiagram(diagram, left, width);
                    break;

                case CodeRenderBlock code:
                    Space(8);
                    WriteFixedWidth(code.Source, left, width);
                    break;

                case MathRenderBlock math:
                    Space(8);
                    ReportMath();
                    WriteFixedWidth(math.Source, left, width);
                    break;

                case QuoteRenderBlock quote:
                    Space(6);
                    WriteIndented(quote.Blocks, left, width, rule: true);
                    break;

                case CalloutRenderBlock callout:
                    Space(8);
                    DrawWrapped(
                        [new Run(callout.Title, Bold: true, Italic: false, Mono: false, options.FontSize)],
                        left + 12,
                        width - 12);
                    WriteIndented(callout.Blocks, left, width, rule: true);
                    break;

                case ListRenderBlock list:
                    Space(6);
                    WriteList(list, left, width);
                    break;

                case ListItemRenderBlock item:
                    WriteBlocks(item.Blocks, left, width);
                    break;

                case TableRenderBlock table:
                    Space(8);
                    WriteTable(table, left, width);
                    break;

                case ThematicBreakRenderBlock:
                    Space(10);
                    Rule(left, width);
                    break;

                case DefinitionListRenderBlock definitions:
                    WriteDefinitions(definitions, left, width);
                    break;

                case FootnotesRenderBlock footnotes:
                    Space(12);
                    Rule(left, width);
                    WriteFootnotes(footnotes, left, width);
                    break;

                case TransclusionRenderBlock transclusion:
                    Space(8);
                    DrawWrapped(
                        [new Run(
                            transclusion.Resolution.Target?.DisplayName ?? transclusion.Resolution.Link.RawTarget,
                            Bold: false, Italic: true, Mono: false, options.FontSize - 1)],
                        left + 12,
                        width - 12);
                    WriteIndented(transclusion.Blocks, left, width, rule: true);
                    break;

                case PropertiesRenderBlock properties:
                    WriteProperties(properties, left, width);
                    break;

                default:
                    break;
            }
        }

        private void WriteBlocks(IEnumerable<RenderBlock> blocks, float left, float width)
        {
            foreach (RenderBlock block in blocks)
            {
                Write(block, left, width);
            }
        }

        private void WriteIndented(IReadOnlyList<RenderBlock> blocks, float left, float width, bool rule)
        {
            int startPage = _pages;
            float top = _y;

            WriteBlocks(blocks, left + 14, width - 14);

            if (!rule || _canvas is null)
            {
                return;
            }

            // A callout that runs past a page break starts again at the top of the page it
            // finishes on. Measuring from where it began would draw the rule from a
            // coordinate on the previous page, which came out as a bar down the full height
            // of a nearly empty page.
            float start = _pages == startPage
                ? top - options.FontSize
                : options.Margin;

            if (_y - 2 <= start)
            {
                return;
            }

            using var paint = new SKPaint { Color = new SKColor(0xB0, 0xB6, 0xBE), StrokeWidth = 2 };

            Ink?.DrawLine(left + 3, start, left + 3, _y - 2, paint);
        }

        private void WriteList(ListRenderBlock list, float left, float width)
        {
            int number = list.Start;

            foreach (ListItemRenderBlock item in list.Items)
            {
                string marker = list.IsOrdered
                    ? $"{number++}."
                    : item.Task switch
                    {
                        TaskState.Checked => "[x]",
                        TaskState.Unchecked => "[ ]",
                        _ => "•",
                    };

                // Claim room for the first line before anything is drawn. That settles which
                // page the item starts on, so the marker and the line it belongs to cannot
                // end up on different pages - and it means the marker can be drawn on that
                // line's own baseline rather than guessed at from where the item began.
                EnsureRoom(options.FontSize * 1.45f);

                float top = _y;

                using (var font = new SKFont(typography.Regular, options.FontSize))
                using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
                {
                    // The item's first block opens with the same leading space every
                    // paragraph gets, so the marker has to clear it too or it rides above
                    // the words it belongs to. Space() is skipped at the top of a page,
                    // and so is this.
                    float lead = top > options.Margin ? ParagraphLead : 0f;

                    Ink?.DrawText(marker, left + 2, top + lead + options.FontSize, SKTextAlign.Left, font, paint);
                }

                WriteBlocks(item.Blocks, left + 18, width - 18);
            }
        }

        private void WriteTable(TableRenderBlock table, float left, float width)
        {
            int columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Cells.Sum(c => c.ColumnSpan));

            if (columns == 0)
            {
                return;
            }

            float columnWidth = width / columns;

            foreach (TableRowRenderBlock row in table.Rows)
            {
                // Keep the row whole. Cells are drawn by resetting the cursor to the row's
                // top for each one, which is only meaningful while they share a page - a
                // break inside a cell left the following cells writing against a coordinate
                // from the page before, and the rest of the table went off the paper.
                EnsureRoom(MeasureRow(row, left, columnWidth));

                float top = _y;
                float lowest = _y;
                float x = left;

                foreach (TableCellRenderBlock cell in row.Cells)
                {
                    float span = columnWidth * cell.ColumnSpan;

                    _y = top;

                    foreach (RenderBlock block in cell.Blocks)
                    {
                        if (block is ParagraphRenderBlock paragraph)
                        {
                            DrawWrapped(
                                RunsOf(paragraph.Inlines, options.FontSize - 0.5f, bold: row.IsHeader),
                                x + 3,
                                span - 6);
                        }
                        else
                        {
                            Write(block, x + 3, span - 6);
                        }
                    }

                    lowest = Math.Max(lowest, _y);
                    x += span;
                }

                _y = lowest + 3;

                Rule(left, width);
            }
        }

        /// <summary>How tall a row will be, worked out by writing it with the ink off.</summary>
        private float MeasureRow(TableRowRenderBlock row, float left, float columnWidth)
        {
            float start = _y;
            bool wasMeasuring = _measuring;

            _measuring = true;

            try
            {
                float lowest = _y;
                float x = left;

                foreach (TableCellRenderBlock cell in row.Cells)
                {
                    float span = columnWidth * cell.ColumnSpan;

                    _y = start;

                    foreach (RenderBlock block in cell.Blocks)
                    {
                        if (block is ParagraphRenderBlock paragraph)
                        {
                            DrawWrapped(
                                RunsOf(paragraph.Inlines, options.FontSize - 0.5f, bold: row.IsHeader),
                                x + 3,
                                span - 6);
                        }
                        else
                        {
                            Write(block, x + 3, span - 6);
                        }
                    }

                    lowest = Math.Max(lowest, _y);
                    x += span;
                }

                return lowest - start + 6;
            }
            finally
            {
                _measuring = wasMeasuring;
                _y = start;
            }
        }

        private void WriteDefinitions(DefinitionListRenderBlock definitions, float left, float width)
        {
            foreach (DefinitionRenderBlock definition in definitions.Items)
            {
                Space(6);
                DrawWrapped(RunsOf(definition.Term, options.FontSize, bold: true), left, width);

                foreach (IReadOnlyList<RenderBlock> blocks in definition.Definitions)
                {
                    WriteBlocks(blocks, left + 16, width - 16);
                }
            }
        }

        private void WriteFootnotes(FootnotesRenderBlock footnotes, float left, float width)
        {
            int order = 1;

            foreach ((string _, IReadOnlyList<RenderBlock> blocks) in footnotes.Notes)
            {
                Space(4);
                DrawWrapped(
                    [new Run($"{order++}.", Bold: true, Italic: false, Mono: false, options.FontSize - 1)],
                    left,
                    width);

                WriteBlocks(blocks, left + 16, width - 16);
            }
        }

        private void WriteProperties(PropertiesRenderBlock properties, float left, float width)
        {
            var rows = new List<(string Key, string Value)>();

            Add("Type", properties.Frontmatter.Type);
            Add("Status", properties.Frontmatter.Status);
            Add("Updated", properties.Frontmatter.Updated?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add("Tags", string.Join(", ", properties.Frontmatter.Tags));
            Add("Aliases", string.Join(", ", properties.Frontmatter.Aliases));

            if (rows.Count == 0)
            {
                return;
            }

            Space(4);

            foreach ((string key, string value) in rows)
            {
                DrawWrapped(
                    [
                        new Run($"{key}  ", Bold: true, Italic: false, Mono: false, options.FontSize - 1),
                        new Run(value, Bold: false, Italic: false, Mono: false, options.FontSize - 1),
                    ],
                    left,
                    width);
            }

            Space(4);
            Rule(left, width);

            void Add(string key, string? value)
            {
                if (value is { Length: > 0 })
                {
                    rows.Add((key, value));
                }
            }
        }

        private void WriteDiagram(DiagramRenderBlock diagram, float left, float width)
        {
            if (!diagram.IsRendered)
            {
                WriteFixedWidth(diagram.Source, left, width);
                return;
            }

            SKPicture? picture = null;

            try
            {
                using var svg = new SKSvg();

                // The same lookup the reader draws diagrams through. Without it the labels
                // came out as a smudge wherever the platform's own font matching fails,
                // which is every label on every diagram under WebAssembly.
                DiagramTypefaces.Install(svg.Settings);

                picture = svg.FromSvg(
                    SvgTextCapability.CanDrawText
                        ? diagram.Svg
                        : SvgStyleFlattener.RemoveFontFamilies(diagram.Svg));

                if (picture is null)
                {
                    WriteFixedWidth(diagram.Source, left, width);
                    return;
                }

                float scale = Math.Min(1f, width / Math.Max(1f, picture.CullRect.Width));
                float height = picture.CullRect.Height * scale;

                // A diagram taller than a page is scaled to fit rather than cut in half.
                float available = options.PageHeight - options.Margin - options.Margin;

                if (height > available)
                {
                    scale *= available / height;
                    height = available;
                }

                EnsureRoom(height);

                if (_measuring)
                {
                    _y += height;
                    Space(6);

                    return;
                }

                _canvas!.Save();
                _canvas.Translate(left, _y);
                _canvas.Scale(scale);
                _canvas.DrawPicture(picture);
                _canvas.Restore();

                _y += height + 6;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                diagnostics.Add($"diagram: {ex.Message}");
                WriteFixedWidth(diagram.Source, left, width);
            }
            finally
            {
                picture?.Dispose();
            }
        }

        /// <summary>Code, math and unrendered diagrams: one line per line, never wrapped.</summary>
        private void WriteFixedWidth(string source, float left, float width)
        {
            using var font = new SKFont(typography.Mono, options.FontSize - 1);
            using var paint = new SKPaint { Color = new SKColor(0x24, 0x29, 0x2F), IsAntialias = true };

            float lineHeight = font.Size * 1.4f;

            foreach (string line in source.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
            {
                EnsureRoom(lineHeight);

                Ink?.DrawText(Truncate(line, font, width), left + 4, _y + font.Size, SKTextAlign.Left, font, paint);
                _y += lineHeight;
            }

            _y += 4;
        }

        private void Heading(string text, int level, float? left = null, float? width = null)
        {
            float size = options.FontSize * level switch
            {
                1 => 1.9f,
                2 => 1.5f,
                3 => 1.25f,
                4 => 1.12f,
                _ => 1f,
            };

            DrawWrapped(
                [new Run(text, Bold: true, Italic: false, Mono: false, size)],
                left ?? options.Margin,
                width ?? options.ContentWidth);

            _y += 2;
        }

        /// <summary>
        /// Greedy word wrapping over styled runs. Each word is measured in its own face,
        /// so a bold word inside a sentence takes the width it will actually draw at.
        /// </summary>
        private void DrawWrapped(IReadOnlyList<Run> runs, float left, float width)
        {
            var line = new List<Placed>();
            float x = 0;
            float height = 0;

            foreach (Run run in runs)
            {
                using var font = new SKFont(typography.For(run.Bold, run.Italic, run.Mono), run.Size);

                foreach (string word in Words(run.Text))
                {
                    float advance = font.MeasureText(word);

                    if (x + advance > width && line.Count > 0)
                    {
                        Flush();
                    }

                    if (word.Length == 1 && word[0] == ' ' && line.Count == 0)
                    {
                        continue;
                    }

                    line.Add(new Placed(word, x, run, advance));
                    x += advance;
                    height = Math.Max(height, run.Size * 1.45f);
                }
            }

            Flush();

            void Flush()
            {
                if (line.Count == 0)
                {
                    return;
                }

                EnsureRoom(height);

                foreach (Placed placed in line)
                {
                    using var font = new SKFont(
                        typography.For(placed.Run.Bold, placed.Run.Italic, placed.Run.Mono), placed.Run.Size);

                    using var paint = new SKPaint { Color = placed.Run.Color, IsAntialias = true };

                    float baseline = _y + placed.Run.Size;

                    Ink?.DrawText(placed.Text, left + placed.X, baseline, SKTextAlign.Left, font, paint);

                    if (placed.Run.Strikethrough)
                    {
                        using var strike = new SKPaint { Color = placed.Run.Color, StrokeWidth = 0.7f };

                        Ink?.DrawLine(
                            left + placed.X,
                            baseline - (placed.Run.Size * 0.3f),
                            left + placed.X + placed.Width,
                            baseline - (placed.Run.Size * 0.3f),
                            strike);
                    }

                    if (placed.Run.Href is { Length: > 0 } href)
                    {
                        Link(
                            new SKRect(left + placed.X, _y, left + placed.X + placed.Width, baseline + 2),
                            href,
                            placed.Run.IsInternal);
                    }
                }

                _y += height;
                x = 0;
                height = 0;
                line.Clear();
            }
        }

        /// <summary>Records a link annotation on the current page.</summary>
        private void Link(SKRect rect, string target, bool internalDestination)
        {
            if (_measuring || _canvas is null || target.Length == 0)
            {
                return;
            }

            using SKData data = SKData.CreateCopy(Encoding.UTF8.GetBytes(target));

            if (internalDestination)
            {
                _canvas.DrawLinkDestinationAnnotation(rect, data);
            }
            else
            {
                _canvas.DrawUrlAnnotation(rect, data);
            }
        }

        private void Rule(float left, float width)
        {
            EnsureRoom(6);

            using var paint = new SKPaint { Color = new SKColor(0xD0, 0xD5, 0xDC), StrokeWidth = 0.6f };

            Ink?.DrawLine(left, _y, left + width, _y, paint);
            _y += 6;
        }

        private void Space(float points)
        {
            if (_y > options.Margin)
            {
                _y += points;
            }
        }

        private void EnsureRoom(float height)
        {
            if (_measuring)
            {
                return;
            }

            if (_canvas is null)
            {
                Begin();
            }

            if (_y + height <= options.PageHeight - options.Margin)
            {
                return;
            }

            End();
            Begin();
        }

        private void Begin()
        {
            if (_canvas is null)
            {
                _canvas = pdf.BeginPage(options.PageWidth, options.PageHeight);
                _pages++;
            }

            _y = options.Margin;
        }

        private void End()
        {
            if (_canvas is null)
            {
                return;
            }

            pdf.EndPage();
            _canvas = null;
        }

        private void ReportMath()
        {
            if (_mathReported)
            {
                return;
            }

            _mathReported = true;
            diagnostics.Add("Math is written as its TeX source: typesetting it needs a browser.");
        }

        /// <summary>Splits text into words and the spaces between them, both of which wrap.</summary>
        private static IEnumerable<string> Words(string text)
        {
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    continue;
                }

                if (i > start)
                {
                    yield return text[start..i];
                }

                yield return " ";
                start = i + 1;
            }

            if (start < text.Length)
            {
                yield return text[start..];
            }
        }

        private static string Truncate(string line, SKFont font, float width)
        {
            if (font.MeasureText(line) <= width)
            {
                return line;
            }

            int length = line.Length;

            while (length > 1 && font.MeasureText(string.Concat(line.AsSpan(0, length), "…")) > width)
            {
                length--;
            }

            return string.Concat(line.AsSpan(0, length), "…");
        }

        private static string DestinationOf(string relativePath) => "detangle:" + relativePath;

        /// <summary>Flattens inlines into styled runs, resolving links to PDF targets.</summary>
        private List<Run> RunsOf(IEnumerable<RenderInline> inlines, float size, bool bold = false)
        {
            var runs = new List<Run>();

            Walk(inlines, bold, italic: false, mono: false, strike: false, href: null, isInternal: false);

            return runs;

            void Walk(
                IEnumerable<RenderInline> nodes,
                bool isBold,
                bool italic,
                bool mono,
                bool strike,
                string? href,
                bool isInternal)
            {
                foreach (RenderInline node in nodes)
                {
                    switch (node)
                    {
                        case TextRun text:
                            runs.Add(new Run(text.Text, isBold, italic, mono, size)
                            {
                                Strikethrough = strike,
                                Href = href,
                                IsInternal = isInternal,
                                Color = href is null ? SKColors.Black : LinkColor,
                            });
                            break;

                        case CodeRun code:
                            runs.Add(new Run(code.Code, isBold, italic, Mono: true, size - 0.5f)
                            {
                                Href = href,
                                IsInternal = isInternal,
                            });
                            break;

                        case StyleRun style:
                            Walk(
                                style.Children,
                                isBold || style.Style.HasFlag(TextStyle.Bold),
                                italic || style.Style.HasFlag(TextStyle.Italic),
                                mono,
                                strike || style.Style.HasFlag(TextStyle.Strikethrough),
                                href,
                                isInternal);
                            break;

                        case LinkRun link:
                            (string? target, bool internalTarget) = TargetOf(link);

                            Walk(
                                link.Children.Count > 0
                                    ? link.Children
                                    : [new TextRun(link.Resolution.Link.Label ?? link.Resolution.Link.RawTarget)],
                                isBold,
                                italic,
                                mono,
                                strike,
                                target,
                                internalTarget);
                            break;

                        case ImageRun image:
                            runs.Add(new Run($"[{image.AlternateText ?? image.Resolution.Link.RawTarget}]",
                                isBold, Italic: true, mono, size));
                            break;

                        case MathRun math:
                            ReportMath();
                            runs.Add(new Run(math.Source, isBold, italic, Mono: true, size - 0.5f));
                            break;

                        case TagRun tag:
                            runs.Add(new Run($"#{tag.Tag}", isBold, italic, mono, size)
                            {
                                Color = MutedColor,
                            });
                            break;

                        case FootnoteReferenceRun footnote:
                            runs.Add(new Run($"[{footnote.Order}]", isBold, italic, mono, size * 0.8f));
                            break;

                        case BreakRun:
                            runs.Add(new Run(" ", isBold, italic, mono, size));
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        private (string? Target, bool Internal) TargetOf(LinkRun link)
        {
            if (link.Url is { Length: > 0 } url)
            {
                return (url, false);
            }

            Links++;

            if (link.Resolution.Target is { } target && included.Contains(target.RelativePath))
            {
                return (DestinationOf(target.RelativePath), true);
            }

            BrokenLinks++;

            return (null, false);
        }

        private static readonly SKColor LinkColor = new(0x1F, 0x6F, 0xEB);

        private static readonly SKColor MutedColor = new(0x5B, 0x64, 0x70);

        /// <summary>One stretch of text sharing a face, a size and a link target.</summary>
        private sealed record Run(string Text, bool Bold, bool Italic, bool Mono, float Size)
        {
            public bool Strikethrough { get; init; }

            public string? Href { get; init; }

            public bool IsInternal { get; init; }

            public SKColor Color { get; init; } = SKColors.Black;
        }

        /// <summary>A run's word, placed on the line being built.</summary>
        private sealed record Placed(string Text, float X, Run Run, float Width);
    }
}
