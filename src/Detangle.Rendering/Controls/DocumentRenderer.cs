using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Detangle.Core.Dbml;
using Detangle.Core.Linking;
using Detangle.Core.Parsing;
using Detangle.Rendering.Highlighting;
using Detangle.Rendering.Model;

namespace Detangle.Rendering.Controls;

/// <summary>Raised when the reader activates a link.</summary>
/// <param name="Resolution">The chain's answer for the link that was clicked.</param>
/// <param name="ExternalUrl">The href, when the link pointed outside the vault.</param>
public sealed record LinkActivatedEventArgs(LinkResolution Resolution, string? ExternalUrl);

/// <summary>
/// Turns a <see cref="RenderDocument"/> into an Avalonia control tree.
/// <para>
/// This class makes no editorial decisions — every question of meaning was settled in
/// the render model. What is left is presentation, and the one presentational rule worth
/// stating is the link decoration from plan.md section 5.5: a link's appearance is driven
/// by the rule that resolved it, so a reader can see at a glance which links were written
/// correctly and which ones Detangle had to work for.
/// </para>
/// </summary>
public sealed class DocumentRenderer
{
    private readonly DocumentTheme _theme;
    private readonly CodeHighlighter _highlighter;
    private readonly IImageLoader _images;

    /// <summary>Creates a renderer.</summary>
    /// <param name="theme">Colours and metrics.</param>
    /// <param name="images">How attachments are loaded; defaults to the filesystem.</param>
    public DocumentRenderer(DocumentTheme? theme = null, IImageLoader? images = null)
    {
        _theme = theme ?? DocumentTheme.Light;
        _highlighter = new CodeHighlighter(_theme.Highlighting);
        _images = images ?? FileImageLoader.Instance;
    }

    /// <summary>Raised when a link in a rendered document is clicked.</summary>
    public event EventHandler<LinkActivatedEventArgs>? LinkActivated;

    /// <summary>
    /// Builds the hover preview for a link, when one is available. The shell supplies
    /// this because previewing a page means rendering it, which needs the vault the
    /// renderer itself knows nothing about.
    /// </summary>
    public Func<LinkResolution, Control?>? PreviewFactory { get; set; }

    /// <summary>Renders a document into a scrollable panel of blocks.</summary>
    public Control Render(RenderDocument document)
    {
        var panel = new StackPanel { Spacing = 12 };

        foreach (RenderBlock block in document.Blocks)
        {
            panel.Children.Add(RenderBlockControl(block));
        }

        foreach (string diagnostic in document.Diagnostics)
        {
            panel.Children.Add(Caption(diagnostic));
        }

        return panel;
    }

