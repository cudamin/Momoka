using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Momoka.NaturalContinuationSourceExtractor;

// SDK Roslyn parses complete nodes. It never edits, normalizes or repairs a production method.
public sealed class ExtractCaller : BuildTask
{
    [Required] public string WorkspaceRoot { get; set; } = "";
    [Required] public string SourceRoot { get; set; } = "";
    [Required] public string PinsPath { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            Generate();
            return true;
        }
        catch (Exception error)
        {
            Log.LogErrorFromException(error, showStackTrace: true);
            return false;
        }
    }

    private void Generate()
    {
        var workspace = Path.GetFullPath(WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var sourceRoot = Inside(workspace, SourceRoot);
        var outputRoot = Inside(workspace, OutputDirectory);
        var pinsPath = Inside(workspace, PinsPath);
        var utf8 = new UTF8Encoding(false, true);
        var pinsBytes = File.ReadAllBytes(pinsPath);
        using var pins = JsonDocument.Parse(pinsBytes);
        if (pins.RootElement.GetProperty("baseCommit").GetString() != "82176c7d57c15ddef2a312af2a6232ecb8c8897b")
            throw new InvalidOperationException("R046 wrong base commit");
        var loaded = new Dictionary<string, (string Text, CompilationUnitSyntax Root)>();
        var sources = new List<object>();
        var members = new List<object>();
        foreach (var pin in pins.RootElement.GetProperty("sources").EnumerateArray())
        {
            var relative = pin.GetProperty("path").GetString() ?? throw new InvalidOperationException("Missing source path");
            if (Path.IsPathRooted(relative) || relative.Split('/').Any(part => part is ".." or "."))
                throw new InvalidOperationException("R046 source path must be worktree relative");
            var path = Inside(sourceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Path.Combine(sourceRoot, relative));
            var bytes = File.ReadAllBytes(path);
            var sha256 = Hash(bytes);
            if (sha256 != pin.GetProperty("sha256").GetString())
                throw new InvalidOperationException("R046 SOURCE_HASH_MISMATCH " + relative + " actual=" + sha256);
            var text = utf8.GetString(bytes);
            if (!relative.EndsWith(".cs", StringComparison.Ordinal))
            {
                _ = XDocument.Parse(text);
                sources.Add(new { path = relative, sha256 });
                continue;
            }
            var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest), path);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException("R046 SOURCE_SYNTAX_INVALID " + relative + ": " + string.Join("; ", errors.Select(d => d.ToString())));
            if (!loaded.TryAdd(relative, (text, tree.GetCompilationUnitRoot())))
                throw new InvalidOperationException("R046 SOURCE_DUPLICATE " + relative);
            sources.Add(new { path = relative, sha256 });
        }

        ClassDeclarationSyntax Owner(string relative)
        {
            if (!loaded.TryGetValue(relative, out var source)) throw new InvalidOperationException("R046 unpinned source " + relative);
            var namespaces = source.Root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().ToArray();
            if (namespaces.Length != 1 || namespaces[0].Name.ToString() != "Momoka.Shell.ViewModels")
                throw new InvalidOperationException("R046 WRONG_NAMESPACE " + relative);
            var candidates = namespaces[0].Members.OfType<ClassDeclarationSyntax>()
                .Where(c => c.Identifier.ValueText == "PlayerViewModel").ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("R046 OWNER_MISSING_OR_AMBIGUOUS " + relative);
            return candidates[0];
        }

        MemberDeclarationSyntax Node(ClassDeclarationSyntax owner, string name)
        {
            var candidates = owner.Members.Where(node => node switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText == name,
                PropertyDeclarationSyntax property => property.Identifier.ValueText == name,
                FieldDeclarationSyntax field => field.Declaration.Variables.Any(v => v.Identifier.ValueText == name),
                EventFieldDeclarationSyntax field => field.Declaration.Variables.Any(v => v.Identifier.ValueText == name),
                RecordDeclarationSyntax record => record.Identifier.ValueText == name,
                _ => false
            }).ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("R046 MEMBER_MISSING_OR_AMBIGUOUS " + name + " count=" + candidates.Length);
            var node = candidates[0];
            if (node is MethodDeclarationSyntax method && method.Body is null && method.ExpressionBody is null)
                throw new InvalidOperationException("R046 METHOD_HAS_NO_BODY " + name);
            if (node is FieldDeclarationSyntax field && field.Declaration.Variables.Count != 1)
                throw new InvalidOperationException("R046 MULTIVARIABLE_FIELD " + name);
            return node;
        }

        var vm = Owner("src/Momoka.Shell/ViewModels/PlayerViewModel.cs");
        var transport = Owner("src/Momoka.Shell/ViewModels/PlayerViewModel.Transport.cs");
        var events = Owner("src/Momoka.Shell/ViewModels/PlayerViewModel.Events.cs");
        var versions = Owner("src/Momoka.Shell/ViewModels/PlayerViewModel.Versions.cs");
        var start = (MethodDeclarationSyntax)Node(transport, "StartPlaybackAsync");
        var expectedParameters = new[] { "item", "parent", "choice", "episodes", "replaceExisting", "identity", "continuingIntent", "continuationCancellation", "expectedContinuation" };
        if (!start.ParameterList.Parameters.Select(p => p.Identifier.ValueText).SequenceEqual(expectedParameters))
            throw new InvalidOperationException("R046 START_SIGNATURE_CHANGED");
        var declarations = start.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .Where(d => d.Declaration.Variables.Any(v => v.Identifier.ValueText == "continuation")).ToArray();
        if (declarations.Length != 1 || declarations[0].AwaitKeyword.IsKind(SyntaxKind.None)
            || declarations[0].UsingKeyword.IsKind(SyntaxKind.None)
            || declarations[0].Declaration.Variables.Single().Initializer?.Value.ToString() != "result.Continuation")
            throw new InvalidOperationException("R046 CONTINUATION_AWAIT_USING_MISSING_OR_AMBIGUOUS");
        var playCalls = start.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression.ToString() == "_playback.PlayAsync").ToArray();
        if (playCalls.Length != 1 || playCalls[0].ArgumentList.Arguments.Count != 6
            || playCalls[0].ArgumentList.Arguments[4].Expression.ToString() != "continuationCancellation"
            || playCalls[0].ArgumentList.Arguments[5].Expression.ToString() != "expectedContinuation")
            throw new InvalidOperationException("R046 PLAY_SIGNATURE_OR_ADMISSION_FENCE_CHANGED");
        var linked = start.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .SingleOrDefault(i => i.Expression.ToString() == "CancellationTokenSource.CreateLinkedTokenSource");
        if (linked is null || !linked.ArgumentList.Arguments.Any(a => a.Expression.ToString() == "continuationCancellation"))
            throw new InvalidOperationException("R046 LINKED_CONTINUATION_PREPARATION_MISSING");
        if (!start.DescendantNodes().OfType<ArgumentSyntax>().Any(a => a.NameColon?.Name.Identifier.ValueText == "expectedContinuation"
                && a.Expression.ToString() == "continuation"))
            throw new InvalidOperationException("R046 RECURSIVE_EXPECTED_CONTINUATION_MISSING");

        var code = new StringBuilder("// Complete current-tree syntax nodes, verbatim. Do not edit generated output.\n#nullable enable\n");
        code.Append("using Momoka.Configuration;\nusing Momoka.Diagnostics;\nusing Momoka.Emby;\nusing Momoka.Infrastructure;\nusing Momoka.Mpv;\nusing Momoka.Playback;\nusing Microsoft.UI.Xaml.Controls;\nusing Microsoft.UI.Xaml.Media.Imaging;\nnamespace Momoka.Shell.ViewModels;\npublic sealed partial class PlayerViewModel\n{\n");
        void Emit(ClassDeclarationSyntax owner, string name)
        {
            var node = Node(owner, name);
            var text = node.ToFullString();
            var path = node.SyntaxTree.FilePath;
            var relative = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            var line = node.SyntaxTree.GetText().Lines.GetLineFromPosition(node.FullSpan.Start).LineNumber + 1;
            if (loaded[relative].Text.Substring(node.FullSpan.Start, node.FullSpan.Length) != text)
                throw new InvalidOperationException("R046 NONVERBATIM_MEMBER " + name);
            members.Add(new { path = relative, name, offset = node.FullSpan.Start, length = node.FullSpan.Length, sha256 = Hash(utf8.GetBytes(text)) });
            code.Append("\n#line ").Append(line).Append(" \"").Append(path.Replace('\\', '/')).Append("\"\n").Append(text).Append("\n#line default\n");
        }
        foreach (var name in new[]
        {
            "Category", "_session", "_skips", "_lifetime", "_startIntent", "_chapterStills", "_parent", "_nowPlaying",
            "PendingPlayback", "_pendingPlayback", "_playerHold", "_playbackAttempt", "_generation", "_playerUp",
            "_episodeLookupBusy", "_episodeSwitchTargetId", "_versionSwitchTarget", "_seekTouched", "_seekSent",
            "_volumeMemory", "_aspect", "_shaderContext", "_outputWatch", "_shaderSettings", "_shaderResolver", "_shaderPinned",
            "_embyMarks", "_playbackScope", "_activeIntent", "_preparing", "_playbackIdle", "_presentedHeadless", "_preparingPictureInHost",
            "_stopTask", "_versionDiscovery", "_coverBackdropItemId", "_coverBackdropLoad", "_coverGeneration", "_replacingPlayback",
            "Episodes", "HasAggregateQueue", "Settings", "PreparePresentation", "MeasureRefreshHz", "Noticed", "RefreshRequested",
            "PlayerShown", "PlayerHidden", "PictureAspectChanged", "ChaptersChanged", "PlaybackStarted"
        }) Emit(vm, name);
        Emit(events, "_volumeReady");
        Emit(versions, "_versionLoad");
        foreach (var name in new[]
        {
            "PlayAsync", "PlayReplacingAsync", "StopAsync", "StopPlaybackAsync", "StartPlaybackAsync", "NextEpisodeToAutoPlay",
            "ResolveNextEpisodeAsync", "ResolveSeriesAsync", "FillSiblingsAsync", "EnterPlayer", "LeavePlayer", "ShowCover", "HideCover"
        }) Emit(transport, name);
        Emit(events, "OnNowPlayingChanged");
        code.Append("}\n");
        var facts = JsonSerializer.Serialize(new { workspaceRoot = workspace.TrimEnd(Path.DirectorySeparatorChar), sources, members, pinsSha256 = Hash(pinsBytes) }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(outputRoot);
        Save("PlayerViewModel.g.cs", code.ToString());
        Save("SourceFacts.json", facts + "\n");
        Save("SourceFacts.g.cs", "// Production source provenance, not a copied behavior model.\nnamespace Momoka.NaturalContinuationCallerTests;\ninternal static class SourceFacts\n{\n    internal const string Json = @\"" + facts.Replace("\"", "\"\"") + "\";\n}\n");
        void Save(string name, string text)
        {
            var path = Path.Combine(outputRoot, name);
            if (!File.Exists(path) || File.ReadAllText(path, utf8) != text) File.WriteAllText(path, text, utf8);
        }
    }

    private static string Inside(string root, string path)
    {
        var absolute = Path.GetFullPath(path);
        if (!absolute.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && !absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R046 FOREIGN_WORKTREE_INPUT_OR_OUTPUT " + absolute);
        return absolute;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
