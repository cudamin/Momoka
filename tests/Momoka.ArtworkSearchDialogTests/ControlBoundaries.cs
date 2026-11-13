using System.Collections;
using System.Collections.Specialized;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public class RoutedEventArgs : EventArgs;

    public class FrameworkElement
    {
        public object? Tag { get; set; }
        public Visibility Visibility { get; set; } = Visibility.Visible;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class SelectionChangedEventArgs : Microsoft.UI.Xaml.RoutedEventArgs;
    public sealed class ContentDialogClosedEventArgs : EventArgs;

    public class ContentDialog : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Title { get; set; }
        public Microsoft.UI.Xaml.ElementTheme RequestedTheme { get; set; }
        public string PrimaryButtonText { get; set; } = "";
        public string CloseButtonText { get; set; } = "";
        public bool IsPrimaryButtonEnabled { get; set; } = true;
        public event Action<ContentDialog, ContentDialogClosedEventArgs>? Closed;
        public void Hide() => Closed?.Invoke(this, new());
    }

    // These boundaries dispatch only value-change events. No filtering, cancellation or UI policy.
    public class Selector : Microsoft.UI.Xaml.FrameworkElement
    {
        private IEnumerable? _items;
        private object? _selected;
        public event Action<object, SelectionChangedEventArgs>? SelectionChanged;

        public object? ItemsSource
        {
            get => _items;
            set
            {
                if (_items is INotifyCollectionChanged old) old.CollectionChanged -= OnItemsChanged;
                _items = value as IEnumerable;
                if (_items is INotifyCollectionChanged current) current.CollectionChanged += OnItemsChanged;
                OnItemsChanged(this, null);
            }
        }

        public object? SelectedItem
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                _selected = value;
                SelectionChanged?.Invoke(this, new());
            }
        }

        public int SelectedIndex
        {
            get => Array.IndexOf(Items, _selected);
            set => SelectedItem = value < 0 ? null : Items.ElementAtOrDefault(value);
        }

        internal object[] Items => _items?.Cast<object>().ToArray() ?? [];

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs? args)
        {
            if (_selected is not null && !Items.Contains(_selected)) SelectedItem = null;
        }
    }

    public sealed class ComboBox : Selector;
    public sealed class GridView : Selector { public double Height { get; set; } = 380; }

    public sealed class ToggleSwitch : Microsoft.UI.Xaml.FrameworkElement
    {
        private bool _isOn;
        public event Action<object, Microsoft.UI.Xaml.RoutedEventArgs>? Toggled;
        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                Toggled?.Invoke(this, new());
            }
        }
    }

    public sealed class TextBlock : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; }
    public sealed class ProgressBar : Microsoft.UI.Xaml.FrameworkElement;
    public sealed class Button : Microsoft.UI.Xaml.FrameworkElement
    {
        public bool IsEnabled { get; set; } = true;
        internal Action? Click { get; set; }
        internal void Invoke() => Click?.Invoke();
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging { public sealed class BitmapImage; }

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int width) =>
            Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(bytes.Length > 0 ? new() : null);
    }
}
