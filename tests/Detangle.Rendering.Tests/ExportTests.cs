using System.Text.RegularExpressions;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Detangle.Rendering.Export;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for export (plan.md section 6.6). The exit criterion for the phase is that a
/// vault survives a round trip through export without losing a link, so most of this is
/// about counting anchors and checking that each one points at a file that exists.
/// </summary>
public partial class ExportTests : IDisposable
{
    private readonly string _output = Path.Combine(
        Path.GetTempPath(), "detangle-export-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_output))
            {
                Directory.Delete(_output, recursive: true);
            }
            else if (File.Exists(_output))
            {
                File.Delete(_output);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TextIsEscapedOnTheWayOut()
    {
        string html = Emit(("page.md", "Compare `a < b && c > d` in code.\n"));

        Assert.Contains("&lt; b &amp;&amp; c &gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RawHtmlInAVaultIsNotWrittenIntoTheExport()
    {
        // HTML is parsed so that a wikilink inside a comment stays a comment, and then
        // dropped at render time. Nothing a note claims to be markup reaches the export.
        string html = Emit(("page.md", "A <script>alert(1)</script> paragraph.\n"));

        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;script", html, StringComparison.Ordinal);
        Assert.Contains("paragraph", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AResolvedLinkBecomesAnAnchor()
    {
        string html = Emit(
            ("page.md", "See [[My Target]].\n"),
            ("concepts/my-target.md", "# My Target\n"));

        // The point of the whole product: an alias-style link that no other exporter can
        // follow comes out as a working anchor, and says how it was resolved.
        Assert.Contains("href=\"concepts/my-target.html\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"resolved-normalized\"", html, StringComparison.Ordinal);
        Assert.Contains("normalized-name match", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenLinkIsMarkedRatherThanDropped()
    {
        string html = Emit(("page.md", "See [[nowhere at all]].\n"));

        Assert.Contains("class=\"broken-link\"", html, StringComparison.Ordinal);
        Assert.Contains("nowhere at all", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExternalLinkKeepsItsUrlAndGetsSafeRelAttributes()
    {
        string html = Emit(("page.md", "See [docs](https://example.com/docs).\n"));

        Assert.Contains("href=\"https://example.com/docs\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingsCarryTheAnchorsTheSlugsMade()
    {
        string html = Emit(("page.md", "## A & B\n"));

        Assert.Contains("<h2 id=\"a--b\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ADiagramIsInlinedAsSvg()
    {
        RenderTestVault vault = RenderTestVault.Build(
            VaultFlavor.Generic,
            new RenderOptions { DiagramRenderer = new Diagrams.MermaiderDiagramRenderer() },
            ("page.md", "```mermaid\ngraph TD;\n  A-->B;\n```\n"));

        string html = new HtmlEmitter(_ => null).Emit(vault.Render("page.md"));

        Assert.Contains("<figure class=\"diagram diagram-mermaid\">", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportingASiteWritesAPageForEveryDocument()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("index.md", "# Index\n\nSee [[wiki/one]].\n"),
            ("wiki/one.md", "# One\n\nBack to [[index]] and on to [[Two]].\n"),
            ("wiki/two.md", "# Two\n"));

        ExportReport report = SiteExporter.ExportSite(vault.Vault, vault.Builder, Options());

        Assert.Equal(3, report.Pages);
        Assert.True(File.Exists(Path.Combine(_output, "index.html")));
        Assert.True(File.Exists(Path.Combine(_output, "wiki", "one.html")));
        Assert.True(File.Exists(Path.Combine(_output, "detangle.css")));
        Assert.True(File.Exists(Path.Combine(_output, "search-index.json")));
    }

    [Fact]
    public void EveryLinkInTheExportPointsAtAFileThatExists()
    {
        // The phase 7 exit criterion, stated as a test: nothing may be lost on the way out.
        RenderTestVault vault = RenderTestVault.Build(
            ("index.md", "# Index\n\n- [[wiki/one]]\n- [[Two]]\n"),
            ("wiki/one.md", "# One\n\nUp to [[index]], across to [[two]].\n"),
            ("wiki/two.md", "# Two\n\nBack to [[wiki/one#One]].\n"));

        ExportReport report = SiteExporter.ExportSite(vault.Vault, vault.Builder, Options());

        Assert.Equal(0, report.BrokenLinks);
        Assert.True(report.Links >= 5, $"only {report.Links} links were written");

        foreach (string file in Directory.GetFiles(_output, "*.html", SearchOption.AllDirectories))
        {
            string directory = Path.GetDirectoryName(file)!;

            foreach (Match match in HrefPattern().Matches(File.ReadAllText(file)))
            {
                string href = match.Groups[1].Value;

                if (href.StartsWith("http", StringComparison.Ordinal) || href.StartsWith('#'))
                {
                    continue;
                }

                string target = Uri.UnescapeDataString(href.Split('#')[0]);

                Assert.True(
                    File.Exists(Path.Combine(directory, target.Replace('/', Path.DirectorySeparatorChar))),
                    $"{Path.GetFileName(file)} links to {href}, which was not exported");
            }
        }
    }

    [Fact]
    public void ALinkToAnUnexportedPageIsCountedAsBroken()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("page.md", "See [[missing]].\n"));

        Assert.Equal(1, SiteExporter.ExportSite(vault.Vault, vault.Builder, Options()).BrokenLinks);
    }

    [Fact]
    public void DraftsAreLeftOutUnlessAskedFor()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("public.md", "# Public\n"),
            ("secret.md", "---\ndraft: true\n---\n\n# Secret\n"));

        Assert.Equal(1, SiteExporter.ExportSite(vault.Vault, vault.Builder, Options()).Pages);
        Assert.False(File.Exists(Path.Combine(_output, "secret.html")));

        Assert.Equal(
            2,
            SiteExporter.ExportSite(vault.Vault, vault.Builder, Options() with { IncludeDrafts = true }).Pages);
    }

    [Fact]
    public void TheNavigationIsWrittenIntoEveryPage()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("index.md", "# Index\n\n- [[wiki/one]]\n"),
            ("wiki/one.md", "# One\n"));

        SiteExporter.ExportSite(vault.Vault, vault.Builder, Options());

        string page = File.ReadAllText(Path.Combine(_output, "wiki", "one.html"));

        Assert.Contains("<nav class=\"sidebar\">", page, StringComparison.Ordinal);

        // Deeper pages have to walk back up to the stylesheet and to their siblings.
        Assert.Contains("href=\"../detangle.css\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"active\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSearchIndexHasAnEntryPerPage()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("one.md", "# One\n\nAttention is all you need.\n"),
            ("two.md", "# Two\n"));

        SiteExporter.ExportSite(vault.Vault, vault.Builder, Options());

        string index = File.ReadAllText(Path.Combine(_output, "search-index.json"));

        Assert.StartsWith("[{", index, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"one.html\"", index, StringComparison.Ordinal);
        Assert.Contains("Attention is all you need.", index, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleFileExportKeepsLinksAsJumpsInsideItself()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("one.md", "# One\n\nSee [[two]].\n"),
            ("two.md", "# Two\n"));

        ExportReport report = SiteExporter.ExportSingleFile(
            vault.Vault, vault.Builder, Options() with { OutputPath = Path.Combine(_output, "all.html") });

        string html = File.ReadAllText(Path.Combine(_output, "all.html"));

        Assert.Equal(2, report.Pages);
        Assert.Equal(0, report.BrokenLinks);
        Assert.Contains("id=\"page-two\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#page-two\"", html, StringComparison.Ordinal);

        // Self-contained means self-contained: the stylesheet is in the file.
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleFileExportOfOnePageIsJustThatPage()
    {
        RenderTestVault vault = RenderTestVault.Build(("one.md", "# One\n"), ("two.md", "# Two\n"));

        ExportReport report = SiteExporter.ExportSingleFile(
            vault.Vault,
            vault.Builder,
            Options() with { OutputPath = Path.Combine(_output, "one.html") },
            vault.Vault.Index.ByRelativePath("one.md").Single());

        Assert.Equal(1, report.Pages);
        Assert.DoesNotContain("Two", File.ReadAllText(Path.Combine(_output, "one.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTortureVaultExportsWithoutThrowing()
    {
        VaultSnapshot vault = VaultScanner.Scan(FindVault("torture"));
        var builder = new RenderModelBuilder(vault);

        ExportReport report = SiteExporter.ExportSite(vault, builder, Options());

        Assert.True(report.Pages > 5, $"only {report.Pages} pages were exported");
        Assert.True(report.Links > 0);
        Assert.True(
            report.BrokenLinks < report.Links,
            "the torture vault has broken links on purpose, but not only broken links");
    }

    [Fact]
    public void APdfIsWrittenAndIsAPdf()
    {
        RenderTestVault vault = RenderTestVault.Build(
            ("one.md", "# One\n\nSome prose with **bold** and a [[two]] link.\n\n```csharp\nvar x = 1;\n```\n"),
            ("two.md", "# Two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n"));

        string path = Path.Combine(_output, "vault.pdf");

        ExportReport report = PdfExporter.Export(
            vault.Vault.Documents.Where(d => d.IsMarkdown).Select(vault.Builder.Build),
            new PdfOptions { OutputPath = path, Title = "Vault" });

        Assert.Equal(2, report.Pages);
        Assert.Equal(0, report.BrokenLinks);

        byte[] bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length > 1000, $"the PDF is only {bytes.Length} bytes");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void APdfOfATortureVaultSurvivesEverythingInIt()
    {
        VaultSnapshot vault = VaultScanner.Scan(FindVault("torture"));
        var builder = new RenderModelBuilder(vault);
        string path = Path.Combine(_output, "torture.pdf");

        ExportReport report = PdfExporter.Export(
            vault.Documents.Where(d => d.IsMarkdown).Select(builder.Build),
            new PdfOptions { OutputPath = path, Title = "Torture" });

        Assert.True(File.Exists(path));
        Assert.True(report.Pages > 5);
    }

    [Fact]
    public void APdfSaysWhatItCouldNotTypeset()
    {
        RenderTestVault vault = RenderTestVault.Build(("math.md", "$$\nE = mc^2\n$$\n"));

        ExportReport report = PdfExporter.Export(
            [vault.Render("math.md")],
            new PdfOptions { OutputPath = Path.Combine(_output, "math.pdf") });

        Assert.Contains(report.Diagnostics, d => d.Contains("Math", StringComparison.Ordinal));
    }

    private ExportOptions Options() => new() { OutputPath = _output, Title = "Test Vault" };

    private static string Emit(params (string Path, string Content)[] files)
    {
        RenderTestVault vault = RenderTestVault.Build(files);
        RenderDocument rendered = vault.Render(files[0].Path);

        var emitter = new HtmlEmitter(resolution =>
            resolution.Link.IsExternal
                ? resolution.Link.RawTarget
                : resolution.Target is { } target
                    ? SiteExporter.HtmlPathOf(target.RelativePath)
                    : null);

        return emitter.Emit(rendered);
    }

    [GeneratedRegex("href=\"([^\"]+)\"")]
    private static partial Regex HrefPattern();

    private static string FindVault(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "vaults", name);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"the {name} fixture vault was not found.");
    }
}
