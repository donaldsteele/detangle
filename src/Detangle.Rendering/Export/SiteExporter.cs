using System.Globalization;
using System.Text;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Detangle.Rendering.Model;

namespace Detangle.Rendering.Export;

/// <summary>What an export should produce.</summary>
public sealed record ExportOptions
{
    /// <summary>Where the export goes: a directory for a site, a file for a single page.</summary>
    public required string OutputPath { get; init; }

    /// <summary>The title on every page and in the browser tab.</summary>
    public string Title { get; init; } = "Wiki";

    /// <summary>Include pages marked "draft: true" or "publish: false".</summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>Copy images and attachments next to the pages that use them.</summary>
    public bool CopyAttachments { get; init; } = true;

    /// <summary>Write the prebuilt search index and the search box that reads it.</summary>
    public bool BuildSearchIndex { get; init; } = true;
}

/// <summary>What an export did.</summary>
/// <param name="Pages">Pages written.</param>
/// <param name="Attachments">Files copied.</param>
/// <param name="Links">Vault links written as anchors.</param>
/// <param name="BrokenLinks">Links that had no target to point at.</param>
/// <param name="Diagnostics">Anything that went wrong along the way.</param>
public sealed record ExportReport(
    int Pages,
    int Attachments,
    int Links,
    int BrokenLinks,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>A one-line summary for the status bar.</summary>
    public override string ToString() =>
        $"{Pages:N0} pages · {Attachments:N0} files · {Links:N0} links · {BrokenLinks:N0} broken";
}

/// <summary>
/// Exports a vault as HTML (plan.md section 6.6).
/// <para>
/// This is the "publish my LLM wiki" button, and its whole value is that the links
/// survive. Every anchor is written from a resolution, so a page reached by an alias or
/// a normalized name keeps working in the exported site — which is the one thing every
/// other static-site generator loses, because they match link text against filenames and
/// silently drop what does not match.
/// </para>
/// <para>
/// The output has no network dependency: diagrams are inline SVG, the stylesheet and the
/// search script are written here rather than fetched, and every link is relative. It
/// opens from a file:// URL.
/// </para>
/// </summary>
public static class SiteExporter
{
    /// <summary>Writes one HTML file per page, plus a stylesheet, navigation and a search index.</summary>
    /// <param name="vault">The vault to export.</param>
    /// <param name="builder">The render model builder, already configured with a diagram backend.</param>
    /// <param name="options">Where it goes and what it includes.</param>
    public static ExportReport ExportSite(
        VaultSnapshot vault, RenderModelBuilder builder, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<string>();
        List<VaultDocument> pages = [.. Pages(vault, options)];
        var included = new HashSet<string>(pages.Select(p => p.RelativePath), StringComparer.Ordinal);

        Directory.CreateDirectory(options.OutputPath);

        NavigationTreeBuilder.Result navigation = NavigationTreeBuilder.Build(vault, Read);
        var attachments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = new List<SearchEntry>();

        int links = 0;
        int broken = 0;

        foreach (VaultDocument page in pages)
        {
            RenderDocument rendered;

            try
            {
                rendered = builder.Build(page);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"{page.RelativePath}: {ex.Message}");
                continue;
            }

            var emitter = new HtmlEmitter(resolution =>
            {
                string? href = HrefFor(page, resolution, included, attachments);

                if (resolution.Link.IsExternal)
                {
                    return href;
                }

                links++;

                if (href is null)
                {
                    broken++;
                }

                return href;
            });

            string body = emitter.Emit(rendered);
            string html = Page(options, page, body, navigation, included);
            string output = Path.Combine(options.OutputPath, HtmlPathOf(page.RelativePath));

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, html);

            index.Add(new SearchEntry(
                HtmlPathOf(page.RelativePath),
                page.DisplayName,
                Summarize(rendered)));

