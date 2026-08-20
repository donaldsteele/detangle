using Detangle.Core.Linking;
using Detangle.Core.Parsing;
using Detangle.Core.Vault;
using Detangle.Rendering.Markdown;
using Detangle.Rendering.Model;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigMarkdown = Markdig.Markdown;

namespace Detangle.Rendering;

/// <summary>
/// Turns a vault document into a <see cref="RenderDocument"/>: Markdig's AST, plus the
/// resolver's answer for every link, plus embedded documents inlined in place.
/// <para>
/// The model it produces contains no Avalonia types. Everything that requires a decision
/// — which callout dialect this is, whether a pipe payload was a size or an alias, which
/// section of an embedded note the anchor selects, whether a link earns a dotted
/// underline — happens here, where it can be asserted on in a test. The control factory
/// downstream only translates.
/// </para>
/// </summary>
public sealed class RenderModelBuilder
{
    private readonly LinkResolver _resolver;
    private readonly IDocumentContentReader _reader;
    private readonly RenderOptions _options;

    /// <summary>Creates a builder over one vault snapshot.</summary>
    /// <param name="vault">The scanned vault.</param>
    /// <param name="reader">How document text is read; defaults to the filesystem.</param>
    /// <param name="options">Rendering knobs.</param>
    /// <param name="rememberedChoices">Disambiguation choices from the sidecar database.</param>
    public RenderModelBuilder(
        VaultSnapshot vault,
        IDocumentContentReader? reader = null,
        RenderOptions? options = null,
        IReadOnlyDictionary<string, string>? rememberedChoices = null)
    {
        _reader = reader ?? FileDocumentContentReader.Instance;
        _options = options ?? RenderOptions.Default;
        _resolver = vault.CreateResolver(rememberedChoices);
    }

    /// <summary>Builds the render model for one document.</summary>
    public RenderDocument Build(VaultDocument document)
    {
        var context = new BuildContext();

        string? content = _reader.Read(document);

        if (content is null)
        {
            return new RenderDocument(
                document, [], [], [$"{document.RelativePath} could not be read."]);
        }

        (DocumentFrontmatter frontmatter, string body) = DocumentBody.Split(content);
        MarkdownDocument parsed = MarkdigMarkdown.Parse(body, RenderPipeline.Instance);

        var blocks = new List<RenderBlock>();

        if (_options.IncludeProperties && frontmatter.Kind != FrontmatterKind.None)
        {
            blocks.Add(BuildProperties(document, frontmatter, context));
        }

        context.Push(document.RelativePath, body);
        blocks.AddRange(BuildBlocks(parsed, document, context));
        context.Pop();

        return new RenderDocument(document, blocks, context.Resolutions, context.Diagnostics);
    }

    private PropertiesRenderBlock BuildProperties(
        VaultDocument document, DocumentFrontmatter frontmatter, BuildContext context)
    {
        var references = new List<LinkResolution>();

        // Reference keys are links even though they carry no brackets, so they resolve
        // through the same chain as everything else and appear in the same counters.
        foreach (LinkReference reference in document.Links.Where(l => l.Syntax == LinkSyntax.Frontmatter))
        {
            LinkResolution resolution = _resolver.Resolve(reference);
            references.Add(resolution);
            context.Resolutions.Add(resolution);
        }

        return new PropertiesRenderBlock(frontmatter, references);
    }

    private List<RenderBlock> BuildBlocks(
        IEnumerable<Block> source, VaultDocument document, BuildContext context)
    {
        var blocks = new List<RenderBlock>();

        foreach (Block block in source)
        {
            RenderBlock? rendered = BuildBlock(block, document, context);

            if (rendered is not null)
            {
                blocks.Add(rendered);
            }
        }

        return blocks;
    }

