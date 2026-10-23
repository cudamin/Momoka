using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

// Only generated controls and handler invocation endpoints are substituted.
// Constructor, Apply, row values, selection checks, Hide and still flow are production.
public sealed partial class MissingEpisodesDialog
{
    private readonly ListView List = new();
    private readonly TextBlock Summary = new();
    private readonly ToggleSwitch SpecialsSwitch = new();
    private readonly ToggleSwitch UnairedSwitch = new();

    private void InitializeComponent()
    {
        SpecialsSwitch.Toggled += OnToggled;
        UnairedSwitch.Toggled += OnToggled;
    }

    internal IReadOnlyList<MissingEpisodeRow> Rows => _rows;
    internal object? ListSource => List.ItemsSource;
    internal Visibility ListVisibility => List.Visibility;
    internal string SummaryText => Summary.Text;
    internal void InvokeMoviePilot(EmbyItem episode) => OnSearchMoviePilot(new Button { Tag = episode }, new RoutedEventArgs());
    internal void InvokeAniBt(EmbyItem episode) => OnSearchAniBt(new Button { Tag = episode }, new RoutedEventArgs());
    internal void SetFilters(bool specials, bool unaired)
    {
        SpecialsSwitch.IsOn = specials;
        UnairedSwitch.IsOn = unaired;
    }
    internal Task<byte[]?> Fetch(ArtworkRef artwork) => _fetch(artwork, CancellationToken.None);
}
