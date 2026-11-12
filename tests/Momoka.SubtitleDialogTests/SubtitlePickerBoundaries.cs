using Momoka.Emby;

namespace Momoka.Tests
{
    /// <summary>Only the WinRT IO endpoints are inert; each read guard runs in the real picker.</summary>
    internal static class SubtitlePickerBoundary
    {
        internal static Func<Task<ServerSubtitleFile?>> Pick { get; set; } = () => Task.FromResult<ServerSubtitleFile?>(null);
        internal static Action? AfterPick { get; set; }
        internal static Action? AfterProperties { get; set; }
        internal static Action? AfterOpen { get; set; }
        internal static Action? AfterLoad { get; set; }
        internal static Action? AfterRead { get; set; }
        internal static ulong? Size { get; set; }
        internal static uint? Loaded { get; set; }
        internal static int Picks { get; set; }
        internal static int Properties { get; set; }
        internal static int Opens { get; set; }
        internal static int Loads { get; set; }
        internal static int Reads { get; set; }
        internal static int StreamsDisposed { get; set; }
        internal static int ReadersDisposed { get; set; }

        internal static void Reset()
        {
            Pick = () => Task.FromResult<ServerSubtitleFile?>(null);
            AfterPick = AfterProperties = AfterOpen = AfterLoad = AfterRead = null;
            Size = null;
            Loaded = null;
            Picks = Properties = Opens = Loads = Reads = StreamsDisposed = ReadersDisposed = 0;
        }
    }
}

namespace Microsoft.UI
{
    public readonly record struct WindowId(ulong Value);
    public static class Win32Interop
    {
        public static nint GetWindowFromWindowId(WindowId value) => (nint)value.Value;
    }
}

namespace WinRT.Interop
{
    public static class InitializeWithWindow
    {
        public static void Initialize(object value, nint window) { }
    }
}

namespace Windows.Storage
{
    public sealed class StorageFile(string name, byte[] bytes)
    {
        public string Name { get; } = name;
        public Task<BasicProperties> GetBasicPropertiesAsync()
        {
            Momoka.Tests.SubtitlePickerBoundary.Properties++;
            Momoka.Tests.SubtitlePickerBoundary.AfterProperties?.Invoke();
            return Task.FromResult(new BasicProperties(Momoka.Tests.SubtitlePickerBoundary.Size ?? (ulong)bytes.Length));
        }
        public Task<Streams.RandomAccessStream> OpenReadAsync()
        {
            Momoka.Tests.SubtitlePickerBoundary.Opens++;
            Momoka.Tests.SubtitlePickerBoundary.AfterOpen?.Invoke();
            return Task.FromResult(new Streams.RandomAccessStream(bytes));
        }
    }
    public readonly record struct BasicProperties(ulong Size);
}

namespace Windows.Storage.Pickers
{
    public sealed class FileOpenPicker
    {
        public List<string> FileTypeFilter { get; } = [];
        public async Task<StorageFile?> PickSingleFileAsync()
        {
            Momoka.Tests.SubtitlePickerBoundary.Picks++;
            var file = await Momoka.Tests.SubtitlePickerBoundary.Pick();
            Momoka.Tests.SubtitlePickerBoundary.AfterPick?.Invoke();
            return file is null ? null : new StorageFile(file.FileName, Convert.FromBase64String(file.Data));
        }
    }
}

namespace Windows.Storage.Streams
{
    public sealed class RandomAccessStream(byte[] bytes) : IDisposable
    {
        internal byte[] Bytes { get; } = bytes;
        public ulong Size => Momoka.Tests.SubtitlePickerBoundary.Size ?? (ulong)Bytes.Length;
        public RandomAccessStream GetInputStreamAt(ulong offset) => this;
        public void Dispose() => Momoka.Tests.SubtitlePickerBoundary.StreamsDisposed++;
    }

    public sealed class DataReader(RandomAccessStream stream) : IDisposable
    {
        public Task<uint> LoadAsync(uint count)
        {
            Momoka.Tests.SubtitlePickerBoundary.Loads++;
            Momoka.Tests.SubtitlePickerBoundary.AfterLoad?.Invoke();
            return Task.FromResult(Momoka.Tests.SubtitlePickerBoundary.Loaded ?? (uint)stream.Bytes.Length);
        }
        public void ReadBytes(byte[] bytes)
        {
            Momoka.Tests.SubtitlePickerBoundary.Reads++;
            stream.Bytes.CopyTo(bytes, 0);
            Momoka.Tests.SubtitlePickerBoundary.AfterRead?.Invoke();
        }
        public void Dispose() => Momoka.Tests.SubtitlePickerBoundary.ReadersDisposed++;
    }
}
