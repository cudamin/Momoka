using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>三个库的单服务器夹具夹带其它来源的旧偏好，驱动真实排序表、来源切换和宽窄窗口。</summary>
    private async Task HomeSourcesAsync()
    {
        using var fixture = CreateFixture();
        var libraries = new[]
        {
            new EmbyItem { Id = "movies", Name = "电影", Type = EmbyItemType.CollectionFolder, CollectionType = "movies" },
            new EmbyItem { Id = "series", Name = "电视节目", Type = EmbyItemType.CollectionFolder, CollectionType = "tvshows" },
            new EmbyItem { Id = "mixed", Name = "混合内容", Type = EmbyItemType.CollectionFolder }
        };
        var poster = await FixtureImageAsync(400, 600);
        var content = new EmbyItem
        {
            Id = "home-sources-movie",
            Name = "离线影片",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" }
        };
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Views", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = libraries, TotalRecordCount = libraries.Length }));
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(new[] { content }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { content } }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync("other.invalid", "other");
        var other = fixture.Session.Capture();
        var otherLibrary = other.Bind(new EmbyItem { Id = "movies", Name = "电影", Type = EmbyItemType.CollectionFolder });
        await fixture.SelectAsync("current.invalid", "user");
        var current = fixture.Session.Capture();
        var currentLibraries = libraries.Select(library => current.Bind(library)).ToArray();
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan([
            new() { Key = HomeLayout.LibraryKey(otherLibrary), Title = "最近添加 · 电影", Visible = false },
            new() { Key = HomeLayout.LibraryKey(currentLibraries[1]), Title = "最近添加 · 电视节目", Visible = false },
            new() { Key = HomeLayout.Libraries, Title = "媒体库" }], [otherLibrary, .. currentLibraries]));
        var hidden = fixture.Settings.Ui.HomeRows[0];
        fixture.Settings.AggregateMode = false;
        Require(fixture.Session.ApplyAggregateMode() == 1, "未真正收口成单来源");
        Require(!other.IsAvailable && current.IsAvailable, "单来源模式没有撤销其它在线账号");
        fixture.Capabilities.Sources = [new(current, MediaServerKind.Emby, true)];

        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "home-sources"));
        paths.EnsureCreated();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var settings = new SettingsPage();
        settings.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!,
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), new UpdateClient(new RejectNetwork()),
            fixture.Capabilities, fixture.Images);
        await settings.ViewModel.ReloadAsync();
        settings.ViewModel.SelectedCategory = "主页";
        // 接入正式页面的来源变更回调；不调用真实网络，不以重建页面掩盖同页不刷新。
        Field(settings, "_session", fixture.Session);
        void SourcesChanged(object? sender, EventArgs args)
        {
            fixture.Capabilities.Sources = fixture.Session.Sources.Select(source =>
                new Momoka.Services.ServerSettingsSource(source, source.Connection.ServerKind, true)).ToArray();
            _ = Call(settings, "OnSourceListChanged", sender, args);
        }
        fixture.Session.SourcesChanged += SourcesChanged;
        var frame = new Frame();
        _root.Children.Add(settings);
        try
        {
            // 直接挂载的页面也必须开启真实访问票据；否则正式来源变更回调会拒绝此夹具。
            BeginSettingsFixtureVisit(settings);
            var table = settings.ViewModel.HomeRows!;
            var choices = table.Rows;
            async Task CheckTableAsync(int count, string state)
            {
                await UntilAsync(() => choices.Count == count);
                await LayoutAsync(settings);
                await WaitForSettingsSettledAsync(settings);
                var scroll = Descendants(settings).OfType<ScrollViewer>().Single(view => view.Name == "SettingsScroll");
                scroll.ChangeView(0, 650, null, disableAnimation: true);
                await LayoutAsync(settings);
                await Task.Delay(450); // 真实 ListView 换位与主题过渡结束后再保存可见行。
                var list = Descendants(settings).OfType<ListView>().Single(view => ReferenceEquals(view.ItemsSource, choices));
                Require(list.Items.Count == count && Math.Abs(list.ActualHeight - table.ListHeight) < 1,
                    $"{state}：隐藏来源没有收起整行或留下空白");
                Require(settings.ViewModel.MeasureHomeRows() is { Ok: true }, $"{state}：范围内版面未对账");
                for (var index = 0; index < list.Items.Count; index++)
                {
                    var container = (ListViewItem)list.ContainerFromIndex(index);
                    Require(container?.Content is HomeRowChoice choice && choice.Key == choices[index].Key,
                        $"{state}：真实控件仍绑定旧来源");
                }
                Require(ReferenceEquals(choices, table.Rows), "来源变化替换了绑定集合对象");
            }

            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 980);
                await CheckTableAsync(6, "单来源");
                Require(choices.Count(row => HomeLayout.LibraryId(row.Key) is not null) == 3
                    && choices.All(row => row.Key != hidden.Key), "单服务器三个库仍列出了其它来源");
                await SaveProbeScreenAsync($"settings-home-sources-single-{width}-screen.png");
            }
            var series = choices.Single(row => row.Key == HomeLayout.LibraryKey(currentLibraries[1]));
            Require(!series.Visible, "当前账号的勾选偏好没有恢复");
            series.Visible = true;
            series.DownCommand.Execute(null);
            var currentOrder = choices.Select(row => row.Key).ToArray();
            Require(fixture.Settings.Ui.HomeRows[0].Key == hidden.Key && !fixture.Settings.Ui.HomeRows[0].Visible,
                "编辑当前表清空或移动了隐藏来源偏好");
            // 切到其它分类后再回主页也必须沿用范围和编辑，而不是读回全局表。
            settings.ViewModel.SelectedCategory = "界面";
            settings.ViewModel.SelectedCategory = "主页";
            await CheckTableAsync(6, "设置页重入");
            Require(choices.Select(row => row.Key).SequenceEqual(currentOrder), "设置页重入丢失当前排序");

            fixture.Settings.AggregateMode = true;
            var server = fixture.Settings.Servers.Single(server => server.Id == other.ServerProfileId);
            var account = server.Accounts.Single(account => account.Id == other.AccountProfileId);
            Require(await fixture.Session.TryRestoreAsync(server, account, CancellationToken.None), "聚合恢复来源失败");
            await CheckTableAsync(7, "聚合恢复");
            Require(choices[0].Key == hidden.Key && !choices[0].Visible, "恢复来源没有还原原排序和勾选");
            Require(choices.Any(row => row.Key == HomeLayout.LibraryKey(currentLibraries[0]))
                && choices.Any(row => row.Key == HomeLayout.LibraryKey(otherLibrary)), "聚合模式合并了同号库");
            await SaveProbeScreenAsync("settings-home-sources-aggregate-screen.png");

            var staleSeries = choices.Single(row => row.Key == HomeLayout.LibraryKey(currentLibraries[1]));
            fixture.Settings.AggregateMode = false;
            fixture.Session.ApplyAggregateMode();
            // 来源已撤销但 UI 队列尚未重填：旧控件交来的勾选不得改动已经隐藏来源的偏好。
            staleSeries.Visible = false;
            Require(fixture.Settings.Ui.HomeRows.Single(row => row.Key == staleSeries.Key).Visible,
                "来源切换期间的旧控件覆盖了隐藏来源偏好");
            await CheckTableAsync(4, "切到另一服务器");
            Require(choices[0].Key == hidden.Key && !choices[0].Visible
                && choices.All(row => currentLibraries.All(library => HomeLayout.LibraryKey(library) != row.Key)),
                "切到另一服务器仍列出了之前的三个库");
            await SaveProbeScreenAsync("settings-home-sources-switched-screen.png");
            var currentServer = fixture.Settings.Servers.Single(server => server.Id == current.ServerProfileId);
            var currentAccount = currentServer.Accounts.Single(account => account.Id == current.AccountProfileId);
            Require(await fixture.Session.TryRestoreAsync(currentServer, currentAccount, CancellationToken.None), "原来源恢复失败");
            await CheckTableAsync(6, "切回原服务器");
            Require(choices.Select(row => row.Key).SequenceEqual(currentOrder)
                && choices.Single(row => row.Key == HomeLayout.LibraryKey(currentLibraries[1])).Visible,
                "切回来源丢失排序或勾选");
            await SaveProbeScreenAsync("settings-home-sources-restored-screen.png");

            // 主页也只能建立当前三个库的货架，但保存时仍保留隐藏来源。
            _root.Children.Remove(settings);
            _root.Children.Add(frame);
            var restoredSource = fixture.Session.Capture();
            restoredSource.Bind(currentLibraries);
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, currentLibraries, null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady);
            var all = Get<(HomeRowPlan Row, CardShelf Shelf)[]>(home.ViewModel, "_all");
            Require(all.Count(entry => HomeLayout.LibraryId(entry.Row.Key) is not null) == 3
                && all.All(entry => entry.Row.Key != hidden.Key), "主页仍为隐藏来源创建了空货架");
            Require(fixture.Settings.Ui.HomeRows[0].Key == hidden.Key && !fixture.Settings.Ui.HomeRows[0].Visible,
                "主页归一化抹掉了隐藏来源偏好");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 980);
                await LayoutAsync(home);
                await Task.Delay(700); // 首次进场淡入结束，不能拿半透明货架冒充最终界面。
                await SaveProbeScreenAsync($"home-sources-current-{width}-screen.png");
            }
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "排序检查发出了写入或播放请求");
        }
        finally
        {
            fixture.Session.SourcesChanged -= SourcesChanged;
            Field(settings, "_session", null);
            settings.Release();
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(settings);
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
