namespace Windows.System
{
    public enum VirtualKey { Escape, Enter }
}

namespace Microsoft.UI.Xaml.Input
{
    public sealed class KeyRoutedEventArgs(Windows.System.VirtualKey key)
    {
        public Windows.System.VirtualKey Key { get; } = key;
        public bool Handled { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class CommandBar
    {
        public bool IsOpen { get; set; }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class LibraryPage
    {
        private bool _selecting;
        private bool _batchBusy;
        private CancellationTokenSource? _batchCancellation;
        public List<object> XamlRoot { get; } = [];
        public Microsoft.UI.Xaml.Controls.CommandBar LibraryBatchCommandBar { get; } = new();
        public int SelectionEnds { get; private set; }
        private void EndSelection() { SelectionEnds++; _selecting = false; }
        internal void SetSelection(bool selecting, CancellationTokenSource? cancellation = null)
        {
            _selecting = selecting;
            _batchBusy = cancellation is not null;
            _batchCancellation = cancellation;
        }
        internal Microsoft.UI.Xaml.Input.KeyRoutedEventArgs PressSelectionKey(Windows.System.VirtualKey key)
        {
            var args = new Microsoft.UI.Xaml.Input.KeyRoutedEventArgs(key);
            OnSelectionKeyDown(this, args);
            return args;
        }
    }
}
