using System.Text;
using Momoka.Diagnostics;
using Momoka.Infrastructure;

namespace Momoka.Shell;

/// <summary>自检收尾不依赖 UI：先写完整报告，再发布成功码；任何异常都不能越过关闭。</summary>
internal static partial class ShellSelfCheck
{
    private const string Category = "自检";

    /// <summary>0 only after every check passed and the final report was written. <c>Program</c> returns this code.</summary>
    public static int ExitCode { get; private set; } = 1;

    private static bool CompleteReport(AppPaths paths, string content, int failures,
        Action<string, string, Encoding>? writer = null)
    {
        ExitCode = 1;
        var written = Write(paths, "selfcheck-shell.txt", content, writer);
        if (written && failures == 0) ExitCode = 0;
        return written;
    }

    private static bool Write(AppPaths paths, string name, string content,
        Action<string, string, Encoding>? writer = null)
    {
        try
        {
            paths.EnsureCreated();
            (writer ?? File.WriteAllText)(Path.Combine(paths.LogDirectory, name), content, Encoding.UTF8);
            return true;
        }
        catch (Exception error)
        {
            Log.Error(Category, $"写入自检报告 {name} 失败", error);
            return false;
        }
    }

    private static async Task FinishAsync(Func<Task> run, Action close)
    {
        try
        {
            await run();
        }
        catch (Exception error)
        {
            ExitCode = 1;
            Log.Error(Category, "自检本身出错", error);
        }
        finally
        {
            close();
        }
    }
}
