using System.Reflection;
using System.Xml.Linq;
using Momoka.Emby;
using Momoka.LocalMedia;
using Momoka.Metadata;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

#pragma warning disable RS0030 // The synchronous console harness must await the actual asynchronous filesystem operation.
public static class LocalMediaTests
{
    public static void Register()
    {
        Test("本机整理路径：接受网络共享绝对路径，拒绝设备、相对与上级跳转", () =>
        {
            Assert.Equal(@"\\server\share\movies", LocalMediaFiles.FullPath(@"\\server\share\movies\"));
            Assert.Equal(@"\\server\share\movies", LocalMediaFiles.FullPath(@"//server/share/movies"));
            Assert.Equal(@"\\server\share", LocalMediaFiles.FullPath(@"\\server\share"));
            Assert.Equal(@"\\localhost\C$\Temp", LocalMediaFiles.FullPath(@"\\localhost\C$\Temp"));
            foreach (var path in new[] { @"\\server", @"\\server\", @"\\\share\dir", @"\\server\\dir", @"\\?\UNC\server\share\dir",
                @"\\.\C:\dir", @"\??\C:\dir", @"\\server\share\..\dir", @"\\server\share\dir\file.mkv:stream", @"C:relative", @"C:\dir\..\other", "relative", "" })
                Assert.Throws<IOException>(() => LocalMediaFiles.FullPath(path), path);
            Assert.True(LocalMediaOrganizer.IsNetworkPath(@"\\server\share"));
            Assert.True(LocalMediaOrganizer.IsNetworkPath(@"//server/share"));
            Assert.False(LocalMediaOrganizer.IsNetworkPath(@"C:\"));
            Assert.False(LocalMediaOrganizer.IsNetworkPath(@"\\.\C:\"));
        });

        Test("本机整理路径：UNC 使用扩展前缀", () =>
        {
            Assert.Equal(@"\\?\UNC\server\share\dir\file.mkv", LocalMediaFiles.ExtendedPath(@"\\server\share\dir\file.mkv"));
            Assert.Equal(@"\\?\C:\dir\file.mkv", LocalMediaFiles.ExtendedPath(@"C:\dir\file.mkv"));
        });

        Test("本机整理：回环网络共享上的扫描与原地入库", () =>
        {
            var share = LoopbackShare(Path.GetTempPath());
            if (share is null)
            {
                Skip("本机整理：回环网络共享上的扫描与原地入库", "本机没有可访问的回环 SMB 管理共享");
                return;
            }
            var root = Path.Combine(share, "momoka-unc-" + Guid.NewGuid().ToString("N"));
            var sourceRoot = Path.Combine(root, "source");
            var metadata = Path.Combine(Path.GetTempPath(), "momoka-unc-meta-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(sourceRoot);
                File.WriteAllText(Path.Combine(sourceRoot, "original.mkv"), "network-video");
                var source = LocalMediaOrganizer.Scan(sourceRoot, true, CancellationToken.None).Single();
                Assert.True(source.Path.StartsWith(@"\\localhost\", StringComparison.OrdinalIgnoreCase), "扫描结果应保持网络路径");
                var plan = LocalMediaOrganizer.Prepare(source, metadata, "UNC", Movie());
                var result = Execute(plan);
                Assert.True(result.Success, result.Summary + string.Join(";", result.Items.Select(item => item.Message)));
                Assert.Equal("network-video", File.ReadAllText(source.Path));
                Assert.Equal(source.Path, plan.VideoPath);
                Assert.True(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                if (Directory.Exists(metadata)) Directory.Delete(metadata, recursive: true);
            }
        });

        Test("本机扫描：只列具体视频且递归选项生效", () =>
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

        Test("本机规划：视频保持原位，NFO与图片写入元数据目录", () =>
        {
            using var fixture = Video();
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            var plan = LocalMediaOrganizer.Prepare(source, fixture.Metadata, "Library", new("A: Film?", 2026, LocalMediaType.Movie, "themoviedb", "123")
            {
                PosterUrl = "https://example.invalid/poster.jpg",
                BackdropUrl = "https://example.invalid/fanart.webp"
            });
            Assert.Equal(source.Path, plan.VideoPath);
            Assert.False(File.Exists(Path.Combine(fixture.Source, "A_ Film_ (2026).mkv")));
            var directory = Path.Combine(fixture.Metadata, "Library", "A_ Film_ (2026)");
            Assert.Equal(Path.Combine(directory, "A_ Film_ (2026).nfo"),
                plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path);
            Assert.Equal(Path.Combine(directory, "poster.jpg"),
                plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path);
            Assert.Equal(Path.Combine(directory, "fanart.webp"),
                plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Backdrop).Path);
            Assert.False(plan.Sidecars.Any(item => item.Kind is LocalMediaOutputKind.Video));
        });

        Test("本机规划：Windows设备名不作为目录名", () =>
        {
            using var fixture = Video();
            var plan = LocalMediaOrganizer.Prepare(LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single(),
                fixture.Metadata, "Library", new("CON", null, LocalMediaType.Movie, "themoviedb", "123"));
            Assert.True(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path.Contains(
                Path.Combine("Library", "_CON"), StringComparison.Ordinal));
        });

        Test("本机规划：已有元数据不覆盖", () =>
        {
            using var fixture = Video();
            var existing = fixture.Write("metadata/Library/Film (2026)/Film (2026).nfo", "existing");
            Assert.Throws<IOException>(() => Prepare(fixture, Movie()));
            Assert.Equal("existing", File.ReadAllText(existing));
        });

        Test("本机入库执行：源文件不改动且movie XML写入元数据目录", () =>
        {
            using var fixture = Video();
            var source = Path.Combine(fixture.Source, "original.mkv");
            var sourceTime = File.GetLastWriteTimeUtc(source);
            var plan = Prepare(fixture, Movie() with { Overview = "A & B < C" });
            var result = Execute(plan);
            Assert.True(result.Success, result.Summary + string.Join(";", result.Items.Select(item => item.Message)));
            Assert.Equal("video", File.ReadAllText(source));
            Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(source));
            Assert.Equal(source, plan.VideoPath);
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Video && item.Success));
            var nfoPath = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path;
            Assert.True(File.Exists(nfoPath), string.Join(";", Directory.EnumerateFiles(fixture.Metadata, "*", SearchOption.AllDirectories)));
            Assert.True(nfoPath.StartsWith(fixture.Metadata, StringComparison.OrdinalIgnoreCase));
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

        Test("本机入库执行：同长度同时间改内容也拒绝", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            File.WriteAllText(plan.Source.Path, "other");
            File.SetLastWriteTimeUtc(plan.Source.Path, plan.Source.LastWriteTimeUtc);
            var result = Execute(plan);
            Assert.False(result.Success);
            Assert.False(Directory.Exists(fixture.Metadata));
        });

        Test("本机入库执行：计划单次消费且取消不能重放", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var first = LocalMediaOrganizer.ExecuteAsync(plan, Images, cancellation.Token).GetAwaiter().GetResult();
            Assert.True(first.Cancelled);
            Assert.False(Execute(plan).Success);
            Assert.False(Directory.Exists(fixture.Metadata));
        });

        Test("本机入库执行：图片失败准确保留部分完成", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie() with { PosterUrl = "https://example.invalid/poster" });
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) => Task.FromException<byte[]>(new IOException("图片失败")), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(result.Success);
            Assert.True(File.Exists(plan.VideoPath));
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Video && item.Success));
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Poster && !item.Success));
            Assert.False(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path));
        });

        Test("本机入库重试：同作品NFO复用并补写缺失图片，别的作品仍拒写", () =>
        {
            using var fixture = Video();
            var metadata = Movie() with { PosterUrl = "https://example.invalid/poster.jpg" };
            var first = Prepare(fixture, metadata);
            var posterPath = first.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path;
            var nfoPath = first.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path;
            var failed = LocalMediaOrganizer.ExecuteAsync(first, (_, _) =>
                Task.FromException<byte[]>(new IOException("图片失败")), CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(failed.Success);
            var nfoBytes = File.ReadAllBytes(nfoPath);

            var retry = Prepare(fixture, metadata);
            var result = Execute(retry);
            Assert.True(result.Success, result.Summary + string.Join(";", result.Items.Select(item => item.Message)));
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.MovieNfo && item.Success
                && item.Message.Contains("复用", StringComparison.Ordinal)));
            Assert.True(nfoBytes.SequenceEqual(File.ReadAllBytes(nfoPath)), "重试不应改写已核对的 NFO");
            Assert.True(File.Exists(posterPath));
            Assert.Throws<IOException>(() => Prepare(fixture, metadata with { MediaId = "999" }));
        });

        Test("本机规划：演职人员头像按人物编号写入 people 子目录", () =>
        {
            using var fixture = Video();
            var metadata = Movie() with
            {
                People =
                [
                    new LocalMediaPerson("演员甲", "角色甲", false) { Id = "101", ImageUrl = "https://image.tmdb.org/t/p/original/a.jpg" },
                    new LocalMediaPerson("导演乙", "", true) { Id = "201", ImageUrl = "https://image.tmdb.org/t/p/original/c.png" },
                    new LocalMediaPerson("无头像", "", false) { Id = "301" }
                ]
            };
            var plan = Prepare(fixture, metadata);
            var images = plan.Sidecars.Where(item => item.Kind == LocalMediaOutputKind.PersonImage).ToArray();
            Assert.Equal(2, images.Length);
            Assert.True(images.All(item => item.Optional));
            Assert.True(images[0].Path.EndsWith(Path.Combine("people", "101.jpg"), StringComparison.OrdinalIgnoreCase));
            Assert.True(images[1].Path.EndsWith(Path.Combine("people", "201.png"), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(images[0].Path, plan.Metadata.People[0].ImagePath);
            Assert.Equal("", plan.Metadata.People[2].ImagePath);
        });

        Test("本机入库执行：头像失败只少一张脸，算完成", () =>
        {
            using var fixture = Video();
            var metadata = Movie() with
            {
                People = [new LocalMediaPerson("演员甲", "角色甲", false)
                    { Id = "101", ImageUrl = "https://image.tmdb.org/t/p/original/a.jpg" }]
            };
            var plan = Prepare(fixture, metadata);
            var result = LocalMediaOrganizer.ExecuteAsync(plan, (_, _) =>
                Task.FromException<byte[]>(new IOException("头像失败")), CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(result.Success, result.Summary);
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.PersonImage && !item.Success && item.Optional));
            Assert.True(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.MovieNfo).Path));
        });

        Test("本机入库重试：已写好的头像与 NFO 一并复用", () =>
        {
            using var fixture = Video();
            var metadata = Movie() with
            {
                People = [new LocalMediaPerson("演员甲", "角色甲", false)
                    { Id = "101", ImageUrl = "https://image.tmdb.org/t/p/original/a.jpg" }]
            };
            var first = Prepare(fixture, metadata);
            var firstResult = Execute(first);
            Assert.True(firstResult.Success, firstResult.Summary);
            var portrait = first.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.PersonImage).Path;
            Assert.True(File.Exists(portrait));

            var retry = Execute(Prepare(fixture, metadata));
            Assert.True(retry.Success, retry.Summary);
            Assert.True(retry.Items.Any(item => item.Kind == LocalMediaOutputKind.PersonImage && item.Success
                && item.Message.Contains("复用", StringComparison.Ordinal)));
        });

        Test("本机索引：旧版没有人物编号的条目自动放开一次补头像", () =>
        {
            using var fixture = Video();
            var index = Path.Combine(Path.GetTempPath(), "momoka-library-" + Guid.NewGuid().ToString("N"), "local-library.json");
            try
            {
                var library = new LocalMediaLibrary(index);
                var old = Movie() with { People = [new LocalMediaPerson("演员甲", "角色甲", false)] };
                var plan = Prepare(fixture, old);
                _ = library.ExecuteAsync("lib", plan, Images, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(library.WasAttempted("lib", plan.Source.Path));
                Assert.Equal("", library.Entries.Single().Metadata.People.Single().Id);

                library.RetryMissingPeople("lib");
                Assert.False(library.WasAttempted("lib", plan.Source.Path));

                var upgraded = old with
                {
                    People = [new LocalMediaPerson("演员甲", "角色甲", false)
                        { Id = "101", ImageUrl = "https://image.tmdb.org/t/p/original/a.jpg" }]
                };
                var retry = Prepare(fixture, upgraded);
                _ = library.ExecuteAsync("lib", retry, Images, CancellationToken.None).GetAwaiter().GetResult();
                var entry = library.Entries.Single();
                Assert.True(entry.Complete);
                Assert.Equal("101", entry.Metadata.People.Single().Id);
                Assert.True(entry.Metadata.People.Single().ImagePath.EndsWith(
                    Path.Combine("people", "101.jpg"), StringComparison.OrdinalIgnoreCase));
                Assert.Equal("", entry.Metadata.People.Single().ImageUrl);

                library.RetryMissingPeople("lib");
                Assert.True(library.WasAttempted("lib", retry.Source.Path), "补过头像后不应再放开");
            }
            finally
            {
                var directory = Path.GetDirectoryName(index)!;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });

        Test("本机索引：补头像失败不把已入库条目变成待校正", () =>
        {
            using var fixture = Video();
            var index = Path.Combine(Path.GetTempPath(), "momoka-library-" + Guid.NewGuid().ToString("N"), "local-library.json");
            try
            {
                var library = new LocalMediaLibrary(index);
                var old = Movie() with { People = [new LocalMediaPerson("演员甲", "角色甲", false)] };
                var plan = Prepare(fixture, old);
                _ = library.ExecuteAsync("lib", plan, Images, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(library.Entries.Single().Complete);

                library.RetryMissingPeople("lib");
                library.RecognitionFailed("lib", plan.Source.Path, "无法连接TMDB，请检查网络后重试。");
                Assert.Equal(0, library.Issues.Count);
                Assert.True(library.Entries.Single().Complete);
            }
            finally
            {
                var directory = Path.GetDirectoryName(index)!;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });

        Test("本机索引：只放开未完成条目的重试记录", () =>
        {
            using var fixture = Video();
            var index = Path.Combine(Path.GetTempPath(), "momoka-library-" + Guid.NewGuid().ToString("N"), "local-library.json");
            try
            {
                var library = new LocalMediaLibrary(index);
                var metadata = Movie() with { PosterUrl = "https://example.invalid/poster.jpg" };
                var plan = Prepare(fixture, metadata);
                _ = library.ExecuteAsync("lib", plan, (_, _) =>
                    Task.FromException<byte[]>(new IOException("图片失败")), CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(library.WasAttempted("lib", plan.Source.Path));
                Assert.False(library.Entries.Single().Complete);

                library.RetryIncomplete("lib");
                Assert.False(library.WasAttempted("lib", plan.Source.Path));

                var retry = Prepare(fixture, metadata);
                _ = library.ExecuteAsync("lib", retry, Images, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(library.Entries.Single().Complete);
                library.RetryIncomplete("lib");
                Assert.True(library.WasAttempted("lib", retry.Source.Path));
            }
            finally
            {
                var directory = Path.GetDirectoryName(index)!;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });

        Test("本机索引：旧版图片格式失败的条目自动放开一次重试", () =>
        {
            using var fixture = Video();
            var index = Path.Combine(Path.GetTempPath(), "momoka-library-" + Guid.NewGuid().ToString("N"), "local-library.json");
            try
            {
                var library = new LocalMediaLibrary(index);
                var metadata = Movie() with { PosterUrl = "https://example.invalid/poster.jpg" };
                var plan = Prepare(fixture, metadata);
                // 目标后缀是 .jpg，实际回 PNG：旧版就是这样把 TMDB 的图片拒在外面。
                _ = library.ExecuteAsync("lib", plan, (_, _) =>
                    Task.FromResult(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), CancellationToken.None).GetAwaiter().GetResult();
                Assert.False(library.Entries.Single().Complete);
                Assert.True(library.WasAttempted("lib", plan.Source.Path));

                library.RetryBrokenArtwork("lib");
                Assert.False(library.WasAttempted("lib", plan.Source.Path));

                var retry = Prepare(fixture, metadata);
                _ = library.ExecuteAsync("lib", retry, Images, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(library.Entries.Single().Complete);
            }
            finally
            {
                var directory = Path.GetDirectoryName(index)!;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });

        Test("本机索引：已看与收藏按作品分组保存并可重读", () =>
        {
            using var fixture = Video();
            var index = Path.Combine(Path.GetTempPath(), "momoka-library-" + Guid.NewGuid().ToString("N"), "local-library.json");
            try
            {
                var library = new LocalMediaLibrary(index);
                var plan = Prepare(fixture, Movie());
                _ = library.ExecuteAsync("lib", plan, Images, CancellationToken.None).GetAwaiter().GetResult();
                var entry = library.Entries.Single();
                Assert.False(entry.Watched || entry.Favorite);

                Assert.True(library.SetUserState("lib", LocalMediaEntryArtwork.GroupKey(entry), watched: true, favorite: true));
                // 重试补写会用这一轮的新元数据重建条目；已看与收藏是用户状态，不能跟着清零。
                library.RetryIncomplete("lib");
                _ = library.ExecuteAsync("lib", Prepare(fixture, Movie()), Images, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(library.Entries.Single().Watched && library.Entries.Single().Favorite);
                var reopened = new LocalMediaLibrary(index);
                var saved = reopened.Entries.Single();
                Assert.True(saved.Watched && saved.Favorite);
            }
            finally
            {
                var directory = Path.GetDirectoryName(index)!;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
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

        Test("本机图像执行：新同名文件在图片等待读取中出现也不覆盖", () =>
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

        Test("本机图像执行：图片等待期间取消保留视频回执并列明取消", () =>
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
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Video && item.Success));
            Assert.True(File.Exists(plan.VideoPath));
            Assert.False(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path));
        });

        Test("本机入库执行：源已被别的写句柄占用就拒绝", () =>
        {
            using var fixture = Video();
            var plan = Prepare(fixture, Movie());
            using var writer = new FileStream(plan.Source.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            Assert.False(Execute(plan).Success);
            Assert.False(Directory.Exists(fixture.Metadata));
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
            fixture.Write("metadata/Library/Series (2020)/tvshow.nfo", "<!DOCTYPE tvshow [<!ENTITY external SYSTEM 'file:///missing'>]><tvshow><uniqueid type='tmdb'>100</uniqueid></tvshow>");
            Assert.Throws<System.Xml.XmlException>(() => Prepare(fixture, new("Series", 2020, LocalMediaType.Episode, "themoviedb", "100") { Season = 1, Episode = 2 }));
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
            Assert.True(File.Exists(plan.VideoPath));
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
            Assert.True(result.Items.Any(item => item.Kind == LocalMediaOutputKind.Video && item.Success));
            Assert.False(File.Exists(plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.Poster).Path));
        });

        Test("本机规划：开始已取消时不读取hash也不写元数据", () =>
        {
            using var fixture = Video();
            var source = LocalMediaOrganizer.Scan(fixture.Source, true, CancellationToken.None).Single();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var method = typeof(LocalMediaOrganizer).GetMethods().SingleOrDefault(item => item.Name == "Prepare" && item.GetParameters().Length == 5);
            Assert.NotNull(method, "Prepare应接收取消令牌");
            var error = Assert.Catch<TargetInvocationException>(() => method!.Invoke(null, [source, fixture.Metadata, "Library", Movie(), cancellation.Token]));
            Assert.True(error.InnerException is OperationCanceledException);
            Assert.False(Directory.Exists(fixture.Metadata));
        });

        Test("TMDB 识别：中英双语片名与集数能从验收文件名中拆出并匹配中文标题", () =>
        {
            const string context = "爱·回家之三代同糖.Like.Mother.Like.Daughter.S01.2026.1080p.MyTVSuper.WEB-DL.H265.AAC-ADWeb/爱·回家之三代同糖.Like.Mother.Like.Daughter.S01E75.2026.1080p.MyTVSuper.WEB-DL.H265.AAC-ADWeb.mkv";
            var parsed = LocalMediaName.Parse(context);
            Assert.Equal(1, parsed.Season);
            Assert.Equal(75, parsed.Episode);
            Assert.True(parsed.IsSeries);
            Assert.Equal("", parsed.Problem);
            Assert.Contains("Like Mother Like Daughter", parsed.Title);
            var queries = LocalMediaName.SearchQueries(parsed);
            Assert.True(queries.Any(query => query.Contains("爱·回家之三代同糖", StringComparison.Ordinal)));
            Assert.True(queries.Any(query => query.Contains("Like Mother Like Daughter", StringComparison.Ordinal)));
            var candidates = new[]
            {
                new TmdbMediaCandidate { Id = "331026", Title = "爱·回家之三代同糖", OriginalTitle = "愛·回家之三代同糖", Year = 2026, IsSeries = true }
            };
            var match = LocalMediaName.UniqueExactMatch(parsed, candidates);
            Assert.NotNull(match);
            Assert.Equal("331026", match!.Id);
        });

        Test("本机文件读数：从文件名读出大小、分辨率、编码、音频与制作组", () =>
        {
            Assert.Equal("641 MB  ·  1080p  ·  HEVC  ·  AAC  ·  ADWeb",
                LocalMediaFileInfo.Label(
                    @"C:\TEST\爱·回家之三代同糖.Like.Mother.Like.Daughter.S01E75.2026.1080p.MyTVSuper.WEB-DL.H265.AAC-ADWeb.mkv",
                    672126840));
            Assert.Equal("1.2 GB  ·  2160p  ·  H264  ·  EAC3", LocalMediaFileInfo.Label("Movie.2160p.H264.EAC3.mkv", 1288490189));
            Assert.Equal("", LocalMediaFileInfo.Label("Movie.mkv", null));
        });

        Test("演职人员：本机头像直接带进卡片且不冒充可导航人物", () =>
        {
            var item = new EmbyItem
            {
                Type = EmbyItemType.Series,
                People =
                [
                    new EmbyPerson
                    {
                        Id = "local-person:101", Name = "演员甲", Role = "角色甲", Type = "Actor",
                        PrimaryImageTag = "local", LocalArtworkPath = @"C:\meta\people\101.jpg"
                    },
                    new EmbyPerson { Id = "local-person:201", Name = "无头像", Type = "Actor" }
                ]
            };
            var cast = ItemDetail.Cast(item, portraitsOnly: true);
            Assert.Equal(1, cast.Count);
            Assert.Equal(@"C:\meta\people\101.jpg", cast[0].Card.LocalArtworkPath);
            Assert.True(cast[0].Card.IsDisplayOnly);
            Assert.Equal("角色甲", cast[0].Credit);
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
        LocalMediaOrganizer.Prepare(LocalMediaOrganizer.Scan(files.Source, true, CancellationToken.None).Single(), files.Metadata, "Library", metadata);
    private static byte[] Jpeg() => [255, 216, 255, 224, 0, 2, 255, 217];
    private static Task<byte[]> Images(string _, CancellationToken cancellation) => Task.FromResult(Jpeg());
    private static LocalMediaResult Execute(LocalMediaPlan plan) => LocalMediaOrganizer.ExecuteAsync(plan, Images, CancellationToken.None).GetAwaiter().GetResult();

    private sealed class MediaFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Momoka-local-media-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_root, "source");
        public string Metadata => Path.Combine(_root, "metadata");
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
