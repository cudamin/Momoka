using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task JellyfinDialogCapabilitiesAsync()
    {
        var item = new EmbyItem { Id = "offline-jellyfin", Name = "离线电影", Type = EmbyItemType.Movie };
        try
        {
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                foreach (var supported in new[] { true, false, true })
                {
                    var expected = supported ? Visibility.Visible : Visibility.Collapsed;
                    var refresh = new RefreshMetadataDialog(supported) { XamlRoot = _root.XamlRoot };
                    var refreshShown = refresh.ShowAsync().AsTask();
                    try
                    {
                        await LayoutAsync(refresh);
                        Require(Get<StackPanel>(refresh, "ThumbnailsRow").Visibility == expected,
                            "缩略图替换行没有随服务器能力整行显隐");
                        Get<ToggleSwitch>(refresh, "ThumbnailsSwitch").IsOn = true;
                        Require(refresh.ReplaceThumbnails == supported, "隐藏的缩略图替换仍会提交");
                        Require(Get<ComboBox>(refresh, "ModeBox").Visibility == Visibility.Visible,
                            "支持的元数据刷新模式被隐藏");
                        if (!supported) await SaveProbeScreenAsync($"jellyfin-refresh-{width}.png");
                    }
                    finally { refresh.Hide(); await refreshShown; }

                    var applied = 0;
                    var removed = 0;
                    var identify = new IdentifyDialog(item, null,
                        () => Task.FromResult(new List<ExternalIdInfo>()),
                        _ => Task.FromResult(new List<RemoteSearchResult>
                        {
                            new() { Name = "离线候选电影", ProductionYear = 2026, SearchProviderName = "离线元数据源" }
                        }),
                        supported ? _ => Task.FromResult<byte[]?>(null) : null,
                        (_, _) => { applied++; return Task.CompletedTask; },
                        supported ? () => { removed++; return Task.CompletedTask; }
                    : null)
                    { XamlRoot = _root.XamlRoot };
                    var identifyShown = identify.ShowAsync().AsTask();
                    try
                    {
                        await LayoutAsync(identify);
                        Require(Get<Button>(identify, "RemoveButton").Visibility == expected,
                            "移除识别没有随服务器能力显隐");
                        if (!supported)
                        {
                            Call(identify, "OnAskRemove", identify, new RoutedEventArgs());
                            Call(identify, "OnConfirmRemove", identify, new RoutedEventArgs());
                            Require(removed == 0 && Get<StackPanel>(identify, "ConfirmArea").Visibility == Visibility.Collapsed,
                                "不支持的识别重置仍能进入确认或执行");
                        }
                        Get<TextBox>(identify, "NameBox").Text = item.Name;
                        Call(identify, "OnSearch", identify, new RoutedEventArgs());
                        await UntilAsync(() => identify.Candidates.Count == 1);
                        await LayoutAsync(identify);
                        var choices = Descendants(identify).OfType<Button>().ToArray();
                        Require(choices.Single(button => AutomationProperties.GetAutomationId(button) == "IdentifyResult").Visibility == expected,
                            "识别的无效海报区域未收起");
                        var textChoice = choices.Single(button => AutomationProperties.GetAutomationId(button) == "IdentifyTextResult");
                        Require(textChoice.Visibility == (supported ? Visibility.Collapsed : Visibility.Visible),
                            "无预览时没有保留可用的文字选择");
                        if (!supported) await SaveProbeScreenAsync($"jellyfin-identify-{width}.png");
                        Call(identify, "OnPick", new Button { Tag = identify.Candidates[0] }, new RoutedEventArgs());
                        Call(identify, "OnApply", identify, new RoutedEventArgs());
                        await UntilAsync(() => applied == 1);
                    }
                    finally { identify.Hide(); await identifyShown; }

                    var cover = new CoverDialog(item, (_, _) => Task.FromResult(new RemoteImageResult()),
                        (_, _, _, _, _) => Task.FromResult<byte[]?>(null), (_, _, _, _) => Task.CompletedTask,
                        (_, _, _) => Task.CompletedTask, (_, _, _) => Task.CompletedTask,
                        _ => Task.FromResult<EmbyItem?>(item), supported ? _ => Task.FromResult<byte[]?>(null) : null)
                    { XamlRoot = _root.XamlRoot };
                    var coverShown = cover.ShowAsync().AsTask();
                    try
                    {
                        await LayoutAsync(cover);
                        var picker = new CoverPickerDialog(item, ItemArtwork.SingleKinds.First(), new RemoteImageResult
                        {
                            Images = [new() { Url = "https://image.invalid/fixture.jpg", ProviderName = "离线图片源", Width = 1000, Height = 1500 }]
                        }, cover);
                        var picking = (Task<ContentDialogResult>)Call(cover, "ShowPanelAsync", picker)!;
                        await LayoutAsync(cover);
                        Require(Descendants(cover).OfType<Border>().Single(border => border.Name == "CoverCandidatePreview").Visibility == expected,
                            "无效远程图片预览没有隐藏");
                        Require(Get<Button>(picker, "UploadArtworkButton").Visibility == Visibility.Visible,
                            "本机图片上传被错误隐藏");
                        Get<GridView>(picker, "Grid").SelectedIndex = 0;
                        Require(picker.Picked is not null && picker.IsPrimaryButtonEnabled, "可用的远程封面候选不能选择");
                        if (!supported) await SaveProbeScreenAsync($"jellyfin-cover-{width}.png");
                        Get<TaskCompletionSource<ContentDialogResult>>(cover, "_panelCompletion").SetResult(ContentDialogResult.None);
                        await picking;
                    }
                    finally
                    {
                        Get<TaskCompletionSource<ContentDialogResult>?>(cover, "_panelCompletion")?.TrySetResult(ContentDialogResult.None);
                        await UntilAsync(() => Get<TaskCompletionSource<ContentDialogResult>?>(cover, "_panelCompletion") is null);
                        cover.Hide();
                        await coverShown;
                    }
                }
            }
        }
        finally { ResizeInspect(1420, 980); }
    }
}