    private RenderBlock? BuildBlock(Block block, VaultDocument document, BuildContext context)
    {
        switch (block)
        {
            case HeadingBlock heading:
                {
                    string text = ToPlainText(heading.Inline);

                    return new HeadingRenderBlock(
                        heading.Level, BuildInlines(heading.Inline, document, context), context.Slug(text), text);
                }

            case ParagraphBlock paragraph:
                {
                    List<RenderInline> inlines = BuildInlines(paragraph.Inline, document, context);

                    // A paragraph that is nothing but embeds is not a paragraph: it is the
                    // embedded documents themselves, promoted to block level.
                    List<RenderBlock>? promoted = PromoteStandaloneEmbeds(inlines, document, context);

                    if (promoted is not null)
                    {
                        return promoted.Count == 1 ? promoted[0] : new QuoteRenderBlock(promoted);
                    }

                    return new ParagraphRenderBlock(inlines);
                }

            // Before FencedCodeBlock: Markdig models "$$…$$" as a fenced block subclass, so
            // testing the base type first would swallow every block equation as code.
            case MathBlock math:
                return new MathRenderBlock(ToText(math.Lines));

            case FencedCodeBlock fenced:
                {
                    string language = (fenced.Info ?? string.Empty).Split(' ')[0].ToLowerInvariant();

                    return new CodeRenderBlock(
                        language, ToText(fenced.Lines), _options.DiagramLanguages.Contains(language));
                }

            case CodeBlock code:
                return new CodeRenderBlock(string.Empty, ToText(code.Lines), IsDiagram: false);

            case AdmonitionBlock admonition:
                return new CalloutRenderBlock(
                    admonition.Kind,
                    admonition.Title ?? Capitalize(admonition.Kind),
                    CalloutDialect.MkDocs,
                    admonition.IsCollapsible,
                    admonition.StartsCollapsed,
                    BuildBlocks(admonition, document, context));

            case QuoteBlock quote:
                return BuildQuoteOrCallout(quote, document, context);

            case ListBlock list:
                return new ListRenderBlock(
                    list.IsOrdered,
                    int.TryParse(list.OrderedStart, out int start) ? start : 1,
                    [.. list.OfType<ListItemBlock>().Select(item => BuildListItem(item, document, context))]);

            case Table table:
                return BuildTable(table, document, context);

            case ThematicBreakBlock:
                return new ThematicBreakRenderBlock();

            case HtmlBlock:
                // Parsed so that comments and raw blocks hide their contents from the
                // graph, then dropped: there is nothing here that can render markup.
                return null;

            case DefinitionList definitions:
                return BuildDefinitionList(definitions, document, context);

            case FootnoteGroup footnotes:
                return new FootnotesRenderBlock(
                [
                    .. footnotes.OfType<Footnote>().Select(note =>
                        (TrimFootnoteLabel(note.Label, note.Order),
                            (IReadOnlyList<RenderBlock>)BuildBlocks(note, document, context))),
                ]);

            case LinkReferenceDefinitionGroup:
                // Link reference definitions are metadata, not content; Markdig has already
                // wired them into the links that use them.
                return null;

            case ContainerBlock container:
                return new QuoteRenderBlock(BuildBlocks(container, document, context));

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads an Obsidian callout out of a blockquote: a blockquote whose first line is
    /// "[!kind]", optionally suffixed with "+" or "-" for a collapsible callout and
    /// followed by a title.
    /// <para>
    /// The marker is read from the document source rather than from the paragraph's
    /// inlines. Markdig sees "[!note]" as an unmatched link delimiter and splits it across
    /// several literals, and it releases a block's source lines once inlines are parsed —
    /// so the source text, addressed by the block's span, is the only place the marker
    /// still exists as the author wrote it.
    /// </para>
    /// </summary>
    private RenderBlock BuildQuoteOrCallout(QuoteBlock quote, VaultDocument document, BuildContext context)
    {
        if (quote.FirstOrDefault() is not ParagraphBlock paragraph
            || context.TextOf(paragraph) is not { Length: > 0 } markerText)
        {
            return new QuoteRenderBlock(BuildBlocks(quote, document, context));
        }

        int newline = markerText.IndexOf(NewLine);
        string first = (newline < 0 ? markerText : markerText[..newline]).TrimEnd();

        if (!first.StartsWith("[!", StringComparison.Ordinal))
        {
            return new QuoteRenderBlock(BuildBlocks(quote, document, context));
        }

        int close = first.IndexOf(']', StringComparison.Ordinal);

        if (close < 3)
        {
            return new QuoteRenderBlock(BuildBlocks(quote, document, context));
        }

        string kind = first[2..close].Trim().ToLowerInvariant();
        string afterMarker = first[(close + 1)..];

        bool collapsible = afterMarker.StartsWith('+') || afterMarker.StartsWith('-');
        bool startsCollapsed = afterMarker.StartsWith('-');

        if (collapsible)
        {
            afterMarker = afterMarker[1..];
        }

        string title = afterMarker.Trim();

        // The marker line is consumed by the header. The rest of the paragraph is body,
        // taken from the already-built inlines rather than re-parsed: the source text
        // still carries its "> " quote markers, and feeding that back through the parser
        // would wrap the body in a second blockquote.
        List<RenderInline> inlines = BuildInlines(paragraph.Inline, document, context);
        List<RenderInline> bodyInlines = DropPrefix(inlines, first.Length);

        var body = new List<RenderBlock>();

        if (bodyInlines.Count > 0)
        {
            body.Add(new ParagraphRenderBlock(bodyInlines));
        }

        body.AddRange(BuildBlocks(quote.Skip(1), document, context));

        return new CalloutRenderBlock(
            kind,
            title.Length > 0 ? title : Capitalize(kind),
            CalloutDialect.Obsidian,
            collapsible,
            startsCollapsed,
            body);
    }

    /// <summary>
    /// Drops the first <paramref name="count"/> characters of text from an inline list,
    /// splitting a run when the boundary falls inside one, then discards the line break
    /// that separated the consumed line from the rest.
    /// </summary>
    private static List<RenderInline> DropPrefix(List<RenderInline> inlines, int count)
    {
        int index = 0;

        while (index < inlines.Count && count > 0)
        {
            switch (inlines[index])
            {
                case TextRun text when text.Text.Length > count:
                    inlines[index] = new TextRun(text.Text[count..]);
                    count = 0;
                    continue;

                case TextRun text:
                    count -= text.Text.Length;
                    index++;
                    continue;

                case CodeRun code:
                    count -= code.Code.Length + 2;
                    index++;
                    continue;

                case BreakRun:
                    count = 0;
                    continue;

                default:
                    // Anything richer than text on the marker line is title content, and
                    // the character count has already accounted for its source form.
                    count = 0;
                    continue;
            }
        }

        List<RenderInline> remainder = inlines[index..];

        while (remainder.Count > 0 && (remainder[0] is BreakRun || remainder[0] is TextRun { Text.Length: 0 }))
        {
            remainder.RemoveAt(0);
        }

        return remainder;
    }

    private const char NewLine = '\n';

    private ListItemRenderBlock BuildListItem(
        ListItemBlock item, VaultDocument document, BuildContext context)
    {
        TaskState task = TaskState.None;

        if (item.FirstOrDefault() is ParagraphBlock { Inline: { } inline }
            && inline.FirstChild is TaskList taskList)
        {
            task = taskList.Checked ? TaskState.Checked : TaskState.Unchecked;
        }

        return new ListItemRenderBlock(BuildBlocks(item, document, context), task);
    }

    private TableRenderBlock BuildTable(Table table, VaultDocument document, BuildContext context)
    {
        var alignments = new List<ColumnAlignment>();

        foreach (TableColumnDefinition? column in table.ColumnDefinitions)
        {
            alignments.Add(column?.Alignment switch
            {
                TableColumnAlign.Left => ColumnAlignment.Left,
                TableColumnAlign.Center => ColumnAlignment.Center,
                TableColumnAlign.Right => ColumnAlignment.Right,
                _ => ColumnAlignment.None,
            });
        }

        var rows = new List<TableRowRenderBlock>();

        foreach (TableRow row in table.OfType<TableRow>())
        {
            var cells = new List<TableCellRenderBlock>();

            foreach (TableCell cell in row.OfType<TableCell>())
            {
                cells.Add(new TableCellRenderBlock(BuildBlocks(cell, document, context), cell.ColumnSpan));
            }

            rows.Add(new TableRowRenderBlock(cells, row.IsHeader));
        }

        return new TableRenderBlock(rows, alignments);
    }

    /// <summary>
    /// Reads a definition list. Markdig models each entry as an item holding a term block
    /// followed by its definition blocks, so the term is taken from that block rather than
    /// from "whatever came first" — which would silently promote a definition to a term
    /// whenever an entry was written without one.
    /// </summary>
    private DefinitionListRenderBlock BuildDefinitionList(
        DefinitionList list, VaultDocument document, BuildContext context)
    {
        var items = new List<DefinitionRenderBlock>();

        foreach (DefinitionItem item in list.OfType<DefinitionItem>())
        {
            var term = new List<RenderInline>();
            var definitions = new List<IReadOnlyList<RenderBlock>>();

            foreach (Block part in item)
            {
                if (part is DefinitionTerm { Inline: { } inline })
                {
                    term.AddRange(BuildInlines(inline, document, context));
                    continue;
                }

                definitions.Add(BuildBlocks([part], document, context));
            }

            items.Add(new DefinitionRenderBlock(term, definitions));
        }

        return new DefinitionListRenderBlock(items);
    }

    private List<RenderInline> BuildInlines(
        ContainerInline? container, VaultDocument document, BuildContext context, bool skipFirstLiteral = false)
    {
        var inlines = new List<RenderInline>();

        if (container is null)
        {
            return inlines;
        }

        bool skipped = !skipFirstLiteral;

        foreach (Inline inline in container)
        {
            if (!skipped && inline is LiteralInline)
            {
                skipped = true;
                continue;
            }

            RenderInline? rendered = BuildInline(inline, document, context);

            if (rendered is not null)
            {
                inlines.Add(rendered);
            }
        }

        return inlines;
    }

    private RenderInline? BuildInline(Inline inline, VaultDocument document, BuildContext context)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new TextRun(literal.Content.ToString());

            case EmphasisInline emphasis:
                return new StyleRun(
                    ToTextStyle(emphasis), BuildInlines(emphasis, document, context));

            case CodeInline code:
                return new CodeRun(code.Content);

            case LineBreakInline lineBreak:
                return new BreakRun(lineBreak.IsHard);

            case MathInline math:
                return new MathRun(math.Content.ToString());

            case TaskList:
                // The checkbox is modelled on the list item, not as inline content.
                return null;

            case HtmlInline:
                // Recognised so that raw markup hides nothing from the graph, then
                // dropped: this renderer has no way to honour it.
                return null;

            case WikiLinkInline wikiLink:
                return BuildWikiLink(wikiLink, document, context);

            case LinkInline link:
                return BuildMarkdownLink(link, document, context);

            case AutolinkInline autolink:
                return new LinkRun(
                    NotAttempted(document, autolink.Url),
                    [new TextRun(autolink.Url)],
                    autolink.Url);

            case FootnoteLink footnote:
                // Markdig keeps the "^" in a footnote's label; the reader shows the label
                // the author typed between the brackets, which does not include it.
                return new FootnoteReferenceRun(
                    TrimFootnoteLabel(footnote.Footnote.Label, footnote.Index), footnote.Index);

            case HtmlEntityInline entity:
                return new TextRun(entity.Transcoded.ToString());

            case ContainerInline container:
                return new StyleRun(TextStyle.None, BuildInlines(container, document, context));

            default:
                return null;
        }
    }

