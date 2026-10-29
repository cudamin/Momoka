namespace Momoka.Tests;

internal static class RunnerContractTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> cases)
    {
        cases.Add(("跑器参数：无参数才运行，单独帮助不构造夹具，未知/重复/路径/敏感参数一律拒绝", () =>
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Null(Program.HandleArguments([], output, error));
            Check.Equal<int?>(0, Program.HandleArguments(["--help"], output, error));
            Check.Contains("其他参数退出码 2", output.ToString());
            foreach (var args in new[]
            {
                new[] { "unknown" }, new[] { "--help", "--help" }, new[] { "--help", "--play" },
                new[] { "--PLAY" }, new[] { "--play" }, new[] { "--ui" }, new[] { "--self-check" },
                new[] { "--shell-dll", "C:\\untrusted\\Momoka.dll" }, new[] { "--source", "https://untrusted.invalid" },
                new[] { "--token", "nonsecret-placeholder" }, new[] { "--" }, new[] { "" }
            }) Check.Equal<int?>(2, Program.HandleArguments(args, output, error));
            Check.False(error.ToString().Contains("nonsecret-placeholder", StringComparison.Ordinal), "拒绝参数时不回显潜在敏感内容");
            return Task.CompletedTask;
        }));
        cases.Add(("README边界：当前程序集命令/编译标志/允许参数/原样生产与非UI覆盖明确", async () =>
        {
            using var stream = typeof(RunnerContractTests).Assembly.GetManifestResourceStream("Project.README.md")!;
            using var reader = new StreamReader(stream); var text = await reader.ReadToEndAsync();
            foreach (var marker in new[]
            {
                "-c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false",
                "Momoka.SearchLifetimeTests\\bin\\Release\\net10.0\\Momoka.Tests.dll", "--help", "--unknown-mode",
                "严格区分大小写", "原样提取", "WeakReference", "0 警告、0 错误", "不构造夹具", "不证明 WinRT",
                "文件读取", "不由尚未显示", "Rescrap", "只结束 UI 等待", "不宣称与某个部署版本同步"
            }) Check.Contains(marker, text);
            Check.False(text.Contains("dotnet run", StringComparison.Ordinal), "不能把MSBuild开关混作严格跑器参数");
        }));
    }
}
