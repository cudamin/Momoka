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
                Assert.False(dialog.IsPrimaryButtonEnabled, "新预览必须重新明确确认");
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