    private RenderInline? BuildWikiLink(WikiLinkInline wikiLink, VaultDocument document, BuildContext context)
    {
        LinkReference? reference = LinkFactory.FromWikiLink(
            document.RelativePath, wikiLink.Body, wikiLink.IsEmbed, wikiLink.Line + 1, wikiLink.Column);

        if (reference is null)
        {
            return null;
        }

        LinkResolution resolution = _resolver.Resolve(reference);
        context.Resolutions.Add(resolution);

        if (reference.IsEmbed && !IsDocumentEmbed(resolution))
        {
            (double? width, double? height) = LinkFactory.ParseSizeSpec(reference.SizeSpec);

            return new ImageRun(resolution, reference.Label ?? reference.RawTarget, width, height);
        }

        // A document embed inside a paragraph is promoted to a block by the caller; it is
        // carried as a link here so that promotion can find it.
        return new LinkRun(resolution, [new TextRun(DisplayTextFor(reference, resolution))]);
    }

    private RenderInline? BuildMarkdownLink(LinkInline link, VaultDocument document, BuildContext context)
    {
        LinkReference? reference = LinkFactory.FromMarkdownLink(
            document.RelativePath,
            link.Url ?? string.Empty,
            null,
            link.IsImage,
            link.Line + 1,
            link.Column);

        if (reference is null)
        {
            return null;
        }

        List<RenderInline> children = BuildInlines(link, document, context);

        if (reference.IsExternal)
        {
            return new LinkRun(
                NotAttempted(document, reference.RawTarget),
                children.Count > 0 ? children : [new TextRun(reference.RawTarget)],
                link.Url);
        }

        LinkResolution resolution = _resolver.Resolve(reference);
        context.Resolutions.Add(resolution);

        if (link.IsImage)
        {
            return new ImageRun(resolution, ToPlainText(children));
        }

        return new LinkRun(
            resolution,
            children.Count > 0 ? children : [new TextRun(DisplayTextFor(reference, resolution))]);
    }

