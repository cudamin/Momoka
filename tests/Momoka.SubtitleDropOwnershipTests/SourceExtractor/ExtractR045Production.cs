using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Momoka.Tests.SourceExtractor;

// Separate .NET task avoids RoslynCodeTaskFactory's netstandard-only compiler references.
// Its only IO is reading authorized production source and writing this project's generated nodes.
public sealed class ExtractR045Production : BuildTask
{
    [Required] public string WorkspaceRoot { get; set; } = "";
    [Required] public string ShellSourceRoot { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var workspace = Path.GetFullPath(WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var sourceRoot = Path.GetFullPath(ShellSourceRoot);
            var outputRoot = Path.GetFullPath(OutputDirectory);
            if (!sourceRoot.StartsWith(workspace, StringComparison.OrdinalIgnoreCase)
                || !outputRoot.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R045 production inputs/outputs must stay in this worktree");
            Directory.CreateDirectory(outputRoot);
            var utf8 = new UTF8Encoding(false, true);
            var manifest = new List<string>();
            Func<byte[], string> hash = bytes =>
            {
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            };
            Func<string, string, ClassDeclarationSyntax> readClass = (relative, className) =>
            {
                var path = Path.Combine(sourceRoot, relative);
                var bytes = File.ReadAllBytes(path);
                var text = utf8.GetString(bytes);
                var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest), path);
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException("R045 production parse failed: " + string.Join("; ", errors.Select(d => d.ToString())));
                var syntaxRoot = tree.GetRoot();
                var expectedNamespace = relative.StartsWith("Views/", StringComparison.Ordinal)
                    ? "Momoka.Shell.Views" : "Momoka.Shell.ViewModels";
                var namespaces = syntaxRoot.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().ToArray();
                if (namespaces.Length != 1 || namespaces[0].Name.ToString() != expectedNamespace)
                    throw new InvalidOperationException("R045 production namespace changed: " + path);
                var candidates = namespaces[0].Members.OfType<ClassDeclarationSyntax>()
                    .Where(c => c.Identifier.ValueText == className).ToArray();
                if (candidates.Length != 1) throw new InvalidOperationException("R045 missing/ambiguous class " + className + " in " + path);
                manifest.Add("source " + path + " SHA256=" + hash(bytes));
                return candidates[0];
            };
            Func<ClassDeclarationSyntax, string, string, string> member = (owner, kind, name) =>
            {
                var candidates = owner.Members.Where(node =>
                    (kind == "method" && node is MethodDeclarationSyntax && ((MethodDeclarationSyntax)node).Identifier.ValueText == name)
                    || (kind == "property" && node is PropertyDeclarationSyntax && ((PropertyDeclarationSyntax)node).Identifier.ValueText == name)
                    || (kind == "field" && node is FieldDeclarationSyntax && ((FieldDeclarationSyntax)node).Declaration.Variables.Any(v => v.Identifier.ValueText == name))
                    || (kind == "event" && node is EventFieldDeclarationSyntax && ((EventFieldDeclarationSyntax)node).Declaration.Variables.Any(v => v.Identifier.ValueText == name))
                    || (kind == "record" && node is RecordDeclarationSyntax && ((RecordDeclarationSyntax)node).Identifier.ValueText == name)).ToArray();
                if (candidates.Length != 1) throw new InvalidOperationException("R045 missing/ambiguous production " + kind + " " + owner.Identifier.ValueText + "." + name + " count=" + candidates.Length);
                var node = candidates[0];
                var method = node as MethodDeclarationSyntax;
                if (method != null && method.Body == null && method.ExpressionBody == null)
                    throw new InvalidOperationException("R045 method has no production body: " + name);
                var field = node as FieldDeclarationSyntax;
                if (field != null && field.Declaration.Variables.Count != 1)
                    throw new InvalidOperationException("R045 unsupported multi-variable production field: " + name);
                var text = node.ToFullString();
                manifest.Add("member " + owner.Identifier.ValueText + "." + name + " SHA256=" + hash(utf8.GetBytes(text)));
                var line = node.SyntaxTree.GetText().Lines.GetLineFromPosition(node.FullSpan.Start).LineNumber + 1;
                return "\n#line " + line + " \"" + node.SyntaxTree.FilePath.Replace("\\", "/") + "\"\n" + text + "\n#line default\n";
            };
            var page = readClass("Views/PlayerPage.xaml.cs", "PlayerPage");
            var input = readClass("Views/PlayerPage.Input.cs", "PlayerPage");
            var transition = readClass("Views/PlayerPage.Transition.cs", "PlayerPage");
            var vm = readClass("ViewModels/PlayerViewModel.cs", "PlayerViewModel");
            var interaction = readClass("ViewModels/PlayerViewModel.Interaction.cs", "PlayerViewModel");
            var mpv = readClass("ViewModels/PlayerViewModel.Mpv.cs", "PlayerViewModel");
            var events = readClass("ViewModels/PlayerViewModel.Events.cs", "PlayerViewModel");
            var header = "// Complete production syntax nodes, extracted verbatim at every build.\n#nullable enable\n";
            var pageCode = header + "using Momoka.Diagnostics;\nusing Momoka.Playback;\nusing Momoka.Shell.ViewModels;\nusing Momoka.Shell.Interop;\nusing Momoka.Shell.Windowing;\nusing Microsoft.UI.Xaml;\nusing Microsoft.UI.Xaml.Controls;\nusing Windows.ApplicationModel.DataTransfer;\nusing Windows.Storage;\nnamespace Momoka.Shell.Views;\npublic sealed partial class PlayerPage\n{\n";
            foreach (var name in new[] { "ViewModel", "Attached" }) pageCode += member(page, "property", name);
            foreach (var name in new[] { "_shell", "_window", "_cursorVisibilityEvents", "_cursorHidden", "_woke" }) pageCode += member(page, "field", name);
            foreach (var name in new[] { "_presentationGeneration", "_onStage" }) pageCode += member(transition, "field", name);
            pageCode += member(input, "field", "LogCategory");
            foreach (var name in new[] { "Attach", "Detach", "Shutdown", "ReleaseVideoSurface" }) pageCode += member(page, "method", name);
            foreach (var name in new[] { "OnPictureDragOver", "OnPictureDrop" }) pageCode += member(input, "method", name);
            pageCode += "}\n";
            var vmCode = header + "using Momoka.Configuration;\nusing Momoka.Emby;\nusing Momoka.Mpv;\nusing Momoka.Playback;\nusing Momoka.Services;\nusing Microsoft.UI.Xaml.Controls;\nnamespace Momoka.Shell.ViewModels;\npublic sealed partial class PlayerViewModel\n{\n";
            foreach (var name in new[] { "_playback", "_settings", "_lifetime", "_nowPlaying", "_playbackAttempt", "_generation", "_tracksRevision", "_preparingPictureInHost" }) vmCode += member(vm, "field", name);
            vmCode += member(vm, "event", "Noticed");
            foreach (var name in new[] { "Settings", "PictureInHostWindow" }) vmCode += member(vm, "property", name);
            vmCode += member(interaction, "field", "_interactionVersion");
            vmCode += member(interaction, "record", "InteractionContext");
            foreach (var name in new[] { "CaptureInteraction", "IsCurrentInteraction" }) vmCode += member(interaction, "method", name);
            vmCode += member(mpv, "method", "AddExternalSubtitlesAsync");
            vmCode += member(events, "method", "OnTracksChanged");
            vmCode += "}\n";
            Action<string, string> save = (name, contents) =>
            {
                var path = Path.Combine(outputRoot, name);
                if (!File.Exists(path) || File.ReadAllText(path, utf8) != contents)
                    File.WriteAllText(path, contents, utf8);
            };
            save("PlayerPage.g.cs", pageCode);
            save("PlayerViewModel.g.cs", vmCode);
            var manifestText = string.Join("\n", manifest) + "\n";
            save("SourceManifest.txt", manifestText);
            save("SourceManifest.g.cs", "// Generated production hashes, not a behavior model.\nnamespace Momoka.Tests;\n" +
                "internal static class ProductionManifest\n{\n    internal const string Text = @\"" +
                manifestText.Replace("\"", "\"\"") + "\";\n}\n");
            return true;
        }
        catch (Exception error)
        {
            Log.LogErrorFromException(error, showStackTrace: true);
            return false;
        }
    }
}
