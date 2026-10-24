using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Momoka.AtomicFileWriteTests;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"专项启动/证据失败：{error.GetType().FullName} HResult=0x{error.HResult:X8}");
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? "", StringComparer.Ordinal);
        var sourcePath = metadata["AtomicFileSourcePath"];
        var compiledHash = metadata["AtomicFileSourceSha256"];
        var currentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)));
        Check.Equal("Release", metadata["BuildConfiguration"], "只运行当前 Release 输出");
        Check.That(compiledHash.Equals(currentHash, StringComparison.OrdinalIgnoreCase),
            $"拒绝旧产物：编译 source SHA={compiledHash}，当前文件 SHA={currentHash}");
        Check.That(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393),
            "本专项必须在 Windows >= 14393 上实测，不将平台缺失跳过算绿");
        var projectRoot = Path.GetFullPath(metadata["FixtureProjectRoot"]);
        var outputRoot = Path.Combine(projectRoot, "outputs");
        var evidenceDirectory = "";
        var controlsOnly = false;
        var backupFailuresOnly = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--evidence-directory" when index + 1 < args.Length:
                    evidenceDirectory = Path.GetFullPath(args[++index]);
                    break;
                case "--controls-only": controlsOnly = true; break;
                case "--backup-failures-only": backupFailuresOnly = true; break;
                default: throw new ContractFailure($"未知/不完整参数：{args[index]}");
            }
        }
        Check.That(!(controlsOnly && backupFailuresOnly), "对照限定与备份失败限定不能混用");
        var scope = controlsOnly ? "controls-only" : backupFailuresOnly ? "backup-failures-only" : "all";
        if (evidenceDirectory.Length == 0)
            evidenceDirectory = Path.Combine(outputRoot, $"direct-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Check.That(evidenceDirectory.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "匿名输出只能在当前测试目录 ignored outputs 内，拒绝其他目录");
        Check.That(!Directory.Exists(evidenceDirectory) && !File.Exists(evidenceDirectory), "不覆盖已有证据");
        RequireLocalOrdinaryPath(projectRoot);
        Directory.CreateDirectory(evidenceDirectory);
        var fixtureRoot = Path.Combine(evidenceDirectory, "anonymous-fixtures");
        Directory.CreateDirectory(fixtureRoot);
        using var transcript = new StreamWriter(Path.Combine(evidenceDirectory, "transcript.log"), append: false, new UTF8Encoding(false))
        { AutoFlush = true };
        void Log(string text) { Console.WriteLine(text); transcript.WriteLine(text); }
        var environment = new
        {
            startedAt = DateTimeOffset.Now.ToString("O"),
            os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            fileSystem = new DriveInfo(Path.GetPathRoot(projectRoot)!).DriveFormat,
            compiledSourceSha256 = compiledHash,
            currentSourceSha256 = currentHash,
            sourcePath,
            projectRoot,
            configuration = metadata["BuildConfiguration"],
            controlsOnly,
            backupFailuresOnly,
            scope,
            noNetwork = true,
            noSettingsAccountsUiNativePlayback = true,
            fixedTargetBudget = PressureCases.TargetBudget,
            fixedWritesPerTarget = PressureCases.WritesPerTarget,
            retryBudget = 0
        };
        Log("=== AtomicFile 独立真实文件系统契约 ===");
        Log(JsonSerializer.Serialize(environment, Json));
        var allProduct = AtomicWriteCases.All().Concat(BackupFailureCases.All())
            .Concat(PressureCases.All()).Concat(ReaderCases.All()).ToArray();
        var product = backupFailuresOnly ? BackupFailureCases.All().ToArray() : allProduct;
        var controls = ControlCases.All().ToArray();
        var results = new List<CaseResult>();
        CaseResult? firstProductFailure = null;
        void RunGroup(string category, IEnumerable<TestCase> cases)
        {
            Log($"=== {category} ===");
            foreach (var test in cases)
            {
                var path = Path.Combine(fixtureRoot, test.Id);
                Directory.CreateDirectory(path);
                var fixture = new TestFixture(path, Log);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                Exception? failure = null;
                try { test.Run(fixture); }
                catch (Exception error) { failure = error; }
                timer.Stop();
                var result = new CaseResult(category, test.Id, test.Requirement, failure is null, timer.ElapsedMilliseconds,
                    fixture.Facts, failure is null ? null : ErrorView.From(failure));
                results.Add(result);
                Log($"[{(result.Passed ? "PASS" : "FAIL")}] {test.Id} —— {test.Requirement} ({timer.ElapsedMilliseconds} ms)");
                if (failure is not null)
                {
                    Log($"    首次异常类型={failure.GetType().FullName}，HResult=0x{failure.HResult:X8}");
                    Log(failure.ToString());
                    if (category == "production-contract" && firstProductFailure is null) firstProductFailure = result;
                }
                // Preserve a checkpoint after every case. A crash/interruption is never a green report.
                File.WriteAllText(Path.Combine(evidenceDirectory, "checkpoint.json"),
                    JsonSerializer.Serialize(new { environment, complete = false, results, firstProductFailure }, Json),
                    new UTF8Encoding(false));
            }
        }
        // Controls are separate and run once; their success cannot offset any product failure.
        if (!backupFailuresOnly) RunGroup("oracle-and-kernel-control", controls);
        if (!controlsOnly) RunGroup("production-contract", product);
        var productResults = results.Where(result => result.Category == "production-contract").ToArray();
        var controlResults = results.Where(result => result.Category == "oracle-and-kernel-control").ToArray();
        var summary = new
        {
            complete = true,
            contractsExecuted = !controlsOnly,
            allRequiredContractsExecuted = !controlsOnly && !backupFailuresOnly,
            scope,
            productPassed = productResults.Count(result => result.Passed),
            productFailed = productResults.Count(result => !result.Passed),
            productRequired = allProduct.Length,
            productSelected = controlsOnly ? 0 : product.Length,
            productNotExecuted = allProduct.Length - productResults.Length,
            controlsRequired = controls.Length,
            controlsNotExecuted = controls.Length - controlResults.Length,
            controlsPassed = controlResults.Count(result => result.Passed),
            controlsFailed = controlResults.Count(result => !result.Passed),
            skipped = 0,
            firstProductFailure,
            exitCode = results.Any(result => !result.Passed) ? 1 : 0,
            finishedAt = DateTimeOffset.Now.ToString("O")
        };
        File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"),
            JsonSerializer.Serialize(new { environment, summary, results }, Json), new UTF8Encoding(false));
        Log("=== 汇总（正确/错误对照不累加为生产通过） ===");
        Log(JsonSerializer.Serialize(summary, Json));
        Log($"证据目录：{evidenceDirectory}");
        return summary.exitCode;
    }

    private static void RequireLocalOrdinaryPath(string path)
    {
        Check.That(!path.StartsWith(@"\\", StringComparison.Ordinal), "禁止网络盘/UNC 测试目录");
        Check.Equal(DriveType.Fixed, new DriveInfo(Path.GetPathRoot(path)!).DriveType, "只用本树本地固定盘");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            Check.That((directory.Attributes & FileAttributes.ReparsePoint) == 0, "禁止重解析目录穿过匿名输出边界");
    }

    private sealed record CaseResult(string Category, string Id, string Requirement, bool Passed, long ElapsedMilliseconds,
        IReadOnlyDictionary<string, object?> Facts, ErrorView? Error);

    private sealed record ErrorView(string Type, string HResult, string Message, string? Stack, ErrorView? Inner)
    {
        public static ErrorView From(Exception error) => new(error.GetType().FullName ?? error.GetType().Name,
            $"0x{error.HResult:X8}", error.Message, error.StackTrace, error.InnerException is null ? null : From(error.InnerException));
    }
}
