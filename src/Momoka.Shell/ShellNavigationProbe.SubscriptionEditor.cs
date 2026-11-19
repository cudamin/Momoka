using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotSubscriptionEditorAsync()
    {
        using var server = new MoviePilotJourneyServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = fixture.Services.GetRequiredService<MoviePilotService>();
        var subscription = (await service.SubscriptionsAsync(default)).Single(item => item.Id == 4);
        var originalTheme = ThemeHost.Current.Id;
        ContentDialog? dialog = null;
        try
        {
            ResizeInspect(1000, 900);
            var showing = MoviePilotSubscriptionEditor.ShowAsync(_root, subscription, service,
                draft => service.UpdateSubscriptionAsync(subscription, draft, default));
            await UntilAsync(() => FindEditor() is not null);
            dialog = FindEditor()!;
            var editor = (MoviePilotSubscriptionEditor)dialog.Content;
            await UntilAsync(() => !editor.ViewModel.Busy && editor.ViewModel.Paths.Count > 0);
            await LayoutAsync(editor);
            var paths = Get<ComboBox>(editor, "SavePathInput");
            Require(paths.Text == "/downloads/series" && paths.SelectedItem is MoviePilotSubscriptionChoice { Value: "/downloads/series" },
                "编辑器没有显示实际保存路径");
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(editor);
                await SaveProbeScreenAsync($"mp-editor-basic-{theme.Id}-1000.png");
            }
            var flyoutTheme = ThemeHost.Current.Id;
            ThemeHost.Apply(Momoka.Theming.UiThemes.All[0].Id);
            await LayoutAsync(editor);
            var sites = Get<DropDownButton>(editor, "SubscriptionSitesInput");
            sites.Flyout.ShowAt(sites);
            await LayoutAsync(editor);
            var flyout = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<FlyoutPresenter>().Single();
            Require(flyout.Padding == new Thickness(4) && flyout.CornerRadius.TopLeft == 8,
                "多选下拉的展开态没有接 ComboBox 下拉那档内边距与圆角");
            Require(flyout.Background is AcrylicBrush, "多选下拉的展开态不是 ComboBox 下拉那块亚克力");
            var options = Descendants(flyout).OfType<CheckBox>().ToArray();
            Require(options.Length == editor.ViewModel.Sites.Count
                && options.All(option => Math.Abs(option.ActualHeight - 32) < 0.5),
                "多选下拉的展开态没有按 ComboBox 的 32 高整行排");
            var sample = editor.ViewModel.Sites.FirstOrDefault(row => !row.Selected) ?? editor.ViewModel.Sites[0];
            var wasSelected = sample.Selected;
            sample.Selected = !wasSelected;
            await LayoutAsync(editor);
            await SaveProbeScreenAsync("mp-editor-sites-flyout-1000.png");
            sample.Selected = wasSelected;
            sites.Flyout.Hide();
            ThemeHost.Apply(flyoutTheme);
            await LayoutAsync(editor);
            paths.SelectedItem = editor.ViewModel.Paths.Single(path => path.Value == "/downloads/anime");
            await LayoutAsync(editor);
            Require(editor.Read().SavePath == "/downloads/anime", "选择目录没有写入真实路径");
            var tabs = Get<SelectorBar>(editor, "EditorTabs");
            tabs.SelectedItem = Get<SelectorBarItem>(editor, "AdvancedTab");
            Get<TextBox>(editor, "IncludeInput").Text = "WEB-DL";
            await LayoutAsync(editor);
            await SaveProbeScreenAsync("mp-editor-advanced-1000.png");
            ResizeInspect(520, 900);
            await LayoutAsync(editor);
            Require(editor.ActualWidth <= _root.ActualWidth && Get<Grid>(editor, "MatchFields").ColumnDefinitions.Count == 1,
                "窄窗编辑表没有重排为单列");
            await SaveProbeScreenAsync("mp-editor-advanced-520.png");
            var save = Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton");
            ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)).Invoke();
            await showing;
            Require(server.Subscriptions.Writes.Count == 1 && server.Subscriptions.WriteBodies.Single().Count == 3
                && server.Subscriptions.Items[4]["save_path"]!.ToString() == "/downloads/anime"
                && server.Subscriptions.Items[4]["include"]!.ToString() == "WEB-DL", "编辑未增量保存路径和规则");
        }
        finally { dialog?.Hide(); ThemeHost.Apply(originalTheme); ResizeInspect(1420, 980); }

        ContentDialog? FindEditor() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>()
            .FirstOrDefault(value => value.Content is MoviePilotSubscriptionEditor);
    }
}
