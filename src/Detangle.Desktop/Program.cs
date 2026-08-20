using Avalonia;

namespace Detangle.Desktop;

internal static class Program
{
    // Avalonia must not use any Avalonia, third-party APIs or any SynchronizationContext-reliant
    // code before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Referenced by the Avalonia designer and by the visual designer tooling.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.DetangleApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
