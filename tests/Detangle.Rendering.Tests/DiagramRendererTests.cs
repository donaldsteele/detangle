using Detangle.Rendering.Diagrams;
using Detangle.Rendering.Model;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the diagram backends: that Mermaid and DBML both render offline with no
/// runtime dependencies, that failures become diagnostics rather than exceptions, and
/// that the cache keys on everything that changes the picture.
/// </summary>
public class DiagramRendererTests
{
    private static readonly MermaiderDiagramRenderer Renderer = new();

    [Fact]
    public async Task RendersMermaidToSvg()
    {
        DiagramResult result = await Renderer.RenderAsync(
            DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Contains("<svg", result.Svg, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    [Fact]
    public async Task RendersSeveralMermaidDiagramTypes()
    {
        foreach (string source in (string[])
        [
            "sequenceDiagram\n  Alice->>Bob: Hello\n",
            "classDiagram\n  class Animal\n",
            "stateDiagram-v2\n  [*] --> Idle\n",
            "erDiagram\n  USER ||--o{ ORDER : places\n",
        ])
        {
            DiagramResult result = await Renderer.RenderAsync(
                DiagramKind.Mermaid, source, DiagramTheme.Light, TestContext.Current.CancellationToken);

            Assert.Contains("<svg", result.Svg, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RendersDbmlThroughTheErdEmitter()
    {
        DiagramResult result = await Renderer.RenderAsync(
            DiagramKind.Dbml,
            "Table users {\n  id int [pk]\n}\nTable orders {\n  user_id int [ref: > users.id]\n}\n",
            DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Contains("<svg", result.Svg, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ReportsWhatADbmlDiagramOmits()
    {
        DiagramResult result = await Renderer.RenderAsync(
            DiagramKind.Dbml,
            "Table users {\n  id int [pk]\n  role role_enum [default: 'member']\n}\n"
                + "Enum role_enum {\n  member\n}\n",
            DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Contains(result.Diagnostics, d => d.Contains("1 enum", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Contains("default value", StringComparison.Ordinal));
        Assert.NotEmpty(result.Svg);
    }

    [Fact]
    public async Task ABrokenMermaidFenceBecomesADiagnosticNotAnException()
    {
        DiagramResult result = await Renderer.RenderAsync(
            DiagramKind.Mermaid, "not a diagram at all\n???\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Empty(result.Svg);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task ADbmlBlockWithNoTablesSaysSo()
    {
        DiagramResult result = await Renderer.RenderAsync(
            DiagramKind.Dbml, "// just a comment\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Empty(result.Svg);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task ThemeChangesTheRenderedSvg()
    {
        DiagramResult light = await Renderer.RenderAsync(
            DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, TestContext.Current.CancellationToken);
        DiagramResult dark = await Renderer.RenderAsync(
            DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Dark, TestContext.Current.CancellationToken);

        Assert.NotEqual(light.Svg, dark.Svg);
    }

    [Theory]
    [InlineData("graph TD;")]
    [InlineData("graph TD")]
    [InlineData("flowchart LR;")]
    public async Task AcceptsHeadersWrittenWithATrailingSemicolon(string header)
    {
        // mermaid.js and the Mermaid docs both accept "graph TD;"; Mermaider rejects the
        // header outright, so a perfectly valid diagram would otherwise fail to render.
        DiagramResult result = await Renderer.RenderAsync(
            DiagramKind.Mermaid, header + "\n  A-->B\n", DiagramTheme.Light,
            TestContext.Current.CancellationToken);

        Assert.Contains("<svg", result.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void MeasuresSvgFromItsViewBox()
    {
        (double width, double height) = MermaiderDiagramRenderer.MeasureSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 320 240\"></svg>");

        Assert.Equal(320, width);
        Assert.Equal(240, height);
    }

    [Fact]
    public void FallsBackToWidthAndHeightAttributes()
    {
        (double width, double height) = MermaiderDiagramRenderer.MeasureSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\"></svg>");

        Assert.Equal(100, width);
        Assert.Equal(50, height);
    }

    [Fact]
    public async Task TheCacheServesASecondRenderOfTheSameDiagram()
    {
        var counting = new CountingRenderer();
        var cache = new CachingDiagramRenderer(counting);

        await cache.RenderAsync(DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, TestContext.Current.CancellationToken);
        await cache.RenderAsync(DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Equal(1, counting.Calls);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Theory]
    [InlineData(DiagramKind.Dbml, "graph TD;\n  A-->B;\n", DiagramTheme.Light, "backend")]
    [InlineData(DiagramKind.Mermaid, "graph TD;\n  A-->C;\n", DiagramTheme.Light, "backend")]
    [InlineData(DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Dark, "backend")]
    [InlineData(DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, "other-backend")]
    public void EveryInputChangesTheCacheKey(
        DiagramKind kind, string source, DiagramTheme theme, string backend)
    {
        string baseline = CachingDiagramRenderer.KeyFor(
            DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, "backend");

        Assert.NotEqual(baseline, CachingDiagramRenderer.KeyFor(kind, source, theme, backend));
    }

    [Fact]
    public async Task FailedRendersAreNotPersisted()
    {
        var store = new RecordingStore();
        var cache = new CachingDiagramRenderer(Renderer, store);

        await cache.RenderAsync(DiagramKind.Mermaid, "not a diagram at all\n???\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        // A broken fence is one keystroke from working; persisting the failure would
        // outlive the mistake.
        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task SuccessfulRendersArePersistedAndReadBack()
    {
        var store = new RecordingStore();
        var counting = new CountingRenderer();

        await new CachingDiagramRenderer(counting, store)
            .RenderAsync(DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        DiagramResult second = await new CachingDiagramRenderer(counting, store)
            .RenderAsync(DiagramKind.Mermaid, "graph TD;\n  A-->B;\n", DiagramTheme.Light, TestContext.Current.CancellationToken);

        Assert.Single(store.Written);
        Assert.Equal(1, counting.Calls);
        Assert.Contains("<svg", second.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void FenceLanguagesBecomeDiagramBlocks()
    {
        var options = new RenderOptions { DiagramRenderer = Renderer };

        IReadOnlyList<RenderBlock> blocks = RenderTestVault.Build(
            Core.Vault.VaultFlavor.Generic,
            options,
            ("page.md", "```mermaid\ngraph TD;\n  A-->B;\n```\n\n```dbml\nTable t {\n  id int [pk]\n}\n```\n"))
            .Body("page.md");

        var mermaid = Assert.IsType<DiagramRenderBlock>(blocks[0]);
        var dbml = Assert.IsType<DiagramRenderBlock>(blocks[1]);

        Assert.Equal(DiagramKind.Mermaid, mermaid.Kind);
        Assert.True(mermaid.IsRendered);
        Assert.Equal(DiagramKind.Dbml, dbml.Kind);
        Assert.True(dbml.IsRendered);
        Assert.NotNull(dbml.Schema);
    }

    [Fact]
    public void WithNoBackendADiagramFenceKeepsItsSource()
    {
        var diagram = Assert.IsType<DiagramRenderBlock>(
            RenderTestVault.BodyOf("```mermaid\ngraph TD;\n  A-->B;\n```\n")[0]);

        Assert.False(diagram.IsRendered);
        Assert.Contains("A-->B", diagram.Source, StringComparison.Ordinal);
    }

    private sealed class CountingRenderer : IDiagramRenderer
    {
        private readonly MermaiderDiagramRenderer _inner = new();

        public int Calls { get; private set; }

        public bool IsAvailable => true;

        public Task<DiagramResult> RenderAsync(
            DiagramKind kind, string source, DiagramTheme theme, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _inner.RenderAsync(kind, source, theme, cancellationToken);
        }
    }

    private sealed class RecordingStore : IDiagramCacheStore
    {
        public Dictionary<string, string> Written { get; } = new(StringComparer.Ordinal);

        public string? Read(string key) => Written.TryGetValue(key, out string? svg) ? svg : null;

        public void Write(string key, string svg) => Written[key] = svg;
    }
}
