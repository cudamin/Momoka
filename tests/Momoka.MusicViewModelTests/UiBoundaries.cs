using Momoka.Emby;

// Only framework values, card image IO and navigation data are substitutes. The two VMs and their
// inherited loading/notice logic are linked production source, using the actual pinned generator.
namespace Windows.System
{
    public enum VirtualKey { Enter, Escape, Back, Control, Menu, LeftWindows, RightWindows }
}

namespace Windows.UI.Core
{
    public enum CoreVirtualKeyStates { None, Down }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum FocusState { Programmatic }
}

namespace Microsoft.UI.Input
{
    public static class InputKeyboardSource
    {
        public static HashSet<Windows.System.VirtualKey> KeysDown { get; } = [];
        public static Windows.UI.Core.CoreVirtualKeyStates GetKeyStateForCurrentThread(Windows.System.VirtualKey key) =>
            KeysDown.Contains(key) ? Windows.UI.Core.CoreVirtualKeyStates.Down : Windows.UI.Core.CoreVirtualKeyStates.None;
    }
}

namespace Microsoft.UI.Xaml.Input
{
    public sealed class KeyboardAccelerator;
    public sealed class KeyboardAcceleratorInvokedEventArgs
    {
        public bool Handled { get; set; }
    }
    public sealed class KeyRoutedEventArgs : EventArgs
    {
        public Windows.System.VirtualKey Key { get; set; }
        public bool Handled { get; set; }
        public object? OriginalSource { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public sealed class AutoSuggestBox
    {
        public bool IsLoaded { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        public bool AcceptFocus { get; set; } = true;
        public Microsoft.UI.Xaml.Visibility Visibility { get; set; }
        public int FocusCalls { get; private set; }
        public bool Focus(Microsoft.UI.Xaml.FocusState _) { FocusCalls++; return AcceptFocus; }
    }

    public sealed class AutoSuggestBoxQuerySubmittedEventArgs : EventArgs
    {
        public string? QueryText { get; init; }
    }

    public sealed class TextBox
    {
        public string Name { get; init; } = "TextBox";
        public string Text { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }
        public string SelectedText
        {
            get => Text.Substring(SelectionStart, SelectionLength);
            set
            {
                Text = Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, value);
                Select(SelectionStart + value.Length, 0);
            }
        }
        public void Select(int start, int length) => (SelectionStart, SelectionLength) = (start, length);
        public event Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? PreviewKeyDown;
        public event Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? KeyUp;
        public event Action<TextBox, TextCompositionStartedEventArgs>? TextCompositionStarted;
        public event Action<TextBox, TextCompositionEndedEventArgs>? TextCompositionEnded;
        public event Action<object, Microsoft.UI.Xaml.RoutedEventArgs>? LostFocus;
        internal void Press(Windows.System.VirtualKey key) => PreviewKeyDown?.Invoke(this, new() { Key = key });
        internal bool Release(Windows.System.VirtualKey key, object? originalSource = null)
        {
            var args = new Microsoft.UI.Xaml.Input.KeyRoutedEventArgs { Key = key, OriginalSource = originalSource ?? this };
            KeyUp?.Invoke(this, args);
            return args.Handled;
        }
        internal void BeginComposition() => TextCompositionStarted?.Invoke(this, new());
        internal void EndComposition() => TextCompositionEnded?.Invoke(this, new());
        internal void Blur() => LostFocus?.Invoke(this, new());
    }

    public sealed class TextCompositionStartedEventArgs : EventArgs;
    public sealed class TextCompositionEndedEventArgs : EventArgs;
}

namespace Momoka.Shell.Platform
{
    public interface IUiDispatcher
    {
        bool Post(Action work);
        void Run(Action work);
    }
}

namespace Momoka.Shell.Views
{
    public sealed class CardItem
    {
        public CardItem(EmbyItem item, EmbyImageStore images, int width, double aspect = 1,
            string? subtitle = null, bool indicators = true)
        {
            Item = item;
            Width = width;
            Subtitle = subtitle;
            Indicators = indicators;
        }
        public EmbyItem Item { get; }
        public int Width { get; }
        public string? Subtitle { get; }
        public bool Indicators { get; }
        public bool Abandoned { get; private set; }
        public bool IsPlaying { get; set; }
        public void ClaimCard(object owner) { }
        public void AbandonPoster() => Abandoned = true;
        public Task EnsurePosterAsync(Task? readiness = null) => Task.CompletedTask;
    }

    internal sealed record LibraryRequest
    {
        public required IServiceProvider Services { get; init; }
        public required string Title { get; init; }
        public EmbySessionScope? Source { get; init; }
        public string? ParentId { get; init; }
        public string? MusicRootId { get; init; }
        public string? MusicContainerType { get; init; }
        public string? SearchTerm { get; init; }
    }
}
