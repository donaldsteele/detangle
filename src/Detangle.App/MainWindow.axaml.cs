using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Styling;
using Detangle.Rendering.Controls;
using Detangle.Rendering.Model;

namespace Detangle.App;

/// <summary>
/// The phase 2 reader window: pick a document on the left, read it on the right.
/// <para>
/// Rendering happens in code rather than through a data template because the output is a
/// control tree built per document, not a repeated item shape — and because link
/// activation has to reach the view model with the resolution attached.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private readonly DocumentRenderer _renderer;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _renderer = new DocumentRenderer(
            ActualThemeVariant == ThemeVariant.Dark ? DocumentTheme.Dark : DocumentTheme.Light);

        _renderer.LinkActivated += OnLinkActivated;

        OpenButton.Click += (_, _) => ViewModel?.OpenVault(VaultPathBox.Text ?? string.Empty);
        DataContextChanged += OnDataContextChanged;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Render(viewModel.RenderedDocument);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.RenderedDocument))
        {
            Render(ViewModel?.RenderedDocument);
        }
    }

    private void Render(RenderDocument? document)
    {
        DocumentHost.Content = document is null ? null : _renderer.Render(document);
        DocumentScroller.Offset = default;
    }

    private void OnLinkActivated(object? sender, LinkActivatedEventArgs e)
    {
        if (e.ExternalUrl is { Length: > 0 } url)
        {
            // External links leave the app entirely; the vault never navigates to them.
            OpenExternal(url);
            return;
        }

        if (e.Resolution.Target is { } target)
        {
            ViewModel?.Navigate(target.RelativePath);
        }
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or PlatformNotSupportedException)
        {
            // A desktop with no registered handler is not an error worth interrupting
            // reading for.
        }
    }
}