    private Control RenderBlockControl(RenderBlock block) => block switch
    {
        ParagraphRenderBlock paragraph => Paragraph(paragraph),
        HeadingRenderBlock heading => Heading(heading),
        CodeRenderBlock code => Code(code),
        DiagramRenderBlock diagram => Diagram(diagram),
        QuoteRenderBlock quote => Quote(quote),
        CalloutRenderBlock callout => Callout(callout),
        ListRenderBlock list => List(list),
        TableRenderBlock table => TableControl(table),
        ThematicBreakRenderBlock => new Border
        {
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 8),
        },
        MathRenderBlock math => Math(math),
        DefinitionListRenderBlock definitions => Stack(definitions.Items),
        DefinitionRenderBlock definition => Definition(definition),
        FootnotesRenderBlock footnotes => Footnotes(footnotes),
        TransclusionRenderBlock transclusion => Transclusion(transclusion),
        PropertiesRenderBlock properties => Properties(properties),
        ListItemRenderBlock item => Stack(item.Blocks),
        TableCellRenderBlock cell => Stack(cell.Blocks),
        _ => new Control(),
    };

    private StackPanel Stack(IEnumerable<RenderBlock> blocks)
    {
        var panel = new StackPanel { Spacing = 8 };

        foreach (RenderBlock block in blocks)
        {
            panel.Children.Add(RenderBlockControl(block));
        }

        return panel;
    }

    private Control Paragraph(ParagraphRenderBlock paragraph) => TextBlockFor(paragraph.Inlines);

    private Control Heading(HeadingRenderBlock heading)
    {
        SelectableTextBlock text = TextBlockFor(heading.Inlines);

        text.FontSize = _theme.HeadingSizeFor(heading.Level);
        text.FontWeight = heading.Level <= 3 ? FontWeight.SemiBold : FontWeight.Medium;
        text.Margin = new Thickness(0, heading.Level == 1 ? 4 : 12, 0, 0);

        // The slug is what an anchor link scrolls to, so it travels on the control.
        text.Tag = heading.Slug;

        return heading.Level <= 2
            ? new Border
            {
                Child = text,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6),
            }
            : text;
    }

    private Control Code(CodeRenderBlock code)
    {
        var lines = new StackPanel();

        foreach (IReadOnlyList<HighlightSpan> line in _highlighter.Highlight(code.Language, code.Source))
        {
            var text = new SelectableTextBlock
            {
                FontFamily = _theme.CodeFontFamily,
                FontSize = _theme.FontSize * 0.92,
                Foreground = _theme.Foreground,
                TextWrapping = TextWrapping.NoWrap,
            };

            foreach (HighlightSpan span in line)
            {
                text.Inlines?.Add(new Run(span.Text)
                {
                    Foreground = span.Foreground is null ? _theme.Foreground : Brush.Parse(span.Foreground),
                    FontWeight = span.IsBold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = span.IsItalic ? FontStyle.Italic : FontStyle.Normal,
                });
            }

            lines.Children.Add(text);
        }

        var body = new ScrollViewer
        {
            Content = lines,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var panel = new StackPanel { Spacing = 4 };

        if (code.Language.Length > 0)
        {
            // A diagram fence is labelled as such: until phase 3 renders it, the reader
            // should know this block is a picture Detangle has not drawn yet.
            panel.Children.Add(Caption(code.IsDiagram ? $"{code.Language} diagram" : code.Language));
        }

        panel.Children.Add(body);

        return new Border
        {
            Background = _theme.CodeBackground,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = panel,
        };
    }

    /// <summary>
    /// Draws a rendered diagram, or — when rendering failed — the source and the reason,
    /// which is the one thing a reader can act on. A DBML fence also gets the detail panel
    /// beside it carrying everything an erDiagram cannot express (plan.md section 4.2).
    /// </summary>
    private Control Diagram(DiagramRenderBlock diagram)
    {
        var panel = new StackPanel { Spacing = 8 };

        if (diagram.IsRendered)
        {
            panel.Children.Add(SvgControl(diagram));
        }
        else
        {
            panel.Children.Add(Caption($"{diagram.Kind.ToString().ToLowerInvariant()} diagram"));
            panel.Children.Add(SourceLines(diagram.Source));
        }

        foreach (string diagnostic in diagram.Diagnostics)
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = diagnostic,
                FontSize = _theme.FontSize * 0.85,
                Foreground = diagram.IsRendered ? _theme.Muted : _theme.UnresolvedLink,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (diagram.Schema is { } schema && !schema.IsEmpty)
        {
            panel.Children.Add(SchemaDetailPanel(schema));
        }

        return new Border
        {
            Background = _theme.SurfaceBackground,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12),
            Child = panel,
        };
    }

    /// <summary>
    /// Lays out source text one line per text block rather than as one wrapped block.
    /// <para>
    /// Wrapping multi-line source turns a fence into a wall of reflowed text, and it also
    /// steps around a measure pass that fails to settle for certain multi-line strings
    /// inside a bordered stack — a diagram Detangle could not draw must never be able to
    /// hang the page it is on. Fenced code has always been drawn this way; diagram source
    /// now matches it.
    /// </para>
    /// </summary>
    private Control SourceLines(string source)
    {
        var lines = new StackPanel();

        foreach (string line in SplitLines(source))
        {
            lines.Children.Add(new SelectableTextBlock
            {
                Text = line,
                FontFamily = _theme.CodeFontFamily,
                FontSize = _theme.FontSize * 0.9,
                Foreground = _theme.Muted,
                TextWrapping = TextWrapping.NoWrap,
            });
        }

        return new ScrollViewer
        {
            Content = lines,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>Splits text into lines, tolerating either line ending.</summary>
    private static string[] SplitLines(string text) =>
        text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private Control SvgControl(DiagramRenderBlock diagram)
    {
        try
        {
            var image = new Image
            {
                Source = new SvgImage { Source = SvgSource.LoadFromSvg(diagram.Svg) },
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            if (diagram.Width > 0 && diagram.Height > 0)
            {
                image.MaxWidth = diagram.Width;
                image.MaxHeight = diagram.Height;
            }

            return image;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // A backend that produced SVG the display cannot parse is still a failed
            // render from the reader's point of view.
            return Caption($"This diagram could not be displayed: {ex.Message}");
        }
    }

    /// <summary>
    /// The table-detail panel: enums, groups, indexes, defaults and notes, none of which
    /// survive the trip through Mermaid's erDiagram.
    /// </summary>
    private Control SchemaDetailPanel(DbmlSchema schema)
    {
        var panel = new StackPanel { Spacing = 6 };

        foreach (DbmlTable table in schema.Tables)
        {
            var rows = new StackPanel { Spacing = 2 };

            foreach (DbmlColumn column in table.Columns)
            {
                var flags = new List<string>();

                if (column.IsPrimaryKey)
                {
                    flags.Add("pk");
                }

                if (column.IsUnique)
                {
                    flags.Add("unique");
                }

                if (column.IsNotNull)
                {
                    flags.Add("not null");
                }

                if (column.Default is { Length: > 0 } value)
                {
                    flags.Add($"default {value}");
                }

                rows.Children.Add(Caption(
                    $"{column.Name} · {column.Type}{(flags.Count > 0 ? " · " + string.Join(", ", flags) : string.Empty)}"));
            }

            foreach (DbmlIndex index in table.Indexes)
            {
                rows.Children.Add(Caption(
                    $"index ({string.Join(", ", index.Columns)}){(index.IsUnique ? " unique" : string.Empty)}"));
            }

            if (table.Note is { Length: > 0 } note)
            {
                rows.Children.Add(Caption(note));
            }

            panel.Children.Add(Disclosure(table.QualifiedName, rows, startsOpen: true));
        }

        foreach (DbmlEnum enumeration in schema.Enums)
        {
            panel.Children.Add(Caption(
                $"enum {enumeration.Name}: {string.Join(", ", enumeration.Values.Select(v => v.Name))}"));
        }

        foreach (DbmlTableGroup group in schema.TableGroups)
        {
            panel.Children.Add(Caption($"group {group.Name}: {string.Join(", ", group.Tables)}"));
        }

        foreach (DbmlStickyNote note in schema.Notes)
        {
            panel.Children.Add(Caption($"{note.Name}: {note.Text}"));
        }

        return new Border
        {
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = panel,
        };
    }

    private Control Quote(QuoteRenderBlock quote) => new Border
    {
        BorderBrush = _theme.Border,
        BorderThickness = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(14, 2, 0, 2),
        Child = Stack(quote.Blocks),
    };

    private Control Callout(CalloutRenderBlock callout)
    {
        IBrush accent = _theme.AccentFor(callout.Kind);

        var header = new SelectableTextBlock
        {
            Text = callout.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = accent,
            FontSize = _theme.FontSize,
        };

        StackPanel body = Stack(callout.Blocks);

        Control content = callout.IsCollapsible
            ? Disclosure(callout.Title, body, startsOpen: !callout.StartsCollapsed)
            : new StackPanel { Spacing = 8, Children = { header, body } };

        return new Border
        {
            Background = _theme.SurfaceBackground,
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(14, 10),
            Child = content,
        };
    }

    private Control List(ListRenderBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };
        int number = list.Start;

        foreach (ListItemRenderBlock item in list.Items)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(4, 0, 0, 0),
            };

            Control marker = item.Task switch
            {
                TaskState.None => new SelectableTextBlock
                {
                    Text = list.IsOrdered ? $"{number}." : "•",
                    Foreground = _theme.Muted,
                    Margin = new Thickness(0, 0, 8, 0),
                    MinWidth = list.IsOrdered ? 22 : 14,
                },
                _ => new CheckBox
                {
                    IsChecked = item.Task == TaskState.Checked,
                    // Task boxes reflect the file; editing them is phase 7's business.
                    IsHitTestVisible = false,
                    Margin = new Thickness(0, 0, 6, 0),
                    MinWidth = 22,
                },
            };

            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            StackPanel content = Stack(item.Blocks);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);

            panel.Children.Add(row);
            number++;
        }

        return panel;
    }

    private Control TableControl(TableRenderBlock table)
    {
        var grid = new Grid();

        for (int column = 0; column < System.Math.Max(1, table.Alignments.Count); column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TableRowRenderBlock row = table.Rows[rowIndex];

            for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                TableCellRenderBlock cell = row.Cells[cellIndex];

                var border = new Border
                {
                    BorderBrush = _theme.Border,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = row.IsHeader ? _theme.SurfaceBackground : null,
                    Padding = new Thickness(10, 6),
                    Child = Stack(cell.Blocks),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                if (cellIndex < table.Alignments.Count)
                {
                    border.HorizontalAlignment = table.Alignments[cellIndex] switch
                    {
                        ColumnAlignment.Center => HorizontalAlignment.Center,
                        ColumnAlignment.Right => HorizontalAlignment.Right,
                        _ => HorizontalAlignment.Stretch,
                    };
                }

                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, cellIndex);
                Grid.SetColumnSpan(border, System.Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(border);
            }
        }

        return new Border
        {
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1, 1, 0, 0),
            CornerRadius = new CornerRadius(4),
            Child = new ScrollViewer
            {
                Content = grid,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    /// <summary>
    /// Renders block math as its TeX source in a bordered card. KaTeX layout needs a
    /// JavaScript host, which arrives with phase 3's WebView backend; showing the source
    /// verbatim until then is honest, and it keeps the equation selectable and copyable.
    /// </summary>
    private Control Math(MathRenderBlock math) => new Border
    {
        Background = _theme.CodeBackground,
        BorderBrush = _theme.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(14, 10),
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Caption("math"),
                new SelectableTextBlock
                {
                    Text = math.Source.Trim(),
                    FontFamily = _theme.CodeFontFamily,
                    FontSize = _theme.FontSize,
                    Foreground = _theme.Foreground,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        },
    };

    private Control Definition(DefinitionRenderBlock definition)
    {
        var panel = new StackPanel { Spacing = 4 };

        SelectableTextBlock term = TextBlockFor(definition.Term);
        term.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(term);

        foreach (IReadOnlyList<RenderBlock> blocks in definition.Definitions)
        {
            StackPanel body = Stack(blocks);
            body.Margin = new Thickness(18, 0, 0, 0);
            panel.Children.Add(body);
        }

        return panel;
    }

    private Control Footnotes(FootnotesRenderBlock footnotes)
    {
        var panel = new StackPanel { Spacing = 6 };

        foreach ((string label, IReadOnlyList<RenderBlock> blocks) in footnotes.Notes)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            var marker = new SelectableTextBlock
            {
                Text = label,
                Foreground = _theme.Muted,
                FontSize = _theme.FontSize * 0.85,
                Margin = new Thickness(0, 0, 8, 0),
            };

            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            StackPanel content = Stack(blocks);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);

            panel.Children.Add(row);
        }

        return new Border
        {
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Margin = new Thickness(0, 16, 0, 0),
            Child = panel,
        };
    }

    private Control Transclusion(TransclusionRenderBlock transclusion)
    {
        var panel = new StackPanel { Spacing = 8 };

        // The source chip is what separates borrowed text from the page's own words.
        panel.Children.Add(Caption(
            transclusion.Resolution.Target?.RelativePath ?? transclusion.Resolution.Link.RawTarget));

        if (transclusion.Error is { Length: > 0 } error)
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = error,
                Foreground = _theme.UnresolvedLink,
                FontSize = _theme.FontSize * 0.9,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            panel.Children.Add(Stack(transclusion.Blocks));
        }

        return new Border
        {
            Background = _theme.SurfaceBackground,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Child = panel,
        };
    }

    private Control Properties(PropertiesRenderBlock properties)
    {
        DocumentFrontmatter frontmatter = properties.Frontmatter;
        var rows = new StackPanel { Spacing = 4 };

        void AddRow(string key, IEnumerable<Control> values)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };

            var label = new SelectableTextBlock
            {
                Text = key,
                Foreground = _theme.Muted,
                FontSize = _theme.FontSize * 0.9,
            };

            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var value = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (Control control in values)
            {
                value.Children.Add(control);
            }

            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            rows.Children.Add(row);
        }

        void AddText(string key, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                AddRow(key, [PlainText(text)]);
            }
        }

        AddText("title", frontmatter.Title);
        AddText("type", frontmatter.Type);
        AddText("status", frontmatter.Status);
        AddText("id", frontmatter.Id);

        if (frontmatter.Tags.Count > 0)
        {
            AddRow("tags", frontmatter.Tags.Select(tag => Chip($"#{tag}")));
        }

        if (frontmatter.Aliases.Count > 0)
        {
            AddRow("aliases", frontmatter.Aliases.Select(Chip));
        }

        if (properties.References.Count > 0)
        {
            AddRow("references", properties.References.Select(reference =>
                LinkControl(reference, reference.Target?.DisplayName ?? reference.Link.RawTarget, null)));
        }

        AddText("created", frontmatter.Created?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        AddText("updated", frontmatter.Updated?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        foreach (KeyValuePair<string, string> extra in frontmatter.Extra)
        {
            AddText(extra.Key, extra.Value);
        }

        return new Border
        {
            Background = _theme.SurfaceBackground,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            Child = rows,
        };
    }

    private SelectableTextBlock TextBlockFor(IEnumerable<RenderInline> inlines)
    {
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = _theme.FontSize,
            FontFamily = _theme.FontFamily,
            Foreground = _theme.Foreground,
        };

        foreach (RenderInline inline in inlines)
        {
            AppendInline(text.Inlines!, inline, TextStyle.None);
        }

        return text;
    }

    private void AppendInline(InlineCollection target, RenderInline inline, TextStyle style)
    {
        switch (inline)
        {
            case TextRun text:
                target.Add(StyledRun(text.Text, style));
                break;

            case StyleRun styled:
                foreach (RenderInline child in styled.Children)
                {
                    AppendInline(target, child, style | styled.Style);
                }

                break;

            case CodeRun code:
                target.Add(new Run(code.Code)
                {
                    FontFamily = _theme.CodeFontFamily,
                    Background = _theme.CodeBackground,
                    FontSize = _theme.FontSize * 0.92,
                });
                break;

            case BreakRun @break:
                if (@break.IsHard)
                {
                    target.Add(new LineBreak());
                }
                else
                {
                    target.Add(new Run(" "));
                }

                break;

            case MathRun math:
                target.Add(new Run(math.Source)
                {
                    FontFamily = _theme.CodeFontFamily,
                    Foreground = _theme.Muted,
                });
                break;

            case TagRun tag:
                target.Add(new InlineUIContainer(Chip($"#{tag.Tag}")));
                break;

            case FootnoteReferenceRun footnote:
                target.Add(new Run(footnote.Label)
                {
                    BaselineAlignment = BaselineAlignment.Superscript,
                    FontSize = _theme.FontSize * 0.75,
                    Foreground = _theme.Link,
                });
                break;

            case LinkRun link:
                target.Add(new InlineUIContainer(
                    LinkControl(link.Resolution, RenderModelBuilder.ToPlainText(link.Children), link.Url)));
                break;

            case ImageRun image:
                target.Add(new InlineUIContainer(ImageControl(image)));
                break;
        }
    }

    private Run StyledRun(string text, TextStyle style)
    {
        var run = new Run(text);

        if (style.HasFlag(TextStyle.Bold))
        {
            run.FontWeight = FontWeight.SemiBold;
        }

        if (style.HasFlag(TextStyle.Italic))
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (style.HasFlag(TextStyle.Strikethrough))
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        if (style.HasFlag(TextStyle.Highlight))
        {
            run.Background = _theme.HighlightBackground;
        }

        if (style.HasFlag(TextStyle.Superscript))
        {
            run.BaselineAlignment = BaselineAlignment.Superscript;
            run.FontSize = _theme.FontSize * 0.75;
        }

        if (style.HasFlag(TextStyle.Subscript))
        {
            run.BaselineAlignment = BaselineAlignment.Subscript;
            run.FontSize = _theme.FontSize * 0.75;
        }

        return run;
    }

    /// <summary>
    /// Builds a clickable link, decorated by the rule that resolved it (plan.md section
    /// 5.5): nothing for steps 1-3, a dotted underline for the normalized steps, a marker
    /// for the structural ones, and unresolved styling for a placeholder.
    /// </summary>
    private Control LinkControl(LinkResolution resolution, string text, string? externalUrl)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = _theme.FontSize,
            FontFamily = _theme.FontFamily,
            TextWrapping = TextWrapping.Wrap,
            Foreground = resolution.Confidence switch
            {
                ResolutionConfidence.Unresolved => _theme.UnresolvedLink,
                _ when resolution.IsAmbiguous => _theme.AmbiguousLink,
                _ => _theme.Link,
            },
        };

        label.TextDecorations = resolution.Confidence switch
        {
            ResolutionConfidence.Exact => null,
            ResolutionConfidence.Unresolved => TextDecorations.Underline,
            _ => DottedUnderline(),
        };

        string tooltip = resolution.Explain();

        if (resolution.IsAmbiguous)
        {
            tooltip += $"\nAlso matches: {string.Join(", ", resolution.Candidates.Skip(1).Select(c => c.RelativePath))}";
        }
        else if (resolution.Suggestions.Count > 0)
        {
            tooltip += $"\nDid you mean: {string.Join(", ", resolution.Suggestions.Select(c => c.RelativePath))}";
        }

        ToolTip.SetTip(label, tooltip);

        var button = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };

        button.Click += (_, _) => LinkActivated?.Invoke(this, new LinkActivatedEventArgs(resolution, externalUrl));

        if (externalUrl is null && resolution.Target is { IsMarkdown: true })
        {
            AttachPreview(button, resolution, tooltip);
        }

        return button;
    }

    /// <summary>
    /// Replaces a link's plain tooltip with a rendered preview of the page it leads to,
    /// built on first hover so that a page full of links costs nothing until one is
    /// actually pointed at.
    /// </summary>
    private void AttachPreview(Control target, LinkResolution resolution, string tooltip)
    {
        bool built = false;

        target.PointerEntered += (_, _) =>
        {
            if (built || PreviewFactory is null)
            {
                return;
            }

            built = true;
            Control? preview = PreviewFactory(resolution);

            if (preview is null)
            {
                return;
            }

            ToolTip.SetTip(target, new StackPanel
            {
                Spacing = 6,
                MaxWidth = 460,
                Children = { Caption(tooltip), preview },
            });
        };
    }

    private Control ImageControl(ImageRun image)
    {
        IImage? source = image.Resolution.Target is { } target ? _images.Load(target) : null;

        if (source is null)
        {
            return Chip(image.AlternateText is { Length: > 0 } alt
                ? $"[missing image: {alt}]"
                : $"[missing image: {image.Resolution.Link.RawTarget}]");
        }

        var control = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        if (image.Width is double width)
        {
            control.Width = width;
        }

        if (image.Height is double height)
        {
            control.Height = height;
        }

        if (image.AlternateText is { Length: > 0 } tooltip)
        {
            ToolTip.SetTip(control, tooltip);
        }

        return control;
    }

    /// <summary>
    /// A collapsible section built from a toggle and a panel rather than from an
    /// Expander. Expander carries a theme transition, and a page holding several of them
    /// inside nested layout containers can leave the measure pass never settling — a
    /// schema panel with thirty tables must not be able to hang the reader.
    /// </summary>
    private Control Disclosure(string header, Control content, bool startsOpen)
    {
        content.IsVisible = startsOpen;

        var toggle = new ToggleButton
        {
            IsChecked = startsOpen,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = (startsOpen ? "▾ " : "▸ ") + header,
                FontWeight = FontWeight.SemiBold,
                FontSize = _theme.FontSize * 0.9,
                Foreground = _theme.Foreground,
            },
        };

        toggle.IsCheckedChanged += (_, _) =>
        {
            bool open = toggle.IsChecked == true;
            content.IsVisible = open;

            if (toggle.Content is TextBlock label)
            {
                label.Text = (open ? "▾ " : "▸ ") + header;
            }
        };

        return new StackPanel { Spacing = 2, Children = { toggle, content } };
    }

    private SelectableTextBlock PlainText(string text) => new()
    {
        Text = text,
        FontSize = _theme.FontSize,
        Foreground = _theme.Foreground,
        TextWrapping = TextWrapping.Wrap,
    };

    private Control Chip(string text) => new Border
    {
        Background = _theme.SurfaceBackground,
        BorderBrush = _theme.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 1),
        Margin = new Thickness(0, 0, 6, 4),
        Child = new TextBlock
        {
            Text = text,
            FontSize = _theme.FontSize * 0.82,
            Foreground = _theme.Muted,
        },
    };

    private SelectableTextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = _theme.FontSize * 0.78,
        Foreground = _theme.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextDecorationCollection DottedUnderline() =>
    [
        new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            StrokeDashArray = [1, 2],
            StrokeThickness = 1,
        },
    ];
}