    /// <summary>
    /// Replaces a paragraph made only of document embeds with the embedded documents.
    /// Returns null when the paragraph holds anything else, in which case the embeds stay
    /// inline as links — an embed in the middle of a sentence cannot become a block
    /// without reordering the author's text.
    /// </summary>
    private List<RenderBlock>? PromoteStandaloneEmbeds(
        List<RenderInline> inlines, VaultDocument document, BuildContext context)
    {
        var embeds = new List<LinkRun>();

        foreach (RenderInline inline in inlines)
        {
            switch (inline)
            {
                case LinkRun link when link.Resolution.Link.IsEmbed && IsDocumentEmbed(link.Resolution):
                    embeds.Add(link);
                    break;

                case BreakRun:
                    break;

                case TextRun text when text.Text.Trim().Length == 0:
                    break;

                default:
                    return null;
            }
        }

        if (embeds.Count == 0)
        {
            return null;
        }

        return [.. embeds.Select(embed => BuildTransclusion(embed.Resolution, document, context))];
    }

    /// <summary>
    /// Inlines an embedded document, narrowed to the anchored section when the embed
    /// named one. Cycles and over-deep chains produce an explanatory block rather than a
    /// hang or a stack overflow.
    /// </summary>
    private RenderBlock BuildTransclusion(
        LinkResolution resolution, VaultDocument document, BuildContext context)
    {
        VaultDocument? target = resolution.Target;

        if (target is null)
        {
            return new TransclusionRenderBlock(
                resolution, [], $"\"{resolution.Link.RawTarget}\" matches no file in this vault.");
        }

        if (context.Contains(target.RelativePath))
        {
            string cycle = $"{string.Join(" -> ", context.Stack)} -> {target.RelativePath}";
            context.Diagnostics.Add($"Embed cycle: {cycle}.");

            return new TransclusionRenderBlock(resolution, [], $"Embed cycle: {cycle}.");
        }

        if (context.Depth >= _options.MaxTransclusionDepth)
        {
            return new TransclusionRenderBlock(
                resolution, [], $"Embeds nested more than {_options.MaxTransclusionDepth} deep were not expanded.");
        }

        string? content = _reader.Read(target);

        if (content is null)
        {
            return new TransclusionRenderBlock(resolution, [], $"{target.RelativePath} could not be read.");
        }

        (_, string body) = DocumentBody.Split(content);
        MarkdownDocument parsed = MarkdigMarkdown.Parse(body, RenderPipeline.Instance);

        IEnumerable<Block> selected = SelectAnchoredSection(parsed, target, resolution.Link);

        context.Push(target.RelativePath, body);
        List<RenderBlock> blocks = BuildBlocks(selected, target, context);
        context.Pop();

        return new TransclusionRenderBlock(resolution, blocks);
    }