            diagnostics.AddRange(rendered.Diagnostics.Select(d => $"{page.RelativePath}: {d}"));
        }

        int copied = options.CopyAttachments
            ? CopyAttachments(vault, attachments, options.OutputPath, diagnostics)
            : 0;

        File.WriteAllText(Path.Combine(options.OutputPath, "detangle.css"), Stylesheet);

        if (options.BuildSearchIndex)
        {
            File.WriteAllText(Path.Combine(options.OutputPath, "search-index.json"), SearchIndex(index));

            File.WriteAllText(Path.Combine(options.OutputPath, "search.js"), SearchScript);
        }

        WriteEntryPoint(options, pages, included);

        return new ExportReport(index.Count, copied, links, broken, diagnostics);
    }

    /// <summary>
    /// Writes one self-contained HTML file. With a single document it is that page; with
    /// none it is the whole vault, every page one after another with in-page anchors, so
    /// the result can be mailed to somebody as one attachment.
    /// </summary>
    public static ExportReport ExportSingleFile(
        VaultSnapshot vault, RenderModelBuilder builder, ExportOptions options, VaultDocument? only = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        string? directory = Path.GetDirectoryName(options.OutputPath);

        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        List<VaultDocument> pages = only is null ? [.. Pages(vault, options)] : [only];
        var included = new HashSet<string>(pages.Select(p => p.RelativePath), StringComparer.Ordinal);
        var diagnostics = new List<string>();
        var body = new StringBuilder();

        int links = 0;
        int broken = 0;

        foreach (VaultDocument page in pages)
        {
            RenderDocument rendered = builder.Build(page);

            // Inside one file every page is a section, so a vault link becomes a jump to
            // that section rather than a request for a file that is not there.
            var emitter = new HtmlEmitter(resolution =>
            {
                if (resolution.Link.IsExternal)
                {
                    return resolution.Link.RawTarget;
                }

                links++;

                if (resolution.Target is not { } target || !included.Contains(target.RelativePath))
                {
                    broken++;
                    return null;
                }

                return "#" + SectionIdOf(target.RelativePath);
            });

            body.Append(CultureInfo.InvariantCulture,
                $"<section id=\"{HtmlEmitter.Escape(SectionIdOf(page.RelativePath))}\" class=\"page\">\n");

            if (pages.Count > 1)
            {
                body.Append(CultureInfo.InvariantCulture,
                    $"<p class=\"page-path\">{HtmlEmitter.Escape(page.RelativePath)}</p>\n");
            }

            body.Append(emitter.Emit(rendered));
            body.Append("</section>\n");

            diagnostics.AddRange(rendered.Diagnostics.Select(d => $"{page.RelativePath}: {d}"));
        }

        string contents = pages.Count > 1 ? Contents(pages) : string.Empty;

        File.WriteAllText(
            options.OutputPath,
            Shell(options.Title, contents + body, inlineStylesheet: true, navigation: null, search: false));

        return new ExportReport(pages.Count, 0, links, broken, diagnostics);
    }

    /// <summary>The exported file name for a vault-relative markdown path.</summary>
    public static string HtmlPathOf(string relativePath) =>
        LinkNormalizer.StripKnownExtension(relativePath) + ".html";

    /// <summary>The in-page anchor a document gets in a single-file export.</summary>
    public static string SectionIdOf(string relativePath) =>
        "page-" + LinkNormalizer.Normalize(relativePath).Replace('/', '-');

    private static IEnumerable<VaultDocument> Pages(VaultSnapshot vault, ExportOptions options) =>
        vault.Documents
            .Where(d => d.IsMarkdown)
            .Where(d => options.IncludeDrafts || !d.Frontmatter.IsDraft)
            .OrderBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The URL one page uses to reach a link's target, or null when there is nothing to
    /// point at. Attachments are recorded on the way past so only the files the export
    /// actually references get copied.
    /// </summary>
    private static string? HrefFor(
        VaultDocument source,
        LinkResolution resolution,
        HashSet<string> included,
        HashSet<string> attachments)
    {
        if (resolution.Link.IsExternal)
        {
            return resolution.Link.RawTarget;
        }

        string anchor = resolution.Link.Anchor is { Length: > 0 } fragment
            ? "#" + Uri.EscapeDataString(fragment)
            : string.Empty;

        if (resolution.Link.IsSelfReference)
        {
            return anchor.Length > 0 ? anchor : null;
        }

        if (resolution.Target is not { } target)
        {
            return null;
        }

        if (!target.IsMarkdown)
        {
            attachments.Add(target.RelativePath);

            return Relative(source.DirectoryPath, target.RelativePath) + anchor;
        }

        return included.Contains(target.RelativePath)
            ? Relative(source.DirectoryPath, HtmlPathOf(target.RelativePath)) + anchor
            : null;
    }

    private static string Relative(string fromDirectory, string targetPath) =>
        Core.Editing.MarkdownNormalizer.RelativePath(fromDirectory, targetPath) is { Length: > 0 } path
            ? string.Join('/', path.Split('/').Select(Uri.EscapeDataString).Select(s => s == ".." ? ".." : s))
            : targetPath;

    private static int CopyAttachments(
        VaultSnapshot vault, HashSet<string> attachments, string outputPath, List<string> diagnostics)
    {
        int copied = 0;

        foreach (string relativePath in attachments)
        {
            VaultDocument? document = vault.Index.ByRelativePath(relativePath).FirstOrDefault();

            if (document is null)
            {
                continue;
            }

            string destination = Path.Combine(
                outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(document.AbsolutePath, destination, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"{relativePath}: {ex.Message}");
            }
        }

        return copied;
    }

    /// <summary>
    /// Writes index.html when the vault has no page that would already be one, so the
    /// export opens on something rather than on a directory listing.
    /// </summary>
    private static void WriteEntryPoint(
        ExportOptions options, List<VaultDocument> pages, HashSet<string> included)
    {
        string entry = Path.Combine(options.OutputPath, "index.html");

        if (File.Exists(entry) || pages.Count == 0)
        {
            return;
        }

        var body = new StringBuilder("<h1>");
        body.Append(HtmlEmitter.Escape(options.Title));
        body.Append("</h1>\n<ul class=\"contents\">\n");

        foreach (VaultDocument page in pages)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"<li><a href=\"{HtmlEmitter.Escape(HtmlPathOf(page.RelativePath))}\">"
                + $"{HtmlEmitter.Escape(page.DisplayName)}</a></li>\n");
        }

        body.Append("</ul>\n");

        _ = included;

        File.WriteAllText(
            entry,
            Shell(options.Title, body.ToString(), inlineStylesheet: false, navigation: null, options.BuildSearchIndex));
    }

    private static string Contents(List<VaultDocument> pages)
    {
        var builder = new StringBuilder("<nav class=\"contents\"><h1>Contents</h1>\n<ul>\n");

        foreach (VaultDocument page in pages)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<li><a href=\"#{HtmlEmitter.Escape(SectionIdOf(page.RelativePath))}\">"
                + $"{HtmlEmitter.Escape(page.DisplayName)}</a></li>\n");
        }

        return builder.Append("</ul></nav>\n").ToString();
    }

    private static string Page(
        ExportOptions options,
        VaultDocument page,
        string body,
        NavigationTreeBuilder.Result navigation,
        HashSet<string> included)
    {
        string depth = string.Concat(
            Enumerable.Repeat("../", page.DirectoryPath.Length == 0 ? 0 : page.DirectoryPath.Count(c => c == '/') + 1));

        var sidebar = new StringBuilder();
        WriteNavigation(sidebar, navigation.Roots, depth, included, page.RelativePath);

        return Shell(
            $"{page.DisplayName} · {options.Title}",
            body,
            inlineStylesheet: false,
            sidebar.ToString(),
            options.BuildSearchIndex,
            depth);
    }

    private static void WriteNavigation(
        StringBuilder builder,
        IReadOnlyList<NavigationNode> nodes,
        string depth,
        HashSet<string> included,
        string current)
    {
        builder.Append("<ul>\n");

        foreach (NavigationNode node in nodes)
        {
            builder.Append("<li>");

            if (node.Document is { } document && included.Contains(document.RelativePath))
            {
                bool active = string.Equals(document.RelativePath, current, StringComparison.Ordinal);

                builder.Append(CultureInfo.InvariantCulture,
                    $"<a{(active ? " class=\"active\"" : string.Empty)} "
                    + $"href=\"{HtmlEmitter.Escape(depth + HtmlPathOf(document.RelativePath))}\">"
                    + $"{HtmlEmitter.Escape(node.Title)}</a>");
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"<span>{HtmlEmitter.Escape(node.Title)}</span>");
            }

            if (node.Children.Count > 0)
            {
                WriteNavigation(builder, node.Children, depth, included, current);
            }

            builder.Append("</li>\n");
        }

        builder.Append("</ul>\n");
    }

    private static string Shell(
        string title,
        string body,
        bool inlineStylesheet,
        string? navigation,
        bool search,
        string depth = "")
    {
        var builder = new StringBuilder();

        builder.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n");
        builder.Append("<meta charset=\"utf-8\" />\n");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        builder.Append(CultureInfo.InvariantCulture, $"<title>{HtmlEmitter.Escape(title)}</title>\n");

        if (inlineStylesheet)
        {
            builder.Append("<style>\n").Append(Stylesheet).Append("</style>\n");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<link rel=\"stylesheet\" href=\"{depth}detangle.css\" />\n");
        }

        // A page with no sidebar is one column and has to say so: the stylesheet lays
        // the body out as a two-column grid, so a lone <main> took the sidebar's column
        // and a single-file export rendered in an 18rem strip down the left of the page.
        builder.Append(navigation is { Length: > 0 }
            ? "</head>\n<body>\n"
            : "</head>\n<body class=\"single\">\n");

        if (navigation is { Length: > 0 })
        {
            builder.Append("<nav class=\"sidebar\">\n");

            if (search)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"<input id=\"search\" type=\"search\" placeholder=\"Search\" data-root=\"{depth}\" />\n");
                builder.Append("<ul id=\"results\"></ul>\n");
            }

            builder.Append(navigation).Append("</nav>\n");
        }

        builder.Append("<main>\n").Append(body).Append("</main>\n");

        if (search && navigation is { Length: > 0 })
        {
            builder.Append(CultureInfo.InvariantCulture, $"<script src=\"{depth}search.js\"></script>\n");
        }

        return builder.Append("</body>\n</html>\n").ToString();
    }

    /// <summary>
    /// The first few hundred words of a page, for the search index. Headings are kept
    /// with the prose so a search for a section title finds the page it is on.
    /// </summary>
    private static string Summarize(RenderDocument document)
    {
        var builder = new StringBuilder();

        foreach (RenderBlock block in document.Blocks)
        {
            string text = block switch
            {
                ParagraphRenderBlock paragraph => RenderModelBuilder.ToPlainText(paragraph.Inlines),
                HeadingRenderBlock heading => heading.Text,
                _ => string.Empty,
            };

            if (text.Length == 0)
            {
                continue;
            }

            builder.Append(text).Append(' ');

            if (builder.Length > 2000)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string? Read(VaultDocument document)
    {
        try
        {
            return File.Exists(document.AbsolutePath) ? File.ReadAllText(document.AbsolutePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>One page in the exported search index.</summary>
    /// <param name="Path">The page's file, relative to the export root.</param>
    /// <param name="Title">What the result shows.</param>
    /// <param name="Text">The page's prose, for matching.</param>
    public sealed record SearchEntry(string Path, string Title, string Text);

    /// <summary>
    /// Writes the index by hand rather than through a serializer. The shape is three
    /// strings per page and the export has to keep working under trimming, where
    /// reflection-based serialization is exactly the thing that stops working silently.
    /// </summary>
    private static string SearchIndex(List<SearchEntry> entries)
    {
        var builder = new StringBuilder("[");

        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"path\":").Append(JsonText.Quote(entries[i].Path));
            builder.Append(",\"title\":").Append(JsonText.Quote(entries[i].Title));
            builder.Append(",\"text\":").Append(JsonText.Quote(entries[i].Text)).Append('}');
        }

        return builder.Append(']').ToString();
    }

    private const string SearchScript = """
        // Prebuilt-index search for an exported vault. No dependencies, no network: the
        // index is one JSON file sitting beside the pages, so this works from file://.
        (function () {
          var box = document.getElementById('search');
          var results = document.getElementById('results');
          if (!box || !results) { return; }
          var root = box.getAttribute('data-root') || '';
          var index = [];
          fetch(root + 'search-index.json')
            .then(function (r) { return r.json(); })
            .then(function (data) { index = data; })
            .catch(function () { box.placeholder = 'Search needs a web server'; });
          box.addEventListener('input', function () {
            var query = box.value.trim().toLowerCase();
            results.textContent = '';
            if (query.length < 2) { return; }
            var hits = 0;
            for (var i = 0; i < index.length && hits < 20; i++) {
              var entry = index[i];
              if (entry.title.toLowerCase().indexOf(query) < 0 &&
                  entry.text.toLowerCase().indexOf(query) < 0) { continue; }
              var item = document.createElement('li');
              var link = document.createElement('a');
              link.href = root + entry.path;
              link.textContent = entry.title;
              item.appendChild(link);
              results.appendChild(item);
              hits++;
            }
          });
        })();
        """;

    private const string Stylesheet = """
        /* Detangle export. One stylesheet, no framework, no fetched fonts. */
        :root {
          color-scheme: light dark;
          --bg: #ffffff; --fg: #1b1f24; --muted: #5b6470; --border: #d8dee6;
          --link: #1f6feb; --broken: #c0392b; --surface: #f6f8fa;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --bg: #0d1117; --fg: #e6edf3; --muted: #8b949e; --border: #30363d;
            --link: #6cb6ff; --broken: #ff7b72; --surface: #161b22;
          }
        }
        * { box-sizing: border-box; }
        body {
          margin: 0; background: var(--bg); color: var(--fg);
          font: 16px/1.65 -apple-system, BlinkMacSystemFont, "Segoe UI", Inter, system-ui, sans-serif;
          display: grid; grid-template-columns: minmax(0, 18rem) minmax(0, 1fr);
        }
        @media (max-width: 60rem) { body { display: block; } }
        /* One document, no sidebar: a single centred column rather than a grid cell. */
        body.single { display: block; }
        body.single main { max-width: 46rem; margin: 0 auto; padding: 2.5rem 1.25rem 4rem; }
        body.single .contents { margin-bottom: 2.5rem; }
        .sidebar {
          padding: 1.5rem 1rem; border-right: 1px solid var(--border);
          max-height: 100vh; overflow: auto; position: sticky; top: 0;
        }
        .sidebar ul { list-style: none; margin: 0; padding-left: 0.75rem; }
        .sidebar > ul { padding-left: 0; }
        .sidebar a, .sidebar span { display: block; padding: 0.15rem 0; font-size: 0.9rem; }
        .sidebar span { color: var(--muted); font-weight: 600; }
        .sidebar a.active { font-weight: 700; }
        #search { width: 100%; padding: 0.4rem 0.5rem; margin-bottom: 0.75rem;
          border: 1px solid var(--border); border-radius: 6px;
          background: var(--surface); color: inherit; }
        #results { list-style: none; padding: 0; margin: 0 0 1rem; }
        main { padding: 2.5rem clamp(1rem, 4vw, 3rem); max-width: 54rem; }
        a { color: var(--link); }
        a.resolved-normalized, a.resolved-heuristic { text-decoration-style: dotted; }
        .broken-link { color: var(--broken); text-decoration: line-through dotted; }
        h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 2rem 0 0.75rem; }
        h1 { font-size: 2rem; } h2 { font-size: 1.5rem; } h3 { font-size: 1.2rem; }
        pre { background: var(--surface); padding: 0.9rem 1rem; border-radius: 8px;
          overflow-x: auto; border: 1px solid var(--border); }
        code { font-family: ui-monospace, "Cascadia Code", Menlo, Consolas, monospace; font-size: 0.9em; }
        pre code { font-size: 0.85rem; }
        blockquote { margin: 1rem 0; padding: 0.1rem 1rem; border-left: 3px solid var(--border); color: var(--muted); }
        .transclusion { border-left-color: var(--link); }
        .transclusion-source { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.04em; }
        table { border-collapse: collapse; width: 100%; display: block; overflow-x: auto; }
        th, td { border: 1px solid var(--border); padding: 0.4rem 0.6rem; text-align: left; }
        th { background: var(--surface); }
        .callout { border: 1px solid var(--border); border-left-width: 4px;
          border-radius: 6px; padding: 0.75rem 1rem; margin: 1rem 0; background: var(--surface); }
        .callout-title, summary { font-weight: 650; margin: 0 0 0.35rem; }
        .callout-warning, .callout-caution, .callout-danger { border-left-color: #d29922; }
        .callout-tip, .callout-success { border-left-color: #3fb950; }
        .callout-note, .callout-info { border-left-color: var(--link); }
        .properties { background: var(--surface); border: 1px solid var(--border);
          border-radius: 8px; padding: 0.75rem 1rem; margin-bottom: 1.5rem; font-size: 0.9rem; }
        .properties dl { display: grid; grid-template-columns: auto 1fr; gap: 0.2rem 1rem; margin: 0; }
        .properties dt { color: var(--muted); }
        .properties dd { margin: 0; }
        .diagram { margin: 1.5rem 0; overflow-x: auto; }
        .diagram svg { max-width: 100%; height: auto; }
        .diagnostic { color: var(--broken); font-size: 0.85rem; }
        .math { font-family: ui-monospace, monospace; background: var(--surface); padding: 0.1rem 0.3rem; }
        .math-block { display: block; padding: 0.75rem 1rem; border-radius: 8px; overflow-x: auto; }
        .tag { color: var(--muted); }
        .task { list-style: none; margin-left: -1.2rem; }
        .page + .page { border-top: 1px solid var(--border); margin-top: 3rem; padding-top: 1rem; }
        .page-path { color: var(--muted); font-size: 0.8rem; font-family: ui-monospace, monospace; }
        img { max-width: 100%; height: auto; }
        """;
}
