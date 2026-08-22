using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Diagnostics;

/// <summary>The kinds of problem the Link Doctor reports (plan.md section 6.3).</summary>
public enum FindingKind
{
    /// <summary>A link that matched no file.</summary>
    BrokenLink,

    /// <summary>A link that matched more than one file.</summary>
    AmbiguousLink,

    /// <summary>A page nothing links to.</summary>
    OrphanPage,

    /// <summary>Two or more files that normalize to the same name.</summary>
    DuplicateSlug,

    /// <summary>A well-linked page nobody has touched in a long time.</summary>
    StalePage,

    /// <summary>A page long enough to be worth splitting.</summary>
    OversizedPage,

    /// <summary>Missing or malformed frontmatter.</summary>
    FrontmatterIssue,

    /// <summary>A link that only resolved because the chain worked for it.</summary>
    NonCanonicalLink,
}

/// <summary>How serious a finding is.</summary>
public enum FindingSeverity
{
    /// <summary>Something is broken.</summary>
    Error,

    /// <summary>Something is ambiguous or decaying.</summary>
    Warning,

    /// <summary>Something could be tidier.</summary>
    Info,
}

/// <summary>One Link Doctor finding.</summary>
public sealed record Finding
{
    /// <summary>What kind of problem this is.</summary>
    public required FindingKind Kind { get; init; }

    /// <summary>How serious it is.</summary>
    public required FindingSeverity Severity { get; init; }

    /// <summary>The document the finding is about.</summary>
    public required VaultDocument Document { get; init; }

    /// <summary>A one-line description, in the reader's terms.</summary>
    public required string Message { get; init; }

    /// <summary>1-based line, when the finding is about a specific link.</summary>
    public int Line { get; init; }

    /// <summary>The resolution that produced a link finding.</summary>
    public LinkResolution? Resolution { get; init; }

    /// <summary>
    /// The canonical form a non-canonical link should be rewritten to, or the fix a
    /// broken link's best candidate suggests. Null when there is no safe rewrite.
    /// </summary>
    public string? SuggestedRewrite { get; init; }

    /// <summary>Other documents involved — ambiguity candidates, duplicate slugs.</summary>
    public IReadOnlyList<VaultDocument> Related { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => $"{Severity} {Kind}: {Document.RelativePath}: {Message}";
}

/// <summary>Thresholds for the findings that have them.</summary>
public sealed record LinkDoctorOptions
{
    /// <summary>The defaults from plan.md section 6.3.</summary>
    public static LinkDoctorOptions Default { get; } = new();

    /// <summary>A page with at least this many inbound links counts as well-linked.</summary>
    public int StaleInboundThreshold { get; init; } = 3;

    /// <summary>How old a well-linked page must be before it is called stale.</summary>
    public TimeSpan StaleAge { get; init; } = TimeSpan.FromDays(90);

    /// <summary>Line count at which a page is worth splitting.</summary>
    public int OversizedSoftLines { get; init; } = 400;

    /// <summary>Line count at which a page is definitely too long.</summary>
    public int OversizedHardLines { get; init; } = 800;

    /// <summary>The moment "now" is measured from; injected so findings are testable.</summary>
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Finds everything wrong with a vault's links and pages.
/// <para>
/// This panel is the product's point (plan.md section 6.3). The categories mirror the
/// llm-wiki linter's so Detangle is a drop-in visual replacement for it, and every
/// finding carries enough to act on: the line, the resolution, the candidates, and —
/// where one exists — the canonical text a link should be rewritten to.
/// </para>
/// </summary>
public static class LinkDoctor
{
    /// <summary>Examines a vault.</summary>
    /// <param name="graph">The vault's resolved link graph.</param>
    /// <param name="contentReader">Reads a document's text, for length checks.</param>
    /// <param name="options">Thresholds.</param>
    public static IReadOnlyList<Finding> Examine(
        LinkGraph graph,
        Func<VaultDocument, string?> contentReader,
        LinkDoctorOptions? options = null)
    {
        options ??= LinkDoctorOptions.Default;

        var findings = new List<Finding>();

        foreach (VaultDocument document in graph.Vault.Documents.Where(d => d.IsMarkdown))
        {
            findings.AddRange(ExamineLinks(graph, document));
            findings.AddRange(ExamineDocument(graph, document, contentReader, options));
        }

        findings.AddRange(DuplicateSlugs(graph.Vault));

        return findings;
    }

