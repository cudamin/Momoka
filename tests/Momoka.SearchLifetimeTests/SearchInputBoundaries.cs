using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Interop
{
    internal static class Native
    {
        internal static bool CtrlHeld { get; set; }
        internal static bool ShiftHeld { get; set; }
        internal static bool AltHeld { get; set; }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class LibraryPage
    {
        internal List<string> SearchSubmissions { get; } = [];
        internal List<string> SearchEdits { get; } = [];

        internal void FocusSearchFixture(TextBox field) => OnSearchInputGotFocus(this, new RoutedEventArgs { OriginalSource = field });

        internal void SubmitSearchFixture(string text) => OnSearchSubmitted(new AutoSuggestBox(), new() { QueryText = text });

        internal void EditSearchFixture(string text, AutoSuggestionBoxTextChangeReason reason, bool current = true) =>
            OnSearchTextChanged(new AutoSuggestBox { Text = text }, new() { Reason = reason, Current = current });

        private Task SubmitSearchAsync(string text)
        {
            SearchSubmissions.Add(text);
            return Task.CompletedTask;
        }

        private Task SearchTypingAsync(string text)
        {
            SearchEdits.Add(text);
            ViewModel.SearchText = text;
            return Task.CompletedTask;
        }
    }
}
