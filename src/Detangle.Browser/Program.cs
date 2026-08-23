using System.Reflection;
using Avalonia;
using Avalonia.Browser;
using Detangle.App;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

namespace Detangle.Browser;

/// <summary>
/// The WASM demo's entry point (plan.md section 8, phase 9).
/// <para>
/// The demo exists to prove one claim the website makes: that a Mermaid fence and a DBML
/// fence render with no network and no plugins. Running the same renderer in a browser
/// tab, with the network tab open and empty, is a more convincing argument than a
/// screenshot.
/// </para>
/// </summary>
internal static class Program
{
    /// <summary>Where the demo wiki is unpacked before the scanner is pointed at it.</summary>
    private const string VaultPath = "/samples";

    /// <summary>The argument "?selftest=1" becomes, asking for the full variant matrix.</summary>
    private const string SelfTestFlag = "--selftest";

    /// <summary>
    /// Printed after the matrix so a scraper knows the table is complete rather than
    /// guessing from a timeout. tools/wasm-console.mjs waits for this line.
    /// </summary>
    private const string SelfTestDone = "detangle: selftest complete";

    private static async Task Main(string[] args)
    {
        UnpackSampleVault();

        DetangleApp.StartupVault = VaultPath;
        DetangleApp.ThemeOverride = true;

        // A tab has no file manager and nothing to drag a folder from, and its filesystem
        // goes away when it closes. Saying so once here is what keeps the shell from
        // offering commands that would quietly do nothing.
        DetangleApp.Capabilities = HeadCapabilities.Browser;

        // The host page turns "?page=wiki/schema" into an argument, and "?selftest=1" into
        // the flag below.
        string[] pages = args.Where(argument => argument != SelfTestFlag).ToArray();

        if (pages is [{ Length: > 0 } page, ..])
        {
            DetangleApp.StartupPage = page;
        }

        // The browser console is the only place a diagnosis is visible on this platform,
        // and diagram text drawing is the one thing here that differs from the desktop.
        bool drawsText = Detangle.Rendering.Diagrams.SvgTextCapability.CanDrawText;

        Console.WriteLine($"detangle: svg text draws={drawsText} - {Detangle.Rendering.Diagrams.SvgTextCapability.Diagnosis}");

        if (args.Contains(SelfTestFlag))
        {
            // The full matrix is verbose, so it stays behind the flag: the one-line
            // diagnosis above is what every visitor should see, and this is what someone
            // confirming the defect — or confirming a fix — needs instead.
            Console.WriteLine(Detangle.Rendering.Diagrams.SvgTextSelfTest.Table());

            // The matrix says every delivery of a family collapses. This says which layer
            // did it, and whether handing the renderer a real face repairs it.
            Console.WriteLine(Detangle.Rendering.Diagrams.SvgTextLayerProbe.Table());

            // And the same sixteen cells again with the permissive font lookup in place,
            // which is the only comparison that shows whether it repairs the defect or
            // just moves it.
            Console.WriteLine("with DiagramTypefaces installed:");
            Console.WriteLine(Detangle.Rendering.Diagrams.SvgTextSelfTest.Table(
                Detangle.Rendering.Diagrams.DiagramTypefaces.Install));
            Console.WriteLine(SelfTestDone);
        }

        await BuildAvaloniaApp().StartBrowserAppAsync("out").ConfigureAwait(true);
    }

    /// <summary>Builds the shared application, exactly as the desktop head does.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DetangleApp>()
            .WithInterFont();

    /// <summary>
    /// Writes the embedded demo wiki into the browser's in-memory filesystem.
    /// <para>
    /// The scanner reads files, and a browser has no folder to give it — but the .NET
    /// browser runtime does provide a real in-memory filesystem, so the vault is unpacked
    /// into one at startup and everything downstream works unchanged. No part of this
    /// touches the network.
    /// </para>
    /// </summary>
    private static void UnpackSampleVault()
    {
        Assembly assembly = typeof(Program).Assembly;
        const string Prefix = "Detangle.Browser.Samples.";

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream? stream = assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);

            string path = Path.Combine(VaultPath, RelativePathOf(name[Prefix.Length..]));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, reader.ReadToEnd());
        }
    }

    /// <summary>
    /// Turns an embedded resource name back into a path.
    /// <para>
    /// Resource names replace directory separators with dots, and the file extension is
    /// a dot too, so the last one is the extension and every earlier one was a folder.
    /// The demo vault is named accordingly: no filename in it contains a dot.
    /// </para>
    /// </summary>
    private static string RelativePathOf(string resourceName)
    {
        int extension = resourceName.LastIndexOf('.');

        return extension < 0
            ? resourceName
            : resourceName[..extension].Replace('.', '/') + resourceName[extension..];
    }
}
