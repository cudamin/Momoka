using System.Text;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Momoka.DetailImageArrivalTests.SourceExtractor;

public sealed class ExtractDetailArtwork : BuildTask
{
    [Required] public string WorkspaceRoot { get; set; } = "";
    [Required] public string SourceRoot { get; set; } = "";
    [Required] public string PosterSource { get; set; } = "";
    [Required] public string OutputSource { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            RunSelfTest();
            var workspace = Path.GetFullPath(WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var sourceRoot = Inside(workspace, SourceRoot);
            var detailPath = Inside(workspace, Path.Combine(sourceRoot, "DetailViewModel.cs"));
            var posterPath = Inside(workspace, PosterSource);
            var outputPath = Inside(workspace, OutputSource);
            var detail = Parse(detailPath);
            var poster = Parse(posterPath);
            var detailType = Owner(detail, "Momoka.Shell.ViewModels", "DetailViewModel");
            var posterType = Owner(poster, "Momoka.Shell.Views", "PosterCard");

            var generated = new StringBuilder(
                "// Complete current-tree syntax nodes, emitted verbatim at build time.\n" +
                "#nullable enable\n" +
                "using System.ComponentModel;\n" +
                "using Microsoft.UI.Xaml.Controls;\n" +
                "using Microsoft.UI.Xaml.Media;\n" +
                "using Microsoft.UI.Xaml.Media.Imaging;\n" +
                "using Momoka.Diagnostics;\n" +
                "using Momoka.Emby;\n" +
                "using Momoka.Shell.Views;\n" +
                "namespace Momoka.Shell.ViewModels\n{\ninternal sealed partial class DetailArtworkFixture\n{\n");
            AppendField(generated, detailType, "_imagePresentation", "Task");
            AppendMethod(generated, detailType, "UseImagePresentation", "void", "Task", "ready");
            AppendMethod(generated, detailType, "LoadArtworkAsync", "Task", "EmbyItem", "item",
                "CancellationTokenSource", "art");
            AppendMethod(generated, detailType, "LoadArtworkCoreAsync", "Task", "EmbyItem", "item",
                "CancellationTokenSource", "art");
            AppendMethod(generated, detailType, "ShowStill", "void", "BitmapSource", "picture");
            AppendMethod(generated, detailType, "UseConnectedPoster", "bool", "EmbyItem", "item",
                "BitmapSource", "picture");
            AppendMethod(generated, detailType, "UseConnectedPlate", "bool", "EmbyItem", "item",
                "BitmapImage", "picture");
            AppendMethod(generated, detailType, "Cancel", "void");
            AppendMethod(generated, detailType, "Fresh", "bool", "CancellationTokenSource", "art",
                "EmbyItem", "item");
            generated.Append("}\n}\nnamespace Momoka.Shell.Views\n{\ninternal sealed partial class PosterCard\n{\n");
            AppendMethod(generated, posterType, "RetainConnectedPoster", "Action");
            generated.Append("}\n}\n");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var utf8 = new UTF8Encoding(false);
            var contents = generated.ToString();
            if (!File.Exists(outputPath) || File.ReadAllText(outputPath, utf8) != contents)
                File.WriteAllText(outputPath, contents, utf8);
            return true;
        }
        catch (Exception error)
        {
            Log.LogErrorFromException(error, showStackTrace: true);
            return false;
        }
    }

    private static CompilationUnitSyntax Parse(string path)
    {
        var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
        return ParseText(text, path);
    }

    private static CompilationUnitSyntax ParseText(string text, string path = "self-test.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest), path);
        var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException("Production C# parse failed: " + string.Join("; ", errors.Select(error => error.ToString())));
        return tree.GetCompilationUnitRoot();
    }