    /// <summary>The findings for one document's links.</summary>
    private static IEnumerable<Finding> ExamineLinks(LinkGraph graph, VaultDocument document)
    {
        foreach (LinkResolution resolution in graph.OutboundFrom(document))
        {
            if (resolution.Rule == ResolutionRule.NotAttempted)
            {
                continue;
            }

            if (resolution.IsUnresolved)
            {
                yield return new Finding
                {
                    Kind = FindingKind.BrokenLink,
                    Severity = FindingSeverity.Error,
                    Document = document,
                    Line = resolution.Link.Line,
                    Resolution = resolution,
                    Message = $"\"{resolution.Link.RawTarget}\" matches no file in this vault.",

                    // No fuzzy candidates are asked for here. Finding near misses means an
                    // edit-distance pass over every name in the vault, and examining a
                    // 5,000-file wiki would spend most of its time on suggestions for
                    // findings the reader may never open. SuggestFix does it on demand.
                };

                continue;
            }

            if (resolution.IsAmbiguous)
            {
                yield return new Finding
                {
                    Kind = FindingKind.AmbiguousLink,
                    Severity = FindingSeverity.Warning,
                    Document = document,
                    Line = resolution.Link.Line,
                    Resolution = resolution,
                    Message = $"\"{resolution.Link.RawTarget}\" matches {resolution.Candidates.Count} files; "
                        + $"showing {resolution.Target?.RelativePath}.",
                    Related = resolution.Candidates,
                };

                continue;
            }

            // Steps 4 and beyond mean the link only worked because the chain worked for
            // it. That is a feature at read time and a cleanup at write time.
            if (resolution.Rule >= ResolutionRule.PathSuffix
                && resolution.Rule <= ResolutionRule.Identifier
                && resolution.Target is { } target)
            {
                yield return new Finding
                {
                    Kind = FindingKind.NonCanonicalLink,
                    Severity = FindingSeverity.Info,
                    Document = document,
                    Line = resolution.Link.Line,
                    Resolution = resolution,
                    Message = $"\"{resolution.Link.RawTarget}\" resolved by {resolution.Rule}.",
                    SuggestedRewrite = CanonicalTargetFor(document, target),
                };
            }
        }
    }

    /// <summary>The findings about a document itself.</summary>
    private static IEnumerable<Finding> ExamineDocument(
        LinkGraph graph,
        VaultDocument document,
        Func<VaultDocument, string?> contentReader,
        LinkDoctorOptions options)
    {
        int inbound = graph.InboundCount(document);

        if (inbound == 0)
        {
            yield return new Finding
            {
                Kind = FindingKind.OrphanPage,
                Severity = FindingSeverity.Info,
                Document = document,
                Message = "Nothing links to this page.",
            };
        }

        DateTimeOffset updated = document.Frontmatter.Updated ?? document.LastModified;

        if (inbound >= options.StaleInboundThreshold && options.Now - updated > options.StaleAge)
        {
            yield return new Finding
            {
                Kind = FindingKind.StalePage,
                Severity = FindingSeverity.Warning,
                Document = document,
                Message = $"{inbound} pages link here, and it has not changed since "
                    + $"{updated:yyyy-MM-dd}.",
            };
        }

        string? content = contentReader(document);

        if (content is not null)
        {
            int lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;

            if (lines >= options.OversizedSoftLines)
            {
                yield return new Finding
                {
                    Kind = FindingKind.OversizedPage,
                    Severity = lines >= options.OversizedHardLines
                        ? FindingSeverity.Warning
                        : FindingSeverity.Info,
                    Document = document,
                    Message = $"{lines} lines; consider splitting this page.",
                };
            }
        }

        foreach (string problem in FrontmatterProblems(document))
        {
            yield return new Finding
            {
                Kind = FindingKind.FrontmatterIssue,
                Severity = FindingSeverity.Info,
                Document = document,
                Line = 1,
                Message = problem,
            };
        }
    }

    private static IEnumerable<string> FrontmatterProblems(VaultDocument document)
    {
        foreach (string diagnostic in document.Frontmatter.Diagnostics)
        {
            yield return diagnostic;
        }

        if (document.Frontmatter.Kind != Parsing.FrontmatterKind.None
            && string.IsNullOrWhiteSpace(document.Frontmatter.Title))
        {
            yield return "This page has frontmatter but no title.";
        }

        if (document.Frontmatter.Created is { } created && created > DateTimeOffset.UtcNow.AddDays(1))
        {
            yield return $"The created date is in the future ({created:yyyy-MM-dd}).";
        }
    }

