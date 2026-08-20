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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(
                theme: ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                    ? Rendering.DiagramTheme.Dark
                    : Rendering.DiagramTheme.Light);

            // "detangle path/to/vault" opens straight into that vault, which is how the
            // app is launched from a shell and from the phase 2 verification run.
            if (desktop.Args is [{ Length: > 0 } path, ..])
            {
                viewModel.OpenVault(path);
            }

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
