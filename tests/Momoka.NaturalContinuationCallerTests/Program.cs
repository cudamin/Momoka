using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Diagnostics;

namespace Momoka.NaturalContinuationCallerTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !Path.IsPathRooted(args[0]) || !Path.IsPathRooted(args[1]))
        {
            Console.Error.WriteLine("Pass current worktree absolute root and a fresh local artifact directory.");
            return 2;
        }
        var root = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var bound = JsonDocument.Parse(SourceFacts.Json);
        if (!root.TrimEnd(Path.DirectorySeparatorChar).Equals(bound.RootElement.GetProperty("workspaceRoot").GetString(), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Current source inputs must be the worktree bound by this build.");
            return 2;
        }
        var output = Path.GetFullPath(args[1]);
        if (!output.StartsWith(Path.Combine(root, "artifacts", "r046-caller") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output))
        {
            Console.Error.WriteLine("Outputs must be a NEW directory inside this tree's artifacts/r046-caller.");
            return 2;
        }
        Directory.CreateDirectory(output);
        Log.UseSink(NullLogSink.Instance);
        try
        {
            VerifySources(root);
            await File.WriteAllTextAsync(Path.Combine(output, "source-facts.json"), SourceFacts.Json + "\n").ConfigureAwait(false);
            var assemblies = new[] { typeof(Program).Assembly, typeof(Momoka.Playback.PlaybackContinuation).Assembly };
            var runtime = assemblies.Select(assembly => new
            {
                assembly = assembly.GetName().Name,
                path = assembly.Location,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
            });
            await File.WriteAllTextAsync(Path.Combine(output, "runtime-boundaries.json"),
                JsonSerializer.Serialize(new
                {
                    workspace = root,
                    sdk = "10.0.400",
                    real = "verbatim VM caller/NowPlaying/Leave/Stop + Core lease/session/navigation/DTOs",
                    fake = "Play/Validate/Stop outcome, readonly anonymous metadata and inert presentation",
                    assemblies = runtime,
                    coreServiceOrNativeVerified = false,
                    appOrWindowStarted = false,
                    settingsOrSecretsUsed = false
                },
                    new JsonSerializerOptions { WriteIndented = true }) + "\n").ConfigureAwait(false);
            using var queue = new ManagedQueue();
            return await queue.Run(async () =>
            {
                var runner = new CaseRunner(output);
                CallerCases.Register(runner, output);
                return await runner.Run().ConfigureAwait(true);
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "fatal.txt"), error.ToString() + "\n").ConfigureAwait(false);
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    private static void VerifySources(string root)
    {
        var utf8 = new UTF8Encoding(false, true);
        using var facts = JsonDocument.Parse(SourceFacts.Json);
        foreach (var source in facts.RootElement.GetProperty("sources").EnumerateArray())
        {
            var relative = source.GetProperty("path").GetString()!;
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new TestFailure("Foreign source input");
            var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            Check.Equal(source.GetProperty("sha256").GetString(), actual, "current source hash " + relative);
        }
        foreach (var member in facts.RootElement.GetProperty("members").EnumerateArray())
        {
            var text = utf8.GetString(File.ReadAllBytes(Path.Combine(root, member.GetProperty("path").GetString()!)));
            var node = text.Substring(member.GetProperty("offset").GetInt32(), member.GetProperty("length").GetInt32());
            var actual = Convert.ToHexStringLower(SHA256.HashData(utf8.GetBytes(node)));
            Check.Equal(member.GetProperty("sha256").GetString(), actual, "FULL_METHOD_SOURCE_HASH " + member.GetProperty("name").GetString());
        }
        var localPins = Path.Combine(root, "tests", "Momoka.NaturalContinuationCallerTests", "SourcePins.json");
        Check.Equal(facts.RootElement.GetProperty("pinsSha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(localPins))), "fixture pins match compiled provenance");
        Console.WriteLine("当前源与完整语法成员 SHA256 核验通过；真实 VM caller，窄 fake 服务，未运行 CoreService/native。 ");
    }
}
