using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;

namespace Momoka.Tests;

/// <summary>Explicit anonymous runner for the existing App/Schedule/Run path, never Program.Main's profile resolver.</summary>
internal static class AnonymousSelfCheckHost
{
    internal sealed record Input(string Root, string Shell, string Output);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        Input input;
        try { input = Validate(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine("Anonymous host input rejected: " + error.GetType().Name);
            return 2;
        }
        if (!OperatingSystem.IsWindows()) return 2;
        var code = 1;
        Exception? failure = null;
        var dpiContextAccepted = false;
        using var mutex = new Mutex(false, SelfCheckRun.MutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { Console.Error.WriteLine("Another diagnostic owns the desktop"); return 2; }
        var baseDirectory = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
        var currentDirectory = Environment.CurrentDirectory;
        using var network = new HttpRequests();
        var manifest = new List<object>();
        try
        {
            Directory.CreateDirectory(input.Output);
            var paths = new AppPaths(input.Output);
            paths.EnsureCreated();
            var settings = new AppSettings { DeviceId = "anonymous-selfcheck-host" };
            settings.Servers.Clear();
            settings.Ui.AutoCheckUpdates = false;
            settings.MoviePilot.Enabled = false;
            settings.MoviePilot.AniBtEnabled = false;
            settings.Danmaku.Enabled = false;
            settings.SubtitleTranslation.Enabled = false;
            settings.Mpv.CustomConfigEnabled = false;
            settings.Mpv.SvpEnabled = false;
            new SettingsStore(paths, new RejectSecrets()).Save(settings);
            if (settings.Servers.Count != 0 || settings.LastServerId is not null || settings.LastAccountId is not null)
                throw new InvalidOperationException("Anonymous settings contain an account");

            var shellDirectory = Path.GetDirectoryName(input.Shell)!;
            foreach (var name in new[] { "Momoka.dll", "Momoka.Core.dll", "Momoka.pri", "Momoka.runtimeconfig.json", "Momoka.deps.json" })
            {
                var path = Path.Combine(shellDirectory, name);
                if (!File.Exists(path)) throw new FileNotFoundException("Current Shell payload is incomplete", name);
                manifest.Add(new { path, sha256 = Hash(path) });
            }
            if (Hash(typeof(AppPaths).Assembly.Location) != Hash(Path.Combine(shellDirectory, "Momoka.Core.dll")))
                throw new InvalidOperationException("Host and Shell must load byte-identical current Core");
            var resolver = new AssemblyDependencyResolver(input.Shell);
            Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
            {
                var path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(shellDirectory, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            }
            IntPtr ResolveNative(Assembly assembly, string name)
            {
                var path = resolver.ResolveUnmanagedDllToPath(name);
                return path is null ? IntPtr.Zero : NativeLibrary.Load(path);
            }
            AssemblyLoadContext.Default.Resolving += Resolve;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNative;
            try
            {
                // The installed Foundation auto-initializer stamps this process's AppContext base for MRT/PRI.
                // Only this temporary process changes; no production profile or machine environment is read.
                AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", shellDirectory + Path.DirectorySeparatorChar);
                Environment.CurrentDirectory = shellDirectory;
                if (!SetDllDirectory(shellDirectory)) throw new InvalidOperationException("Native search directory was not set");
                var thread = new Thread(() =>
                {
                    try
                    {
                        dpiContextAccepted = SetProcessDpiAwarenessContext(new IntPtr(-4));
                        var shell = AssemblyLoadContext.Default.LoadFromAssemblyPath(input.Shell);
                        var program = shell.GetType("Momoka.Shell.Program", throwOnError: true)!;
                        var run = program.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic,
                            [typeof(string[]), typeof(AppPaths), typeof(string), typeof(bool)])
                            ?? throw new MissingMethodException("Current Program.Run signature changed");
                        code = (int)run.Invoke(null, [new[] { "--self-check", "--dump-ui", "--screen", "1" }, paths, null, true])!;
                    }
                    catch (TargetInvocationException error) { failure = error.InnerException ?? error; }
                    catch (Exception error) { failure = error; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            finally
            {
                AssemblyLoadContext.Default.Resolving -= Resolve;
                AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveNative;
                SetDllDirectory(null);
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", baseDirectory);
            Environment.CurrentDirectory = currentDirectory;
            mutex.ReleaseMutex();
            if (Directory.Exists(input.Output))
            {
                var report = Path.Combine(input.Output, "logs", "selfcheck-shell.txt");
                var result = new
                {
                    pid = Environment.ProcessId,
                    root = input.Root,
                    shell = input.Shell,
                    input.Output,
                    host = typeof(AnonymousSelfCheckHost).Assembly.Location,
                    manifest,
                    applicationExit = code,
                    dpiContextAccepted,
                    reportExists = File.Exists(report),
                    reportSha256 = File.Exists(report) ? Hash(report) : null,
                    networkRequests = network.Count,
                    failure = failure?.GetType().FullName,
                    coverage = "Actual anonymous App and selfcheck timer/report; zero saved accounts; not a full server selfcheck pass"
                };
                File.WriteAllText(Path.Combine(input.Output, "anonymous-host-receipt.json"), JsonSerializer.Serialize(result, JsonOptions), new UTF8Encoding(false));
            }
        }
        if (failure is not null) { Console.Error.WriteLine("Anonymous host failed: " + failure.GetType().FullName); return 1; }
        if (network.Count != 0) { Console.Error.WriteLine("Anonymous host observed an HTTP request"); return 1; }
        if (!File.Exists(Path.Combine(input.Output, "logs", "selfcheck-shell.txt")))
        {
            Console.Error.WriteLine("Anonymous selfcheck report was not written");
            return 1;
        }
        return code;
    }

    internal static Input Validate(string[] args)
    {
        if (args is not ["anonymous-selfcheck-host", var root, var shell, var output]) throw new ArgumentException("Explicit host arguments required");
        root = Local(root);
        shell = Local(shell);
        output = Local(output);
        if (!File.Exists(Path.Combine(root, "Momoka.sln"))) throw new ArgumentException("Not a worktree root");
        var expected = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release", "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        if (!string.Equals(shell, expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(shell))
            throw new ArgumentException("Only this worktree's current Release Shell is allowed");
        var allowed = Path.Combine(root, "artifacts", "anonymous-selfcheck") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Path.Exists(output))
            throw new ArgumentException("Use a new anonymous evidence directory");
        return new(root, shell, output);
    }

    private static string Local(string path)
    {
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/')
            || path.IndexOf(':', 2) >= 0 || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Ordinary absolute local path required");
        path = Path.GetFullPath(path);
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed) throw new ArgumentException("Fixed local drive required");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reparse paths are not allowed");
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class RejectSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("Anonymous host must not protect credentials");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("Anonymous host must not decrypt credentials");
    }
    private sealed class HttpRequests : EventListener
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string? directory);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
