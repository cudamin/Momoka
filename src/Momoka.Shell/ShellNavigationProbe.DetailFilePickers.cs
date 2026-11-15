using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DetailFilePickersAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var item = Movie("file-pickers");
        item.Name = "一体文件选择栏";
        item.Overview = string.Concat(Enumerable.Repeat("离线剧情说明在封面下面显示两行，悬停可以查看全文。", 12));
        var first = new MediaSource
        {
            Id = "first", Name = "原始版本 · 1080p", Container = "mkv",
            MediaStreams =
            [
                new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 },
                new() { Index = 1, Type = "Audio", DisplayTitle = "国语", IsDefault = true },
                new() { Index = 2, Type = "Audio", DisplayTitle = "日语" },
                new() { Index = 3, Type = "Subtitle", DisplayTitle = "简体中文", IsDefault = true },
                new() { Index = 4, Type = "Subtitle", DisplayTitle = "English" }
            ]
        };
        item.MediaSources = [first, new() { Id = "empty", Name = "无音轨版本 · 720p", Container = "mkv",
            MediaStreams = [new() { Index = 0, Type = "Video", Codec = "h264", Width = 1280, Height = 720 }] }];
        var image = await FixtureImageAsync(400, 600);
        item.ImageTags["Primary"] = "fixture";
        fixture.Transport.Reply = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) }
            : request.RequestUri.AbsolutePath.EndsWith("/Items/file-pickers", StringComparison.Ordinal) ? Json(item)
            : FakeTransport.Standard(request));
        fixture.Settings.Ui.EnableImmersive = false;
        ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
        var frame = new Frame();
        var reads = new List<string>();
        _root.Children.Add(frame);
        try
        {
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "选择栏详情导航失败");
            var page = (DetailPage)frame.Content;
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady && page.ViewModel.Sources.Count == 2 && page.ViewModel.AudioTracks.Count > 2);
            var vm = page.ViewModel;
            foreach (var width in new[] { 1280, 360, 1280 })
            {
                ResizeProbeClient(width, 900);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(vm.PageWidth - width) < 1; });
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                await LayoutAsync(page);
                Require(vm.HeroInset.Top == 32 && Get<Grid>(page, "HeroContent").Margin.Top == 32,
                    "顶部内容没有整体增加16DIP留白");
                var prefix = vm.IsCompact ? "" : "Column";
                var picks = new[] { Get<DetailFilePicker>(page, prefix + "SourcePicker"),
                    Get<DetailFilePicker>(page, prefix + "AudioPicker"), Get<DetailFilePicker>(page, prefix + "SubtitlePicker") };
                Require(picks.All(picker => picker.ActualWidth > 0), "三类选择栏未呈现");
                Require(picks.All(picker => picker.CornerRadius == new CornerRadius(4)
                    && Descendants(picker).OfType<Grid>().Single(grid => grid.Name == "RootGrid").CornerRadius == new CornerRadius(4)),
                    "选择栏仍为胶囊形状，没有保持4DIP小圆角矩形");
                Require(page.PickerFit().Ok && page.PickerGlass().Ok, "选择栏闭合或浮层配置不符");
                foreach (var picker in picks)
                {
                    Require(picker.PickerFlyout.Placement == FlyoutPlacementMode.LeftEdgeAlignedTop
                        && picker.PickerFlyout.ShouldConstrainToRootBounds, "选择栏未设置左侧优先或根约束");
                    var selected = picker.SelectedItem;
                    picker.IsDropDownOpen = true;
                    await UntilAsync(() => picker.IsDropDownOpen && picker.FlyoutContent.IsLoaded);
                    await SettleAsync();
                    var texts = Descendants(picker.FlyoutContent).OfType<TextBlock>().ToArray();
                    Require(texts.Any(text => text.Text == "当前" + picker.Label)
                        && texts.Any(text => text.Text == "其他" + picker.Label), "展开列表缺少当前/其他分组");
                    Require(ReferenceEquals(selected, picker.SelectedItem), "打开列表就改变了选择");
                    var content = Box(picker.FlyoutContent);
                    var button = Box(picker);
                    Require(content.Left >= -1 && content.Top >= -1 && content.Right <= _root.ActualWidth + 1
                        && content.Bottom <= _root.ActualHeight + 1, "选择浮层超出窗口根");
                    if (button.Left >= content.Width + 16)
                        Require(content.Right <= button.Left + 1, "有空间时浮层没有显示在左侧");
                    await SaveProbeScreenAsync($"detail-file-picker-{picker.Label}-{width}-screen.png");
                    picker.IsDropDownOpen = false;
                    await SettleAsync();
                    Require(!picker.IsDropDownOpen && ReferenceEquals(selected, picker.SelectedItem), "关闭列表改变选择或未关闭");
                    reads.Add($"{width}/{picker.Label}: 控件{button.Left:0}-{button.Right:0}，浮层{content.Left:0}-{content.Right:0}，原对象/分组/边界通过");
                }
            }
            var wideOpen = Get<DetailFilePicker>(page, "ColumnAudioPicker");
            wideOpen.IsDropDownOpen = true;
            await UntilAsync(() => wideOpen.IsDropDownOpen);
            fixture.Settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            ResizeProbeClient(760, 900);
            await UntilAsync(() => { page.UpdateLayout(); return page.ViewModel.IsCompact; });
            await LayoutAsync(page);
            Require(!wideOpen.IsDropDownOpen, "跨窄窗布局后旧宽栏浮层没有关闭");
            var compactOpen = Get<DetailFilePicker>(page, "AudioPicker");
            compactOpen.IsDropDownOpen = true;
            await UntilAsync(() => compactOpen.IsDropDownOpen);
            ResizeProbeClient(1280, 900);
            await UntilAsync(() => { page.UpdateLayout(); return !page.ViewModel.IsCompact; });
            await LayoutAsync(page);
            Require(!compactOpen.IsDropDownOpen, "跨宽窗布局后旧窄栏浮层没有关闭");
            fixture.Settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            // 页左沿按钮可触发平台回退；额外给同一原生控件足够左侧空间核对首选位置。
            var positioned = new DetailFilePicker
            {
                Label = "版本", ItemsSource = vm.Sources, SelectedItem = vm.SelectedSource,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(700, 100, 0, 0), Width = 400
            };
            _root.Children.Add(positioned);
            try
            {
                await LayoutAsync(positioned);
                positioned.IsDropDownOpen = true;
                await UntilAsync(() => positioned.IsDropDownOpen && positioned.FlyoutContent.IsLoaded);
                await SettleAsync();
                Require(Box(positioned.FlyoutContent).Right <= Box(positioned).Left + 1,
                    "足够左侧空间时原生浮层没有优先在左侧");
                await SaveProbeScreenAsync("detail-file-picker-left-space-screen.png");
            }
            finally { positioned.IsDropDownOpen = false; _root.Children.Remove(positioned); }
            var source = Get<DetailFilePicker>(page, "ColumnSourcePicker");
            var audio = Get<DetailFilePicker>(page, "ColumnAudioPicker");
            var subtitle = Get<DetailFilePicker>(page, "ColumnSubtitlePicker");
            await SelectAsync(audio, "日语");
            Require(ReferenceEquals(audio.SelectedItem, vm.SelectedAudio) && vm.SelectedAudio?.Stream?.Index == 2,
                "音频UIA提交没有回写原对象");
            await SelectAsync(subtitle, "English");
            Require(ReferenceEquals(subtitle.SelectedItem, vm.SelectedSubtitle) && vm.SelectedSubtitle?.Stream?.Index == 4,
                "字幕UIA提交没有回写原对象");
            await SelectAsync(source, "无音轨版本");
            await LayoutAsync(page);
            Require(ReferenceEquals(source.SelectedItem, vm.SelectedSource) && vm.SelectedSource?.Source.Id == "empty",
                "版本UIA提交没有回写原对象");
            Require(vm.AudioVisibility == Visibility.Visible && audio.ActualWidth > 0 && !audio.HasItems
                && Descendants(audio).OfType<TextBlock>().Any(text => text.Text == "无音轨"),
                "无音轨时固定栏或空状态没有保留");
            audio.IsDropDownOpen = true;
            Require(!audio.IsDropDownOpen, "无音轨打开空浮层");
            await SaveProbeScreenAsync("detail-file-picker-empty-screen.png");
            await SelectAsync(source, "原始版本");
            audio.IsDropDownOpen = true;
            await UntilAsync(() => audio.IsDropDownOpen);
            vm.AudioTracks.Clear();
            await SettleAsync();
            Require(!audio.IsDropDownOpen && !audio.HasItems, "集合更换没有关闭旧音频浮层");
            await vm.ReloadAsync();
            await UntilAsync(() => vm.AudioTracks.Count > 2);
            await LayoutAsync(page);
            fixture.Settings.Ui.EnableImmersive = true;
            fixture.Settings.Ui.UseAcrylicControls = false;
            fixture.Settings.Ui.ControlBackgroundOpacity = 15d / 255 * 100;
            fixture.Settings.Ui.ControlBackgroundColor = "#FFFFFF";
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            var reference = Get<Button>(page, "ColumnMoreButton");
            foreach (var picker in new[] { source, audio, subtitle })
                await CheckPickerBackgroundAsync(picker, reference, reads);
            source.IsDropDownOpen = true;
            await UntilAsync(() => source.IsDropDownOpen && source.FlyoutContent.IsLoaded);
            await SettleAsync();
            await CheckPickerItemBackgroundsAsync(source, reads);
            var surface = (Border)source.FlyoutContent;
            var oldFill = surface.Background;
            try
            {
                surface.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
                var rejected = false;
                try { Require(surface.Background is AcrylicBrush, "浮层不是亚克力"); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "浮层材质检查未拒绝实色反例");
            }
            finally { surface.Background = oldFill; }
            Require(surface.Background is AcrylicBrush, "浮层亚克力恢复失败");
            source.IsDropDownOpen = false;
            fixture.Settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            var flyout = source.PickerFlyout;
            var placement = flyout.Placement;
            try
            {
                flyout.Placement = FlyoutPlacementMode.RightEdgeAlignedTop;
                Require(!page.PickerGlass().Ok, "浮层配置检查未拒绝右侧反例");
            }
            finally { flyout.Placement = placement; }
            Require(page.PickerGlass().Ok, "左侧浮层恢复后仍失败");
            audio.IsDropDownOpen = true;
            await UntilAsync(() => audio.IsDropDownOpen);
            page.Release();
            Require(!audio.IsDropDownOpen, "Release没有当场关闭音频浮层");
            frame.Content = null;
            await SettleAsync();
            Require(!audio.IsDropDownOpen, "离页残留选择浮层");
            Require(fixture.Actions.Plays == 0, "选择栏检查意外播放");
            reads.Add("音频/字幕/版本UIA原对象提交，空态，集合换代关闭，离页关闭，错误placement反例均通过；未播放。");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-file-pickers.txt"), reads);
        }

        async Task SelectAsync(DetailFilePicker picker, string contains)
        {
            picker.IsDropDownOpen = true;
            await UntilAsync(() => picker.IsDropDownOpen && picker.FlyoutContent.IsLoaded);
            await SettleAsync();
            var row = Descendants(picker.FlyoutContent).OfType<ListViewItem>().Single(container =>
                Descendants(container).OfType<TextBlock>().Any(text => text.Text.Contains(contains, StringComparison.Ordinal)));
            // WinUI 的容器 peer 将选择语义转给父 ListView 创建的原对象数据 peer。
            // microsoft-ui-xaml 2a732746: ItemsControlAutomationPeer_Partial.cpp grouped modern-panel path.
            var listPeer = FrameworkElementAutomationPeer.CreatePeerForElement(picker.ItemsList)!;
            _ = listPeer.GetChildren();
            var containerPeer = FrameworkElementAutomationPeer.CreatePeerForElement(row)!;
            var peer = containerPeer.EventsSource;
            Require(peer is ListViewItemDataAutomationPeer dataPeer && ReferenceEquals(dataPeer.Item, row.Content),
                "列表项没有框架生成的原对象数据项UIA peer");
            Require(peer!.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider, "列表项没有原生UIA单选语义");
            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            await UntilAsync(() => !picker.IsDropDownOpen);
        }
    }
}
