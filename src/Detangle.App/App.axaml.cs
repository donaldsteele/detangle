using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

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

    /// <summary>
    /// What the running head can do. Each head states its own before the framework
    /// starts, the same way it states its update service; the desktop's is the default
    /// because it is the head where everything here is true.
    /// </summary>
    public static HeadCapabilities Capabilities { get; set; } = HeadCapabilities.Desktop;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// A vault to open at startup. The desktop head takes it from the command line; the
    /// WASM demo points it at the sample wiki it unpacked into the browser's filesystem.
    /// </summary>
    public static string? StartupVault { get; set; }

    /// <summary>
    /// Forces a palette instead of following the operating system. The WASM demo sets it
    /// dark because the page around it is dark; the desktop head leaves it null and takes
    /// whatever the system is set to.
    /// </summary>
    public static bool? ThemeOverride { get; set; }

    /// <summary>
    /// A vault-relative page to open instead of the vault's front door, so a link can
    /// point at the page it is describing rather than at the reader in general.
    /// </summary>
    public static string? StartupPage { get; set; }

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

    private ShellViewModel CreateShell()
    {
        // Follow the desktop's own light or dark setting rather than picking one. An
        // application that opens white on a machine set to dark has ignored the only
        // preference its reader already expressed.
        bool dark = ThemeOverride ?? ActualThemeVariant == ThemeVariant.Dark;

        var viewModel = new ShellViewModel
        {
            UpdateService = UpdateService,
            IsDarkTheme = dark,
            Capabilities = Capabilities,

            // A tab has nowhere to keep a recent list, and a list of folders on a machine
            // the browser cannot reach would be a list of dead links.
            Settings = Capabilities.CanPersistAcrossSessions ? AppSettings.Open() : AppSettings.None,
        };

        viewModel.RefreshRecentVaults();

        if (ThemeOverride is null)
        {
            ActualThemeVariantChanged += (_, _) =>
                viewModel.FollowSystemTheme(ActualThemeVariant == ThemeVariant.Dark);
        }

        if (StartupVault is { Length: > 0 } vault)
        {
            viewModel.OpenVault(vault);

            if (StartupPage is { Length: > 0 } page)
            {
                viewModel.OpenPath(page);
            }
        }

        return viewModel;
    }
}
