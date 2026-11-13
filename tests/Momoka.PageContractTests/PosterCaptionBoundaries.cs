namespace Microsoft.UI.Xaml
{
    public sealed class Style(string key)
    {
        internal string Key { get; } = key;
    }
    internal static class VisualStateManager
    {
        internal static bool GoToState(Momoka.Shell.Views.PosterCard card, string state, bool transitions)
        {
            card.CaptionStateReading = state;
            return true;
        }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class PosterCard
    {
        internal sealed class CaptionText
        {
            internal Microsoft.UI.Xaml.Style? Style { get; set; }
        }
        private readonly CaptionText TitleLine = new();
        private readonly CaptionText SubLine = new();
        public bool OnScrim { get; set; }
        internal string CaptionStateReading { get; set; } = "";
        internal string? TitleStyleReading => TitleLine.Style?.Key;
        internal string? SubtitleStyleReading => SubLine.Style?.Key;
    }
}
