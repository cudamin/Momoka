using Microsoft.UI.Xaml;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class PosterCard
{
    /// <summary>Library navigation keeps its home caption; media on a backdrop keeps primary/secondary ink.</summary>
    internal static string CaptionState(bool onScrim, string? itemType) => !onScrim ? "CaptionPlain"
        : itemType == EmbyItemType.CollectionFolder ? "CaptionLibrary" : "CaptionOnScrim";

    // Both the surface flag and a recycled card's item can change the caption state.
    internal void ApplyCaptionStyles()
    {
        TitleLine.Style = Ink(OnScrim ? "EgOnScrimBodyStyle" : "EgBodyStyle");
        SubLine.Style = Ink(OnScrim ? "EgOnScrimDataStyle" : Card?.IsUpcoming == true ? "EgCaptionStyle" : "EgDataStyle");
        VisualStateManager.GoToState(this, CaptionState(OnScrim, Card?.Item.Type), false);

        static Style Ink(string key) => (Style)Application.Current.Resources[key];
    }
}
