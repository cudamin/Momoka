using System.Net;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task ResumeQueryAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Transport.Reply = (request, _) => Query(request, "StartIndex") is { } offset
            ? Task.FromResult(Items(20, 100, "page" + offset)) : Task.FromResult(FakeTransport.Standard(request));
        using var vm = fixture.Library();
        await vm.ReloadAsync();
        vm.Cancel();
        vm.Resume();
        await UntilAsync(() => !vm.Busy);
        await vm.LoadUntilAsync(25);
        Require(vm.Cards.Count == 40 && vm.Cards[20].Item.Id.StartsWith("page20", StringComparison.Ordinal),
            "切回 Emby 必须恢复查询并接收下一页");
    }

    private async Task SeasonRefreshRaceAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Detail();
        await vm.ReloadAsync();
        var write = Pending<HttpResponseMessage>();
        var season = Pending<HttpResponseMessage>();
        var enteredWrite = Pending<bool>();
        var enteredSeason = Pending<bool>();
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("PlayedItems", StringComparison.Ordinal))
            {
                enteredWrite.TrySetResult(true);
                return write.Task;
            }
            if (Query(request, "SeasonId") == "b")
            {
                enteredSeason.TrySetResult(true);
                return season.Task;
            }
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var saving = vm.ToggleWatchedCommand.ExecuteAsync(null);
        await enteredWrite.Task;
        vm.SelectedSeason = vm.Seasons.Last();
        await enteredSeason.Task;
        try
        {
            write.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await saving;
            Require(vm.IsReady && !vm.Busy && vm.Episodes.Count > 0 && vm.PlayTarget is not null,
                "整页刷新被旧切季标志取消，留下空列表或空播放目标");
        }
        finally
        {
            season.TrySetResult(Json(new { Items = new[] { Episode("b") } }));
            await SettleAsync();
        }
    }

    private async Task NavigationRestoreRaceAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var request = LibraryRequest.Search(fixture.Services, "old");
        request.LoadedCount = 60;
        request.ScrollOffset = 300;
        var page = new LibraryPage();
        page.ViewModel.Attach(request, fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Images, fixture.Capabilities);
        Field(page, "_released", false);
        Field(page, "_navigation", 1);
        var reply = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        var newRequests = 0;
        fixture.Transport.Reply = (message, _) =>
        {
            if (Query(message, "SearchTerm") == "old")
            {
                entered.TrySetResult(true);
                return reply.Task;
            }
            newRequests++;
            return Task.FromResult(Items(20, 200, "new"));
        };
        try
        {
            var restoring = (Task)Call(page, "LoadNavigationAsync", request, 1)!;
            await entered.Task;
            await page.ViewModel.SearchAsync("new");
            reply.SetResult(Items(20, 200, "old"));
            await restoring;
            Require(newRequests == 1 && page.ViewModel.Cards.Count == 20,
                "旧导航恢复给新搜索补页或恢复了旧位置");
        }
        finally
        {
            reply.TrySetResult(Items(20, 200));
            page.Release();
            page.ViewModel.Dispose();
        }
    }

    private async Task PagingFailureAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var count = 0;
        fixture.Transport.Reply = (request, _) => Query(request, "StartIndex") is not { } offset ? Task.FromResult(FakeTransport.Standard(request))
            : Task.FromResult(offset == "0" ? Items(20, 200) : Fail());
        HttpResponseMessage Fail() { count++; return new HttpResponseMessage(HttpStatusCode.InternalServerError); }
        using var vm = fixture.Library();
        await vm.ReloadAsync();
        await vm.LoadUntilAsync(120);
        Require(count == 1 && vm.Cards.Count == 20 && !vm.Busy, "失败分页必须立即结束且保留已读卡片");
    }

    private async Task PagingCancelAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var entered = Pending<bool>();
        var reply = Pending<HttpResponseMessage>();
        var count = 0;
        fixture.Transport.Reply = (request, _) =>
        {
            if (Query(request, "StartIndex") is not { } offset) return Task.FromResult(FakeTransport.Standard(request));
            if (offset == "0") return Task.FromResult(Items(20, 200));
            count++;
            entered.TrySetResult(true);
            return reply.Task;
        };
        using var vm = fixture.Library();
        await vm.ReloadAsync();
        var pending = vm.LoadUntilAsync(120);
        await entered.Task;
        vm.Cancel();
        reply.SetResult(Items(20, 200, "late"));
        await pending;
        await SettleAsync();
        Require(count == 1 && vm.Cards.Count == 20 && !vm.Busy, "离页后补页不能重新启动");
    }

    private async Task QueryFailureAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Transport.Reply = (request, _) => Query(request, "SearchTerm") is not { } term ? Task.FromResult(FakeTransport.Standard(request))
            : Task.FromResult(term == "first" ? Items(20, 200, "first") : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var vm = fixture.Library();
        await vm.ReloadAsync();
        await vm.SearchAsync("second");
        var count = fixture.Transport.Requests.Count;
        vm.LoadMore();
        await SettleAsync();
        Require(count == fixture.Transport.Requests.Count && vm.Cards.All(card => card.Item.Id.StartsWith("first", StringComparison.Ordinal)),
            "新查询失败后不允许旧数量作为新偏移");
    }

    private async Task SearchStateAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var calls = 0;
        fixture.Transport.Reply = (request, _) => Query(request, "SearchTerm") is null ? Task.FromResult(FakeTransport.Standard(request))
            : Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Items(1, 1));
        var request = LibraryRequest.Search(fixture.Services);
        using var vm = new LibraryViewModel();
        vm.Attach(request, fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Images, fixture.Capabilities);
        await vm.SearchAsync("submitted");
        await vm.SearchAsync("submitted");
        Require(calls == 2 && vm.Cards.Count == 1 && request.SearchTerm == "submitted", "同词必须可重试且原导航载荷保存新词");
        using var returned = new LibraryViewModel();
        returned.Attach(request, fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Images, fixture.Capabilities);
        Require(returned.SearchText == "submitted" && returned.Heading.Contains("submitted", StringComparison.Ordinal), "返回重建丢失搜索词");
        var oldResponse = Pending<HttpResponseMessage>();
        var repeatCalls = 0;
        fixture.Transport.Reply = (_, _) => ++repeatCalls == 1 ? oldResponse.Task
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var oldSearch = vm.SearchAsync("repeat");
        await vm.SearchAsync("repeat");
        oldResponse.TrySetResult(Items(1, 1));
        await oldSearch;
        Require(fixture.Settings.Ui.RecentSearches.Values.SelectMany(entries => entries).All(entry => entry.Term != "repeat"),
            "同词旧查询在取消后仍写入最近搜索");
    }

    private async Task PlaybackBusyAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Library();
        await vm.ReloadAsync();
        var reply = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (request, _) => Query(request, "SearchTerm") is null ? Task.FromResult(FakeTransport.Standard(request)) : reply.Task;
        var loading = vm.SearchAsync("second");
        try
        {
            Require(vm.Busy && !vm.PlayAllCommand.CanExecute(null) && !vm.PlayRandomCommand.CanExecute(null), "新查询中旧卡片不能组成播放列表");
            await vm.PlayAllAsync();
            Require(fixture.Actions.Plays == 0, "播放入口未守忙碌状态");
        }
        finally { reply.TrySetResult(Items(1, 1)); await loading; }
    }

    private async Task JumpCancelAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Library();
        await vm.ReloadAsync();
        var reply = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        var counts = 0;
        fixture.Transport.Reply = (request, _) =>
        {
            if (Query(request, "Limit") != "0") return Task.FromResult(FakeTransport.Standard(request));
            counts++;
            entered.TrySetResult(true);
            return reply.Task;
        };
        var jump = vm.JumpToAsync("Z");
        await entered.Task;
        await vm.SearchAsync("new");
        reply.SetResult(Items(0, 100));
        Require(await jump is null && counts == 1, "已过期字母跳转不允许发第二次计数或补页");
    }

    private async Task SeasonCancelAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Detail();
        await vm.ReloadAsync();
        var first = vm.Seasons.First();
        var second = vm.Seasons.Last();
        var reply = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        fixture.Transport.Reply = (request, _) =>
        {
            if (Query(request, "SeasonId") != second.Id) return Task.FromResult(FakeTransport.Standard(request));
            entered.TrySetResult(true);
            return reply.Task;
        };
        vm.SelectedSeason = second;
        await entered.Task;
        vm.SelectedSeason = first;
        reply.SetResult(Json(new { Items = new[] { Episode(second.Id) } }));
        await SettleAsync();
        Require(!vm.Busy && vm.SelectedSeason == first && vm.Episodes.All(item => item.SeasonId == first.Id)
            && vm.PlayTarget?.SeasonId == first.Id, "旧季请求覆盖了重新选中的季");
    }

    private async Task SeasonFailureAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Detail();
        await vm.ReloadAsync();
        var first = vm.SelectedSeason;
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Items/ep-b", StringComparison.Ordinal)
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)) : Task.FromResult(FakeTransport.Standard(request));
        vm.SelectedSeason = vm.Seasons.Last();
        await UntilAsync(() => !vm.Busy);
        Require(vm.SelectedSeason == first && vm.Episodes.All(item => item.SeasonId == first?.Id)
            && vm.PlayTarget?.SeasonId == first?.Id, "新季目标失败不能留下混合状态");
    }

    private async Task EmptySeasonAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Detail();
        await vm.ReloadAsync();
        fixture.Transport.Reply = (request, _) => Query(request, "SeasonId") == "b"
            ? Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>() })) : Task.FromResult(FakeTransport.Standard(request));
        vm.SelectedSeason = vm.Seasons.Last();
        await UntilAsync(() => !vm.Busy);
        Require(vm.Episodes.Count == 0 && vm.EpisodeVisibility == Visibility.Visible && vm.SeasonVisibility == Visibility.Visible
            && vm.PlayTarget is null, "空季必须保留选择器且清除播放目标");
    }

    private async Task DetailWriteAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Detail();
        await vm.ReloadAsync();
        var reply = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        fixture.Transport.Reply = (request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.Contains("PlayedItems", StringComparison.Ordinal)) return Task.FromResult(FakeTransport.Standard(request));
            entered.TrySetResult(true);
            return reply.Task;
        };
        var writing = vm.ToggleWatchedCommand.ExecuteAsync(null);
        await entered.Task;
        vm.Cancel();
        await fixture.SelectAsync("b.invalid");
        var count = fixture.Transport.Requests.Count;
        reply.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await writing;
        Require(fixture.Transport.Requests.Count == count, "旧写入完成后重新读取了详情");
    }

    private async Task DetailPickerLayoutAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync();
        var series = Series();
        series.ImageTags["Primary"] = "fixture";
        series.ImageTags["Art"] = "fixture";
        var source = new MediaSource
        {
            Id = "source-a",
            Name = "离线版本 · Extended.Directors.Cut.2160p.UHD.BluRay.REMUX.Dolby.Vision.HDR10.HEVC.TrueHD.Atmos",
            Container = "mkv",
            MediaStreams =
            [
                new() { Index = 0, Type = "Video", Codec = "hevc", Width = 3840, Height = 2160 },
                new() { Index = 1, Type = "Audio", DisplayTitle = "English · DTS-HD Master Audio 7.1 · 导演评论及制作团队特别解说完整音轨", IsDefault = true },
                new() { Index = 2, Type = "Subtitle", DisplayTitle = "简体中文／English · 双语特效字幕 · 完整对白、歌曲翻译及制作人员说明", IsDefault = true }
            ]
        };
        var episode = Episode("a");
        episode.Name = "离线单集 · 文件选项宽度验证";
        episode.SeriesName = series.Name;
        episode.ParentIndexNumber = 1;
        episode.MediaSources = [source, new() { Id = "source-b", Name = "离线版本 · 1080p", Container = "mkv", MediaStreams = source.MediaStreams }];
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Items/show", StringComparison.Ordinal)) return Task.FromResult(Json(series));
            if (path.EndsWith("/Items/ep-a", StringComparison.Ordinal)) return Task.FromResult(Json(episode));
            if (path.EndsWith("/Episodes", StringComparison.Ordinal)) return Task.FromResult(Json(new { Items = new[] { episode } }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        var readings = new List<string>();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, episode));
            var page = (DetailPage)frame.Content;
            var vm = page.ViewModel;
            await UntilAsync(() => page.IsReady && vm.StillImage is not null && vm.CornerImage is not null);
            foreach (var all in new[] { true, false })
            {
                if (!all)
                {
                    // 同一页隐藏已量过的行，覆盖隐藏控件仍保留旧尺寸的情况。
                    episode.MediaSources.RemoveAt(1);
                    source.MediaStreams.RemoveAll(stream => stream.Type == "Subtitle");
                    await vm.ReloadAsync();
                }
                Require(vm.Sources.Count == (all ? 2 : 1) && vm.AudioVisibility == Visibility.Visible
                    && vm.SourceVisibility == (all ? Visibility.Visible : Visibility.Collapsed)
                    && vm.SubtitleVisibility == (all ? Visibility.Visible : Visibility.Collapsed), "文件选项夹具显隐状态不符");
                vm.SelectedAudio = vm.AudioTracks[1];
                if (all) vm.SelectedSubtitle = vm.SubtitleTracks[2];
                foreach (var width in new[] { 1023, 1024, 1064, 1280 })
                {
                    var scale = _root.XamlRoot.RasterizationScale;
                    var pixels = (int)Math.Ceiling(width * scale);
                    var height = (int)Math.Ceiling(900 * scale);
                    ResizeInspect(pixels, height);
                    await LayoutAsync(page);
                    // ResizeInspect 接收外窗物理尺寸；补足窗框后才能真正命中页面断点。
                    pixels += (int)Math.Round((width - vm.PageWidth) * scale);
                    ResizeInspect(pixels, height);
                    await LayoutAsync(page);
                    Require(Math.Abs(vm.PageWidth - width) < 0.6 && vm.IsCompact == (width < 1024),
                        $"没有到达指定页面宽度：要求 {width}，实际 {vm.PageWidth:0.00}");
                    var fit = page.PickerFit();
                    var label = all ? "all" : "audio";
                    readings.Add($"{label} {width} DIP：{fit.Ok}；{fit.Detail}");
                    Require(fit.Ok, fit.Detail);
                    Require(fit.Detail.Contains("音频", StringComparison.Ordinal)
                        && fit.Detail.Contains("媒体源", StringComparison.Ordinal) == all
                        && fit.Detail.Contains("字幕", StringComparison.Ordinal) == all, "读入隐藏行或漏掉可见行：" + fit.Detail);
                    if (!vm.IsCompact || !all)
                        Require(fit.Detail.Contains($"摆成 {(all ? 3 : 1)} 行", StringComparison.Ordinal), "文件选项行数不符：" + fit.Detail);
                    await SaveProbeScreenAsync($"detail-pickers-{label}-{width}-screen.png");
                    if (width != 1064) continue;
                    var picker = Get<EgPicker>(page, "ColumnAudioPicker");
                    try
                    {
                        picker.Width = vm.PageWidth + 400;
                        await LayoutAsync(page);
                        var overflow = page.PickerFit();
                        readings.Add($"{label} 超宽负对照：{overflow.Ok}；{overflow.Detail}");
                        Require(!overflow.Ok, "下拉超过本栏，PickerFit 却通过");
                    }
                    finally
                    {
                        picker.ClearValue(FrameworkElement.WidthProperty);
                        await LayoutAsync(page);
                    }
                    Require(page.PickerFit().Ok, "恢复下拉宽度后仍失败");
                }
            }
            Require(fixture.Actions.Plays == 0, "文件选项布局检查意外请求播放");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-picker-layout.txt"), readings);
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private async Task HiddenHomeAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Ui.HomeRows = [new() { Key = HomeLayout.Resume, Visible = false }, new() { Key = HomeLayout.NextUp, Visible = false }];
        using var vm = new HomeViewModel();
        vm.Attach(fixture.Actions, [], fixture.SettingsService, fixture.Session, fixture.Images);
        var count = fixture.Transport.Requests.Count;
        await vm.ReloadAsync();
        Require(count == fixture.Transport.Requests.Count && !vm.NoticeOpen, "隐藏行和关闭轮播不应联网或报错");
    }

    private static Task NotificationEmptyAsync()
    {
        using var vm = new NotificationsViewModel();
        var changed = false;
        vm.PropertyChanged += (_, args) => changed |= args.PropertyName == nameof(vm.EmptyVisibility);
        vm.ShowEmpty = true;
        Require(changed && vm.EmptyVisibility == Visibility.Visible, "空态派生属性漏通知");
        return Task.CompletedTask;
    }

    private async Task NotificationCancelAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var reply = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        fixture.Transport.Reply = (_, _) => { entered.TrySetResult(true); return reply.Task; };
        using var vm = new NotificationsViewModel();
        vm.Attach(fixture.Session, fixture.SettingsService);
        var shown = 0;
        vm.ShowEditor = _ => { shown++; return Task.CompletedTask; };
        var task = vm.AddNotificationCommand.ExecuteAsync(null);
        await entered.Task;
        vm.Cancel();
        reply.SetResult(Json(new[] { new { Id = "fake", Name = "fake" } }));
        await task;
        Require(shown == 0 && !vm.NoticeOpen, "离页后弹出通知编辑器或写入旧错误");
    }

    private async Task StaleCardAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var card = new CardItem(new EmbyItem { Id = "same", Name = "旧身份", Type = EmbyItemType.Movie }, fixture.Images, 170);
        await fixture.SelectAsync("b.invalid");
        var count = fixture.Transport.Requests.Count;
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, card);
        ItemCommands.ToggleFavorite(fixture.Session, fixture.Actions, card);
        await SettleAsync();
        Require(count == fixture.Transport.Requests.Count, "旧卡片向新身份发送了命令");
    }
}
