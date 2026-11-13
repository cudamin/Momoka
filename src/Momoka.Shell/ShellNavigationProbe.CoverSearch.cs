using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task CoverSearchFiltersAsync()
    {
        var image = await CoverMarkedImageAsync(640, 360);
        var item = new EmbyItem { Id = "artwork-search", Name = "离线图片搜索", Type = EmbyItemType.Movie };
        try
        {
            foreach (var width in new[] { 1000, 760, 360 })
            {
                ResizeProbeClient(width, 800);
                var requests = new List<RemoteImageSearch>();
                var tokens = new List<CancellationToken>();
                var late = Pending<RemoteImageResult>();
                var failed = false;
                RemoteImageResult Results(string suffix) => new()
                {
                    Providers = ["FanArt", "TheTVDB"],
                    Images = [new() { ProviderName = "TheTVDB", Url = "https://fixture.invalid/" + suffix }]
                };
                var cover = new CoverDialog(item, (_, _, search, token) =>
                {
                    requests.Add(search);
                    tokens.Add(token);
                    if (search.ProviderName == "FanArt") return late.Task;
                    if (!search.IncludeAllLanguages) return Task.FromResult(new RemoteImageResult { Providers = ["FanArt", "TheTVDB"] });
                    if (failed) return Task.FromException<RemoteImageResult>(new InvalidOperationException("离线搜索失败"));
                    return Task.FromResult(Results("new"));
                }, (_, _, _, _, _) => Task.FromResult<byte[]?>(null), (_, _, _, _) => Task.CompletedTask,
                    (_, _, _) => Task.CompletedTask, (_, _, _) => Task.CompletedTask,
                    _ => Task.FromResult<EmbyItem?>(item), (_, _) => Task.FromResult<byte[]?>(image))
                { XamlRoot = _root.XamlRoot };
                var shown = cover.ShowAsync().AsTask();
                Task<ContentDialogResult>? panel = null;
                try
                {
                    await LayoutAsync(cover);
                    var picker = new CoverPickerDialog(item, ItemArtwork.SingleKinds.Single(kind => kind.ImageType == "Thumb"), Results("initial"), cover);
                    panel = (Task<ContentDialogResult>)Call(cover, "ShowPanelAsync", picker)!;
                    await LayoutAsync(cover);
                    var source = Get<ComboBox>(picker, "SourcePicker");
                    var languages = Get<ToggleSwitch>(picker, "AllLanguagesToggle");
                    var grid = Get<GridView>(picker, "Grid");
                    CoverViewportBounds(source, cover, "图片来源筛选");
                    CoverViewportBounds(languages, cover, "所有语言开关");
                    CoverViewportBounds(Get<Button>(picker, "UploadArtworkButton"), cover, "本机图片上传");
                    Require(AutomationProperties.GetAutomationId(source) == "ArtworkSourcePicker"
                        && AutomationProperties.GetName(source) == "图片来源"
                        && AutomationProperties.GetAutomationId(languages) == "ArtworkAllLanguages",
                        "图片筛选缺少原生自动化定位与名称");
                    Require(languages.IsOn && source.SelectedIndex == 0 && requests.Count == 0 && string.IsNullOrEmpty(cover.PrimaryButtonText),
                        "首次候选不得重发查询，默认全部来源与所有语言，无选择隐藏使用按钮");
                    var choices = Get<System.Collections.ObjectModel.ObservableCollection<CoverChoice>>(picker, "_choices");
                    await UntilAsync(() => choices.Single().Picture is not null);
                    var preview = Descendants(cover).OfType<Border>().Single(border => border.Name == "CoverCandidatePreview");
                    Require(Math.Abs(preview.ActualWidth / preview.ActualHeight - 16d / 9) < 0.03,
                        "缩略图候选必须使用横向图片框");
                    grid.SelectedIndex = 0;
                    Require(cover.PrimaryButtonText == "使用这张" && cover.IsPrimaryButtonEnabled, "选择后宿主按钮未恢复");
                    await SaveProbeScreenAsync($"artwork-search-thumb-{width}.png");
                    source.SelectedIndex = 1;
                    Require(picker.Picked is null && string.IsNullOrEmpty(cover.PrimaryButtonText) && requests.Count == 1,
                        "来源切换必须马上清除旧选择并重新请求");
                    source.SelectedIndex = 2;
                    await UntilAsync(() => choices.Count == 1 && choices[0].Info.Url.EndsWith("/new", StringComparison.Ordinal));
                    late.TrySetResult(Results("old"));
                    await SettleAsync();
                    Require(tokens[0].IsCancellationRequested && choices[0].Info.Url.EndsWith("/new", StringComparison.Ordinal),
                        "旧来源晚到覆盖新结果或未取消请求");
                    languages.IsOn = false;
                    await UntilAsync(() => choices.Count == 0);
                    Require(requests[^1] == new RemoteImageSearch("TheTVDB", false)
                        && Get<Button>(picker, "UploadArtworkButton").Visibility == Visibility.Visible,
                        "语言开关未发送服务器筛选或无结果隐藏了本机上传");
                    await LayoutAsync(cover);
                    await SaveProbeScreenAsync($"artwork-search-empty-{width}.png");
                    failed = true;
                    languages.IsOn = true;
                    await UntilAsync(() => Get<Button>(picker, "RetryArtworkSearch").Visibility == Visibility.Visible);
                    failed = false;
                    Call(picker, "OnRetrySearch", picker, new RoutedEventArgs());
                    await UntilAsync(() => choices.Count == 1 && Get<Button>(picker, "RetryArtworkSearch").Visibility == Visibility.Collapsed);
                    Get<TaskCompletionSource<ContentDialogResult>>(cover, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                    await panel;
                    var disc = new CoverPickerDialog(item, ItemArtwork.SingleKinds.Single(kind => kind.ImageType == "Disc"), new RemoteImageResult(), cover);
                    panel = (Task<ContentDialogResult>)Call(cover, "ShowPanelAsync", disc)!;
                    await LayoutAsync(cover);
                    var notice = Get<TextBlock>(disc, "Notice").Text;
                    Require(notice.Contains("光盘封面", StringComparison.Ordinal) && !notice.Contains("刮削源", StringComparison.Ordinal)
                        && !notice.Contains("对上", StringComparison.Ordinal) && string.IsNullOrEmpty(cover.PrimaryButtonText),
                        "光盘无结果不能推测服务器配置或留下无效使用按钮");
                    if (width == 760) await SaveProbeScreenAsync("artwork-search-disc-empty.png");
                }
                finally
                {
                    late.TrySetResult(new RemoteImageResult());
                    Get<TaskCompletionSource<ContentDialogResult>?>(cover, "_panelCompletion")?.TrySetResult(ContentDialogResult.None);
                    if (panel is not null) await panel;
                    cover.Hide();
                    await shown;
                }
            }
        }
        finally { ResizeProbeClient(1280, 800); }
    }
}
