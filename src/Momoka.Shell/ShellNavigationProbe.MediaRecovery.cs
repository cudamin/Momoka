using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // Only in-memory delegates are supplied: these cases never create a session, client or file picker.
    private async Task IdentifyRecoveryAsync()
    {
        foreach (var remove in new[] { false, true })
        {
            var calls = 0;
            var pending = Pending<bool>();
            Task SaveAsync()
            {
                calls++;
                return calls == 1 ? Task.FromException(new IOException("匿名保存拒绝")) : pending.Task;
            }
            var dialog = new IdentifyDialog(new EmbyItem { Id = "identify-recovery", Name = "匿名片名", Type = EmbyItemType.Movie },
                null, () => Task.FromResult(new List<ExternalIdInfo>()),
                _ => Task.FromResult(new List<RemoteSearchResult> { new() { Name = "匿名候选" } }),
                null, (_, _) => SaveAsync(), SaveAsync)
            { XamlRoot = _root.XamlRoot };
            var shown = dialog.ShowAsync().AsTask();
            try
            {
                await LayoutAsync(dialog);
                Get<TextBox>(dialog, "NameBox").Text = "保留搜索草稿";
                if (remove) MetadataInvoke(Get<Button>(dialog, "RemoveButton"));
                else
                {
                    MetadataInvoke(Get<Button>(dialog, "SearchButton"));
                    await UntilAsync(() => dialog.Candidates.Count == 1 && !Get<bool>(dialog, "_writing"));
                    await LayoutAsync(dialog);
                    MetadataInvoke(Descendants(dialog).OfType<Button>().Single(button =>
                        button.Tag is IdentifyCandidate && button.Visibility == Visibility.Visible));
                }
                var submit = Get<Button>(dialog, remove ? "ConfirmRemoveButton" : "ApplyButton");
                MetadataInvoke(submit);
                await UntilAsync(() => calls == 1 && !Get<bool>(dialog, "_writing"));
                Require(!shown.IsCompleted && !dialog.Touched && submit.IsEnabled && Get<InfoBar>(dialog, "Problem").IsOpen
                    && Get<TextBox>(dialog, "NameBox").Text == "保留搜索草稿", "识别失败未保留输入、反馈和重试入口");
                var prefix = remove ? "identify-remove" : "identify-apply";
                await SaveProbeScreenAsync(prefix + "-failed-screen.png");
                if (remove) MetadataInvoke(Get<Button>(dialog, "RemoveButton"));
                MetadataInvoke(submit);
                await UntilAsync(() => calls == 2 && Get<bool>(dialog, "_writing"));
                dialog.Hide();
                await SettleAsync();
                Require(!shown.IsCompleted && !submit.IsEnabled, "识别写入仍在飞时关闭了弹窗或允许重复提交");
                await SaveProbeScreenAsync(prefix + "-pending-screen.png");
                pending.TrySetResult(true);
                await shown;
                Require(dialog.Touched && calls == 2, "匿名确认后未关闭识别弹窗或重复保存");
            }
            finally
            {
                pending.TrySetResult(false);
                await UntilAsync(() => !Get<bool>(dialog, "_writing"));
                dialog.Hide();
                await shown;
            }
        }

        var search = Pending<List<RemoteSearchResult>>();
        var previews = 0;
        var late = new IdentifyDialog(new EmbyItem { Name = "匿名迟到搜索", Type = EmbyItemType.Movie }, null,
            () => Task.FromResult(new List<ExternalIdInfo>()), _ => search.Task,
            _ => { previews++; throw new InvalidOperationException("已关闭搜索不允许读取预览"); },
            (_, _) => throw new InvalidOperationException("搜索检查不允许写入"), null)
        { XamlRoot = _root.XamlRoot };
        var lateShown = late.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(late);
            MetadataInvoke(Get<Button>(late, "SearchButton"));
            late.Hide();
            await lateShown;
            search.TrySetResult([new RemoteSearchResult { Name = "迟到候选", ImageUrl = "https://image.invalid/never-requested" }]);
            await SettleAsync();
            Require(late.Candidates.Count == 0 && previews == 0, "已关闭搜索仍填入候选或读取预览");
        }
        finally { search.TrySetResult([]); late.Hide(); await lateShown; }
    }

    private async Task FnNasMetadataRecoveryAsync()
    {
        var original = JsonNode.Parse("""{"title":"匿名原标题","overview":"匿名原简介","rating":7,"release_date":"2024-05-01","untouched":"keep"}""")!.AsObject();
        var originalText = original.ToJsonString();
        var calls = 0;
        var pending = Pending<bool>();
        var dialog = new FnNasMetadataDialog(original, body =>
        {
            calls++;
            Require(body["title"]!.GetValue<string>() == "保留飞牛草稿" && body["untouched"]!.GetValue<string>() == "keep",
                "匿名保存未使用当前草稿或丢弃原字段");
            return calls == 1 ? Task.FromException(new IOException("匿名保存拒绝")) : pending.Task;
        })
        { XamlRoot = _root.XamlRoot };
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(dialog);
            Get<TextBox>(dialog, "NameBox").Text = "保留飞牛草稿";
            MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
            await UntilAsync(() => calls == 1 && !Get<bool>(dialog, "_saving"));
            Require(!shown.IsCompleted && !dialog.Changed && dialog.IsPrimaryButtonEnabled
                && Get<InfoBar>(dialog, "Problem").IsOpen && Get<TextBox>(dialog, "NameBox").Text == "保留飞牛草稿"
                && original.ToJsonString() == originalText, "飞牛失败未保留草稿、反馈、原始字段或重试入口");
            await SaveProbeScreenAsync("fnnas-metadata-failed-screen.png");
            MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
            await UntilAsync(() => calls == 2 && Get<bool>(dialog, "_saving"));
            MetadataInvoke(MetadataButton(dialog, "CloseButton"));
            dialog.Hide();
            await SettleAsync();
            Require(!shown.IsCompleted && !dialog.IsPrimaryButtonEnabled, "飞牛写入仍在飞时关闭或允许重复保存");
            await SaveProbeScreenAsync("fnnas-metadata-pending-screen.png");
            pending.TrySetResult(true);
            await shown;
            Require(dialog.Changed && dialog.Edit.Name == "保留飞牛草稿" && calls == 2, "飞牛确认后未关闭或错误发布草稿");
        }
        finally
        {
            pending.TrySetResult(false);
            await UntilAsync(() => !Get<bool>(dialog, "_saving"));
            dialog.Hide();
            await shown;
        }
    }
}
