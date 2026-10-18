using System.Net;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

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

    /// <summary>元素在根坐标里的矩形；树内弹层的孩子也在这套坐标里（<c>null</c> 就是视觉树根）。</summary>
    private static Rect Box(FrameworkElement element) =>
        element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    /// <summary>
    /// 电影／剧／季三页尾部那一份文件选项（<c>PickerPanel</c>）展开态的宽度与位置 —— 用户 2026-10-16
    /// 两条令：「选择栏展开后会触发自动收缩导致选择栏与右侧的组件重叠」、「展开后自动调整选择框长度适配
    /// 选择项中最长的那一条」。
    /// <para>
    /// 框架在弹层打开那一拍会把选中项的文本收掉（模板的 DropDownOpened 态把 <c>ContentPresenter</c> 置零），
    /// 框的期望宽度随即塌回 <see cref="FrameworkElement.MinWidth"/>；而弹层是按<em>最宽</em>那条轨道名撑的。
    /// 整排跟着重排，右边那一颗往左挪进弹层底下 —— 屏上就是那句「与右侧的组件重叠」。这一条钉的就是展开态
    /// 那两个数：**框不许比闭合时窄**，**弹层不许探出框的右沿**（探出去多少就压住右邻多少）。宽版式那一档
    /// 还要在这一排被撑宽之后仍然没人出界（<see cref="DetailPage.PickerFit"/> 那一条的老判据）。
    /// </para>
    /// <para>
    /// 三条轨道名长短分明，是为了让「塌回 MinWidth」和「按最宽那条撑开」在读数里分得开；窗口取 1280 DIP
    /// 那一档，尾部那一份在四种页面上都画得出来。读数落在 <c>detail-picker-open.txt</c>。
    /// </para>
    /// </summary>
    private async Task PickerDropDownAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync();
        var movie = Movie("movie-picker");
        movie.Name = "离线电影 · 文件选项下拉";
        movie.ImageTags["Primary"] = "fixture";
        movie.ImageTags["Art"] = "fixture";
        movie.MediaSources =
        [
            new()
            {
                Id = "source-a",
                Name = "离线版本 · 2160p",
                Container = "mkv",
                MediaStreams =
                [
                    new() { Index = 0, Type = "Video", Codec = "hevc", Width = 3840, Height = 2160 },
                    new() { Index = 1, Type = "Audio", DisplayTitle = "English · DTS-HD Master Audio 7.1 · 导演评论及制作团队特别解说完整音轨", IsDefault = true },
                    new() { Index = 2, Type = "Subtitle", DisplayTitle = "简体中文／English · 双语特效字幕", IsDefault = true },
                    new() { Index = 3, Type = "Audio", DisplayTitle = "Chinese · FLAC stereo · 默认" },
                    new() { Index = 4, Type = "Audio", DisplayTitle = "Chinese · DTS-HD MA 5.1" },
                    new() { Index = 5, Type = "Audio", DisplayTitle = "Chinese · AC3 stereo" },
                    new() { Index = 6, Type = "Audio", DisplayTitle = "Chinese · AC3 5.1" },
                    new() { Index = 7, Type = "Audio", DisplayTitle = "Japanese · AAC stereo" },
                    new() { Index = 8, Type = "Audio", DisplayTitle = "English · AC3 stereo" }
                ]
            }
        ];
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Items/movie-picker", StringComparison.Ordinal)) return Task.FromResult(Json(movie));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        var readings = new List<string>();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, movie));
            var page = (DetailPage)frame.Content;
            var vm = page.ViewModel;
            await UntilAsync(() => page.IsReady && vm.AudioTracks.Count > 3 && vm.SelectedAudio is not null);
            var scale = _root.XamlRoot.RasterizationScale;
            ResizeInspect((int)Math.Ceiling(1280 * scale), (int)Math.Ceiling(900 * scale));
            await LayoutAsync(page);
            var audio = Get<EgPicker>(page, "AudioPicker");
            // 第 0 条是最长的（它自己就比 MinWidth 宽）、第 5 条是最短的（闭合时正好贴在 MinWidth 上）——
            // 用户那张实拍里塌下去的正是后一种。
            foreach (var index in new[] { 0, 5 })
            {
                if (index >= vm.AudioTracks.Count) continue;
                vm.SelectedAudio = vm.AudioTracks[index];
                await SettleAsync();
                var closed = Box(audio);
                audio.IsDropDownOpen = true;
                await SettleAsync();
                await SettleAsync();
                var open = Box(audio);
                var list = audio.PopupPart?.Child is FrameworkElement panel ? Box(panel) : default;
                var fit = page.PickerFit();
                readings.Add($"选中 {index}：闭合 {closed.Left:0}–{closed.Right:0}"
                    + $" → 展开 {open.Left:0}–{open.Right:0}（宽 {open.Width:0}）"
                    + $"，弹层 {list.Left:0}–{list.Right:0}（上 {list.Top:0} 下 {list.Bottom:0}）"
                    + $"，根 {_root.XamlRoot.Size.Width:0}×{_root.XamlRoot.Size.Height:0}"
                    + $"，选中项 {vm.SelectedAudio?.Text}；{fit.Detail}");
                Require(open.Width >= closed.Width - 0.5,
                    $"展开后选择框反而窄了：{closed.Width:0} → {open.Width:0}");
                Require(list.Right <= open.Right + 0.5,
                    $"弹层探出选择框右沿 {list.Right - open.Right:0} 像素，压住了右边那一颗");
                Require(fit.Ok, fit.Detail);
                if (index == 0) await SaveProbeScreenAsync("detail-picker-open-screen.png");
                audio.IsDropDownOpen = false;
                await SettleAsync();
                // 收起之后要回到内容撑出来的宽度：展开态那个数不许留在身上。
                Require(Math.Abs(Box(audio).Width - closed.Width) < 0.5,
                    $"收起后选择框没有回到原来的宽度：{Box(audio).Width:0} 对 {closed.Width:0}");
            }
            Require(fixture.Actions.Plays == 0, "文件选项展开检查意外请求播放");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-picker-open.txt"), readings);
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
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
        vm.Attach(fixture.Actions, [], fixture.SettingsService, fixture.Session, fixture.Images, fixture.Dispatcher);
        var count = fixture.Transport.Requests.Count;
        await vm.ReloadAsync();
        var extra = fixture.Transport.Requests.Skip(count).ToArray();
        Require(extra.Length == 0 && !vm.NoticeOpen,
            $"隐藏行和关闭轮播不应联网或报错（多出 {extra.Length} 条请求：{string.Join(" | ", extra)}；提示条「{vm.NoticeMessage}」）");

        // 定时检查那一拍（2026-10-16「有新媒体入库时主页不会自动刷新」加的）问的也是服务器：这一页上什么
        // 都没有的时候它同样不该问 —— 2026-10-18 之前它无条件问了一次 Items/Latest，就是这条检查抓到的。
        await vm.AutoRefreshAsync();
        var later = fixture.Transport.Requests.Skip(count).ToArray();
        Require(later.Length == 0, $"隐藏行和关闭轮播时定时检查还在问服务器（{string.Join(" | ", later)}）");
    }

    /// <summary>
    /// 「主页的继续观看标记已看之后要自动切换为下一集，如果没有下一集或者是电影标记已看后要移出继续观看」
    /// （用户令 2026-10-15）。走的是真那条路：卡片菜单那条命令 → 服务器写已看 → 那一格换掉或者撤掉。
    /// <para>
    /// 两个面子各走一遍 —— 主页那一排（<see cref="HomeViewModel"/>）和它点进去的那一页
    /// （<see cref="LibraryViewModel"/> 的继续观看那一页）。两处同源是 2026-09-22「一排 1 项、进去 9 项」
    /// 那次留下的规矩，所以这两条断言也必须一起钉。
    /// </para>
    /// </summary>
    private async Task WatchedResumeSlotAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Items = new[] { Movie("movie"), Episode("ep-3", "show"), Episode("ep-12", "show-b") },
                    TotalRecordCount = 3
                }));
            if (path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Query(request, "SeriesId") == "show"
                    ? Json(new { Items = new[] { Episode("ep-4", "show") }, TotalRecordCount = 1 })
                    : Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        using var home = new HomeViewModel();
        home.Attach(fixture.Actions, [], fixture.SettingsService, fixture.Session, fixture.Images, fixture.Dispatcher);
        await home.ReloadAsync();
        await UntilAsync(() => home.Shelves.Count == 1 && home.Shelves[0].Cards.Count == 3);
        var shelf = home.Shelves[0];
        Require(shelf.Title == "继续观看", $"主页第一排该是继续观看，实际是「{shelf.Title}」");

        // 单集：那一格换成本剧的下一集，位置不动、总数不变。
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, Card(shelf, "ep-3"), home.MarkedWatched);
        await UntilAsync(() => shelf.Cards.Any(card => card.Item.Id == "ep-4"));
        Require(shelf.Cards.Count == 3 && shelf.Cards[1].Item.Id == "ep-4",
            $"主页那一排：单集标记已看后该在原位换成下一集，实际 {Ids(shelf.Cards)}");

        // 电影：整格撤掉。
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, Card(shelf, "movie"), home.MarkedWatched);
        await UntilAsync(() => shelf.Cards.All(card => card.Item.Id != "movie"));
        Require(shelf.Cards.Count == 2 && shelf.Cards[0].Item.Id == "ep-4",
            $"主页那一排：电影标记已看后该移出继续观看，实际 {Ids(shelf.Cards)}");

        // 最后一集：这部剧没有下一集（NextUp 空），同样撤掉。
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, Card(shelf, "ep-12"), home.MarkedWatched);
        await UntilAsync(() => shelf.Cards.All(card => card.Item.Id != "ep-12"));
        Require(shelf.Cards.Count == 1 && shelf.Cards[0].Item.Id == "ep-4",
            $"主页那一排：没有下一集该移出继续观看，实际 {Ids(shelf.Cards)}");
        home.Cancel();

        // 点进去的那一页：同一份判据、同一段收尾。
        using var page = new LibraryViewModel();
        page.Attach(
            LibraryRequest.ForRow(fixture.Services, HomeLayout.HomeRowTarget.Resume),
            fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Images, fixture.Capabilities);
        await page.ReloadAsync();
        await UntilAsync(() => page.Cards.Count == 3);

        var first = page.Cards.First(card => card.Item.Id == "ep-3");
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, first, page.MarkedWatched);
        await UntilAsync(() => page.Cards.Any(card => card.Item.Id == "ep-4"));
        Require(page.Cards.Count == 3 && page.Cards[1].Item.Id == "ep-4",
            $"继续观看那一页：单集标记已看后该在原位换成下一集，实际 {Ids(page.Cards)}");

        var movie = page.Cards.First(card => card.Item.Id == "movie");
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, movie, page.MarkedWatched);
        await UntilAsync(() => page.Cards.All(card => card.Item.Id != "movie"));
        Require(page.Cards.Count == 2, $"继续观看那一页：电影标记已看后该移出，实际 {Ids(page.Cards)}");
        page.Cancel();
    }

    private static CardItem Card(CardShelf shelf, string id) =>
        shelf.Cards.FirstOrDefault(card => card.Item.Id == id)
            ?? throw new InvalidOperationException($"那一排上没有 {id}");

    private static string Ids(IEnumerable<CardItem> cards) => string.Join('、', cards.Select(card => card.Item.Id));

    private static EmbyItem Movie(string id) => new() { Id = id, Name = id, Type = EmbyItemType.Movie };

    private static EmbyItem Episode(string id, string series) => new()
    {
        Id = id,
        Name = id,
        Type = EmbyItemType.Episode,
        SeriesId = series,
        SeasonId = series + "-s1"
    };

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
        var server = fixture.Session.Server!;
        var account = fixture.Session.Account!;
        var card = new CardItem(new EmbyItem { Id = "same", Name = "旧身份", Type = EmbyItemType.Movie }, fixture.Images, 170);
        await fixture.SelectAsync("b.invalid");
        fixture.Session.SignOut(server, account);
        var count = fixture.Transport.Requests.Count;
        ItemCommands.ToggleWatched(fixture.Session, fixture.Actions, card);
        ItemCommands.ToggleFavorite(fixture.Session, fixture.Actions, card);
        await SettleAsync();
        Require(count == fixture.Transport.Requests.Count, "旧卡片向新身份发送了命令");
    }
}
