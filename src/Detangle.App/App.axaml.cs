using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Detangle.App;

/// <summary>
/// Application entry point shared by the desktop and browser heads.
/// </summary>
/// <remarks>
/// The Avalonia template's ViewLocator is deliberately absent: it resolves views
/// through Activator.CreateInstance, which is trim-hostile and fails silently
/// under Native AOT. Views are wired explicitly instead. See plan.md section 11.
/// </remarks>
public partial class DetangleApp : Application
{
    /// <summary>
    /// How the app checks for updates. The desktop head sets this to the Velopack
    /// implementation before the framework starts; the browser head leaves it null,
    /// because a page cannot update itself.
    /// </summary>
    public static IUpdateService? UpdateService { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// A vault to open at startup. The desktop head takes it from the command line; the
    /// WASM demo points it at the sample wiki it unpacked into the browser's filesystem.
    /// </summary>
    public static string? StartupVault { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                // "detangle path/to/vault" opens straight into that vault, which is how
                // the app is launched from a shell.
                if (desktop.Args is [{ Length: > 0 } path, ..])
                {
                    StartupVault = path;
                }

                desktop.MainWindow = new MainWindow { DataContext = CreateShell() };
                break;

            // A browser tab has no windows, so the same view is hosted directly. It is
            // the same control tree either way: a demo that ran different code would not
            // be demonstrating the product.
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new ShellView { DataContext = CreateShell() };
                break;

            default:
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ShellViewModel CreateShell()
    {
        var viewModel = new ShellViewModel { UpdateService = UpdateService };

        if (StartupVault is { Length: > 0 } vault)
        {
            viewModel.OpenVault(vault);
        }

        return viewModel;
    }
}
