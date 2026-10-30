using System.Text;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("整理提交界限：实际 Core 服务和源码链接 Dialog；无 WinUI、真实服务器或文件操作");
        MoviePilotTransferSubmissionTests.Register();
        ManualDialogTests.Register();
        foreach (var failure in new[] { "history-404", "history-json", "history-schema", "history-timeout", "history-500", "dependency-404" })
        {
            Test("真实对话框：只读失败恢复编辑／重新预览 " + failure, () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var preview = fixture.Preview();
                var dialog = Dialog(fixture, preview);
                fixture.Transport.Failure = failure;
                TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
                Assert.Equal(0, fixture.Transport.Writes);
                Assert.False(dialog.FixtureSubmitted);
                Assert.True(dialog.FormPanel.IsEnabled);
                Assert.True(dialog.PreviewButton.IsEnabled);
                Assert.Null(dialog.FixturePreview);
                Assert.False(preview.CanSubmit);
                Assert.Contains("没有提交整理请求", dialog.Problem.Message);
                Assert.DoesNotContain("可能已到达", dialog.Problem.Message);
                fixture.Transport.Failure = "";
                TransferSubmissionFixture.Complete(dialog.FixturePreviewAsync());
                Assert.NotNull(dialog.FixturePreview);
                Assert.True(dialog.FixturePreview!.CanSubmit);
                Assert.False(dialog.ReorganizeButton.IsEnabled, "新预览必须重新明确确认");
                dialog.ConfirmChanges.IsChecked = true;
                TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
                Assert.Equal(1, fixture.Transport.Writes);
                Assert.True(dialog.FixtureSubmitted);
            });
        }
        foreach (var failure in new[] { "write-lost", "write-canceled", "write-json" })
        {
            Test("真实对话框：写后不明保持锁定 " + failure, () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var dialog = Dialog(fixture, fixture.Preview());
                fixture.Transport.Failure = failure;
                TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
                Assert.Equal(1, fixture.Transport.Writes);
                Assert.True(dialog.FixtureSubmitted);
                Assert.False(dialog.FormPanel.IsEnabled);
                Assert.False(dialog.PreviewButton.IsEnabled);
                Assert.Contains("可能已到达", dialog.Problem.Message);
                TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
                Assert.Equal(1, fixture.Transport.Writes);
            });
        }
        foreach (var failure in new[] { "history-changed", "dependency-present" })
        {
            Test("真实对话框：核验明确阻止仍恢复但不重复写 " + failure, () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var dialog = Dialog(fixture, fixture.Preview());
                fixture.Transport.Failure = failure;
                TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
                Assert.Equal(0, fixture.Transport.Writes);
                Assert.False(dialog.FixtureSubmitted);
                Assert.True(dialog.FormPanel.IsEnabled);
                Assert.Null(dialog.FixturePreview);
            });
        }
        Test("真实对话框：关闭引发的只读取消不保留已提交锁", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Dialog(fixture, fixture.Preview());
            fixture.Transport.OnHistoryRead = dialog.EmitClosed;
            TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
            Assert.Equal(0, fixture.Transport.Writes);
            Assert.False(dialog.FixtureSubmitted);
            Assert.True(dialog.FormPanel.IsEnabled);
            Assert.True(dialog.PreviewButton.IsEnabled);
            Assert.Null(dialog.FixturePreview);
            Assert.Contains("没有提交整理请求", dialog.Problem.Message);
            Assert.DoesNotContain("可能已到达", dialog.Problem.Message);
        });
        Test("真实对话框：已发请求明确拒绝仍保守锁定，不冒称只读未发", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var dialog = Dialog(fixture, fixture.Preview());
            fixture.Transport.Failure = "write-rejected";
            TransferSubmissionFixture.Complete(dialog.FixtureSubmitAsync());
            Assert.Equal(1, fixture.Transport.Writes);
            Assert.True(dialog.FixtureSubmitted);
            Assert.False(dialog.FormPanel.IsEnabled);
            Assert.DoesNotContain("本次没有提交整理请求", dialog.Problem.Message);
        });
        Test("真实对话框：未传预选记录时保留单集历史查找，季集默认留空交给识别", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var context = new MoviePilotTransferContext("单集", MoviePilotTransferRequest.TypeSeries, "283938", 1, "4",
                [new MoviePilotTransferFile("S01E04.mkv", "/library/S01E04.mkv")]);
            var dialog = new MoviePilotReorganizeDialog(fixture.Service, context);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, dialog.HistoryLookupPanel.Visibility);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, dialog.EpisodeField.Visibility);
            Assert.Equal("", dialog.SeasonBox.Text);
            Assert.Equal("", dialog.EpisodeBox.Text);
            Assert.Equal("单集 · 共 1 项", dialog.ReorganizeSubtitle.Text);
            Assert.Equal("", dialog.FixtureRequest.Type);
            Assert.Equal(0, fixture.Transport.Writes);
        });
        Test("真实对话框：剧多选交接仅收起历史查找且没有写请求", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var selected = fixture.Preview().Request.Histories;
            var context = new MoviePilotTransferContext("剧多选", MoviePilotTransferRequest.TypeSeries, "283938", null, "",
                [new MoviePilotTransferFile("原源", "/downloads/source.mkv")]);
            var dialog = new MoviePilotReorganizeDialog(fixture.Service, context, selected);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, dialog.HistoryLookupPanel.Visibility);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, dialog.BatchSelectionSummary.Visibility);
            Assert.Contains("1 条整理记录", dialog.BatchSelectionSummary.Text);
            Assert.Equal("", dialog.SeasonBox.Text);
            Assert.Equal("", dialog.EpisodeBox.Text);
            Assert.False(dialog.IsPrimaryButtonEnabled);
            Assert.Equal(0, fixture.Transport.Writes);
        });
        return Run();
    }

    private static MoviePilotReorganizeDialog Dialog(TransferSubmissionFixture fixture, MoviePilotTransferPreview preview)
    {
        var request = preview.Request;
        var dialog = new MoviePilotReorganizeDialog(fixture.Service, new MoviePilotTransferContext(
            "Original", request.Type, request.MediaId, null, "", request.Files));
        dialog.FixturePrepare(preview);
        return dialog;
    }
}
