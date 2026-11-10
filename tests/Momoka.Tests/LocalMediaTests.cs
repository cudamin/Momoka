using System.Reflection;
using System.Xml.Linq;
using Momoka.LocalMedia;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

#pragma warning disable RS0030 // The synchronous console harness must await the actual asynchronous filesystem operation.
public static class LocalMediaTests
{
    public static void Register()
    {
        Test("本机链接扫描：只列具体视频且递归选项生效", () =>
        {
            using var fixture = new MediaFiles();
            fixture.Write("source/a.mkv", "video-a");
            fixture.Write("source/a.srt", "subtitle");
            fixture.Write("source/nested/b.mp4", "video-b");
            var organizer = typeof(Momoka.Configuration.AppSettings).Assembly.GetType("Momoka.LocalMedia.LocalMediaOrganizer");
            Assert.NotNull(organizer, "本机目录应能扫描成具体视频清单");
            var scan = organizer!.GetMethod("Scan", BindingFlags.Public | BindingFlags.Static)!;
            var shallow = (System.Collections.IEnumerable)scan.Invoke(null, [fixture.Source, false, CancellationToken.None])!;
            var recursive = (System.Collections.IEnumerable)scan.Invoke(null, [fixture.Source, true, CancellationToken.None])!;
            Assert.Equal(1, shallow.Cast<object>().Count());
            Assert.Equal(2, recursive.Cast<object>().Count());
        });

        Test("本机链接规划：电影年份、危险字符、语言字幕后缀", () =>
        {
            using var fixture = new MediaFiles();
            fixture.Write("source/original.mkv", "video");
            fixture.Write("source/original.zh-Hans.srt", "字幕");
            fixture.Write("source/original.eng.ass", "subtitle");
            fixture.Write("source/unrelated.srt", "unrelated");
            var plan = Prepare(fixture, new("A: Film?", 2026, LocalMediaType.Movie, "themoviedb", "123"));
            Assert.Equal(Path.Combine(fixture.Target, "A_ Film_ (2026)", "A_ Film_ (2026).mkv"), plan.TargetPath);
            Assert.Equal(2, plan.Sidecars.Count(item => item.Kind == LocalMediaOutputKind.SubtitleLink));
            Assert.True(plan.Sidecars.Any(item => item.Path.EndsWith("A_ Film_ (2026).zh-Hans.srt", StringComparison.Ordinal)));
        });

        Test("本机链接规划：Windows设备名不作为目录名", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, new("CON", null, LocalMediaType.Movie, "themoviedb", "123"));
            Assert.Equal(Path.Combine(fixture.Target, "_CON", "_CON.mkv"), plan.TargetPath);
        });

        Test("本机链接规划：拒绝目标位于源扫描树", () =>
        {
            using var fixture = Video();
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            Assert.Throws<IOException>(() => LocalMediaOrganizer.Prepare(source, Path.Combine(fixture.Source, "output"), Movie(), LocalMediaLinkMode.HardLink));
        });

        Test("本机链接规划：已有目标绝不覆盖", () =>
        {
            using var fixture = Video();
            fixture.Write("target/Film (2026)/Film (2026).mkv", "existing");
            Assert.Throws<IOException>(() => Prepare(fixture, Movie()));
            Assert.Equal("existing", File.ReadAllText(Path.Combine(fixture.Target, "Film (2026)", "Film (2026).mkv")));
        });

        Test("本机硬链接执行：真实链接、源保持和movie XML", () =>
        {
            using var fixture = Video();
            fixture.Write("source/original.zh.srt", "字幕");
            var source = Path.Combine(fixture.Source, "original.mkv");
            var sourceTime = File.GetLastWriteTimeUtc(source);
            var plan = Prepare(fixture, Movie() with { Overview = "A & B < C" });
            var result = Execute(plan);
            Assert.True(result.Success, result.Summary + string.Join(";", result.Items.Select(item => item.Message)));
            Assert.Equal("video", File.ReadAllText(source));
            Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(source));
            Assert.Equal("video", File.ReadAllText(plan.TargetPath));
            Assert.Equal(2u, NativeLinkCount(source));
            Assert.Equal("字幕", File.ReadAllText(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.SubtitleLink).Path));
            var nfoPath = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path;
            Assert.True(File.Exists(nfoPath), string.Join(";", Directory.EnumerateFiles(fixture.Target, "*", SearchOption.AllDirectories)));
            var nfo = XDocument.Load(nfoPath);
            Assert.Equal("movie", nfo.Root!.Name.LocalName);
            Assert.Equal("123", nfo.Root.Element("uniqueid")!.Value);
            Assert.Equal("A & B < C", nfo.Root.Element("plot")!.Value);
        });

        Test("本机剧集执行：S00、单集编号与series身份分开", () =>
        {
            using var fixture = Video();
            var metadata = new LocalMediaMetadata("Series", 2020, LocalMediaType.Episode, "themoviedb", "100")
            { Season = 0, Episode = 2, EpisodeId = "200", EpisodeTitle = "Special", EpisodeAired = new DateOnly(2020, 1, 2) };
            var plan = Prepare(fixture, metadata);
            Assert.Equal(Path.Combine(fixture.Target, "Series (2020)", "Season 00", "Series - S00E02.mkv"), plan.TargetPath);
            Assert.True(Execute(plan).Success);
            var episode = XDocument.Load(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.EpisodeNfo).Path);
            Assert.Equal("episodedetails", episode.Root!.Name.LocalName);
            Assert.Equal("200", episode.Root.Element("uniqueid")!.Value);
            Assert.Equal("2020-01-02", episode.Root.Element("aired")!.Value);
            var series = XDocument.Load(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.SeriesNfo).Path);
            Assert.Equal("100", series.Root!.Element("uniqueid")!.Value);
        });

        Test("本机剧集规划：缺季集或身份必须拒绝", () =>
        {
            using var fixture = Video();
            Assert.Throws<ArgumentException>(() => Prepare(fixture, Movie() with { MediaType = LocalMediaType.Episode }));
            Assert.Throws<ArgumentException>(() => Prepare(fixture, Movie() with { MediaId = "" }));
        });

        Test("本机链接执行：同长度同时间改内容也拒绝", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            File.WriteAllText(plan.Source.Path, "other");
            File.SetLastWriteTimeUtc(plan.Source.Path, plan.Source.LastWriteTimeUtc);
            var result = Execute(plan);
            Assert.False(result.Success);
            Assert.False(File.Exists(plan.TargetPath));
        });

        Test("本机链接执行：预览后字幕改变整项拒绝", () =>
        {
            using var fixture = Video();
            var subtitle = fixture.Write("source/original.zh.srt", "before");
            var plan = Prepare(fixture, Movie());
            File.WriteAllText(subtitle, "after");
            Assert.False(Execute(plan).Success);
            Assert.False(File.Exists(plan.TargetPath));
        });

        Test("本机链接执行：计划单次消费且取消不能重放", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var first = LocalMediaOrganizer.ExecuteAsync(plan, Images, cancellation.Token).GetAwaiter().GetResult();
            Assert.True(first.Cancelled);
            Assert.False(Execute(plan).Success);
            Assert.False(File.Exists(plan.TargetPath));
        });

        Test("本机链接执行：图片失败准确保留部分完成", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/poster" });
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromException<byte[]>(new IOException("图片失败")), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.True(File.Exists(plan.TargetPath));
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.VideoLink && item.Success));
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Poster && !item.Success));
            Assert.False(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path));
        });

        Test("本机直接改名：视频字幕更名、旁车按stem命名且忽略目标根", () =>
        {
            using var fixture = Video();
            fixture.Write("source/original.zh.srt", "字幕");
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            var plan = LocalMediaOrganizer.Prepare(source, "not-a-target", Movie() with { PosterUrl = "https://example.invalid/a" }, LocalMediaLinkMode.Rename);
            Assert.Equal(Path.Combine(fixture.Source, "Film (2026).mkv"), plan.TargetPath);
            Assert.True(Execute(plan).Success);
            Assert.False(File.Exists(source.Path));
            Assert.Equal("video", File.ReadAllText(plan.TargetPath));
            Assert.Equal("字幕", File.ReadAllText(Path.Combine(fixture.Source, "Film (2026).zh.srt")));
            Assert.True(File.Exists(Path.Combine(fixture.Source, "Film (2026).nfo")));
            Assert.True(File.Exists(Path.Combine(fixture.Source, "Film (2026)-poster.jpg")));
            Assert.False(File.Exists(Path.Combine(fixture.Source, "poster.jpg")));
        });

        Test("本机改名执行：确认后出现同名文件拒绝并保留源", () =>
        {
            using var fixture = Video();
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            var plan = LocalMediaOrganizer.Prepare(source, "", Movie(), LocalMediaLinkMode.Rename);
            File.WriteAllText(plan.TargetPath, "existing");
            Assert.False(Execute(plan).Success);
            Assert.Equal("video", File.ReadAllText(source.Path));
            Assert.Equal("existing", File.ReadAllText(plan.TargetPath));
        });

        Test("本机扫描：源目录和文件重解析点不跟随", () =>
        {
            using var fixture = Video();
            var destination = fixture.Write("elsewhere/external.mkv", "external");
            Directory.CreateSymbolicLink(Path.Combine(fixture.Source, "junction"), Path.GetDirectoryName(destination)!);
            File.CreateSymbolicLink(Path.Combine(fixture.Source, "alias.mkv"), destination);
            Assert.Equal(1, LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Count);
            Assert.Throws<IOException>(() => LocalMediaOrganizer.Scan(Path.Combine(fixture.Source, "junction"), true, CancellationToken.None));
        });

        Test("本机规划：目标祖先重解析点拒绝", () =>
        {
            using var fixture = Video();
            Directory.CreateDirectory(fixture.Target);
            Directory.CreateSymbolicLink(Path.Combine(fixture.Source, "target-link"), fixture.Target);
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            Assert.Throws<IOException>(() => LocalMediaOrganizer.Prepare(source, Path.Combine(fixture.Source, "target-link", "nested"), Movie(), LocalMediaLinkMode.HardLink));
        });

        Test("本机剧集：同作品共享series NFO且不同作品拒绝混用", () =>
        {
            using var fixture = Video();
            var metadata = new LocalMediaMetadata("Series", 2020, LocalMediaType.Episode, "themoviedb", "100") { Season = 1, Episode = 1, EpisodeId = "201" };
            var first = Prepare(fixture, metadata);
            Assert.True(Execute(first).Success);
            var second = Prepare(fixture, metadata with { Episode = 2, EpisodeId = "202" });
            Assert.True(Execute(second).Success);
            Assert.Throws<IOException>(() => Prepare(fixture, metadata with { MediaId = "999", Episode = 3 }));
        });

        Test("本机软链接：真实目标路径且无复制降级", () =>
        {
            using var fixture = Video();
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            var plan = LocalMediaOrganizer.Prepare(source, fixture.Target, Movie(), LocalMediaLinkMode.SymbolicLink);
            var result = Execute(plan);
            Assert.True(result.Success, string.Join(";", result.Items.Select(item => item.Message)));
            Assert.Equal(source.Path, new FileInfo(plan.TargetPath).ResolveLinkTarget(false)!.FullName);
            Assert.Equal("video", File.ReadAllText(source.Path));
        });

        Test("本机链接执行：任意图片服务失败也是部分失败回执", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/a" });
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromException<byte[]>(new InvalidOperationException("识别服务已断开")), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.True(File.Exists(plan.TargetPath));
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Poster && !item.Success));
        });

        Test("本机链接执行：新同名文件在图片等待读取中出现也不覆盖", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/a" });
            var poster = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path;
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) =>
            {
                File.WriteAllText(poster, "someone-else");
                return Task.FromResult(Jpeg());
            }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.Equal("someone-else", File.ReadAllText(poster));
            Assert.False(Directory.EnumerateFiles(Path.GetDirectoryName(poster)!, ".momoka-*.tmp").Any());
        });

        Test("本机链接执行：图片等待期间取消保留链接并列明取消", () =>
        {
            using var fixture = Video();
            using var cancellation = new CancellationTokenSource();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/a" });
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(Jpeg());
            }, cancellation.Token).GetAwaiter().GetResult();
            Assert.True(result.Cancelled);
            Assert.False(result.Success);
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.VideoLink && item.Success));
            Assert.True(File.Exists(plan.TargetPath));
            Assert.False(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path));
        });

        Test("本机链接执行：源已被别的写句柄占用就拒绝", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            using var writer = new FileStream(plan.Source.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            Assert.False(Execute(plan).Success);
            Assert.False(File.Exists(plan.TargetPath));
        });

        Test("本机剧集：缺单集ID不把整剧ID写到单集NFO", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, new("Series", 2020, LocalMediaType.Episode, "themoviedb", "100") { Season = 1, Episode = 2 });
            Assert.True(Execute(plan).Success);
            var nfo = XDocument.Load(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.EpisodeNfo).Path);
            Assert.Null(nfo.Root!.Element("uniqueid"));
        });

        Test("本机剧集：series NFO禁止DTD及重解析文件", () =>
        {
            using var fixture = Video();
            fixture.Write("target/Series (2020)/tvshow.nfo", "<!DOCTYPE tvshow [<!ENTITY external SYSTEM 'file:///missing'>]><tvshow><uniqueid type='tmdb'>100</uniqueid></tvshow>");
            Assert.Throws<System.Xml.XmlException>(() => Prepare(fixture, new("Series", 2020, LocalMediaType.Episode, "themoviedb", "100") { Season = 1, Episode = 2 }));
        });

        Test("本机链接执行：确认后目标祖先替换为重解析路径拒绝", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            var elsewhere = fixture.Write("elsewhere/keep.txt", "keep");
            Directory.CreateSymbolicLink(fixture.Target, Path.GetDirectoryName(elsewhere)!);
            Assert.False(Execute(plan).Success);
            Assert.Equal("keep", File.ReadAllText(elsewhere));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(elsewhere)!, "Film (2026)", "Film (2026).mkv")));
        });

        Test("本机链接规划：目标根在外但作品目录落入源树仍拒绝", () =>
        {
            using var fixture = new MediaFiles();
            fixture.Write("source/Film (2026)/original.mkv", "video");
            var source = LocalMediaOrganizer.Scan(Path.Combine(fixture.Source, "Film (2026)"), true, CancellationToken.None).Single();
            Assert.Throws<IOException>(() => LocalMediaOrganizer.Prepare(source, fixture.Source, Movie(), LocalMediaLinkMode.HardLink));
        });

        Test("本机扫描：检查取消而不继续遍历", () =>
        {
            using var fixture = Video();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => LocalMediaOrganizer.Scan(fixture.Source, true, cancellation.Token));
        });

        Test("本机图片：PNG计划路径和真实字节一致", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/image.PNG?token=private" });
            var poster = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster);
            Assert.Equal("poster.png", Path.GetFileName(poster.Path));
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromResult(bytes), CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(result.Success, result.Summary);
            Assert.True(File.ReadAllBytes(poster.Path).SequenceEqual(bytes));
            Assert.False(File.Exists(Path.ChangeExtension(poster.Path, ".jpg")));
        });

        Test("本机图片：WEBP输出保持预览后缀", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { BackdropUrl = "https://example.invalid/image.webp" });
            var image = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Backdrop);
            Assert.Equal("fanart.webp", Path.GetFileName(image.Path));
            var bytes = new byte[] { 82, 73, 70, 70, 12, 0, 0, 0, 87, 69, 66, 80, 86, 80, 56, 32 };
            Assert.True(LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromResult(bytes), CancellationToken.None).GetAwaiter().GetResult().Success);
            Assert.True(File.ReadAllBytes(image.Path).SequenceEqual(bytes));
        });

        Test("本机图片：未知或无后缀采用JPEG计划并拒绝PNG", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/image?token=private" });
            var image = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster);
            Assert.Equal("poster.jpg", Path.GetFileName(image.Path));
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromResult(bytes), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.False(File.Exists(image.Path));
            Assert.True(File.Exists(plan.TargetPath));
            Assert.DoesNotContain("private", string.Join(";", result.Items.Select(item => item.Message)));
        });

        Test("本机图片：显式PNG后缀拒绝JPEG且不偷偷改路径", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { EpisodeImageUrl = "", PosterUrl = "https://example.invalid/image.png" });
            var image = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster);
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromResult(Jpeg()), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.False(File.Exists(image.Path));
            Assert.False(File.Exists(Path.ChangeExtension(image.Path, ".jpg")));
        });

        Test("本机图片：非图片字节拒绝且保留部分完成", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/image.jpg" });
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromResult(new byte[] { 1, 2, 3 }), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.VideoLink && item.Success));
            Assert.False(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path));
        });

        Test("本机规划：开始已取消时不读取hash也不写目标", () =>
        {
            using var fixture = Video();
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var method = typeof(LocalMediaOrganizer).GetMethods().SingleOrDefault(item => item.Name == "Prepare" && item.GetParameters().Length == 5);
            Assert.NotNull(method, "Prepare应接收取消令牌");
            var error = Assert.Catch<TargetInvocationException>(() => method!.Invoke(null, [source, fixture.Target, Movie(), LocalMediaLinkMode.HardLink, cancellation.Token]));
            Assert.True(error.InnerException is OperationCanceledException);
            Assert.False(Directory.Exists(fixture.Target));
        });
    }

    private static LocalMediaMetadata Movie() => new("Film", 2026, LocalMediaType.Movie, "themoviedb", "123");
    private static MediaFiles Video()
    {
        var fixture = new MediaFiles();
        fixture.Write("source/original.mkv", "video");
        return fixture;
    }
    private static LocalMediaPlan Prepare(MediaFiles files, LocalMediaMetadata metadata) =>
        LocalMediaOrganizer.Prepare(LocalMediaOrganizer.Scan(files.Source, true, CancellationToken.None).Single(), files.Target, metadata, LocalMediaLinkMode.HardLink);
    private static byte[] Jpeg() => [255, 216, 255, 224, 0, 2, 255, 217];
    private static Task<byte[]> Images(string _, CancellationToken cancellation) => Task.FromResult(Jpeg());
    private static LocalMediaResult Execute(LocalMediaPlan plan) => LocalMediaOrganizer.ExecuteAsync(plan, Images, CancellationToken.None).GetAwaiter().GetResult();
    private static uint NativeLinkCount(string path)
    {
        using var handle = File.OpenHandle(path);
        Assert.True(GetFileInformationByHandle(handle, out var info));
        return info.LinkCount;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out FileInformation info);

    private sealed class MediaFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Momoka-local-media-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_root, "source");
        public string Target => Path.Combine(_root, "target");
        public MediaFiles() => Directory.CreateDirectory(Source);
        public string Write(string path, string text)
        {
            var full = Path.Combine(_root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            return full;
        }
        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
