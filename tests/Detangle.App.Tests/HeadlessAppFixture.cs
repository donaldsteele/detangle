using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// A headless Avalonia application, started once for the whole suite.
/// <para>
/// Avalonia controls are thread-affine, so the platform is set up on a thread of its own
/// that then runs the dispatcher loop; tests marshal onto it with <see cref="Invoke"/>.
/// Without this the control factory could only be tested by not calling it, which is not
/// a test of anything.
/// </para>
/// </summary>
public sealed class HeadlessAppFixture : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _uiThread;

    /// <summary>Starts the headless platform and waits for it to come up.</summary>
    public HeadlessAppFixture()
    {
        using var ready = new ManualResetEventSlim();

        _uiThread = new Thread(() =>
        {
            AppBuilder.Configure<HeadlessTestApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();

            ready.Set();

            Dispatcher.UIThread.MainLoop(_shutdown.Token);
        })
        {
            IsBackground = true,
            Name = "Avalonia headless UI",
        };

        if (OperatingSystem.IsWindows())
        {
            // Windows wants an STA thread for anything that might touch OLE; the headless
            // platform itself does not care, and the other OSes have no apartments.
            _uiThread.SetApartmentState(ApartmentState.STA);
        }

        _uiThread.Start();

        ready.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>Runs a function on the UI thread and returns its result.</summary>
    public T Invoke<T>(Func<T> function) => Dispatcher.UIThread.Invoke(function);

    /// <summary>Runs an action on the UI thread.</summary>
    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

    /// <inheritdoc />
    public void Dispose()
    {
        _shutdown.Cancel();
        _uiThread.Join(TimeSpan.FromSeconds(5));
        _shutdown.Dispose();
    }

    private sealed class HeadlessTestApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}

/// <summary>Shares one headless application across every control test.</summary>
[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessAppFixture>
{
    /// <summary>The collection's name.</summary>
    public const string Name = "headless-avalonia";
}
