using System.Reflection;

namespace Detangle.App;

/// <summary>
/// Placeholder shell view model. Phase 4 replaces this with the real three-pane shell.
/// </summary>
public sealed class MainWindowViewModel
{
    public string Title => "Detangle";

    public string Tagline => "Read the wiki the model actually wrote.";

    public string Version =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? "0.1.0";
}