    /// <summary>
    /// Narrows an embedded document to the section its anchor names: a heading and
    /// everything under it until the next heading of the same or higher level, or the
    /// single block carrying a "^blockid" marker.
    /// </summary>
    private static IEnumerable<Block> SelectAnchoredSection(
        MarkdownDocument parsed, VaultDocument target, LinkReference link)
    {
        if (link.Anchor is not { Length: > 0 } anchor)
        {
            return parsed;
        }

        List<Block> blocks = [.. parsed];

        if (link.AnchorIsBlockId)
        {
            BlockAnchor? marker = target.BlockAnchors.FirstOrDefault(
                a => string.Equals(a.Id, anchor, StringComparison.OrdinalIgnoreCase));

            if (marker is null)
            {
                return [];
            }

            Block? owner = blocks.LastOrDefault(b => b.Line + 1 <= marker.Line);

            return owner is null ? [] : [owner];
        }

        string leaf = anchor.Contains('#', StringComparison.Ordinal)
            ? anchor[(anchor.LastIndexOf('#') + 1)..].Trim()
            : anchor;

        int start = blocks.FindIndex(b =>
            b is HeadingBlock heading && MatchesHeading(heading, leaf));

        if (start < 0)
        {
            return [];
        }

        int level = ((HeadingBlock)blocks[start]).Level;
        int end = blocks.FindIndex(start + 1, b => b is HeadingBlock next && next.Level <= level);

        return end < 0 ? blocks[start..] : blocks[start..end];
    }

    private static bool MatchesHeading(HeadingBlock heading, string anchor)
    {
        string text = ToPlainText(heading.Inline);

        return string.Equals(text.Trim(), anchor, StringComparison.OrdinalIgnoreCase)
            || string.Equals(HeadingSlugger.SlugCore(text), HeadingSlugger.SlugCore(anchor), StringComparison.Ordinal);
    }

