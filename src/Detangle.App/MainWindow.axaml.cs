using Avalonia.Controls;

namespace Detangle.App;

/// <summary>
/// The desktop window. It is a frame around <see cref="ShellView"/> and holds no logic of
/// its own: the WASM demo has no windows at all and hosts the same view directly, so
/// anything that lived here would be missing there.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
