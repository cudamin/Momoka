using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task ScrobblingRecordsAsync()
    {
        using var http = new HttpClient(new RejectNetwork());
        using var bangumi = new BangumiClient(http);
        using var service = new ScrobblingService(new AppSettings(), PassthroughSecretProtector.Instance, bangumi,
            new RejectScrobblingDouban());
        void Record(ScrobblingProvider provider, string title, ScrobblingRecordOutcome outcome) =>
            Call(service, "AddRecord", provider, title, 8, outcome, "匿名同步结果");

        Record(ScrobblingProvider.Douban, "离线示例四", ScrobblingRecordOutcome.Skipped);
        Record(ScrobblingProvider.Douban, "离线示例三", ScrobblingRecordOutcome.Watched);
        Record(ScrobblingProvider.Douban, "离线示例二", ScrobblingRecordOutcome.Watching);
        Record(ScrobblingProvider.Douban, "离线示例一", ScrobblingRecordOutcome.Failed);
        var original = _root.XamlRoot.Size;
        var isCurrent = true;
        try
        {
            await InspectScrobblingDialogAsync(service, ScrobblingProvider.Douban, () => isCurrent, async content =>
            {
                var filter = Get<ComboBox>(content, "ScrobblingRecordsFilter");
                var list = Get<ListView>(content, "ScrobblingRecordsList");
                var summary = Get<TextBlock>(content, "RecordsSummary");
                Require(filter.Visibility == Visibility.Visible && filter.SelectedIndex == 0 && filter.Items.Count == 5
                    && !string.IsNullOrEmpty(AutomationProperties.GetName(filter)), "同步记录没有完整且可访问的筛选选项");
                Require(list.Items.Count == 4 && list.Visibility == Visibility.Visible, "全部记录未完整显示");
                await UntilAsync(() => Descendants(list).OfType<TextBlock>()
                    .Any(text => text.Text.StartsWith("离线示例一", StringComparison.Ordinal) && text.ActualHeight > 0));
                var outcomes = new[] { "在看", "已看", "已跳过", "失败" };
                for (var index = 0; index < outcomes.Length; index++)
                {
                    filter.SelectedIndex = index + 1;
                    Require(list.Items.Count == 1 && list.Items[0] is ScrobblingRecordRow row
                        && row.Outcome == UiText.Translate(outcomes[index]), $"「{outcomes[index]}」筛选显示了错误的记录");
                }
                var firstFailure = list.Items[0];
                var source = list.ItemsSource;
                var selection = filter.SelectedItem;
                await Task.Run(() => Record(ScrobblingProvider.Douban, "新增在看", ScrobblingRecordOutcome.Watching));
                await UntilAsync(() => summary.Text.Contains("5 / 100", StringComparison.Ordinal));
                Require(list.Items.Count == 1 && ReferenceEquals(firstFailure, list.Items[0]), "新增其他结果影响了失败筛选");

                for (var index = 0; index < 24; index++)
                    Record(ScrobblingProvider.Douban, $"新增失败 {index}", ScrobblingRecordOutcome.Failed);
                await UntilAsync(() => list.Items.Count == 25);
                Require(ReferenceEquals(source, list.ItemsSource) && ReferenceEquals(selection, filter.SelectedItem)
                    && ReferenceEquals(firstFailure, list.Items[^1]) && list.Items[0] is ScrobblingRecordRow latest
                    && latest.EpisodeTitle.StartsWith("新增失败 23", StringComparison.Ordinal)
                    && selection is ScrobblingRecordsFilterOption { Text: var count } && count.EndsWith("(25)", StringComparison.Ordinal),
                    "实时记录未保留筛选或旧行，或者最新记录未排首位");

                await LayoutAsync(list);
                var scroll = Descendants(list).OfType<ScrollViewer>().First();
                var panel = (ItemsStackPanel)list.ItemsPanelRoot;
                await ScrollProbeAsync(scroll, scroll.ScrollableHeight * .45);
                Require(panel.FirstVisibleIndex > 0 && panel.FirstVisibleIndex < list.Items.Count, "长列表未滚动到中段");
                var firstVisible = list.Items[panel.FirstVisibleIndex];
                Record(ScrobblingProvider.Douban, "滚动时新增", ScrobblingRecordOutcome.Failed);
                await UntilAsync(() => list.Items.Count == 26);
                await LayoutAsync(list);
                Require(panel.FirstVisibleIndex >= 0 && ReferenceEquals(firstVisible, list.Items[panel.FirstVisibleIndex]),
                    "读取旧记录时，新记录挤走了原先的滚动项");

                isCurrent = false;
                Record(ScrobblingProvider.Douban, "旧活动期结果", ScrobblingRecordOutcome.Failed);
                await SettleAsync();
                Require(list.Items.Count == 26, "已失效活动期的事件仍更新了弹窗");
                isCurrent = true;
                Record(ScrobblingProvider.Douban, "当前活动期结果", ScrobblingRecordOutcome.Failed);
                await UntilAsync(() => list.Items.Count == 28);

                ResizeInspect(360, 520);
                await LayoutAsync(_root);
                await LayoutAsync(content);
                Require(_root.XamlRoot.Size.Width < 400 && content.ActualWidth is > 0 and < 300,
                    $"记录弹窗未随宿主缩窄：宿主 {_root.XamlRoot.Size.Width:0}，正文 {content.ActualWidth:0}（请求 {content.Width:0}）");
                var summaryBottom = summary.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, summary.ActualHeight)).Y;
                var filterOrigin = filter.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point());
                Require(filter.ActualWidth > 0 && filter.ActualHeight > 0 && filterOrigin.Y >= summaryBottom - 1
                    && filterOrigin.X + filter.ActualWidth <= content.ActualWidth + 1, "窄窗中筛选未换行或超出内容边界");

                filter.SelectedIndex = 0;
                Require(list.Items.Count == 32 && ReferenceEquals(source, list.ItemsSource), "切回全部未恢复完整记录");
            });
        }
        finally { ResizeProbeClient((int)Math.Round(original.Width), (int)Math.Round(original.Height)); }

        await InspectScrobblingDialogAsync(service, ScrobblingProvider.Bangumi, () => true, async content =>
        {
            var filter = Get<ComboBox>(content, "ScrobblingRecordsFilter");
            var list = Get<ListView>(content, "ScrobblingRecordsList");
            var empty = Get<TextBlock>(content, "RecordsEmpty");
            Require(filter.Visibility == Visibility.Collapsed && list.Visibility == Visibility.Collapsed
                && list.Items.Count == 0 && empty.Visibility == Visibility.Visible && empty.ActualHeight > 0,
                "没有同步记录时，弹窗未正确显示空状态");
            Record(ScrobblingProvider.Bangumi, "首次已看", ScrobblingRecordOutcome.Watched);
            await UntilAsync(() => filter.Visibility == Visibility.Visible);
            Require(filter.SelectedIndex == 4 && empty.Visibility == Visibility.Visible && list.Items.Count == 0
                && empty.Text == UiText.Translate("这个分类下还没有同步记录。"), "空记录到实时更新未保留初选失败分类");
            Record(ScrobblingProvider.Bangumi, "首次失败", ScrobblingRecordOutcome.Failed);
            await UntilAsync(() => list.Items.Count == 1);
            Require(list.Visibility == Visibility.Visible && empty.Visibility == Visibility.Collapsed, "筛选空状态未恢复为新记录");
        }, ScrobblingRecordOutcome.Failed);
        await InspectScrobblingNotificationToggleAsync();
    }

    private async Task InspectScrobblingNotificationToggleAsync()
    {
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-scrobbling"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "打格子";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);

            const string Notification = "打格子失败时发送系统通知";
            ToggleSwitch? VisibleToggle(string label) => Descendants(page).OfType<ToggleSwitch>()
                .FirstOrDefault(control => AutomationProperties.GetName(control) == UiText.Translate(label) && control.ActualHeight > 0);
            static void Toggle(ToggleSwitch control) =>
                ((IToggleProvider)new ToggleSwitchAutomationPeer(control).GetPattern(PatternInterface.Toggle)).Toggle();

            Require(VisibleToggle(Notification) is null && !fixture.Settings.Scrobbling.NotifyOnFailure,
                "两站关闭时通知开关未隐藏或开启了默认通知");
            var bangumi = VisibleToggle("启用 Bangumi 打格子") ?? throw new InvalidOperationException("找不到 Bangumi 启用开关");
            Toggle(bangumi);
            await UntilAsync(() => VisibleToggle(Notification) is not null);
            var notification = VisibleToggle(Notification)!;
            Require(!notification.IsOn, "通知开关的显示值与设置默认值不一致");
            Toggle(notification);
            Require(fixture.Settings.Scrobbling.NotifyOnFailure, "通过无障碍开关启用通知未写回设置");
            Toggle(bangumi);
            await UntilAsync(() => VisibleToggle(Notification) is null);
            Toggle(bangumi);
            await UntilAsync(() => VisibleToggle(Notification) is { IsOn: true });
        }
        finally
        {
            page.Release();
            page.ViewModel.Dispose();
            _root.Children.Remove(page);
        }
    }

    private async Task InspectScrobblingDialogAsync(ScrobblingService service, ScrobblingProvider provider,
        Func<bool> isCurrent, Func<ScrobblingRecordsView, Task> check, ScrobblingRecordOutcome? initialOutcome = null)
    {
        var dialog = SettingsPage.CreateScrobblingRecordsDialog(_root.XamlRoot, provider, service.GetRecords(provider), initialOutcome);
        var content = (ScrobblingRecordsView)dialog.Content;
        var shown = SettingsPage.ShowScrobblingRecordsDialogAsync(dialog, service, provider, isCurrent);
        try
        {
            await LayoutAsync(content);
            Require(Descendants(dialog).OfType<Button>().Any(button => button.Name == "CloseButton" && button.ActualWidth > 0)
                && DialogDismiss.GetClosesOnOutsideClick(dialog), "同步记录未加载统一弹窗模板");
            await check(content);
        }
        finally
        {
            dialog.Hide();
            await shown.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class RejectScrobblingDouban : IDoubanScrobblingClient
    {
        public Task<bool> CheckLoginAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
