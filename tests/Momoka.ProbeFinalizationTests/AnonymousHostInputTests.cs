namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterAnonymousHostInputs()
    {
        foreach (var args in new[] { Array.Empty<string>(), new[] { "anonymous-selfcheck-host" }, new[] { "unknown" } })
            Test("anonymous host rejects arity " + args.Length, () =>
            {
                Rejected(() => AnonymousSelfCheckHost.Validate(args));
                return Task.CompletedTask;
            });
        Test("anonymous host accepts only new same-tree local paths", () =>
        {
            var root = Path.Combine(_output, "anonymous-inputs");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Momoka.sln"), "fixture");
            var shell = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release", "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(shell)!);
            File.WriteAllText(shell, "input boundary only, never loaded");
            var output = Path.Combine(root, "artifacts", "anonymous-selfcheck", "new-run");
            var valid = AnonymousSelfCheckHost.Validate(["anonymous-selfcheck-host", root, shell, output]);
            Equal(output, valid.Output, "output stays under anonymous worktree evidence");
            foreach (var invalid in new[] { "relative", "https://invalid", @"\\host\share", @"C:relative", Path.Combine(root, "outside") })
                Rejected(() => AnonymousSelfCheckHost.Validate(["anonymous-selfcheck-host", root, shell, invalid]));
            Rejected(() => AnonymousSelfCheckHost.Validate(["anonymous-selfcheck-host", root, Path.Combine(root, "other.dll"), output]));
            Directory.CreateDirectory(output);
            Rejected(() => AnonymousSelfCheckHost.Validate(["anonymous-selfcheck-host", root, shell, output]));
            return Task.CompletedTask;
        });
        Test("anonymous host entry rejects unknown input before loading UI", () =>
        {
            Equal(2, AnonymousSelfCheckHost.Run(["unknown"]), "invalid input has a nonzero code");
            return Task.CompletedTask;
        });

        static void Rejected(Action run)
        {
            try { run(); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("Anonymous input must be rejected");
        }
    }
}
