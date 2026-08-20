using Detangle.Core.Parsing;
using Markdig;

namespace Detangle.Rendering.Markdown;

/// <summary>
/// The Markdig pipeline the reader parses with.
/// <para>
/// Extensions are listed one by one rather than through <c>UseAdvancedExtensions()</c>
/// (plan.md section 11): the bundle pulls in emoji substitution, auto-identifiers and
/// smart typography that silently rewrite a vault's text, and a viewer that shows
/// something other than what the file says is worse than one missing a feature.
/// </para>
/// <para>
/// HTML is parsed but never rendered. Disabling the HTML parsers outright looked safer
/// and was in fact worse: with them off, "&lt;!-- [[link]] --&gt;" stops being a comment and
/// the wikilink inside it becomes a real link — one the graph, which parses with HTML on,
/// correctly ignores. A reader that shows links the graph denies is the exact failure
/// Detangle exists to fix, so HTML is recognised here and then dropped at render time.
/// Nothing in this app can execute it: the output is an Avalonia control tree, not a DOM.
/// </para>
/// </summary>
public static class RenderPipeline
{
    /// <summary>The shared pipeline. Markdig pipelines are immutable and thread-safe.</summary>
    public static MarkdownPipeline Instance { get; } = Build();

    /// <summary>Builds a reader pipeline.</summary>
    public static MarkdownPipeline Build() =>
        new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseWikiLinks()
            .UseAdmonitions()
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseFootnotes()
            .UseDefinitionLists()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseMathematics()
            .Build();
}
