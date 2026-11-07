using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;

namespace Momoka.Shell;

/// <summary>Test-only startup injected into the unchanged Shell assembly; never shipped by the product project.</summary>
internal static class AnonymousEntry
{
    [STAThread]
    internal static int Main(string[] args) => Run(args);

    internal static int Run(string[] args)
    {
        var root = FindRoot();
        string output;
        try { output = ValidateOutput(root, args); }
        catch (Exception error) { Console.Error.WriteLine("Anonymous entry rejected: " + error.GetType().Name); return 2; }
        if (args[0] == "--validate-anonymous-input") return 0;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var mutex = new Mutex(false, SelfCheckRun.MutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return 2;
        var exit = 1;
        using var network = new Requests();
        try
        {
            var paths = new AppPaths(output);
            paths.EnsureCreated();
            var settings = new AppSettings { DeviceId = "same-assembly-anonymous-selfcheck" };
            settings.Servers.Clear();
            settings.Ui.AutoCheckUpdates = false;
            settings.MoviePilot.Enabled = false;
            settings.MoviePilot.AniBtEnabled = false;
            settings.Danmaku.Enabled = false;
            settings.SubtitleTranslation.Enabled = false;
            settings.Mpv.CustomConfigEnabled = false;
            settings.Mpv.SvpEnabled = false;
            new SettingsStore(paths, new RejectSecrets()).Save(settings);
            var sources = new[] { "src/Momoka.Shell/Program.cs", "src/Momoka.Shell/App.xaml.cs", "src/Momoka.Shell/ShellSelfCheck.cs",
                "src/Momoka.Shell/ShellSelfCheck.Run.cs", "src/Momoka.Shell/ShellSelfCheck.Finalization.cs",
                "src/Momoka.Shell/Composition/ShellServices.cs" };
            var before = sources.ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
            var program = typeof(AnonymousEntry).Assembly.GetType("Momoka.Shell.Program", true)!;
            var run = program.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(string[]), typeof(AppPaths), typeof(string), typeof(bool)])!;
            exit = (int)run.Invoke(null, [new[] { "--self-check", "--dump-ui", "--screen", "1" }, paths, null, true])!;
            var selfCheck = typeof(AnonymousEntry).Assembly.GetType("Momoka.Shell.ShellSelfCheck", true)!;
            var ticks = (int)selfCheck.GetField("_ticks", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var report = Path.Combine(paths.LogDirectory, "selfcheck-shell.txt");
            var after = sources.ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
            var receipt = new
            {
                pid = Environment.ProcessId,
                executable = Environment.ProcessPath,
                entryAssembly = Assembly.GetEntryAssembly()?.Location,
                assemblyName = typeof(AnonymousEntry).Assembly.GetName().Name,
                baseDirectory = AppContext.BaseDirectory,
                output,
                instrumented = true,
                actualTimerTicks = ticks,
                applicationExit = exit,
                httpRequests = network.Count,
                reportExists = File.Exists(report),
                reportSha256 = File.Exists(report) ? Hash(report) : null,
                sourcesBefore = before,
                sourcesAfter = after,
                coverage = "Actual same-assembly App/Program.Run/ShellSelfCheck timer with fresh zero accounts; not a full server selfcheck pass"
            };
            File.WriteAllText(Path.Combine(output, "same-assembly-receipt.json"), JsonSerializer.Serialize(receipt,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            if (network.Count != 0 || !File.Exists(report) || ticks < 3 || sources.Any(name => before[name] != after[name])) return 1;
            return exit;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Anonymous same-assembly host failed: " + error.GetType().FullName);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static string ValidateOutput(string root, string[] args)
    {
        if (args is not ["--anonymous-selfcheck" or "--validate-anonymous-input", var output])
            throw new ArgumentException("Explicit anonymous output required");
        if (output.Length < 3 || !char.IsAsciiLetter(output[0]) || output[1] != ':' || output[2] is not ('\\' or '/')
            || output.IndexOf(':', 2) >= 0 || !Path.IsPathFullyQualified(output)) throw new ArgumentException("Absolute local path required");
        output = Path.GetFullPath(output);
        var prefix = Path.Combine(Path.GetFullPath(root), "artifacts", "anonymous-selfcheck") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Path.Exists(output))
            throw new ArgumentException("New worktree anonymous directory required");
        if (new DriveInfo(Path.GetPathRoot(output)!).DriveType != DriveType.Fixed) throw new ArgumentException("Fixed local drive required");
        for (var current = output; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reparse paths prohibited");
        return output;
    }

    private static string FindRoot()
    {
        for (var path = AppContext.BaseDirectory; path is not null; path = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)))
            if (File.Exists(Path.Combine(path, "Momoka.sln"))) return path;
        throw new InvalidOperationException("Host output must remain in its worktree");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class RejectSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("No anonymous credentials");
        public string Unprotect(string value) => throw new InvalidOperationException("No anonymous credentials");
    }
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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
