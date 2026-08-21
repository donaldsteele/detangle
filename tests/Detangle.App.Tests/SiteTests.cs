using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Guards on the website (plan.md section 9).
/// <para>
/// The site is hand-written static HTML with a strict content security policy, and both
/// of those are easy to break silently: an added script breaks the policy, a renamed doc
/// page breaks a link, and neither shows up until somebody loads the deployed page. These
/// are the checks that would otherwise be a manual pass before every deploy.
/// </para>
/// </summary>
public partial class SiteTests
{
    [Fact]
    public void TheInlineScriptMatchesTheHashInTheSecurityPolicy()
    {
        // The theme pre-paint has to be inline to run before the first frame, so it is
        // allowlisted by hash. Editing it without updating the hash means the page loads
        // in the wrong palette and the console fills with policy violations.
        Match script = InlineScript().Match(Read("index.html"));

        Assert.True(script.Success, "index.html no longer has the theme pre-paint script");

        string hash = "sha256-" + Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(script.Groups[1].Value)));

        Assert.Contains(hash, Read("_headers"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSiteLoadsNothingFromAnybodyElse()
    {
        string html = Read("index.html");

        foreach (Match match in ResourceAttribute().Matches(html))
        {
            string url = match.Groups[3].Value;

            Assert.False(
                url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("//", StringComparison.Ordinal),
                $"the page loads \"{url}\" from another origin");
        }
    }

    [Fact]
    public void ThePolicyForbidsEverythingByDefault()
    {
        string headers = Read("_headers");

        Assert.Contains("default-src 'none'", headers, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", headers, StringComparison.Ordinal);
        Assert.Contains("Strict-Transport-Security", headers, StringComparison.Ordinal);

        // 'unsafe-inline' for scripts would make the hash allowlist pointless.
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", headers, StringComparison.Ordinal);

        // WebAssembly needs its own relaxation, and it must be scoped to the demo path.
        Assert.Contains("/demo/*", headers, StringComparison.Ordinal);
        Assert.Contains("'wasm-unsafe-eval'", headers, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDocumentationLinkPointsAtAPageThatExists()
    {
        string html = Read("index.html");
        var linked = new List<string>();

        foreach (Match match in DocLink().Matches(html))
        {
            linked.Add(match.Groups[1].Value);
        }

        // The plan's doclink grid is nine pages, which is the point: enough depth that a
        // skeptic can judge the product before downloading it.
        Assert.Equal(9, linked.Count);

        foreach (string page in linked)
        {
            string markdown = Path.Combine(Root, "docs", Path.ChangeExtension(page, ".md"));

            Assert.True(File.Exists(markdown), $"the site links to docs/{page}, which nothing generates");
        }
    }

    [Fact]
    public void EveryLocalAssetTheSiteReferencesIsCheckedIn()
    {
        string html = Read("index.html");

        foreach (Match match in ResourceAttribute().Matches(html))
        {
            string url = match.Groups[3].Value.Split('#')[0];

            // Generated at deploy time: the docs come out of Detangle's own exporter and
            // the demo out of the WebAssembly build.
            if (url.Length == 0
                || url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("docs/", StringComparison.Ordinal)
                || url.StartsWith("demo/", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(
                File.Exists(Path.Combine(Root, "site", url)),
                $"index.html references {url}, which is not in site/");
        }
    }

    [Fact]
    public void TheDemoIsBuiltFromTheSampleWiki()
    {
        // The demo's whole claim is that it renders a Mermaid and a DBML page with no
        // network. Both fences have to actually be in the wiki it ships.
        string samples = Path.Combine(Root, "samples");

        Assert.Contains(
            "```mermaid",
            File.ReadAllText(Path.Combine(samples, "concepts", "self-attention.md")),
            StringComparison.Ordinal);

        Assert.Contains(
            "```dbml",
            File.ReadAllText(Path.Combine(samples, "wiki", "schema.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoSampleFilenameContainsADotBesidesItsExtension()
    {
        // The WASM demo unpacks the wiki from embedded resources, whose names replace
        // directory separators with dots. A dot inside a filename would come back out as
        // a folder, and the demo would open an empty vault.
        foreach (string file in Directory.GetFiles(Path.Combine(Root, "samples"), "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Path.Combine(Root, "samples"), file);

            Assert.Equal(1, relative.Count(c => c == '.'));
        }
    }

    [Fact]
    public void TheSiteDeployBuildsTheDemoAndTheDocs()
    {
        string workflow = File.ReadAllText(
            Path.Combine(Root, ".github", "workflows", "site.yml"));

        Assert.Contains("--export-site docs public/docs", workflow, StringComparison.Ordinal);
        Assert.Contains("Detangle.Browser.csproj", workflow, StringComparison.Ordinal);

        // And it refuses to publish a site whose demo iframe would be empty.
        Assert.Contains("test -d public/demo/_framework", workflow, StringComparison.Ordinal);
    }

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(Root, "site", fileName));

    // The browser hashes the element's content exactly as written, newlines included, so
    // the capture must not trim them.
    [GeneratedRegex(@"<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InlineScript();

    // Only the attributes that actually load something: any src, and the href of a
    // <link>. An <a href> to somebody else's site is a link, not a dependency.
    [GeneratedRegex(@"(?:<link\b(?![^>]*rel=""canonical"")[^>]*?\b(href)|(src))=""([^""]*)""")]
    private static partial Regex ResourceAttribute();

    [GeneratedRegex("href=\"docs/([a-z-]+\\.html)\"")]
    private static partial Regex DocLink();

    private static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Detangle.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the repository root was not found above the test binaries.");
    }
}
