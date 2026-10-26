using System.Text;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly (string Name, Action<Fixture> Apply)[] OwnershipChanges =
    [
        ("Detach", f => f.Page.Detach()),
        ("Detach后同VM重新Attach", f => f.ReattachSameVm()),
        ("换VM", f => f.ReplaceVm()),
        ("A→B新handle换片", f => f.SwitchFile()),
        ("A→B同handle换片", f => f.SwitchFile(reuseHandle: true)),
        ("VM文件代次改变", f => f.Vm.ChangeVmGeneration()),
        ("仅后端文件代次改变", f => f.AdvancePlaybackGeneration()),
        ("起播意图改变", f => f.Vm.ChangeAttempt()),
        ("同ID条目对象换手", f => f.Vm.SeedItem(new EmbyItem { Id = "A", Name = "新的同ID条目" })),
        ("交互版本作废", f => f.Vm.ChangeInteractionVersion()),
        ("VM存活期取消", f => f.Vm.CancelLifetime()),
        ("页面Shutdown", f => f.Page.Shutdown())
    ];

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("R-045 实际生产OnPictureDrop／AddExternalSubtitlesAsync／InteractionContext，当前Core命令；纯内存、零UI／HTTP／配置／字幕文件IO／native／播放");
        Console.Write(ProductionManifest.Text);
        RegisterBoundaryMatrix();
        RegisterEntryAndReturnGuards();
        RegisterNormalBehavior();
        return TestHarness.Run();
    }

    private static void RegisterBoundaryMatrix()
    {
        foreach (var (name, change) in OwnershipChanges)
        {
            TestHarness.Test("R-045 WinRT读取挂起→" + name + "：旧拖拽零命令", () =>
            {
                using var f = new Fixture();
                var args = f.StartDrop();
                Assert.Equal(1, args.DataView.Reads);
                Assert.Equal(1, args.Deferrals);
                Assert.Equal(0, args.Deferral.Completions);
                change(f);
                f.CompleteData(args, new StorageFile("a.srt"), new StorageFile("a.ass"));
                f.CheckEnded(args);
                Assert.Equal(0, f.A.Commands.Count, "读取前的A拖拽不能恢复后借当前播放权");
                Assert.Equal(0, f.Current.CommandsFor("B"), "B必须收到零条旧拖拽命令");
                Assert.Equal(0, f.Notices.Count, "旧拖拽不能向新页面发成功／失败反馈");
            });
            TestHarness.Test("R-045 第1条命令挂起→" + name + "：停止余下多文件", () =>
            {
                using var f = new Fixture();
                var first = f.A.HoldNext();
                var args = f.StartDrop();
                f.CompleteData(args, new StorageFile("first.srt"), new StorageFile("second.ass"), new StorageFile("third.vtt"));
                Assert.Equal(1, f.A.Commands.Count, "第一条已在合法A上发送");
                Assert.Equal(0, args.Deferral.Completions, "必须等待VM处理，不得提前结束事务");
                change(f);
                f.Finish(first);
                f.CheckEnded(args);
                Assert.Equal(1, f.A.Commands.Count, "丢所有权后不能再发第二或第三条");
                Assert.Equal(0, f.Current.CommandsFor("B"), "B必须收到零条旧拖拽命令");
                Assert.Equal(0, f.Notices.Count, "晚到的A成功不能成为B的字幕成功提示");
            });
            TestHarness.Test("R-045 最后一条命令挂起→" + name + "：拒绝迟到成功反馈", () =>
            {
                using var f = new Fixture();
                var first = f.A.HoldNext();
                var last = f.A.HoldNext();
                var args = f.StartDrop();
                f.CompleteData(args, new StorageFile("first.srt"), new StorageFile("last.ass"));
                f.Finish(first);
                Assert.Equal(2, f.A.Commands.Count, "第二条也只在合法A上发送");
                change(f);
                f.Finish(last);
                f.CheckEnded(args);
                Assert.Equal(2, f.A.Commands.Count);
                Assert.Equal(0, f.Current.CommandsFor("B"));
                Assert.Equal(0, f.Notices.Count, "必须在每条命令后核对，不只在下一次循环前");
            });
        }
    }

    private static void RegisterEntryAndReturnGuards()
    {
        TestHarness.Test("R-045 Capture必须早于WinRT调用本身的换片回调", () =>
        {
            using var f = new Fixture();
            var data = new DataPackageView { BeforeRead = () => f.SwitchFile() };
            var args = f.StartDrop(data);
            f.CompleteData(args, new StorageFile("old.srt"));
            f.CheckEnded(args);
            Assert.Equal(0, f.Current.CommandsFor("B"));
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 WinRT返回失效后不再枚举StorageFile.Path", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            var pathReads = 0;
            f.SwitchFile();
            f.CompleteData(args, new StorageFile("old.srt", () => pathReads++));
            f.CheckEnded(args);
            Assert.Equal(0, pathReads, "应在返回后先拒绝，再读路径");
            Assert.Equal(0, f.Current.CommandsFor("B"));
        });
        TestHarness.Test("R-045 读取路径期间换片：交VM前再次拒绝", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("old.srt", () => f.SwitchFile(reuseHandle: true)));
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 读取路径期间换VM：不能借新VM重新Capture", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("old.srt", f.ReplaceVm));
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 已Detach入口：不读WinRT、不取deferral", () =>
        {
            using var f = new Fixture();
            f.Page.Detach();
            var args = f.StartDrop();
            Assert.Equal(0, args.DataView.Reads);
            Assert.Equal(0, args.Deferrals);
            Assert.Equal(0, f.Ui.Operations);
        });
        TestHarness.Test("R-045 非StorageItems入口：不读WinRT、不取deferral", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop(new DataPackageView { HasStorageItems = false });
            Assert.Equal(0, args.DataView.Reads);
            Assert.Equal(0, args.Deferrals);
            Assert.Equal(0, f.Ui.Operations);
        });
        TestHarness.Test("R-045 活跃外部画面入口不接管原生拖拽", () =>
        {
            using var f = new Fixture();
            f.A.InHost = false;
            var data = new DataPackageView();
            data.Complete(new StorageFile("external.srt"));
            var args = f.StartDrop(data);
            Assert.Equal(0, args.DataView.Reads);
            Assert.Equal(0, args.Deferrals);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 无会话独占设置不扩展拖拽适用性", () =>
        {
            using var f = new Fixture(playing: false);
            f.Settings.Settings.Mpv.Pipeline = VideoPipelineKind.Standalone;
            var data = new DataPackageView();
            data.Complete(new StorageFile("standalone.srt"));
            var args = f.StartDrop(data);
            Assert.Equal(0, args.DataView.Reads);
            Assert.Equal(0, args.Deferrals);
            Assert.Equal(0, f.Notices.Count);
        });
        foreach (var (name, change) in OwnershipChanges.Where(change => change.Name is not "Detach" and not "Detach后同VM重新Attach" and not "换VM"))
        {
            TestHarness.Test("R-045 VM入口拿旧Capture→" + name + "：拒绝重新借权", () =>
            {
                using var f = new Fixture();
                var captured = f.Vm.CaptureInteraction();
                change(f);
                var operation = f.StartVmAdd(["old.srt"], captured);
                f.Ui.Drain();
                Fixture.CheckTask(operation);
                Assert.Equal(0, f.A.Commands.Count);
                Assert.Equal(0, f.Current.CommandsFor("B"));
                Assert.Equal(0, f.Notices.Count);
            });
        }
        TestHarness.Test("R-045 VM每条命令前核页面drop predicate", () =>
        {
            using var f = new Fixture();
            var current = false;
            var operation = f.StartVmAdd(["old.srt"], f.Vm.CaptureInteraction(), () => current);
            f.Ui.Drain();
            Fixture.CheckTask(operation);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 VM每条命令后核页面drop predicate", () =>
        {
            using var f = new Fixture();
            var current = true;
            var first = f.A.HoldNext();
            var operation = f.StartVmAdd(["first.srt", "second.ass"], f.Vm.CaptureInteraction(), () => current);
            Assert.Equal(1, f.A.Commands.Count);
            current = false;
            f.Finish(first);
            Fixture.CheckTask(operation);
            Assert.Equal(1, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 VM路径过滤期间作废：过滤后仍须重核", () =>
        {
            using var f = new Fixture();
            var operation = f.StartVmAdd(new ChangingPaths(() => f.SwitchFile()), f.Vm.CaptureInteraction());
            f.Ui.Drain();
            Fixture.CheckTask(operation);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Current.CommandsFor("B"));
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 停止后迟到拒绝：不向当前播放发加载失败", () =>
        {
            using var f = new Fixture();
            var held = f.A.HoldNext();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("a.srt"), new StorageFile("b.ass"));
            f.StopFile();
            f.Finish(held, accepted: false);
            f.CheckEnded(args);
            Assert.Equal(1, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
    }

    private static void RegisterNormalBehavior()
    {
        TestHarness.Test("R-045 正常单字幕select／反馈／finally", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("ordinary.srt"));
            f.CheckEnded(args);
            Assert.Equal(1, f.A.Commands.Count);
            CheckCommand(f.A.Commands[0], "ordinary.srt");
            CheckNotice(f, InfoBarSeverity.Informational, "已加载字幕：ordinary.srt");
        });
        TestHarness.Test("R-045 正常多字幕顺序／最后成功选中", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("first.srt"), new StorageFile("second.ass"), new StorageFile("third.vtt"));
            f.CheckEnded(args);
            Assert.Equal("first.srt,second.ass,third.vtt", string.Join(',', f.A.Commands.Select(command => command.Arguments[1])));
            foreach (var command in f.A.Commands) CheckCommand(command, command.Arguments[1]);
            CheckNotice(f, InfoBarSeverity.Informational, "已加载 3 条字幕，当前：third.vtt");
        });
        TestHarness.Test("R-045 现有后缀过滤／去重／非文件及空路径不放宽", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("ignored.txt"), new StorageFolder(), new StorageFile(""),
                new StorageFile("FIRST.SRT"), new StorageFile("first.srt"), new StorageFile("ignored.mkv"),
                new StorageFile("second.webvtt"), new StorageFile("third.pgs"));
            f.CheckEnded(args);
            Assert.Equal("FIRST.SRT,second.webvtt,third.pgs", string.Join(',', f.A.Commands.Select(command => command.Arguments[1])));
            CheckNotice(f, InfoBarSeverity.Informational, "已加载 3 条字幕，当前：third.pgs");
        });
        TestHarness.Test("R-045 当前拖入不支持后缀保留必要警告", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("not-subtitle.txt"), new StorageFile("movie.mp4"));
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            CheckNotice(f, InfoBarSeverity.Warning, "拖进来的文件里没有字幕");
        });
        TestHarness.Test("R-045 WinRT仅文件夹／空路径：正常静默释放", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFolder(), new StorageFile(""));
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 当前未开始播放保留提示", () =>
        {
            using var f = new Fixture(playing: false);
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("before-play.srt"));
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            CheckNotice(f, InfoBarSeverity.Warning, "先开始播放，再把字幕拖进来");
        });
        TestHarness.Test("R-045 同片首条失败仍尝试后两条", () =>
        {
            using var f = new Fixture();
            f.A.Answers.Enqueue(false);
            f.A.Answers.Enqueue(true);
            f.A.Answers.Enqueue(true);
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("refused.srt"), new StorageFile("ok.ass"), new StorageFile("last.vtt"));
            f.CheckEnded(args);
            Assert.Equal(3, f.A.Commands.Count);
            CheckNotice(f, InfoBarSeverity.Informational, "已加载 2 条字幕，当前：last.vtt");
        });
        TestHarness.Test("R-045 同片末条失败提示上一条成功", () =>
        {
            using var f = new Fixture();
            f.A.Answers.Enqueue(true);
            f.A.Answers.Enqueue(false);
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("ok.srt"), new StorageFile("refused.ass"));
            f.CheckEnded(args);
            Assert.Equal(2, f.A.Commands.Count);
            CheckNotice(f, InfoBarSeverity.Informational, "已加载字幕：ok.srt");
        });
        TestHarness.Test("R-045 同片全部失败保留加载错误", () =>
        {
            using var f = new Fixture();
            f.A.Answers.Enqueue(false);
            f.A.Answers.Enqueue(false);
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("first.srt"), new StorageFile("second.ass"));
            f.CheckEnded(args);
            Assert.Equal(2, f.A.Commands.Count);
            CheckNotice(f, InfoBarSeverity.Error, "字幕没能加载");
        });
        TestHarness.Test("R-045 同片命令异常由实际Core拒绝后继续／最终成功", () =>
        {
            using var f = new Fixture();
            f.A.ThrowNext = true;
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("failed.srt"), new StorageFile("ok.ass"));
            f.CheckEnded(args);
            Assert.Equal(2, f.A.Commands.Count);
            Assert.True(f.LogSink.Snapshot().Any(entry => entry.Message.Contains("执行 mpv 命令 sub-add", StringComparison.Ordinal)));
            CheckNotice(f, InfoBarSeverity.Informational, "已加载字幕：ok.ass");
        });
        TestHarness.Test("R-045 正常首条挂起：事务等到全部命令完成", () =>
        {
            using var f = new Fixture();
            var first = f.A.HoldNext();
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("first.srt"), new StorageFile("second.ass"));
            Assert.Equal(1, f.A.Commands.Count);
            Assert.Equal(0, args.Deferral.Completions);
            Assert.Equal(0, f.Notices.Count);
            f.Finish(first);
            f.CheckEnded(args);
            Assert.Equal(2, f.A.Commands.Count);
            CheckNotice(f, InfoBarSeverity.Informational, "已加载 2 条字幕，当前：second.ass");
        });
        TestHarness.Test("R-045 正常成功保持实际VM轨道通知刷新", () =>
        {
            using var f = new Fixture();
            f.A.AtDispatch = count => f.Vm.DeliverTracks(new PlaybackUpdate<IReadOnlyList<MpvTrack>>(f.Service.Generation,
                [new MpvTrack(count, "sub", null, "内存外挂", false, true) { External = true, ExternalFilename = "ok.srt" }]));
            var args = f.StartDrop();
            f.CompleteData(args, new StorageFile("ok.srt"));
            f.CheckEnded(args);
            Assert.Equal(1L, f.Vm.TracksRevision);
            Assert.Equal(1, f.Vm.Tracks.Count);
            Assert.Equal("ok.srt", f.Vm.Tracks[0].ExternalFilename);
            Assert.True(f.Vm.Tracks[0].Selected);
            CheckNotice(f, InfoBarSeverity.Informational, "已加载字幕：ok.srt");
        });
        TestHarness.Test("R-045 WinRT读取失败：日志及finally保留", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            args.DataView.Fail(new InvalidOperationException("匿名WinRT读取失败"));
            f.Ui.Drain();
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.True(f.LogSink.Snapshot().Any(entry => entry.Message.Contains("拖拽字幕失败", StringComparison.Ordinal)));
        });
        TestHarness.Test("R-045 WinRT取消：仍释放deferral不冒成功", () =>
        {
            using var f = new Fixture();
            var args = f.StartDrop();
            args.DataView.Fail(new OperationCanceledException("匿名读取取消"));
            f.Ui.Drain();
            f.CheckEnded(args);
            Assert.Equal(0, f.A.Commands.Count);
            Assert.Equal(0, f.Notices.Count);
        });
        TestHarness.Test("R-045 正常StorageItems DragOver接受Copy与原说明", () =>
        {
            using var f = new Fixture();
            var args = new DragEventArgs(new DataPackageView());
            f.Page.DragOver(args);
            Assert.Equal(DataPackageOperation.Copy, args.AcceptedOperation);
            Assert.Equal("加载字幕", args.DragUIOverride.Caption);
            Assert.True(args.Handled);
            Assert.True(args.DragUIOverride.IsCaptionVisible && args.DragUIOverride.IsGlyphVisible);
        });
        TestHarness.Test("R-045 非StorageItems DragOver不接受", () =>
        {
            using var f = new Fixture();
            var args = new DragEventArgs(new DataPackageView { HasStorageItems = false });
            f.Page.DragOver(args);
            Assert.Equal(DataPackageOperation.None, args.AcceptedOperation);
            Assert.False(args.Handled);
        });
        TestHarness.Test("R-045 实际Detach后DragOver不接受", () =>
        {
            using var f = new Fixture();
            f.Page.Detach();
            var args = new DragEventArgs(new DataPackageView());
            f.Page.DragOver(args);
            Assert.Equal(DataPackageOperation.None, args.AcceptedOperation);
            Assert.False(args.Handled);
        });
    }

    private static void CheckCommand(RecordedCommand command, string path)
    {
        Assert.Equal("A", command.Owner);
        Assert.True(command.Arguments.SequenceEqual(["sub-add", path, "select"]));
    }
    private static void CheckNotice(Fixture f, InfoBarSeverity severity, string message)
    {
        Assert.Equal(1, f.Notices.Count);
        Assert.Equal(severity, f.Notices[0].Severity);
        Assert.Contains(message, f.Notices[0].Message);
    }
}

// A controlled external list enumerator, not a subtitle filter or playback ownership model.
internal sealed class ChangingPaths(Action onEnumerated) : IReadOnlyList<string>
{
    public int Count => 1;
    public string this[int index] => index == 0 ? "old.srt" : throw new ArgumentOutOfRangeException(nameof(index));
    public IEnumerator<string> GetEnumerator()
    {
        onEnumerated();
        yield return "old.srt";
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
