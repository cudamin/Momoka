using System.Collections.Concurrent;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Tests;
using static Momoka.Tests.Program;

namespace Momoka.Tests
{
    public sealed partial class PlayerViewModel
    {
        private const string Category = "perf-image-fixture";
        private readonly EmbySession _session;
        private readonly EmbyImageStore _images;
        private readonly CancellationTokenSource _lifetime = new();
        private int _coverGeneration;
        private string? _coverBackdropItemId;
        private Task _coverBackdropLoad = Task.CompletedTask;
        internal VideoFrame? CoverBackdropFrame { get; private set; }
        internal Microsoft.UI.Xaml.Media.Imaging.BitmapImage? CoverBackdrop { get; private set; }
        internal Task Load(EmbyItem? item) => LoadCoverBackdropAsync(item);
        internal string? CurrentCoverId => _coverBackdropItemId;
        internal bool PendingOwner => _coverBackdropCancellation is not null;
        internal void EndLifetime() => _lifetime.Cancel();
        internal PlayerViewModel(StoreFixture fixture) { _session = fixture.Session; _images = fixture.Images; }
    }

    internal static class ControlledCoverDecoder
    {
        internal static Func<byte[], CancellationToken, Task<VideoFrame?>>? Read;
        internal static Task<VideoFrame?> DecodeAsync(byte[] bytes, Momoka.Theming.ThemeColor backdrop, CancellationToken token) =>
            Read is { } read ? read(bytes, token) : Task.FromResult<VideoFrame?>(Frame(bytes));
        internal static VideoFrame Frame(byte[] bytes) => new(16, 9, 64, new byte[16 * 9 * 4], new VideoPresentation.Rect(0, 0, 16, 9));
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public enum DecodePixelType { Physical, Logical }

    /// <summary>Inert UI-thread/size/source endpoint only; all caller control flow is verbatim production.</summary>
    public sealed class BitmapImage
    {
        internal static Func<byte[], Task>? Source;
        internal static int ExpectedThread;
        internal static int Created;
        internal static ConcurrentQueue<int> CreatedThreads { get; } = new();
        internal byte[] Bytes { get; private set; } = [];
        public DecodePixelType DecodePixelType { get; set; }
        public int DecodePixelWidth { get; set; }
        public int DecodePixelHeight { get; set; }

        public BitmapImage()
        {
            if (Environment.CurrentManagedThreadId != ExpectedThread) throw new AssertionException($"WinRT/XAML创建没有回UI上下文：{Environment.CurrentManagedThreadId} != {ExpectedThread}");
            Interlocked.Increment(ref Created);
            CreatedThreads.Enqueue(Environment.CurrentManagedThreadId);
        }

        public async Task SetSourceAsync(Windows.Storage.Streams.IRandomAccessStream stream)
        {
            if (Environment.CurrentManagedThreadId != ExpectedThread) throw new AssertionException("WinRT/XAML源设置没有回UI上下文");
            // The production MemoryStream WinRT adapter is readable but cannot clone. Read its current
            // input interface directly, just as SetSourceAsync may; no GetInputStreamAt/CloneStream.
            stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            Bytes = new byte[stream.Size];
            reader.ReadBytes(Bytes);
            if (Source is { } source) await source(Bytes);
        }
    }
}

namespace System
{
    internal static class InertSourceOperationExtensions
    {
        // Only the fake XAML SetSource endpoint returns Task rather than WinRT IAsyncAction.
        internal static Task AsTask(this Task task, CancellationToken token) => task.WaitAsync(token);
    }
}
