using System.Text;
using Momoka.Configuration;

namespace Momoka.Mpv;

/// <summary>Optional, user-installed SVP/VapourSynth integration. Discovery never loads native code.</summary>
public static class MpvSvp
{
    // SVP's documented Windows endpoint, including for libmpv hosts such as Plex.
    // A fixed endpoint is deliberate: PID discovery enumerates mpv.exe, not Momoka.exe.
    public const string PipeName = "mpvpipe";
    public const string PipePath = @"\\.\pipe\mpvpipe";
    public const string LibraryName = "VSScript.dll";

    public sealed record LibraryStatus(string? Path, string? Problem)
    {
        public bool Found => Path is not null;
    }

    public static bool IsAvailable(MpvSettings settings) => settings.Backend == MpvBackendKind.BuiltInLibMpv;

    // Explicit custom configuration can install arbitrary vf/scripts, so it cannot safely share
    // filter ownership with this first integration. Keep both stored preferences for restoration.
    public static bool IsEnabled(MpvSettings settings) => settings.SvpEnabled && IsAvailable(settings)
        && !(MpvCustomConfig.IsAvailable(settings) && settings.CustomConfigEnabled);

    /// <summary>SVP owns temporal processing and the vf list; menus must not replace its filter.</summary>
    public static bool BlocksCommand(IReadOnlyList<string> command)
    {
        if (command.Count == 0) return false;
        if (command[0] == "vf") return true;
        if (command.Count < 2) return false;
        if (command[1] is "vf" or "interpolation" or "video-sync") return true;
        if (command[1] != "hwdec") return false;
        return command.Count != 3 || command[0] != "set"
            || FrameInterpolationOptions.CopyCompatibleHardwareDecoder(command[2]) != command[2];
    }

    public static IReadOnlyList<PlayerMenuNode> Menu(bool svp) => svp ? CompatibleMenu(PlayerMenuCatalog.Root) : PlayerMenuCatalog.Root;

    private static IReadOnlyList<PlayerMenuNode> CompatibleMenu(IReadOnlyList<PlayerMenuNode> nodes)
    {
        var rows = new List<PlayerMenuNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Kind == PlayerMenuKind.Separator)
            {
                if (rows.Count > 0 && rows[^1].Kind != PlayerMenuKind.Separator) rows.Add(node);
                continue;
            }
            if (node.Kind == PlayerMenuKind.Group)
            {
                var children = CompatibleMenu(node.Children);
                if (children.Count > 0) rows.Add(node with { Children = children });
            }
            else if (!node.Commands.Any(BlocksCommand)) rows.Add(node);
        }
        if (rows.Count > 0 && rows[^1].Kind == PlayerMenuKind.Separator) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    public static LibraryStatus FindLibrary(string? requested) => FindLibrary(requested, LibraryCandidates());

    public static LibraryStatus FindLibrary(string? requested, IEnumerable<string> candidates)
    {
        var explicitPath = TypedPath.Clean(requested ?? "");
        if (explicitPath.Length > 0) return InspectLibrary(explicitPath);
        foreach (var candidate in candidates)
        {
            var status = InspectLibrary(candidate);
            if (status.Found) return status;
        }
        return new(null, "未找到 64 位 VapourSynth。请在 SVP 安装器中添加 mpv／VapourSynth 组件，或填写 VSScript.dll 的路径。");
    }

    private static IEnumerable<string> LibraryCandidates()
    {
        if (Environment.GetEnvironmentVariable("VSSCRIPT_PATH") is { Length: > 0 } registered) yield return registered;
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (root.Length == 0) continue;
            yield return Path.Combine(root, "SVP 4", "mpv64", LibraryName);
            yield return Path.Combine(root, "VapourSynth", "core", LibraryName);
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = TypedPath.Clean(directory);
            if (Path.IsPathFullyQualified(path)) yield return Path.Combine(path, LibraryName);
        }
    }

    private static LibraryStatus InspectLibrary(string path)
    {
        try
        {
            path = TypedPath.Clean(path);
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(path), LibraryName, StringComparison.OrdinalIgnoreCase))
                return new(null, "请填写本机 VSScript.dll 的绝对路径；不接受 URL、网络路径或其他文件。");
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) return new(null, "VSScript.dll 不存在；请检查 SVP 的 64 位组件安装和文件路径。");
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
                return new(null, "SVP 接入不加载网络盘中的原生组件。");
            for (FileSystemInfo? current = new FileInfo(path); current is not null;
                current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new(null, "SVP 接入不加载重解析路径中的原生组件。");
            }
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) return WrongArchitecture();
            stream.Position = 0x3c;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 6) return WrongArchitecture();
            stream.Position = offset;
            if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) return WrongArchitecture();
            return new(path, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(null, "VSScript.dll 不存在或无法读取；请检查 SVP 的 64 位组件安装和文件路径。");
        }
    }

    private static LibraryStatus WrongArchitecture() => new(null, "VSScript.dll 必须是有效的 x64 原生组件；32 位与 ARM64 版本不能用于 Momoka。");
}
