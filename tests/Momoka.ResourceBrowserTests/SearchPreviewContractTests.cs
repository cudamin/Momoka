using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Momoka.AniBt;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>父 Register(root) 接入；只运行本树真实假 handler，不启动 WinUI 或产品入口。</summary>
internal static class SearchPreviewContractTests
{
    internal static void Register(string sourceRoot)
    {
        var root = Path.GetFullPath(sourceRoot);
        var source = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellNavigationProbe.SearchPreview.cs"));
        var directory = Path.Combine(root, "src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64");
        var shellPath = Path.Combine(directory, "Momoka.dll");
        if (!File.Exists(shellPath)) throw new InvalidOperationException("先成功编译本树 Shell Release，不能加载旧树输出");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(shellPath);
        var probe = assembly.GetType("Momoka.Shell.ShellNavigationProbe", throwOnError: true)!;
        var mp = probe.GetNestedType("SearchPreviewMoviePilotServer", BindingFlags.NonPublic)!;
        var ani = probe.GetNestedType("SearchPreviewAniBtServer", BindingFlags.NonPublic)!;

        Test("搜索试用：真实页面、资源来源2与提交入口", () =>
        {
            Assert.Contains("private async Task SearchPreviewAsync()", source);
            Assert.Contains("CreateFixture(moviePilot, aniBt)", source);
            Assert.Contains("await ConnectMoviePilotAsync(fixture)", source);
            Assert.Contains("frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services))", source);
            Assert.Contains("\"SourceTabs\").SelectedIndex = 2", source);
            Assert.Contains("\"SearchInput\").Text = \"离线示例\"", source);
            Assert.Contains("await page.SubmitSearchAsync(\"离线示例\")", source);
        });
        Test("搜索试用：等待底部结束按钮且不自动点击结果", () =>
        {
            Assert.Contains("Grid.SetRow(footer, 2)", source);
            Assert.Contains("Content = \"结束试用\"", source);
            Assert.Contains("await finished.Task", source);
            Assert.Contains("await idle", source);
            foreach (var forbidden in new[] { "Task.Delay", "CaseAsync(", "Record(", "SubscribeAsync(", "DownloadAsync(", "PlayAsync(", ".Invoke(", ".Execute(" })
                Assert.DoesNotContain(forbidden, source);
        });
        Test("搜索试用：五主题、置顶恢复、缩放和可用屏幕限幅", () =>
        {
            Assert.Contains("foreach (var theme in UiThemes.All)", source);
            Assert.Contains("ThemeHost.Apply(theme)", source);
            Assert.Contains("ThemeHost.Apply(previousTheme)", source);
            Assert.Contains("window.TopMost = true", source);
            Assert.Contains("window.TopMost = previousTopMost", source);
            Assert.Contains("Momoka · 搜索离线试用", source);
            Assert.Contains("_root.XamlRoot.RasterizationScale", source);
            Assert.Contains("Math.Min(work.Width", source);
            Assert.Contains("Math.Min(work.Height", source);
            Assert.Contains("app.Move(new Windows.Graphics.PointInt32", source);
        });
        Test("搜索试用：断线与AniBT走真实可用性通知，不保存真实偏好", () =>
        {
            Assert.Contains("await service.RefreshAvailabilityAsync()", source);
            Assert.Contains("fixture.Settings.MoviePilot.AniBtEnabled = !fixture.Settings.MoviePilot.AniBtEnabled", source);
            Assert.Contains("Require(request.Method == HttpMethod.Get, \"搜索离线试用禁止媒体服务器写入\")", source);
            Assert.DoesNotContain("SaveAsync", source);
            Assert.DoesNotContain("poster_path", source);
            Assert.DoesNotContain("BitmapImage", source.Split("private async Task SearchPreviewAsync()", 2)[1]);
        });
        Test("搜索试用：13站点及长站点名由真实协议解析", () =>
        {
            using var data = Reply(mp, "site/");
            var sites = MoviePilotService.ParseSearchSites(data.RootElement);
            Assert.Equal(13, sites.Count);
            Assert.Equal(13, sites.Select(site => site.Id).Distinct().Count());
            Assert.True(sites.Any(site => site.Name.Length > 20));
        });
        Test("搜索试用：8资源涵盖分辨率季集标签日期与做种数", () =>
        {
            using var data = Reply(mp, "search/title?keyword=offline");
            var rows = MoviePilotMediaParser.ParseResources(data.RootElement, null, null);
            Assert.Equal(8, rows.Count);
            Assert.Equal(3, rows.Select(row => row.Resolution).Distinct().Count());
            Assert.Equal(2, rows.SelectMany(row => row.Seasons).Distinct().Count());
            Assert.Equal(8, rows.SelectMany(row => row.Episodes).Distinct().Count());
            Assert.True(rows.All(row => row.PublishedAt is not null && row.Labels.Count > 0 && row.Size > 0));
            Assert.Equal(8, rows.Select(row => row.Seeders).Distinct().Count());
            Assert.True(rows.All(row => row.PageUrl!.StartsWith("https://site.invalid/", StringComparison.Ordinal)));
        });
        Test("搜索试用：MP媒体搜索3结果、不同身份且没有联网海报", () =>
        {
            using var data = Reply(mp, "media/search?keyword=offline");
            var rows = MoviePilotMediaParser.Parse(data.RootElement);
            Assert.Equal(3, rows.Count);
            Assert.Equal(2, rows.Select(row => row.Type).Distinct().Count());
            Assert.Equal(2, rows.Select(row => row.MediaSource).Distinct().Count());
            Assert.True(rows.All(row => string.IsNullOrWhiteSpace(row.PosterUrl)));
        });
        Test("搜索试用：假MP断线返回503并恢复目录", () =>
        {
            using var handler = New(mp);
            using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
            var offline = mp.GetProperty("Offline", BindingFlags.Instance | BindingFlags.NonPublic)!;
            offline.SetValue(handler, true);
            using (var response = Send(invoker, "https://moviepilot.invalid/api/v1/dashboard/system"))
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            offline.SetValue(handler, false);
            using var restored = Send(invoker, "https://moviepilot.invalid/api/v1/dashboard/system");
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        });
        foreach (var path in new[] { "subscribe/", "download/add", "library/refresh", "media/search" })
            Test("搜索试用：假MP拒绝POST " + path, () => Reject(mp, "https://moviepilot.invalid/api/v1/" + path, HttpMethod.Post));
        foreach (var path in new[] { "user/current", "download/clients", "unknown" })
            Test("搜索试用：假MP拒绝未登记GET " + path, () => Reject(mp, "https://moviepilot.invalid/api/v1/" + path, HttpMethod.Get));
        Test("搜索试用：假MP拒绝真实目标", () => Reject(mp, "https://example.com/api/v1/site/", HttpMethod.Get));
        Test("搜索试用：占位登录只在假HTTP中回答", () =>
        {
            using var data = Reply(mp, "login/access-token", HttpMethod.Post);
            Assert.Equal("fixture-token", data.RootElement.GetProperty("access_token").GetString());
        });
        Test("搜索试用：真实AniBT客户端读假目录2结果", () =>
        {
            using var handler = New(ani);
            using var client = new AniBtClient(handler);
            var page = Complete(client.SearchCatalogAsync("离线示例", null, CancellationToken.None));
            Assert.Equal(2, page.Items.Count);
            Assert.True(page.Items.All(item => item.Title.Length > 0));
        });
        foreach (var path in new[] { "/api/torrent/rel_one.torrent", "/images/poster.png", "/api/animes/torrent-match" })
            Test("搜索试用：AniBT拒绝种子图片或匹配 " + path, () => Reject(ani, "https://anibt.net" + path, HttpMethod.Get));
        Test("搜索试用：AniBT拒绝POST与异站目标", () =>
        {
            Reject(ani, "https://anibt.net/api/animes/search", HttpMethod.Post);
            Reject(ani, "https://example.com/api/animes/search", HttpMethod.Get);
        });
    }

    private static HttpMessageHandler New(Type type) => (HttpMessageHandler)Activator.CreateInstance(type, nonPublic: true)!;

    private static HttpResponseMessage Send(HttpMessageInvoker invoker, string url, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        return Complete(invoker.SendAsync(request, CancellationToken.None));
    }

    private static JsonDocument Reply(Type type, string path, HttpMethod? method = null)
    {
        using var invoker = new HttpMessageInvoker(New(type));
        using var response = Send(invoker, "https://moviepilot.invalid/api/v1/" + path, method);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(Complete(response.Content.ReadAsStringAsync()));
    }

    private static void Reject(Type type, string url, HttpMethod method)
    {
        using var invoker = new HttpMessageInvoker(New(type));
        var rejected = false;
        try { using var response = Send(invoker, url, method); }
        catch (InvalidOperationException) { rejected = true; }
        Assert.True(rejected, "假服务必须拒绝未授权目标或操作");
    }

    // 本套件只在控制台运行；沿用现有 ResourceFixture 的有界完成检查，不阻塞 UI 线程。
    private static T Complete<T>(Task<T> task)
    {
        ResourceFixture.Complete(task);
        return task.Result;
    }
}
