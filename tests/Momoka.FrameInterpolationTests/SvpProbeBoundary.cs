// Only absent WinUI focus/event endpoints; the commit/wait/assertion bodies are production source.
namespace Microsoft.UI.Xaml
{
    public enum FocusState { Unfocused, Programmatic }
    public sealed class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
}

namespace Microsoft.UI.Xaml.Input
{
    public static class FocusManager
    {
        internal static object? Current;
        public static object? GetFocusedElement(object root) => Current;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class TextBox
    {
        public object XamlRoot { get; } = new();
        public Func<bool> FocusRequested { get; set; } = () => true;
        private string _text = "";
        internal int TextWrites;
        public string Text { get => _text; set { _text = value; TextWrites++; } }
        private RoutedEventHandler? _lost;
        public event RoutedEventHandler LostFocus { add => _lost += value; remove => _lost -= value; }
        internal int LostSubscribers => _lost?.GetInvocationList().Length ?? 0;
        internal void RaiseLost() => _lost?.Invoke(this, new());
        public bool Focus(FocusState state) => FocusRequested();
    }

    public sealed class ToggleSwitch
    {
        public object XamlRoot { get; } = new();
        public Func<bool> FocusRequested { get; set; } = () => true;
        public bool Focus(FocusState state) => FocusRequested();
    }
}
