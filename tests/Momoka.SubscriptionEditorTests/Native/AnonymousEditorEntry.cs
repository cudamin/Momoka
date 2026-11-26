using System.Diagnostics.Tracing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;

namespace Momoka.Shell;

/// <summary>Injected only by AnonymousLoad.targets; never included by the product entry.</summary>
internal static class AnonymousEditorEntry
{
    [STAThread]
    internal static int Main(string[] args)
    {
        var root = FindRoot();
        string output;
        try { output = AnonymousLoadInput.Validate(root, args); }
        catch (Exception error) { Console.Error.WriteLine("Anonymous editor input rejected: " + error.GetType().Name); return 2; }
        using var mutex = new Mutex(false, SelfCheckRun.MutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { Console.Error.WriteLine("Desktop verification is occupied"); return 2; }
        using var network = new Requests();
        try
        {
            var paths = new AppPaths(output);
            paths.EnsureCreated();
            var sources = new[] { "src/Momoka.Shell/App.xaml.cs", "src/Momoka.Shell/App.xaml",
                "src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml", "src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml.cs",
                "src/Momoka.Shell/Theme/ThemeHost.cs", "src/Momoka.Shell/Theme/Palette.xaml" };
            var before = sources.ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
            WinRT.ComWrappersSupport.InitializeComWrappers();
            AnonymousEditorApp? app = null;
            Application.Start(_ =>
            {
                var queue = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
                app = new AnonymousEditorApp(paths);
            });
            var after = sources.ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
            var passed = app is { Passed: true } && network.Count == 0 && sources.All(name => before[name] == after[name]);
            var receipt = new { passed, pid = Environment.ProcessId, executable = Environment.ProcessPath,
                output, httpRequests = network.Count, loaded = app?.Loaded, checks = app?.Checks,
                failureType = app?.FailureType, sourcesBefore = before, sourcesAfter = after,
                coverage = "Actual App resources and editor XBF/templates; no services, settings, accounts or HTTP; no pixel/input acceptance" };
            File.WriteAllText(Path.Combine(output, "anonymous-editor-load.json"), JsonSerializer.Serialize(receipt,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return passed ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine("Anonymous editor host failed: " + error.GetType().FullName); return 1; }
        finally { mutex.ReleaseMutex(); }
    }
    private static string FindRoot()
    {
        for (var path = AppContext.BaseDirectory; path is not null; path = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)))
            if (File.Exists(Path.Combine(path, "Momoka.sln"))) return path;
        throw new InvalidOperationException("Host output must remain in its worktree");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class Requests : EventListener
    {
        private int _count;
        internal int Count => Volatile.Read(ref _count);
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Net.Http") EnableEvents(source, EventLevel.Informational);
        }
        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            if (args.EventName == "RequestStart") Interlocked.Increment(ref _count);
        }
    }
}

// ProbeShell=true is the existing App constructor boundary that skips ShellServices.Build and settings.
// Do not call App.OnLaunched: the test must never start ordinary probes, services or navigation.
internal sealed class AnonymousEditorApp(AppPaths paths) : App(new StartupOptions
{
    ActivationEventName = "anonymous-editor-load-unused",
    Paths = paths,
    ProbeShell = true
})
{
    private HostWindow? _window;
    internal bool Passed { get; private set; }
    internal bool Loaded { get; private set; }
    internal string? FailureType { get; private set; }
    internal List<string> Checks { get; } = [];

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var root = new Grid { Padding = new Thickness(16) };
        ThemeHost.Register(root);
        _window = new HostWindow { Content = root };
        _window.Closed += Exit;
        root.Loaded += async (_, _) =>
        {
            try
            {
                ThemeHost.Apply("midnight");
                using var json = JsonDocument.Parse("""{"id":1,"name":"Anonymous editor","type":"电视剧","media_source":"themoviedb","media_id":"1","season":1,"total_episode":12}""");
                var editor = new MoviePilotSubscriptionEditor();
                editor.Set(MoviePilotSubscription.Parse(json.RootElement)!);
                root.Children.Add(editor);
                await LayoutAsync(root);
                Loaded = editor.IsLoaded && editor.ActualWidth > 0 && editor.ActualHeight > 0;
                if (!Loaded) throw new InvalidOperationException("Editor was not actually loaded");
                VerifyAccent(editor);
                ThemeHost.Apply("graphite");
                await LayoutAsync(root);
                VerifyAccent(editor);
                Checks.Add("actual editor XBF/templates loaded; both fields and three states follow initial and changed accent");
                Passed = true;
            }
            catch (Exception error) { FailureType = error.GetType().FullName; }
            finally { _window?.Close(); }
        };
        _window.Show(false, ScreenPlacement.NotThePrimary, new WindowBounds(0, 0, 900, 800));
    }
    private static async Task LayoutAsync(FrameworkElement root)
    {
        root.UpdateLayout();
        foreach (var control in Descendants(root).OfType<Control>()) control.ApplyTemplate();
        await Task.Delay(150);
        root.UpdateLayout();
    }
    private static void VerifyAccent(MoviePilotSubscriptionEditor editor)
    {
        foreach (var name in new[] { "SubscriptionSitesInput", "FilterGroupsInput" })
        {
            var picker = (ToggleButton)editor.FindName(name);
            picker.IsChecked = true;
            foreach (var state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                var brush = picker.Resources["ToggleButtonBorderBrush" + state] as SolidColorBrush;
                if (brush is null || brush.Color != ThemeHost.ToColor(ThemeHost.Current.Colors.Accent))
                    throw new InvalidOperationException("Editor checked border did not follow the active theme");
            }
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
