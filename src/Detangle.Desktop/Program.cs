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