    /// <summary>
    /// True when an embed target is a document to inline rather than an image to draw.
    /// An embed that resolved to nothing is judged by what was written: a bare name is a
    /// missing note, and rendering it as a broken image would hide that.
    /// </summary>
    private static bool IsDocumentEmbed(LinkResolution resolution)
    {
        if (resolution.Target is { } target)
        {
            return target.IsMarkdown;
        }

        string extension = Path.GetExtension(resolution.Link.RawTarget);

        return extension.Length == 0 || LinkNormalizer.HasMarkdownExtension(resolution.Link.RawTarget);
    }

    private static string DisplayTextFor(LinkReference reference, LinkResolution resolution)
    {
        if (reference.Label is { Length: > 0 } label)
        {
            return label;
        }

        // An unresolved link shows what was written, not a guess; a resolved one shows the
        // target's own display name, which is how a Dendron dot-path becomes readable.
        return resolution.Target?.DisplayName ?? reference.RawTarget;
    }

    private LinkResolution NotAttempted(VaultDocument document, string url) =>
        new()
        {
            Link = new LinkReference
            {
                SourcePath = document.RelativePath,
                RawTarget = url,
                Syntax = LinkSyntax.Markdown,
            },
            Rule = ResolutionRule.NotAttempted,
        };

    private static TextStyle ToTextStyle(EmphasisInline emphasis) => emphasis.DelimiterChar switch
    {
        '~' => emphasis.DelimiterCount == 2 ? TextStyle.Strikethrough : TextStyle.Subscript,
        '^' => TextStyle.Superscript,
        '=' => TextStyle.Highlight,
        _ => emphasis.DelimiterCount >= 2 ? TextStyle.Bold : TextStyle.Italic,
    };

    private static List<RenderInline> TrimLeadingBreak(List<RenderInline> inlines)
    {
        while (inlines.Count > 0 && inlines[0] is TextRun { Text.Length: 0 })
        {
            inlines.RemoveAt(0);
        }

        return inlines;
    }

    private static string TrimFootnoteLabel(string? label, int order) =>
        label?.TrimStart('^') is { Length: > 0 } trimmed
            ? trimmed
            : order.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string ToText(StringLineGroup lines) => lines.ToSlice().ToString();

    private static string ToPlainText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();

        foreach (Inline inline in container.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case WikiLinkInline wikiLink:
                    builder.Append(wikiLink.Body);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Plain text of a rendered inline list, for headings and titles.</summary>
    public static string ToPlainText(IEnumerable<RenderInline> inlines)
    {
        var builder = new System.Text.StringBuilder();

        foreach (RenderInline inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    builder.Append(text.Text);
                    break;
                case CodeRun code:
                    builder.Append(code.Code);
                    break;
                case StyleRun style:
                    builder.Append(ToPlainText(style.Children));
                    break;
                case LinkRun link:
                    builder.Append(ToPlainText(link.Children));
                    break;
                case TagRun tag:
                    builder.Append('#').Append(tag.Tag);
                    break;
                case BreakRun:
                    builder.Append(' ');
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Per-build state: the embed stack, the slugger, and what was collected.</summary>
    private sealed class BuildContext
    {
        private readonly HeadingSlugger _slugger = new();
        private readonly List<(string Path, string Source)> _stack = [];

        public List<LinkResolution> Resolutions { get; } = [];

        public List<string> Diagnostics { get; } = [];

        public IEnumerable<string> Stack => _stack.Select(entry => entry.Path);

        public int Depth => _stack.Count - 1;

        public string Slug(string headingText) => _slugger.Slug(headingText);

        public void Push(string relativePath, string source) => _stack.Add((relativePath, source));

        public void Pop() => _stack.RemoveAt(_stack.Count - 1);

        public bool Contains(string relativePath) =>
            _stack.Any(entry => string.Equals(entry.Path, relativePath, StringComparison.Ordinal));

        /// <summary>The source text a block was parsed from, addressed by its span.</summary>
        public string? TextOf(Block block)
        {
            if (_stack.Count == 0)
            {
                return null;
            }

            string source = _stack[^1].Source;
            int start = block.Span.Start;
            int end = Math.Min(block.Span.End, source.Length - 1);

            return start < 0 || start > end ? null : source[start..(end + 1)];
        }
    }
}
