using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task AniBtEpisodeSearchAsync()
    {
        var mpServer = new AniBtMoviePilotProbeServer();
        var aniBt = new AniBtProbeServer();
        using var fixture = CreateFixture(mpServer, aniBt);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var mp = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var service = (AniBtService)fixture.Services.GetService(typeof(AniBtService))!;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        var anchor = new Button { Content = "版本来源", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _root.Children.Add(shell);
        _root.Children.Add(anchor);
        var owner = new EmbyItem { Id = "anime", Name = "离线示例动画", Type = EmbyItemType.Series, Genres = ["日本动画"] };
        var episode = new EmbyItem
        {
            Id = "virtual-episode",
            Name = "这是用于检查窄窗口换行与搜索入口的较长单集标题",
            Type = EmbyItemType.Episode,
            ParentIndexNumber = 2,
            IndexNumber = 4
        };
        var panel = new AniBtSearchPanel { Padding = new Thickness(16) };
        MissingEpisodesDialog? missingDialog = null;
        MenuFlyout? menu = null;
        var topMost = _window!.TopMost;
        try
        {
            // 在展开菜单之前置顶；截图时才抬高宿主会把原生弹出层压到宿主后面。
            _window.TopMost = true;
            await LayoutAsync(anchor);
            menu = ItemCommands.Build(fixture.Session, shell, anchor, new CardItem(owner, fixture.Images, 170));
            var versions = menu.Items.OfType<MenuFlyoutSubItem>().Single();
            Require(versions.Text == "搜索不同版本" && versions.Items.OfType<MenuFlyoutItem>()
                .Select(item => item.Text).SequenceEqual(new[] { "使用 MoviePilot", "使用 AniBT API 搜索" }), "动画版本入口未折叠为两个来源");
            menu.ShowAt(anchor);
            await LayoutAsync(anchor);
            ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(versions)
                .GetPattern(PatternInterface.ExpandCollapse)).Expand();
            await LayoutAsync(anchor);
            await SaveProbeScreenAsync("anime-version-menu.png");
            menu.Hide();
            _root.Children.Remove(anchor);
            _root.Children.Remove(shell);

            foreach (var anime in new[] { false, true })
            {
                missingDialog = new MissingEpisodesDialog(owner, [episode], (_, _) => Task.FromResult<byte[]?>(null), true, anime)
                { XamlRoot = _root.XamlRoot };
                var shown = missingDialog.ShowAsync();
                foreach (var width in new[] { 1040, 520 })
                {
                    ResizeInspect(width, 800);
                    await LayoutAsync(missingDialog);
                    await SaveProbeScreenAsync($"missing-search-{anime}-{width}.png");
                }
                if (anime)
                {
                    var dropdown = Descendants(missingDialog).OfType<DropDownButton>().Single(button => button.Name == "SearchMissingSources");
                    Require(dropdown.Visibility == Visibility.Visible, "动画遗漏集未提供来源选择");
                    dropdown.Flyout.ShowAt(dropdown);
                    await LayoutAsync(missingDialog);
                    await SaveProbeScreenAsync("missing-anime-sources.png");
                    var choice = ((MenuFlyout)dropdown.Flyout).Items.OfType<MenuFlyoutItem>().Single(item => item.Name == "MissingAniBt");
                    ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(choice).GetPattern(PatternInterface.Invoke)).Invoke();
                }
                else
                {
                    var button = Descendants(missingDialog).OfType<Button>().Single(button => button.Name == "SearchMissingMoviePilot");
                    Require(button.Visibility == Visibility.Visible, "普通遗漏集未提供 MP 搜索按钮");
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                }
                await shown;
                Require(ReferenceEquals(missingDialog.SearchEpisode, episode) && missingDialog.SearchWithAniBt == anime,
                    "遗漏集搜索未保留所点行和来源");
            }

            _root.Children.Add(panel);
            var query = AniBtSearchQuery.From(episode, owner);
            panel.Search(service, mp, fixture.Launcher, query);
            await UntilAsync(() => panel.ViewModel.Results.Count == 2 && !panel.ViewModel.Busy);
            Require(Get<AutoSuggestBox>(panel, "AnimeSearchInput").Text == owner.Name, "AniBT 关键词混入了 MP 季集格式");
            await panel.ViewModel.Results[0].OpenCommand.ExecuteAsync(null);
            Require(aniBt.Requests.Contains("/rss/anime.xml?bgmId=123&episode=4"), "AniBT RSS 没有收到独立分集参数");
            panel.ViewModel.Browser.Groups[0].IsExpanded = true;
            await LayoutAsync(panel);
            await SaveProbeScreenAsync("anibt-episode-resources.png");
            var row = panel.ViewModel.Browser.Rows.Single();
            var downloading = row.DownloadCommand.ExecuteAsync(null);
            var dialog = await DownloadDialogAsync();
            await UntilAsync(() => !dialog.ViewModel.Busy);
            Get<ComboBox>(dialog, "DownloadClient").SelectedItem = "备用下载器";
            Get<ComboBox>(dialog, "DownloadPath").Text = "/downloads/custom-anime";
            await LayoutAsync(dialog);
            Require(dialog.ViewModel.Options == new MoviePilotDownloadOptions("备用下载器", "/downloads/custom-anime"),
                "AniBT 下载器和自定义路径未进入确认选项");
            await SaveProbeScreenAsync("anibt-episode-download-options.png");
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            await downloading;
            Require(mpServer.MutationAttempts == 0, "取消 AniBT 下载仍然写入 MP");

            var input = Get<AutoSuggestBox>(panel, "AnimeSearchInput");
            Descendants(input).OfType<TextBox>().First().Text = "其他译名";
            await UntilAsync(() => panel.ViewModel.Browser.Rows.Count == 0);
            Require(panel.ViewModel.Episode == 4 && panel.ViewModel.Results.Count == 0, "改词没有作废旧结果或丢失分集范围");
            await panel.SearchAsync("其他译名", panel.ViewModel.Episode);
            await panel.ViewModel.Results[0].OpenCommand.ExecuteAsync(null);
            await panel.ViewModel.ShowAllEpisodesCommand.ExecuteAsync(null);
            Require(aniBt.Requests.Last() == "/rss/anime.xml?bgmId=123" && panel.ViewModel.Episode is null,
                "查看全部集数没有移除 RSS 分集条件");
            Require(mpServer.Requests.All(path => !path.Contains("/search/", StringComparison.Ordinal)) && fixture.Actions.Plays == 0,
                "AniBT 查询借用了 MoviePilot 搜索或触发播放");
        }
        finally
        {
            menu?.Hide();
            missingDialog?.Hide();
            foreach (var dialog in OpenDownloads()) dialog.Hide();
            panel.Release();
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(panel);
            _root.Children.Remove(anchor);
            _root.Children.Remove(shell);
            _window.TopMost = topMost;
        }

        IEnumerable<MoviePilotDownloadDialog> OpenDownloads() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MoviePilotDownloadDialog>().Distinct();

        async Task<MoviePilotDownloadDialog> DownloadDialogAsync()
        {
            await UntilAsync(() => OpenDownloads().Any());
            return OpenDownloads().Single();
        }
    }
}
