using System.Collections.Concurrent;
using Momoka.Tests;

// No WinUI/WinRT reference is loaded. These are only inert values, events and controlled awaits.
namespace Windows.Storage
{
    public interface IStorageItem;
    public sealed class StorageFile(string path, Action? onPathRead = null) : IStorageItem
    {
        public string Path
        {
            get
            {
                onPathRead?.Invoke();
                return path;
            }
        }
    }
    public sealed class StorageFolder : IStorageItem;
}

namespace Windows.ApplicationModel.DataTransfer
{
    public static class StandardDataFormats
    {
        public const string StorageItems = "StorageItems";
    }
    public enum DataPackageOperation { None, Copy }
    public sealed class DataPackageView
    {
        private readonly TaskCompletionSource<IReadOnlyList<Windows.Storage.IStorageItem>> _items = new();
        public bool HasStorageItems { get; init; } = true;
        public Action? BeforeRead { get; set; }
        public int Reads { get; private set; }
        public bool Contains(string format) => HasStorageItems && format == StandardDataFormats.StorageItems;
        public Task<IReadOnlyList<Windows.Storage.IStorageItem>> GetStorageItemsAsync()
        {
            Reads++;
            BeforeRead?.Invoke();
            return _items.Task;
        }
        public void Complete(params Windows.Storage.IStorageItem[] items) => _items.SetResult(items);
        public void Fail(Exception error) => _items.SetException(error);
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Microsoft.UI.Xaml
{
    public sealed class DragUIOverride
    {
        public string Caption { get; set; } = "";
        public bool IsCaptionVisible { get; set; }
        public bool IsGlyphVisible { get; set; }
    }
    public sealed class Deferral
    {
        public int Completions { get; private set; }
        public void Complete() => Completions++;
    }
    public sealed class DragEventArgs(Windows.ApplicationModel.DataTransfer.DataPackageView data)
    {
        public Windows.ApplicationModel.DataTransfer.DataPackageView DataView { get; } = data;
        public Windows.ApplicationModel.DataTransfer.DataPackageOperation AcceptedOperation { get; set; }
        public DragUIOverride DragUIOverride { get; } = new();
        public bool Handled { get; set; }
        public Deferral Deferral { get; } = new();
        public int Deferrals { get; private set; }
        public Deferral GetDeferral()
        {
            Deferrals++;
            return Deferral;
        }
    }
}

namespace Momoka.Shell.Interop
{
    public readonly record struct NativeRect;
    public sealed class CursorVisibilityEvents(IntPtr handle, Action callback) : IDisposable
    {
        public void Dispose()
        {
            GC.KeepAlive(handle);
            GC.KeepAlive(callback);
        }
    }
}

namespace Momoka.Shell.Windowing
{
    public sealed class HostWindow
    {
        public IntPtr Handle => IntPtr.Zero;
        public double RefreshHz() => 60;
        public event Action? GeometryChanged { add { } remove { } }
        public event Action<Momoka.Shell.Interop.NativeRect, Momoka.Shell.Interop.NativeRect>? ClientRectTransition { add { } remove { } }
        public event Action<Momoka.Shell.Interop.NativeRect>? ClientSizeChanged { add { } remove { } }
        public event Action<bool>? InteractiveResizeChanged { add { } remove { } }
        public void SetWin32Keys(object? sink) { }
    }
}

namespace Momoka.Tests
{
    // Async-void event continuations are deterministically delivered on one logical UI thread.
    // No sleeps, dispatcher window, real input or elapsed-time race is used for interleaving.
    internal sealed class TestUiQueue : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
        private int _operations;
        public int Operations => _operations;
        public List<Exception> Errors { get; } = [];
        public override void OperationStarted() => _operations++;
        public override void OperationCompleted() => _operations--;
        public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue((callback, state));
        public void Drain()
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                while (_pending.TryDequeue(out var item))
                {
                    try { item.Callback(item.State); }
                    catch (Exception error) { Errors.Add(error); }
                }
            }
            finally { SetSynchronizationContext(previous); }
        }
    }
}
