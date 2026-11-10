using System.Diagnostics;

namespace Momoka.Playback;

public sealed record PlayerToolResult(int ExitCode, string OutputPath);

public static class PlayerToolRunner
{
    public static async Task<PlayerToolResult> RunAsync(PlayerToolPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PlayerToolPaths.IsExecutableAvailable(plan.ExecutablePath)) throw new PlayerToolException("播放工具程序已不可用。");
        foreach (var input in plan.Inputs) PlayerToolPaths.RequireInput(input);
        PlayerToolPaths.RequireOutput(plan.OutputPath);
        var start = new ProcessStartInfo(plan.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(plan.ExecutablePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
        foreach (var pair in plan.Environment) start.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new PlayerToolException("播放工具无法启动。");
            process.StandardInput.Close();
            // 持续排空而不保留原始日志，工具错误可能回显文件内容或环境中的敏感值。
            var output = DrainAsync(process.StandardOutput);
            var error = DrainAsync(process.StandardError);
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception) { }
            });
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new PlayerToolException("播放工具处理失败（退出码 " + process.ExitCode + "），请检查工具版本和设置。");
            var result = PlayerToolPaths.RequireInput(plan.OutputPath);
            if (new FileInfo(result).Length == 0) throw new PlayerToolException("播放工具没有生成有效输出文件。");
            return new(process.ExitCode, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("播放工具操作已取消。", cancellationToken);
        }
        catch (PlayerToolException) { throw; }
        catch (Exception) { throw new PlayerToolException("播放工具执行失败，请检查本地程序和输出文件夹。"); }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4_096];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { }
    }
}
