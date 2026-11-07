namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterSameAssemblyInputs()
    {
        Test("same-assembly wrapper leaves the observed application visible", () =>
        {
            var root = FindSourceRoot();
            var script = File.ReadAllText(Path.Combine(root, "tests/Momoka.ProbeFinalizationTests/Run-AnonymousSameAssembly.ps1"));
            var launch = script.Split('\n').Single(line => line.Contains("$probe = Start-Process", StringComparison.Ordinal));
            Visible(launch);
            var rejected = false;
            try { Visible(launch + " -WindowStyle Hidden"); }
            catch (InvalidOperationException) { rejected = true; }
            Ensure(rejected, "the old hidden-window launch must be rejected");
            Ensure(launch.Contains("@('--anonymous-selfcheck', $OutputDirectory)", StringComparison.Ordinal), "anonymous arguments unchanged");
            Ensure(script.Contains("if ($BuildOnly) { exit 0 }", StringComparison.Ordinal), "build-only remains noninteractive");
            return Task.CompletedTask;

            static void Visible(string line)
            {
                if (line.Contains("-WindowStyle", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The observed app must use its normal window show state");
            }
            static string FindSourceRoot()
            {
                for (var path = Environment.CurrentDirectory; path is not null; path = Path.GetDirectoryName(path))
                    if (File.Exists(Path.Combine(path, "Momoka.sln"))) return path;
                throw new InvalidOperationException("Current worktree root not found");
            }
        });
        Test("same-assembly anonymous entry accepts only new local output", () =>
        {
            var root = Path.Combine(_output, "same-assembly-inputs");
            Directory.CreateDirectory(root);
            var output = Path.Combine(root, "artifacts", "anonymous-selfcheck", "new-run");
            Equal(output, Momoka.Shell.AnonymousEntry.ValidateOutput(root, ["--validate-anonymous-input", output]), "same tree anonymous output");
            foreach (var bad in new[] { "relative", "https://invalid", @"\\host\share", @"C:relative", Path.Combine(root, "elsewhere") })
                Reject(["--anonymous-selfcheck", bad]);
            foreach (var args in new[] { Array.Empty<string>(), new[] { "unknown", output }, new[] { "--anonymous-selfcheck" } }) Reject(args);
            Directory.CreateDirectory(output);
            Reject(["--anonymous-selfcheck", output]);
            return Task.CompletedTask;

            void Reject(string[] args)
            {
                try { Momoka.Shell.AnonymousEntry.ValidateOutput(root, args); }
                catch (ArgumentException) { return; }
                throw new InvalidOperationException("Invalid anonymous output was accepted");
            }
        });
    }
}
