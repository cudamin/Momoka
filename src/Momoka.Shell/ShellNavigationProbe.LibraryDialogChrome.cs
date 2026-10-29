using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryDialogChromeAsync()
    {
        var savedTheme = ThemeHost.Current.Id;
        try
        {
            ResizeProbeClient(1100, 900);
            var bytes = await FixtureImageAsync();
            var picture = (await PosterLoader.DecodeAsync(bytes, 320))!;
            var item = new EmbyItem { Id = "library-chrome", Name = "离线示例 · 媒体库窗口", Type = EmbyItemType.Movie };
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(_root);
                var cover = Cover(item, () => Task.FromResult<EmbyItem?>(item));
                var dialogs = new ContentDialog[]
                {
                    MetadataDialog(MetadataSample()),
                    new MetadataDialog(item, new ItemMetadataEdit { Name = item.Name }),
                    new RefreshMetadataDialog(),
                    new CollectionDialog(item, []),
                    cover,
                    new CoverPickerDialog(item, ItemArtwork.SingleKinds.First(), new RemoteImageResult(), cover),
                    new ArtworkViewer("离线示例 · 查看图片", picture),
                    new IdentifyDialog(item, "/offline/movie.mkv", () => Task.FromResult(new List<ExternalIdInfo>()),
                        _ => Task.FromResult(new List<RemoteSearchResult>()), null,
                        (_, _) => throw new InvalidOperationException("表面检查不允许识别写入"), null),
                    new SubtitleDialog(item, new MediaSource { Id = "offline", Container = "mkv" },
                        _ => Task.FromResult(new List<RemoteSubtitleInfo>()),
                        _ => throw new InvalidOperationException("表面检查不下载字幕"),
                        _ => throw new InvalidOperationException("表面检查不删除字幕")),
                    new MissingEpisodesDialog(item, [], (_, _) => Task.FromResult<byte[]?>(null)),
                    new FnNasMetadataDialog(new JsonObject { ["Name"] = item.Name }),
                    new FnNasMatchDialog(item.Name, (_, _) => Task.FromResult<IReadOnlyList<Momoka.FnNas.FnNasScrapResult>>([])),
                    new FnNasVersionsDialog(item.Name, [])
                };
                foreach (var dialog in dialogs)
                {
                    dialog.XamlRoot = _root.XamlRoot;
                    dialog.RequestedTheme = ElementTheme.Dark;
                    var shown = dialog.ShowAsync().AsTask();
                    try
                    {
                        await LayoutAsync(dialog);
                        Require(dialog.CornerRadius == (CornerRadius)Application.Current.Resources["EgCardCornerRadius"],
                            $"{theme.Id}/{dialog.GetType().Name} 未使用统一圆角");
                        Require(dialog.BorderThickness == new Thickness(0), $"{dialog.GetType().Name} 留下了方形外边框");
                        Require(BrushColor(dialog.Background) == ThemeHost.ToColor(theme.Colors.Surface),
                            $"{theme.Id}/{dialog.GetType().Name} 背景没有用弹窗玻璃");
                        var surface = Descendants(dialog).OfType<Border>().First(border => border.Name == "BackgroundElement");
                        Require(surface.CornerRadius == dialog.CornerRadius && BrushColor(surface.Background) == BrushColor(dialog.Background),
                            $"{dialog.GetType().Name} 模板没有实际消费圆角或背景");
                        var contentScroll = Descendants(dialog).OfType<ScrollViewer>().Single(view => view.Name == "ContentScrollViewer");
                        Require(contentScroll.Content is Grid top && BrushColor(top.Background).A == 0,
                            $"{dialog.GetType().Name} 正文仍被框架顶层叠色覆盖");
                        await SaveProbeScreenAsync($"library-dialog-{dialog.GetType().Name}-{theme.Id}-screen.png");
                    }
                    finally { dialog.Hide(); await shown; }
                }
                foreach (var (name, panel) in new (string, UserControl)[]
                {
                    ("FilterPanel", new FilterPanel()), ("LibrarySettingsPanel", new LibrarySettingsPanel())
                })
                {
                    var holder = new Grid();
                    panel.HorizontalAlignment = HorizontalAlignment.Center;
                    panel.VerticalAlignment = VerticalAlignment.Center;
                    holder.Children.Add(panel);
                    _root.Children.Add(holder);
                    try
                    {
                        await LayoutAsync(panel);
                        var border = (Border)panel.FindName("LibraryDialogSurface");
                        Require(BrushColor(border.Background) == ThemeHost.ToColor(theme.Colors.Surface)
                            && border.CornerRadius == (CornerRadius)Application.Current.Resources["EgCardCornerRadius"],
                            $"{theme.Id}/{name} 没有统一圆角或弹窗玻璃");
                        await SaveProbeScreenAsync($"library-dialog-{name}-{theme.Id}-screen.png");
                    }
                    finally { _root.Children.Remove(holder); }
                }
                // This ordinary dynamic dialog is the same type MP subscription/resource editors use.
                var ordinary = new ContentDialog { XamlRoot = _root.XamlRoot, Title = "普通 / MP 动态窗口同材质", Content = "离线样式检查", CloseButtonText = "关闭" };
                var ordinaryShown = ordinary.ShowAsync().AsTask();
                try
                {
                    await LayoutAsync(ordinary);
                    Require(BrushColor(ordinary.Background) == ThemeHost.ToColor(theme.Colors.Surface),
                        "普通/MP动态弹窗没有跟上弹窗玻璃");
                }
                finally { ordinary.Hide(); await ordinaryShown; }
                var mp = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot, CloseButtonText = "关闭" };
                var mpShown = mp.ShowAsync().AsTask();
                try
                {
                    await LayoutAsync(mp);
                    Require(BrushColor(mp.Background) == ThemeHost.ToColor(theme.Colors.Surface), "MP专有弹窗背景没有跟上弹窗玻璃");
                    await SaveProbeScreenAsync($"library-dialog-mp-unchanged-{theme.Id}-screen.png");
                }
                finally { mp.Hide(); await mpShown; }
                var confirmationChecked = false;
                await ConfirmDialog.ForLibrary(_root, async dialog =>
                {
                    var shown = dialog.ShowAsync().AsTask();
                    try
                    {
                        await LayoutAsync(dialog);
                        Require(BrushColor(dialog.Background) == ThemeHost.ToColor(theme.Colors.Surface)
                            && dialog.CornerRadius == (CornerRadius)Application.Current.Resources["EgCardCornerRadius"],
                            "媒体库删除确认未使用弹窗玻璃");
                        await SaveProbeScreenAsync($"library-dialog-confirmation-{theme.Id}-screen.png");
                        confirmationChecked = true;
                        return ContentDialogResult.None;
                    }
                    finally { dialog.Hide(); await shown; }
                })("删除确认（离线示例）", "本探针不删除真实媒体", "删除");
                Require(confirmationChecked, "库确认表面检查没有完成；不能把安全取消/吞异常当作检查通过");
            }
        }
        finally { ThemeHost.Apply(savedTheme); }
    }

    private static Windows.UI.Color BrushColor(Brush? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color,
        AcrylicBrush acrylic => acrylic.TintColor,
        _ => default
    };
}
