using Avalonia;
using Velopack;

namespace Detangle.Desktop;

internal static class Program
{
    // Avalonia must not use any Avalonia, third-party APIs or any SynchronizationContext-reliant
    // code before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack has to run before anything else. An install, an update and an
        // uninstall all re-launch this executable with hook arguments, and those runs
        // must do their work and exit rather than opening a window; anything initialized
        // before this point would be initialized during a silent hook for no reason.
        VelopackApp.Build().Run();

        // The desktop head is the one that carries the TextMate grammars, so it is the
        // one that switches them on. It has to precede the window, because ShellView is
        // the only place a DocumentRenderer is constructed and a renderer picks its
        // highlighter then. The export path below does not need it — SiteExporter emits
        // bare <pre><code>, and the PDF writer sets its own type — so it sits above both
        // for one reason only: there is no second place to put it that is still before
        // AppBuilder starts. Lose this line and nothing fails; fenced code just comes out
        // grey, which no test in this repository would notice.
        Highlighting.TextMateCodeHighlighter.Install();

        // "detangle --export-site <vault> <out>" publishes without opening a window,
        // which is how this project's own documentation site is built.
        if (HeadlessExport.TryRun(args, out int exitCode))
        {
            Environment.Exit(exitCode);

            return;
        }

        App.DetangleApp.UpdateService = new VelopackUpdateService();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia designer and by the visual designer tooling.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.DetangleApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