    /// <summary>
    /// Files whose names normalize to the same identifier. Two such files make every bare
    /// link to either of them ambiguous, which is why this is a vault-level finding rather
    /// than a per-link one.
    /// </summary>
    private static IEnumerable<Finding> DuplicateSlugs(VaultSnapshot vault)
    {
        IEnumerable<IGrouping<string, VaultDocument>> groups = vault.Documents
            .Where(d => d.IsMarkdown)
            .GroupBy(d => LinkNormalizer.Normalize(d.Stem), StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (IGrouping<string, VaultDocument> group in groups)
        {
            List<VaultDocument> documents = [.. group];

            foreach (VaultDocument document in documents)
            {
                yield return new Finding
                {
                    Kind = FindingKind.DuplicateSlug,
                    Severity = FindingSeverity.Warning,
                    Document = document,
                    Message = $"\"{group.Key}\" is also the name of "
                        + $"{string.Join(", ", documents.Where(d => d != document).Select(d => d.RelativePath))}.",
                    Related = [.. documents.Where(d => d != document)],
                };
            }
        }
    }

    /// <summary>
    /// The canonical way to write a link from one document to another: a vault-relative
    /// path without its extension, which every one of the thirteen formats resolves at
    /// chain step 1.
    /// </summary>
    public static string CanonicalTargetFor(VaultDocument source, VaultDocument target) =>
        LinkNormalizer.StripKnownExtension(target.RelativePath);

    /// <summary>
    /// Works out what a broken link probably meant, and returns the finding with the
    /// rewrite and the candidates filled in.
    /// <para>
    /// This is separate from <see cref="Examine"/> on purpose: the fuzzy search behind it
    /// costs an edit-distance pass over the vault's names, which is worth paying for the
    /// finding a reader is looking at and not for the several thousand they are not.
    /// </para>
    /// </summary>
    public static Finding SuggestFix(Finding finding)
    {
        if (finding.Kind != FindingKind.BrokenLink || finding.Resolution is not { } resolution)
        {
            return finding;
        }

        IReadOnlyList<VaultDocument> suggestions = resolution.Suggestions;

        return finding with
        {
            SuggestedRewrite = suggestions.Count > 0
                ? CanonicalTargetFor(finding.Document, suggestions[0])
                : null,
            Related = suggestions,
        };
    }

    /// <summary>
    /// Rewrites a link in a document's text to its canonical target, returning the new
    /// text. Only the exact link on the recorded line is touched — a bulk fix has to be
    /// reviewable, so it must never rewrite text it was not asked about.
    /// </summary>
    /// <param name="content">The document's current text.</param>
    /// <param name="finding">The finding to apply.</param>
    /// <returns>The rewritten text, or null when the link was not found where expected.</returns>
    public static string? ApplyRewrite(string content, Finding finding)
    {
        if (finding.SuggestedRewrite is not { Length: > 0 } replacement
            || finding.Resolution is not { } resolution)
        {
            return null;
        }

        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int index = finding.Line - 1;

        if (index < 0 || index >= lines.Length)
        {
            return null;
        }

        string original = resolution.Link.RawTarget;
        string line = lines[index];

        int position = TargetPosition(line, resolution.Link);

        if (position < 0)
        {
            return null;
        }

        lines[index] = string.Concat(
            line.AsSpan(0, position), replacement, line.AsSpan(position + original.Length));

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Where on the line this link's destination actually starts, or -1 when it is not
    /// where the link says it is.
    /// <para>
    /// Searching the line for the target text alone gets this wrong twice. Two links to
    /// the same place on one line both find the first one, so one is rewritten twice and
    /// the other never; and a label that repeats its own destination -
    /// <c>[spec](spec)</c>, which is what a generator writes constantly - is found before
    /// the destination is. Both are avoided by starting from the column the parser
    /// recorded and stepping over the syntax that introduces the destination, rather than
    /// by hunting for the text.
    /// </para>
    /// </summary>
    private static int TargetPosition(string line, LinkReference link)
    {
        if (link.RawTarget.Length == 0)
        {
            // A self-reference ("[[#Heading]]") has no destination to replace, and
            // IndexOf("") would answer 0 and splice the rewrite at the start of the line.
            return -1;
        }

        int from = Math.Clamp(link.Column, 0, line.Length);

        int opening = link.Syntax switch
        {
            // Column is the "[" of "[label](target)"; the destination follows the "](".
            LinkSyntax.Markdown => Step(line.IndexOf("](", from, StringComparison.Ordinal), 2),

            // Column is the first "[" of "[[target|alias]]" or "![[...]]"; the target is
            // first inside the brackets, ahead of any "|" or "#".
            LinkSyntax.WikiLink => Step(line.IndexOf("[[", from, StringComparison.Ordinal), 2),

            // Frontmatter values, tags and block references have no bracketed destination
            // to step over, so the recorded column is the best start there is.
            _ => line.IndexOf(link.RawTarget, from, StringComparison.Ordinal),
        };

        // The bracketed forms must match exactly where the syntax puts them. Anything
        // else - an angle-bracketed or percent-encoded destination that no longer reads
        // back as the parsed target, a line edited since the finding was made - is the
        // "not where expected" case this method already refuses.
        return opening >= 0
            && opening + link.RawTarget.Length <= line.Length
            && line.AsSpan(opening, link.RawTarget.Length).SequenceEqual(link.RawTarget)
                ? opening
                : -1;

        static int Step(int found, int over) => found < 0 ? -1 : found + over;
    }

    /// <summary>
    /// The findings a "fix all safe" pass would apply: non-canonical links that resolved
    /// through steps 4 to 8, which have exactly one correct rewrite (plan.md section 6.3).
    /// </summary>
    public static IEnumerable<Finding> SafeToFix(IEnumerable<Finding> findings) =>
        findings.Where(f => f.Kind == FindingKind.NonCanonicalLink && f.SuggestedRewrite is { Length: > 0 });
}
