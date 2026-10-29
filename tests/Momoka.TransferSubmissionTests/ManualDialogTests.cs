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
            dialog.EpisodeFormatBox.Text = "第{ep}集";
            dialog.EpisodeOffsetBox.Text = "-10";
            dialog.FixtureFromHistory(true);
            var history = dialog.FixtureRequest;
            Assert.True(history.FromHistory);
            Assert.Equal("", history.MediaId);
            Assert.Equal("", history.Season);
            Assert.Equal("", history.EpisodeFormat);
            Assert.Null(history.Problem);
            Assert.Equal(Visibility.Collapsed, dialog.IdentityPanel.Visibility);
            dialog.FixtureFromHistory(false);
            Assert.Equal("1", dialog.FixtureRequest.MediaId);
            Assert.Equal("2", dialog.FixtureRequest.Season);
            Assert.Equal("24", dialog.FixtureRequest.Episode);
            Assert.Equal("第{ep}集", dialog.FixtureRequest.EpisodeFormat);
            Assert.Equal("-10", dialog.FixtureRequest.EpisodeOffset);
            Assert.Equal(Visibility.Visible, dialog.EpisodeRow.Visibility);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：自动类型隐藏编号季集，切类型与来源清除旧身份", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture, series: true);
            dialog.FixtureType(0);
            Assert.Equal("", dialog.FixtureRequest.Type);
            Assert.Equal("", dialog.FixtureRequest.MediaId);
            Assert.Equal(Visibility.Collapsed, dialog.MediaIdPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, dialog.EpisodeRow.Visibility);
            dialog.FixtureType(1);
            Assert.Equal("douban", dialog.FixtureRequest.MediaSource, "自动来源来自后端默认，而不是固定 TMDB");
            dialog.MediaIdBox.Text = "123";
            dialog.FixtureSource(1);
            Assert.Equal("", dialog.FixtureRequest.MediaId);
            dialog.EmitClosed();
        });
        Test("手动整理窗口：电视剧高级字段实际进入预览和入队请求", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Load(fixture, series: true);
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
        Test("手动整理窗口接线：所有操作在滚动区外，历史可展开而选项不再深藏", () =>
        {
            var root = RepositoryRoot();
            var document = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotReorganizeDialog.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
            foreach (var action in new[] { "PreviewButton", "QueueButton", "ReorganizeButton" })
                Assert.False(Named(action).Ancestors().Any(parent => parent.Name.LocalName == "ScrollViewer"));
            Assert.Equal("Expander", Named("AdvancedOptions").Name.LocalName);
            foreach (var option in new[] { "TypeFolderToggle", "CategoryFolderToggle", "ScrapeToggle", "FromHistoryToggle" })
            {
                Assert.Equal("ToggleSwitch", Named(option).Name.LocalName);
                Assert.False(Named(option).Ancestors().Any(parent => parent.Name.LocalName == "Expander"));
            }
            Assert.Equal("None", (string?)document.Root!.Attribute("DefaultButton"));
            Assert.False(document.Descendants().Any(element => ((string?)element.Attribute(x + "Name"))?.Contains("Cue", StringComparison.OrdinalIgnoreCase) == true));
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
