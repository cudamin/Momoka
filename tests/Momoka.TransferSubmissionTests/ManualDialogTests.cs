using System.Xml.Linq;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ManualDialogTests
{
    internal static void Register()
    {
        Test("手动整理窗口：打开时全部自动，不带当前剧集数据", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture, series: true);
            var request = dialog.FixtureRequest;
            Assert.Equal("", request.Type, "类型默认自动，不预选当前条目的电影/电视剧");
            Assert.Equal("", request.MediaId);
            Assert.Equal("", request.Season);
            Assert.Equal("", request.Episode);
            Assert.Equal("", request.TargetPath);
            Assert.Equal("", request.TargetStorage);
            Assert.Equal("", request.TransferType);
            Assert.Null(request.TypeFolder);
            Assert.Null(request.CategoryFolder);
            Assert.Null(request.Scrape);
            Assert.Equal("", dialog.SeasonBox.Text);
            Assert.Equal("", dialog.EpisodeBox.Text);
            Assert.Equal("", dialog.MediaIdBox.Text);
            Assert.Equal(Visibility.Collapsed, dialog.IdentityGrid.Visibility);
            Assert.Equal(Visibility.Collapsed, dialog.HistoryLookupPanel.Visibility);
            Assert.Equal("/downloads/source.mkv", request.Histories.Single().TransferPath);
            Assert.Equal(Visibility.Collapsed, dialog.EpisodeField.Visibility);
            Assert.Equal(0, fixture.Transport.Writes);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：原记录查询失败时显示直接重试，恢复后回到简洁表单", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            fixture.Transport.Failure = "history-500";
            var context = new MoviePilotTransferContext("Original", "电影", "1", null, "",
                [new("source", "/library/original.mkv")]);
            var dialog = new MoviePilotReorganizeDialog(fixture.Service, context);
            TransferSubmissionFixture.Complete(dialog.FixtureLoadAsync());
            Assert.True(dialog.Problem.IsOpen);
            Assert.Equal(Visibility.Visible, dialog.HistoryLookupPanel.Visibility);
            Assert.False(dialog.PreviewButton.IsEnabled);
            fixture.Transport.Failure = "";
            dialog.FixtureFindHistory();
            Assert.False(dialog.Problem.IsOpen);
            Assert.Equal(Visibility.Collapsed, dialog.HistoryLookupPanel.Visibility);
            Assert.True(dialog.PreviewButton.IsEnabled);
            Assert.Equal(0, fixture.Transport.Writes);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：选项加载失败重试会补齐存储来源和目录，再开放预览", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            fixture.Transport.Failure = "options-500";
            var dialog = new MoviePilotReorganizeDialog(fixture.Service,
                new("Original", "电影", "1", null, "", [new("source", "/library/original.mkv")]));
            TransferSubmissionFixture.Complete(dialog.FixtureLoadAsync());
            Assert.True(dialog.Problem.IsOpen);
            Assert.Equal(Visibility.Visible, dialog.HistoryLookupPanel.Visibility);
            Assert.False(dialog.PreviewButton.IsEnabled);
            fixture.Transport.Failure = "";
            dialog.FixtureFindHistory();
            Assert.False(dialog.Problem.IsOpen);
            Assert.Equal(Visibility.Collapsed, dialog.HistoryLookupPanel.Visibility);
            Assert.True(dialog.PreviewButton.IsEnabled);
            Assert.True(((IEnumerable<MoviePilotStorage>)dialog.TargetStorageBox.ItemsSource!).Any(storage => storage.Type == "local"));
            Assert.True(((IEnumerable<MoviePilotMediaSource>)dialog.SourceBox.ItemsSource!).Any(source => source.Id == "douban"));
            Assert.True(((IEnumerable<string>)dialog.TargetPathBox.ItemsSource!).Contains("/library", StringComparer.Ordinal));
            Assert.Equal(0, fixture.Transport.Writes);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：原记录不唯一时直接重选，选择后仍用记录的源文件", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            fixture.Transport.DuplicateHistory = true;
            var dialog = Load(fixture);
            Assert.Equal(Visibility.Visible, dialog.HistoryLookupPanel.Visibility);
            Assert.False(dialog.PreviewButton.IsEnabled);
            var candidates = ((IEnumerable<MoviePilotTransferHistory>)dialog.HistoryBox.ItemsSource!).ToList();
            dialog.HistoryBox.SelectedIndex = candidates.FindIndex(history => history.Id == 2);
            dialog.FixtureSelectHistory();
            Assert.Equal(Visibility.Collapsed, dialog.HistoryLookupPanel.Visibility);
            Assert.Equal(2L, dialog.FixtureRequest.Histories.Single().Id);
            Assert.Equal("/downloads/source.mkv", dialog.FixtureRequest.Histories.Single().TransferPath);
            Assert.True(dialog.PreviewButton.IsEnabled);
            Assert.Equal(0, fixture.Transport.Writes);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：自动匹配只展示，不覆盖自动请求及继承开关", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            Assert.Equal("", dialog.FixtureRequest.TargetPath);
            Assert.Equal("", dialog.FixtureRequest.TargetStorage);
            Assert.Equal("", dialog.FixtureRequest.TransferType);
            Assert.Null(dialog.FixtureRequest.TypeFolder);
            Assert.Null(dialog.FixtureRequest.CategoryFolder);
            Assert.Null(dialog.FixtureRequest.Scrape);
            Assert.True(dialog.TypeFolderToggle.IsOn);
            Assert.True(dialog.ScrapeToggle.IsOn);
            Assert.Equal(Visibility.Visible, dialog.MatchedPathPanel.Visibility);
            Assert.Contains("/library", dialog.MatchedPathText.Text);
            Assert.Equal(0, fixture.Transport.Writes);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：选目录自动带出存储、方式、分类与刮削", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            dialog.TargetPathBox.Text = "/library";
            var request = dialog.FixtureRequest;
            Assert.Equal("/library", request.TargetPath);
            Assert.Equal("local", request.TargetStorage);
            Assert.Equal("softlink", request.TransferType);
            Assert.Equal(true, request.TypeFolder);
            Assert.Equal(false, request.CategoryFolder);
            Assert.Equal(true, request.Scrape);
            Assert.True(dialog.TypeFolderToggle.IsOn);
            Assert.False(dialog.CategoryFolderToggle.IsOn);
            Assert.True(dialog.ScrapeToggle.IsOn);
            Assert.Equal(Visibility.Collapsed, dialog.MatchedPathPanel.Visibility);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：手填陌生路径退到本地复制并关闭分类", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            dialog.TargetPathBox.Text = "/custom/library";
            var request = dialog.FixtureRequest;
            Assert.Equal("/custom/library", request.TargetPath);
            Assert.Equal("local", request.TargetStorage);
            Assert.Equal("copy", request.TransferType);
            Assert.Equal(false, request.TypeFolder);
            Assert.Equal(false, request.CategoryFolder);
            Assert.Equal(false, request.Scrape);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：已手选的整理方式不被后来的目录覆盖", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            dialog.TargetPathBox.Text = "/library";
            dialog.TransferTypeBox.SelectedIndex = 1;
            dialog.FixtureEdited();
            Assert.Equal("copy", dialog.FixtureRequest.TransferType);
            dialog.TargetPathBox.Text = "/custom/library";
            Assert.Equal("copy", dialog.FixtureRequest.TransferType, "与上游一致：只有自动时才采用目录的方式");
            dialog.EmitClosed();
        });
        Test("手动整理窗口：使用匹配路径一次应用存储、方式、分类和刮削", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            dialog.FixtureApplyMatch();
            var request = dialog.FixtureRequest;
            Assert.Equal("local", request.TargetStorage);
            Assert.Equal("/library", request.TargetPath);
            Assert.Equal("softlink", request.TransferType);
            Assert.Equal(true, request.TypeFolder);
            Assert.Equal(false, request.CategoryFolder);
            Assert.Equal(true, request.Scrape);
            Assert.Equal(Visibility.Collapsed, dialog.MatchedPathPanel.Visibility);
            Assert.False(dialog.ReorganizeButton.IsEnabled);
            Assert.Equal(0, fixture.Transport.Writes);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：清空目的路径回到全自动，不残留显式 false", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            dialog.FixtureApplyMatch();
            dialog.TargetPathBox.Text = "";
            Assert.Equal("", dialog.FixtureRequest.TargetStorage);
            Assert.Equal("", dialog.FixtureRequest.TransferType);
            Assert.Null(dialog.FixtureRequest.TypeFolder);
            Assert.Null(dialog.FixtureRequest.Scrape);
            Assert.True(dialog.TypeFolderToggle.IsOn, "显示匹配的有效值而非伪造默认关闭");
            dialog.FixtureFolderToggle(false);
            Assert.Equal(false, dialog.FixtureRequest.TypeFolder);
            Assert.Null(dialog.FixtureRequest.Scrape);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：复用历史隐藏识别区，关闭后恢复手填季集", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture, series: true);
            dialog.FixtureType(2);
            dialog.MediaIdBox.Text = "1";
            dialog.SeasonBox.Text = "2";
            dialog.EpisodeBox.Text = "24";
            dialog.EpisodeFormatBox.Text = "第{ep}集";
            dialog.EpisodeOffsetBox.Text = "-10";
            dialog.FixtureEdited();
            dialog.FixtureFromHistory(true);
            var history = dialog.FixtureRequest;
            Assert.True(history.FromHistory);
            Assert.Equal("", history.MediaId);
            Assert.Equal("", history.Season);
            Assert.Equal("", history.EpisodeFormat);
            Assert.Null(history.Problem);
            Assert.Equal(Visibility.Collapsed, dialog.IdentityGrid.Visibility);
            Assert.Equal(Visibility.Collapsed, dialog.TypeField.Visibility);
            dialog.FixtureFromHistory(false);
            Assert.Equal("1", dialog.FixtureRequest.MediaId);
            Assert.Equal("2", dialog.FixtureRequest.Season);
            Assert.Equal("24", dialog.FixtureRequest.Episode);
            Assert.Equal("第{ep}集", dialog.FixtureRequest.EpisodeFormat);
            Assert.Equal("-10", dialog.FixtureRequest.EpisodeOffset);
            Assert.Equal(Visibility.Visible, dialog.EpisodeField.Visibility);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：自动类型隐藏编号季集，切类型与来源清除旧身份", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture, series: true);
            Assert.Equal("", dialog.FixtureRequest.Type);
            Assert.Equal("", dialog.FixtureRequest.MediaId);
            Assert.Equal(Visibility.Collapsed, dialog.MediaIdPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, dialog.EpisodeField.Visibility);
            dialog.FixtureType(1);
            Assert.Equal("douban", dialog.FixtureRequest.MediaSource, "自动来源来自后端默认，而不是固定 TMDB");
            Assert.Equal(Visibility.Visible, dialog.MediaIdPanel.Visibility);
            dialog.MediaIdBox.Text = "123";
            dialog.FixtureSource(1);
            Assert.Equal("", dialog.FixtureRequest.MediaId);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：电视剧高级字段实际进入预览和入队请求", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture, series: true);
            dialog.FixtureType(2);
            dialog.FixtureSource(1);
            dialog.MediaIdBox.Text = "283938";
            dialog.EpisodeFormatBox.Text = "第{ep}集";
            dialog.EpisodeOffsetBox.Text = "EP*2";
            dialog.EpisodeGroupBox.Text = "group-1";
            dialog.FixtureEdited();
            TransferSubmissionFixture.Complete(dialog.FixturePreviewAsync());
            Assert.NotNull(dialog.FixturePreview);
            Assert.False(dialog.QueueButton.IsEnabled);
            Assert.Equal("第{ep}集", fixture.Transport.LastBody.GetProperty("episode_format").GetString());
            Assert.Equal("EP*2", fixture.Transport.LastBody.GetProperty("episode_offset").GetString());
            Assert.Equal("group-1", fixture.Transport.LastBody.GetProperty("episode_group").GetString());
            dialog.FixtureConfirm();
            Assert.True(dialog.QueueButton.IsEnabled);
            TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync(background: true));
            Assert.Equal(1, fixture.Transport.Writes);
            Assert.True(fixture.Transport.LastBackground);
            Assert.Equal("group-1", fixture.Transport.LastBody.GetProperty("episode_group").GetString());
            dialog.EmitClosed();
        });
        Test("手动整理窗口：修改表单撤销旧确认，取消窗口不写服务器", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture);
            TransferSubmissionFixture.Complete(dialog.FixturePreviewAsync());
            dialog.FixtureConfirm();
            Assert.True(dialog.ReorganizeButton.IsEnabled);
            dialog.PartBox.Text = "part1";
            dialog.FixtureEdited();
            Assert.False(dialog.ReorganizeButton.IsEnabled);
            Assert.False(dialog.QueueButton.IsEnabled);
            Assert.Null(dialog.FixturePreview);
            dialog.EmitClosed();
            Assert.Equal(0, fixture.Transport.Writes);
        });
        Test("手动整理窗口接线：网页两列顺序、固定操作、无折叠和当前文件", () =>
        {
            var root = RepositoryRoot();
            var document = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotReorganizeDialog.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
            foreach (var action in new[] { "PreviewButton", "QueueButton", "ReorganizeButton" })
                Assert.False(Named(action).Ancestors().Any(parent => parent.Name.LocalName == "ScrollViewer"));
            Assert.False(document.Descendants().Any(element => element.Name.LocalName == "Expander"));
            foreach (var removed in new[] { "SourcePathBox", "OriginalSourceBox", "AdvancedOptions", "BatchSelectionSummary" })
                Assert.False(document.Descendants().Any(element => (string?)element.Attribute(x + "Name") == removed));
            Assert.Equal("Collapsed", (string?)Named("HistoryLookupPanel").Attribute("Visibility"));
            foreach (var option in new[] { "TypeFolderToggle", "CategoryFolderToggle", "ScrapeToggle", "FromHistoryToggle" })
            {
                Assert.Equal("ToggleSwitch", Named(option).Name.LocalName);
                Assert.False(Named(option).Ancestors().Any(parent => parent.Name.LocalName == "Expander"));
            }
            Assert.Equal("None", (string?)document.Root!.Attribute("DefaultButton"));
            Assert.Equal("Stretch", (string?)document.Root!.Attribute("HorizontalContentAlignment"));
            Assert.False(document.Descendants().Any(element => ((string?)element.Attribute(x + "Name"))?.Contains("Cue", StringComparison.OrdinalIgnoreCase) == true));
            foreach (var grid in new[] { "DestinationGrid", "RecognitionGrid", "IdentityGrid", "FileOptionsGrid", "FolderOptionsGrid" })
            {
                var definitions = Named(grid).Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions");
                Assert.Equal(2, definitions.Elements().Count(definition => definition.Name.LocalName == "ColumnDefinition"));
            }
            var blocks = Named("DestinationGrid").Parent!.Elements()
                .Select(element => (string?)element.Attribute(x + "Name")).Where(name => name is not null).ToArray();
            Assert.True(blocks.SequenceEqual(new[] { "HistoryLookupPanel", "DestinationGrid", "TargetPathPanel", "RecognitionGrid", "IdentityGrid", "FileOptionsGrid", "FolderOptionsGrid" }));
            var options = Named("FolderOptionsGrid").Elements().Where(element => element.Name.LocalName == "StackPanel")
                .Select(element => (string?)element.Attribute(x + "Name")).ToArray();
            Assert.True(options.SequenceEqual(new[] { "TypeFolderField", "CategoryFolderField", "ScrapeField", "FromHistoryField" }));
            var width = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "ContentDialogMaxWidth");
            var height = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "ContentDialogMaxHeight");
            Assert.Equal(720d, double.Parse(width.Value));
            Assert.True(double.Parse(height.Value) >= 800, "弹窗高度没有放大");
        });
    }

    private static MoviePilotReorganizeDialog Load(TransferSubmissionFixture fixture, bool series = false)
    {
        var context = new MoviePilotTransferContext("Original", series ? MoviePilotTransferRequest.TypeSeries : MoviePilotTransferRequest.TypeMovie,
            "1", series ? 2 : null, series ? "24" : "", [new("source", "/library/original.mkv")]);
        var dialog = new MoviePilotReorganizeDialog(fixture.Service, context);
        Assert.False(dialog.PreviewButton.IsEnabled, "原记录尚未加载时不应开放预览");
        Assert.False(dialog.ReorganizeButton.IsEnabled);
        TransferSubmissionFixture.Complete(dialog.FixtureLoadAsync());
        Assert.False(dialog.Problem.IsOpen, dialog.Problem.Message);
        return dialog;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) return directory.FullName;
        throw new AssertionException("需要当前工作树的对话框 XAML");
    }
}
