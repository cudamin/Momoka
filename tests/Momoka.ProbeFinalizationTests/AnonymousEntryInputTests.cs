namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterSameAssemblyInputs()
    {
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
