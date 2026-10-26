using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Theming;

namespace Momoka.ServerSettingsTabs.Tests;

/// <summary>Export the currently built production scripts, never a copied CSS or credential script.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["contracts"])
        {
            Console.OutputEncoding = Encoding.UTF8;
            Momoka.Tests.ServerSettingsTests.Register();
            return Momoka.Tests.TestHarness.Run();
        }

        if (args is not [var treeArgument, var outputArgument])
        {
            Console.Error.WriteLine("Usage: <absolute worktree root> <absolute payload.json inside that tree>");
            return 2;
        }

        var tree = Path.GetFullPath(treeArgument);
        var output = Path.GetFullPath(outputArgument);
        var core = typeof(EmbyWebConsole).Assembly.Location;
        var prefix = tree.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!File.Exists(Path.Combine(tree, "src", "Momoka.Core", "Momoka.Core.csproj"))
            || !output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !core.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !core.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("The output and current Release Core assembly must belong to this worktree.");
            return 2;
        }

        var sources = new[]
        {
            "src/Momoka.Core/Emby/EmbyWebConsole.cs",
            "src/Momoka.Core/Emby/EmbyWebConsole.NativeSkin.cs",
            "src/Momoka.Core/Emby/EmbyWebConsole.JellyfinSkin.cs"
        }.ToDictionary(path => path, path => Hash(Path.Combine(tree, path)));
        var colors = UiThemes.Default.Colors;
        var payload = new
        {
            tree,
            coreAssembly = core,
            coreHash = Hash(core),
            sources,
            scripts = new Dictionary<string, string[]>
            {
                ["emby"] = [EmbyWebConsole.NativeSkinScript(colors), EmbyWebConsole.HideDrawerScript()],
                ["jellyfin"] = [EmbyWebConsole.JellyfinNativeSkinScript(colors), EmbyWebConsole.HideJellyfinDrawerScript()]
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        Console.WriteLine("Exported current Release production skin/drawer scripts: " + output);
        Console.WriteLine("Core SHA256: " + payload.coreHash);
        return 0;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
