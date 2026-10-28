using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MetadataLayoutAsync()
    {
        foreach (var (width, height) in new[] { (1100, 940), (760, 820), (540, 680) })
        {
            ResizeProbeClient(width, height);
            await LayoutAsync(_root);
            var json = MetadataSample();
            var dialog = MetadataDialog(json);
            var shown = dialog.ShowAsync().AsTask();
            try
            {
                await LayoutAsync(dialog);
                var scroll = Get<ScrollViewer>(dialog, "EditorScroll");
                Require(!dialog.Edit.ApplyTo((JsonObject)json.DeepClone()), "打开完整表单不应改变任何字段");
                Require(Get<TextBlock>(dialog, "ItemPathText").Text.Contains("E04", StringComparison.Ordinal), "文件路径未展示");
                Get<Expander>(dialog, "AdditionalFields").IsExpanded = true;
                await LayoutAsync(dialog);
                MetadataIconButtonsFit(dialog);
                Get<Expander>(dialog, "AdditionalFields").IsExpanded = false;
                // A negative control on the real template proves the check catches text-button padding.
                var lockButton = Get<ToggleButton>(dialog, "NameLock");
                lockButton.Padding = new Thickness(11, 5, 11, 6);
                await LayoutAsync(dialog);
                var rejected = false;
                try { MetadataIconButtonsFit(dialog); }
                catch (InvalidOperationException) { rejected = true; }
                finally { lockButton.Padding = new Thickness(0); }
                Require(rejected, "锁图标内边距负对照未被识别");
                await LayoutAsync(dialog);
                MetadataIconButtonsFit(dialog);
                Require(Get<TextBox>(dialog, "NameBox").Text == "正因为我全裸两次，才领悟到一个道理……",
                    "标题未从服务器整份元数据读入");
                Require(Get<ToggleButton>(dialog, "NameLock").IsChecked == true
                    && Get<ToggleSwitch>(dialog, "LockItemSwitch").IsOn, "字段锁与整项锁未还原");
                Require(Get<StackPanel>(dialog, "IndexRow").Visibility == Visibility.Visible
                    && Get<TextBox>(dialog, "ParentIndexBox").Visibility == Visibility.Visible, "单集必须显示季和集编号");
                foreach (var field in new[] { "NameBox", "SortNameBox", "DateCreatedBox", "OverviewBox", "ProductionYearBox" })
                {
                    var box = Get<TextBox>(dialog, field);
                    Require(box.ActualWidth >= 160, $"{width} 窗口的 {field} 太窄");
                    var bounds = box.TransformToVisual(scroll).TransformBounds(new(0, 0, box.ActualWidth, box.ActualHeight));
                    Require(bounds.X >= -1 && bounds.Right <= scroll.ActualWidth + 1, $"{width} 的 {field} 越出内容区");
                }
                var primary = MetadataButton(dialog, "PrimaryButton");
                var primaryBox = primary.TransformToVisual(_root).TransformBounds(new(0, 0, primary.ActualWidth, primary.ActualHeight));
                Require(primaryBox.Bottom <= _root.ActualHeight && primaryBox.Right <= _root.ActualWidth,
                    "保存按钮被窄窗或长表单裁掉");
                await SaveProbeScreenAsync($"metadata-episode-{width}-top-screen.png");
                await MetadataBringAsync(dialog, Get<TextBox>(dialog, "OverviewBox"));
                await SaveProbeScreenAsync($"metadata-episode-{width}-ratings-screen.png");
                await MetadataBringAsync(dialog, Get<StackPanel>(dialog, "ExternalIdsSection"));
                await SaveProbeScreenAsync($"metadata-episode-{width}-people-screen.png");
                await ScrollProbeAsync(scroll, scroll.ScrollableHeight);
                await SaveProbeScreenAsync($"metadata-episode-{width}-settings-screen.png");
                Require(scroll.ScrollableHeight > 800 && scroll.VerticalOffset > 500, "长表单没有实际滚动到设置尾部");
                Require(Get<ComboBox>(dialog, "LanguageBox").SelectedItem is ComboBoxItem { Tag: "zh" }
                    && Get<ComboBox>(dialog, "CountryBox").SelectedItem is ComboBoxItem { Tag: "CN" }, "语言和国家没有选中原值");
                var people = Get<MetadataListEditor>(dialog, "PeopleEditor");
                Require(people.Rows.Count == 9 && people.IsLocked, "人物或人物锁定未还原");
                dialog.SetItemType(EmbyItemType.Season);
                Require(Get<TextBox>(dialog, "ParentIndexBox").Visibility == Visibility.Collapsed
                    && Equals(Get<TextBox>(dialog, "IndexBox").Header, "季编号")
                    && Get<ComboBox>(dialog, "VideoFormatBox").Visibility == Visibility.Collapsed, "季页显示了无效字段");
                dialog.SetItemType(EmbyItemType.Movie);
                Require(Get<StackPanel>(dialog, "IndexRow").Visibility == Visibility.Collapsed
                    && Get<ComboBox>(dialog, "VideoFormatBox").Visibility == Visibility.Visible, "电影字段显隐错误");
                dialog.SetItemType(EmbyItemType.Episode);
                Require(Get<TextBox>(dialog, "ParentIndexBox").Visibility == Visibility.Visible
                    && dialog.Edit.Basic.ParentIndexNumber == "1", "恢复单集时偏好或编号丢失");
            }
            finally { dialog.Hide(); await shown; }
        }
    }

    private async Task MetadataListsAsync()
    {
        ResizeProbeClient(900, 900);
        var dialog = MetadataDialog(MetadataSample());
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(dialog);
            var people = Get<MetadataListEditor>(dialog, "PeopleEditor");
            var rows = people.Rows;
            await MetadataBringAsync(dialog, people);
            MetadataInvoke(Get<Button>(people, "AddButton"));
            await LayoutAsync(Get<TextBox>(people, "EntryNameBox"));
            Require(!Get<Button>(people, "ApplyEntryButton").IsEnabled, "空人物不能提交");
            Get<TextBox>(people, "EntryNameBox").Text = "离线新演员";
            Get<TextBox>(people, "EntryRoleBox").Text = "新角色 (voice)";
            await UntilAsync(() => Get<Button>(people, "ApplyEntryButton").IsEnabled);
            MetadataInvoke(Get<Button>(people, "ApplyEntryButton"));
            await UntilAsync(() => rows.Count == 10);
            Require(ReferenceEquals(rows, people.Rows) && rows[^1].Person?.Role == "新角色 (voice)", "添加人物替换了集合或丢失角色");
            var added = rows[^1];
            var list = Get<ListView>(people, "Entries");
            Require(list.CanDragItems && list.CanReorderItems && list.AllowDrop, "人物列表没有原生重排入口");
            list.SelectedItem = rows[0];
            await LayoutAsync(list);
            MetadataInvoke(Get<Button>(people, "MoveDownButton"));
            await UntilAsync(() => rows[1].Name == "山下大辉");
            var edited = rows[1];
            var editButton = Descendants(list).OfType<Button>().Single(button => button.Name == "EditEntry" && ReferenceEquals(button.Tag, edited));
            Require(AutomationProperties.GetName(editButton).Length > 0, "模板编辑按钮没有可访问名称");
            MetadataInvoke(editButton);
            await LayoutAsync(Get<TextBox>(people, "EntryNameBox"));
            Get<TextBox>(people, "EntryRoleBox").Text = "这段取消后不应写回";
            MetadataInvoke(Get<Button>(people, "CancelEntryButton"));
            await SettleAsync();
            Require(ReferenceEquals(rows[1], edited) && rows[1].Person?.Role == "Rein Garland (voice)", "取消人物编辑改了草稿");
            list.SelectedItem = added;
            await LayoutAsync(list);
            var remove = Descendants(list).OfType<Button>().Single(button => button.Name == "RemoveEntry" && ReferenceEquals(button.Tag, added));
            MetadataInvoke(remove);
            await UntilAsync(() => rows.Count == 9);
            var applied = MetadataSample();
            Require(dialog.Edit.ApplyTo(applied), "重排后没有要保存的改动");
            Require(applied["People"]![1]!["Id"]!.GetValue<string>() == "person-0"
                && applied["People"]![1]!["Future"]!.GetValue<string>() == "preserved", "人物重排丢了身份或未知字段");
            var genres = Get<MetadataListEditor>(dialog, "GenresEditor");
            await MetadataBringAsync(dialog, genres);
            MetadataInvoke(Get<Button>(genres, "AddButton"));
            await LayoutAsync(Get<TextBox>(genres, "EntryNameBox"));
            Get<TextBox>(genres, "EntryNameBox").Text = "科幻,奇幻";
            await UntilAsync(() => Get<Button>(genres, "ApplyEntryButton").IsEnabled);
            MetadataInvoke(Get<Button>(genres, "ApplyEntryButton"));
            await UntilAsync(() => genres.Rows.Count == 1);
            Require(dialog.Edit.Genres.Single() == "科幻,奇幻", "列表输入被错误按逗号拆分");
            genres.IsLocked = true;
            genres.IsLocked = false;
            Require(!dialog.Edit.LockedFields.Contains("Genres") && genres.Rows.Count == 1,
                "解锁不应该清空已有条目");
            await SaveProbeScreenAsync("metadata-list-edited-screen.png");
        }
        finally { dialog.Hide(); await shown; }
    }

    private async Task MetadataSaveAsync()
    {
        ResizeProbeClient(900, 900);
        var attempts = 0;
        var pending = Pending<bool>();
        var dialog = MetadataDialog(MetadataSample(), _ =>
        {
            attempts++;
            return attempts == 1 ? Task.FromException<bool>(new InvalidOperationException("离线模拟：服务器暂不可用")) : pending.Task;
        });
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(dialog);
            var title = Get<TextBox>(dialog, "NameBox");
            title.Text = "";
            await UntilAsync(() => !dialog.IsPrimaryButtonEnabled && Get<InfoBar>(dialog, "Problem").IsOpen);
            Require(!dialog.IsPrimaryButtonEnabled && Get<InfoBar>(dialog, "Problem").IsOpen, "空标题仍可保存");
            title.Text = "离线修改后的标题";
            await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
            MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
            await UntilAsync(() => attempts == 1 && !Get<bool>(dialog, "_saving"));
            Require(!shown.IsCompleted && title.Text == "离线修改后的标题" && Get<InfoBar>(dialog, "Problem").IsOpen
                && dialog.IsPrimaryButtonEnabled, "失败丢失了表单、输入、反馈或重试入口");
            await SaveProbeScreenAsync("metadata-save-failed-screen.png");
            MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
            await UntilAsync(() => attempts == 2 && Get<bool>(dialog, "_saving"));
            Require(!dialog.IsPrimaryButtonEnabled && Get<ProgressBar>(dialog, "SavingProgress").Visibility == Visibility.Visible,
                "保存中仍可重复提交或没有进度");
            MetadataInvoke(MetadataButton(dialog, "CloseButton"));
            await SettleAsync();
            Require(!shown.IsCompleted, "保存仍进行时取消丢了写入结果");
            pending.TrySetResult(true);
            Require(await shown == ContentDialogResult.Primary && dialog.Changed, "重试成功未返回已确认结果");
        }
        finally { pending.TrySetResult(false); dialog.Hide(); await shown; }
        var cancelledWrites = 0;
        var cancelled = MetadataDialog(MetadataSample(), _ => { cancelledWrites++; return Task.FromResult(true); });
        var cancelledShown = cancelled.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(cancelled);
            Get<TextBox>(cancelled, "NameBox").Text = "取消这个修改";
            MetadataInvoke(MetadataButton(cancelled, "CloseButton"));
            Require(await cancelledShown == ContentDialogResult.None && cancelledWrites == 0, "取消表单发起了保存");
        }
        finally { cancelled.Hide(); await cancelledShown; }
    }

    private EmbyMetadataDialog MetadataDialog(JsonObject sample, Func<EmbyMetadataEdit, Task<bool>>? save = null)
    {
        var dialog = new EmbyMetadataDialog("离线示例 · S01E04", EmbyMetadataEdit.Read(sample), MetadataChoices(),
            save ?? (_ => throw new InvalidOperationException("布局检查不允许提交"))) { XamlRoot = _root.XamlRoot };
        dialog.SetItemType(EmbyItemType.Episode);
        return dialog;
    }

    private static EmbyMetadataEditorInfo MetadataChoices() => new()
    {
        ParentalRatingOptions = [new() { Name = "TV-14", Value = 14 }, new() { Name = "PG-13", Value = 13 }],
        Cultures = [new() { Name = "Chinese", DisplayName = "中文", TwoLetterISOLanguageName = "zh" },
                    new() { Name = "Japanese", DisplayName = "日语", TwoLetterISOLanguageName = "ja" }],
        Countries = [new() { Name = "China", DisplayName = "中国", TwoLetterISORegionName = "CN" },
                     new() { Name = "Japan", DisplayName = "日本", TwoLetterISORegionName = "JP" }],
        ExternalIdInfos = [new() { Name = "IMDb", Key = "Imdb", UrlFormatString = "https://example.test/title/{0}" },
                           new() { Name = "TheTVDB", Key = "Tvdb", UrlFormatString = "https://example.test/episode/{0}" }]
    };

    private static JsonObject MetadataSample()
    {
        var item = JsonNode.Parse("""
            {"Id":"metadata-fixture", "Type":"Episode", "Name":"正因为我全裸两次，才领悟到一个道理……",
             "Path":"/offline/Downloads/被放逐的作家/Season 01/S01E04 1080p.mkv", "Overview":"Rein's old guild learns he dispelled their enchantments, and Cornelius hires an assassin.",
             "DateCreated":"2026-10-28T09:33:45.1234567Z", "PremiereDate":"2026-10-28T00:00:00.1234567+09:00",
             "ProductionYear":2026, "IndexNumber":4, "ParentIndexNumber":1, "OfficialRating":"TV-14", "CustomRating":"TV-14",
             "ProviderIds":{"Imdb":"tt50122563","Tvdb":"12022342"}, "PreferredMetadataLanguage":"zh", "PreferredMetadataCountryCode":"CN",
             "LockedFields":["Name","Cast","FutureLock"], "LockData":true, "People":[]}
            """)!.AsObject();
        var names = new[] { "山下大辉", "琳恩", "青山吉能", "富田美忧", "潘惠美", "小原好美", "冈本信彦", "古川慎", "柴田理惠" };
        var roles = new[] { "Rein Garland (voice)", "Lily Flambert (voice)", "Margaret Ells (voice)", "Mirabelle (voice)", "Elsie Zra (voice)",
                            "Nina (voice)", "Half (voice)", "Cornelius (voice)", "Mother of Assassins (voice)" };
        for (var i = 0; i < names.Length; i++) item["People"]!.AsArray().Add(new JsonObject
            { ["Id"] = $"person-{i}", ["Name"] = names[i], ["Role"] = roles[i], ["Type"] = "Actor", ["Future"] = "preserved" });
        return item;
    }

    private static void MetadataIconButtonsFit(EmbyMetadataDialog dialog)
    {
        foreach (var control in Descendants(dialog).OfType<Control>().Where(control =>
            control is Button or ToggleButton && IconOf(control) is not null && control.Width == 36))
        {
            var icon = IconOf(control)!;
            var reference = new FontIcon { Glyph = icon.Glyph, FontFamily = icon.FontFamily, FontSize = icon.FontSize };
            reference.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = reference.DesiredSize;
            var bounds = icon.TransformToVisual(control).TransformBounds(new(0, 0, icon.ActualWidth, icon.ActualHeight));
            Require(control.Padding == new Thickness(0) && control.HorizontalContentAlignment == HorizontalAlignment.Center
                && control.VerticalContentAlignment == VerticalAlignment.Center, "图标小格仍继承了文字按钮内边距");
            Require(icon.ActualWidth + 0.5 >= size.Width && icon.ActualHeight + 0.5 >= size.Height,
                $"{control.Name} 图标自然尺寸 {size.Width:0.0}×{size.Height:0.0} 被裁成 {icon.ActualWidth:0.0}×{icon.ActualHeight:0.0}");
            Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= control.ActualWidth + 0.5
                && bounds.Bottom <= control.ActualHeight + 0.5,
                $"{control.Name} 图标越出按钮：{bounds}，按钮 {control.ActualWidth:0.0}×{control.ActualHeight:0.0}");
        }
    }

    private static FontIcon? IconOf(Control control) => control switch
    {
        Button button => button.Content as FontIcon,
        ToggleButton toggle => toggle.Content as FontIcon,
        _ => null
    };

    private static Button MetadataButton(ContentDialog dialog, string name) => Descendants(dialog).OfType<Button>().Single(button =>
        button.Name == name && Equals(button.Content, name == "PrimaryButton" ? dialog.PrimaryButtonText : dialog.CloseButtonText));
    private static void MetadataInvoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task MetadataBringAsync(EmbyMetadataDialog dialog, FrameworkElement element)
    {
        var scroll = Get<ScrollViewer>(dialog, "EditorScroll");
        var offset = element.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y + scroll.VerticalOffset;
        await ScrollProbeAsync(scroll, offset);
        await LayoutAsync(element);
    }
}
