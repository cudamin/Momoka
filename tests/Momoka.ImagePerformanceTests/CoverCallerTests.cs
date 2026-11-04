using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using static Momoka.Tests.Program;

namespace Momoka.Tests;

internal static class CoverCallerTests
{
    private static void Reset(TestUiContext ui)
    {
        ControlledCoverDecoder.Read = null;
        BitmapImage.Source = null;
        BitmapImage.ExpectedThread = ui.ThreadId;
        BitmapImage.Created = 0;
        BitmapImage.CreatedThreads.Clear();
    }

    internal static void Register()
    {
        Test("生产遮罩调用方：同步缓存及后台解码尾正确回UI，尺寸两层预算一致", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            ControlledCoverDecoder.Read = (bytes, token) => Task.Run<Momoka.Playback.VideoFrame?>(async () =>
            {
                await Task.Delay(1, token).ConfigureAwait(false);
                Assert.False(Environment.CurrentManagedThreadId == ui.ThreadId);
                return ControlledCoverDecoder.Frame(bytes);
            }, token);
            await using var fixture = new StoreFixture();
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                await vm.Load(fixture.Item("cover"));
                Assert.NotNull(vm.CoverBackdrop);
                Assert.Equal(16, vm.CoverBackdrop!.DecodePixelWidth);
                Assert.Equal(9, vm.CoverBackdrop.DecodePixelHeight);
                Assert.Equal(DecodePixelType.Physical, vm.CoverBackdrop.DecodePixelType);
                Assert.Equal(vm.CoverBackdropFrame!.Width, vm.CoverBackdrop.DecodePixelWidth);
                Assert.Equal(vm.CoverBackdropFrame.Height, vm.CoverBackdrop.DecodePixelHeight);
                Assert.True(BitmapImage.CreatedThreads.All(thread => thread == ui.ThreadId));
                Assert.False(vm.PendingOwner);
                await vm.Load(fixture.Item("cover"));
                Assert.Equal(1, fixture.Handler.Total, "同条目去重");
            });
        });

        Test("生产遮罩调用方：快速换片取消旧下载，不回填及不清掉新owner", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            var stale = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (id, _, _) => id == "old" ? stale.Task : Task.FromResult(ImageHandler.Bytes([9]));
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                var old = vm.Load(fixture.Item("old"));
                await Eventually(() => fixture.Handler.Calls.ContainsKey("old"), "旧下载启动");
                await vm.Load(fixture.Item("new"));
                stale.SetResult(ImageHandler.Bytes([1]));
                await old.WaitAsync(Deadline);
                Assert.True(vm.CoverBackdrop!.Bytes.SequenceEqual(new byte[] { 9 }));
                Assert.Equal(fixture.Item("new").IdentityKey, vm.CurrentCoverId);
                Assert.Equal(1, BitmapImage.Created);
                Assert.False(vm.PendingOwner);
            });
        });

        Test("生产遮罩调用方：旧后台解码晚到或故障不能覆盖新图", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (id, _, _) => Task.FromResult(ImageHandler.Bytes(id == "old" ? new byte[] { 1 } : new byte[] { 9 }));
            var staleDecode = Pending<Momoka.Playback.VideoFrame?>();
            var decoding = Pending<bool>();
            ControlledCoverDecoder.Read = (bytes, _) =>
            {
                if (bytes[0] == 1) { decoding.SetResult(true); return staleDecode.Task; }
                return Task.FromResult<Momoka.Playback.VideoFrame?>(ControlledCoverDecoder.Frame(bytes));
            };
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                var old = vm.Load(fixture.Item("old"));
                await decoding.Task.WaitAsync(Deadline);
                await vm.Load(fixture.Item("new"));
                staleDecode.SetException(new IOException("old failure"));
                await old.WaitAsync(Deadline);
                Assert.True(vm.CoverBackdrop!.Bytes.SequenceEqual(new byte[] { 9 }));
                Assert.Equal(fixture.Item("new").IdentityKey, vm.CurrentCoverId);
                Assert.Equal(1, BitmapImage.Created);
            });
        });

        Test("生产遮罩调用方：旧WinRT源设置暂停期间换片，迟到尾不回填", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (id, _, _) => Task.FromResult(ImageHandler.Bytes(id == "old" ? new byte[] { 1 } : new byte[] { 9 }));
            var sourceStarted = Pending<bool>();
            var sourceRelease = Pending<bool>();
            BitmapImage.Source = bytes => { if (bytes[0] == 1) { sourceStarted.TrySetResult(true); return sourceRelease.Task; } return Task.CompletedTask; };
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                var old = vm.Load(fixture.Item("old"));
                await sourceStarted.Task.WaitAsync(Deadline);
                await vm.Load(fixture.Item("new"));
                sourceRelease.SetResult(true);
                await old.WaitAsync(Deadline);
                Assert.True(vm.CoverBackdrop!.Bytes.SequenceEqual(new byte[] { 9 }));
                Assert.Equal(fixture.Item("new").IdentityKey, vm.CurrentCoverId);
                Assert.Equal(2, BitmapImage.Created);
            });
        });

        Test("生产遮罩调用方：清空条目撤销解码并清两层，寿命取消阻止回填", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            var started = Pending<bool>();
            var decode = Pending<Momoka.Playback.VideoFrame?>();
            ControlledCoverDecoder.Read = (_, _) => { started.TrySetResult(true); return decode.Task; };
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                var old = vm.Load(fixture.Item("old"));
                await started.Task.WaitAsync(Deadline);
                await vm.Load(null);
                decode.TrySetResult(ControlledCoverDecoder.Frame([1]));
                await old.WaitAsync(Deadline);
                Assert.Null(vm.CoverBackdrop);
                Assert.Null(vm.CoverBackdropFrame);
                Assert.Null(vm.CurrentCoverId);
                Assert.False(vm.PendingOwner);
                ControlledCoverDecoder.Read = null;
                vm.EndLifetime();
                await vm.Load(fixture.Item("new"));
                Assert.Null(vm.CoverBackdrop);
                Assert.Null(vm.CoverBackdropFrame);
            });
        });

        Test("生产遮罩调用方：解码失败不启动原大图第二解码，可重新取图", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            ControlledCoverDecoder.Read = (_, _) => Task.FromResult<Momoka.Playback.VideoFrame?>(null);
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                await vm.Load(fixture.Item("bad"));
                Assert.Null(vm.CoverBackdrop);
                Assert.Null(vm.CurrentCoverId);
                Assert.Equal(0, BitmapImage.Created, "没有可信尺寸不得无约束XAML解码");
                ControlledCoverDecoder.Read = null;
                await vm.Load(fixture.Item("bad"));
                Assert.NotNull(vm.CoverBackdrop);
                Assert.Equal(1, BitmapImage.Created);
            });
        });

        Test("生产遮罩调用方：自有404后继承背景成功，不继续缩略图", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (id, _, _) => Task.FromResult(ImageHandler.Bytes([7], id == "parent" ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound));
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                var item = fixture.Item("episode");
                item.ParentBackdropItemId = "parent";
                item.ParentBackdropImageTags = ["parent-tag"];
                item.ImageTags[EmbyImageStore.Thumb] = "thumb-tag";
                await vm.Load(item);
                Assert.Equal("episode,parent", string.Join(',', fixture.Handler.Order));
                Assert.NotNull(vm.CoverBackdrop);
            });
        });

        Test("生产遮罩调用方：源身份取消撤销下载和填充，不借其他身份", async () =>
        {
            using var ui = new TestUiContext();
            Reset(ui);
            await using var fixture = new StoreFixture();
            var stale = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (_, _, _) => stale.Task;
            await ui.Run(async () =>
            {
                var vm = new PlayerViewModel(fixture);
                var load = vm.Load(fixture.Item("source"));
                await Eventually(() => fixture.Handler.Total == 1, "source开始");
                fixture.Scope.IdentityState.Lifetime.Cancel();
                stale.TrySetResult(ImageHandler.Bytes([8]));
                await load.WaitAsync(Deadline);
                Assert.Null(vm.CoverBackdrop);
                Assert.Null(vm.CoverBackdropFrame);
                Assert.Equal(0, BitmapImage.Created);
            });
        });
    }
}