    private static void RunSelfTest()
    {
        const string fixture = "namespace Fixture; internal sealed class Owner { private Task _imagePresentation = Task.CompletedTask; "
            + "private void UseImagePresentation(Task ready) { } private void Cancel() { } }";
        var owner = Owner(ParseText(fixture), "Fixture", "Owner");
        AppendField(new StringBuilder(), owner, "_imagePresentation", "Task");
        AppendMethod(new StringBuilder(), owner, "UseImagePresentation", "void", "Task", "ready");
        AppendMethod(new StringBuilder(), owner, "Cancel", "void");
        ExpectRejected(() => Owner(ParseText(fixture), "Fixture", "Missing"), "missing type");
        ExpectRejected(() => Owner(ParseText(
            "namespace Fixture; internal sealed class Owner { } internal sealed class Owner { }"), "Fixture", "Owner"),
            "ambiguous type");
        ExpectRejected(() => AppendField(new StringBuilder(), owner, "Missing", "Task"), "missing field");
        var ambiguousFieldOwner = Owner(ParseText(
            "namespace Fixture; internal sealed class Owner { private Task _imagePresentation = Task.CompletedTask; private Task _imagePresentation = Task.CompletedTask; }"),
            "Fixture", "Owner");
        ExpectRejected(() => AppendField(new StringBuilder(), ambiguousFieldOwner, "_imagePresentation", "Task"),
            "ambiguous field");
        ExpectRejected(() => AppendMethod(new StringBuilder(), owner, "Missing", "void"), "missing method");
        ExpectRejected(() => AppendMethod(new StringBuilder(), owner, "UseImagePresentation", "bool", "Task", "ready"),
            "changed return type");
        ExpectRejected(() => AppendMethod(new StringBuilder(), owner, "UseImagePresentation", "void", "Task", "other"),
            "changed parameter");
        var ambiguous = Owner(ParseText("namespace Fixture; internal sealed class Owner { private void Cancel() { } private void Cancel() { } }"),
            "Fixture", "Owner");
        ExpectRejected(() => AppendMethod(new StringBuilder(), ambiguous, "Cancel", "void"), "ambiguous method");
    }

    private static void ExpectRejected(Action action, string scenario)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Source extractor self-test accepted " + scenario + ".");
    }

    private static ClassDeclarationSyntax Owner(CompilationUnitSyntax root, string expectedNamespace, string name)
    {
        var namespaces = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>()
            .Where(declaration => declaration.Name.ToString() == expectedNamespace).ToArray();
        if (namespaces.Length != 1)
            throw new InvalidOperationException("Missing or ambiguous production namespace: " + expectedNamespace);
        var candidates = namespaces[0].Members.OfType<ClassDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.ValueText == name).ToArray();
        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidOperationException("Missing or ambiguous production type: " + expectedNamespace + "." + name);
    }

    private static void AppendField(StringBuilder output, ClassDeclarationSyntax owner, string name, string fieldType)
    {
        var candidates = owner.Members.OfType<FieldDeclarationSyntax>()
            .Where(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == name)).ToArray();
        if (candidates.Length != 1 || candidates[0].Declaration.Variables.Count != 1
            || candidates[0].Declaration.Type.ToString() != fieldType)
            throw new InvalidOperationException("Missing or ambiguous production field: " + name);
        AppendNode(output, candidates[0]);
    }

    private static void AppendMethod(StringBuilder output, ClassDeclarationSyntax owner, string name, string returnType,
        params string[] signature)
    {
        var candidates = owner.Members.OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == name && method.ReturnType.ToString() == returnType
                && method.ParameterList.Parameters.SelectMany(parameter =>
                    new[] { parameter.Type?.ToString() ?? "", parameter.Identifier.ValueText })
                    .SequenceEqual(signature, StringComparer.Ordinal))
            .ToArray();
        if (candidates.Length != 1 || candidates[0].Body is null && candidates[0].ExpressionBody is null)
            throw new InvalidOperationException("Missing or ambiguous production method/signature: " + name);
        AppendNode(output, candidates[0]);
    }

    private static void AppendNode(StringBuilder output, MemberDeclarationSyntax node)
    {
        var text = node.WithoutLeadingTrivia().ToFullString();
        output.Append(text);
        if (!text.EndsWith('\n')) output.Append('\n');
    }

    private static string Inside(string workspace, string path)
    {
        var absolute = Path.GetFullPath(path);
        if (!absolute.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production source/output escaped the current workspace.");
        return absolute;
    }
}
