using System.Text;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot();
            var core = FindNewest(root, "src/Momoka.Core/bin", "Momoka.Core.dll");
            var shell = FindNewest(root, "src/Momoka.Shell/bin", "Momoka.dll");
            Console.WriteLine($"Core  ：{core}（{new FileInfo(core).LastWriteTime:yyyy-MM-dd HH:mm:ss}）");
            Console.WriteLine($"Shell ：{shell}（{new FileInfo(shell).LastWriteTime:yyyy-MM-dd HH:mm:ss}）");
            ArchitectureCases.Register(core, shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"架构检查启动失败：{error.GetType().FullName} HResult=0x{error.HResult:X8}");
            Console.Error.WriteLine(error);
            return 2;
        }
    }

    /// <summary>没有给根目录时从当前目录往上找 Momoka.sln；找不到就报错，不猜一个可能属于别的工作树的路径。</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到 Momoka.sln；请把本工作树根目录作为第一个参数传入。");
    }

    /// <summary>
    /// 在项目自己的 bin 目录下取最新的 Release 产物。Release 是硬条件：Debug 输出的分析器/条件编译
    /// 与发布件不同，拿它做架构验收等于验错了对象。找不到或多个候选时间相同都交给调用方看见。
    /// </summary>
    private static string FindNewest(string root, string relativeBin, string fileName)
    {
        var bin = Path.Combine(root, relativeBin);
        if (!Directory.Exists(bin)) throw new DirectoryNotFoundException($"缺少构建输出目录：{bin}。先构建当前工作树。");
        var candidates = Directory.EnumerateFiles(bin, fileName, SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        if (candidates.Length == 0) throw new FileNotFoundException($"在 {bin} 下找不到 Release 的 {fileName}。");
        return candidates[0].FullName;
    }
}
