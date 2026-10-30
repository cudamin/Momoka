// Only unused page controls/owners are boundaries. Release/OnNavigatedFrom/ApplySource/SearchSource
// come verbatim from the current production file at every build; the MoviePilot panel/VM are complete.
namespace Microsoft.UI.Xaml.Navigation { public sealed class NavigationEventArgs : EventArgs; }
namespace Microsoft.UI.Xaml.Controls
{
    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        internal int Departures { get; private set; }
        protected virtual void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) { Departures++; }
    }
}
namespace Momoka.Shell.Views
{
    public sealed partial class LibraryPage
    {
        private bool _released;
        private Momoka.MoviePilot.MoviePilotService? _moviePilot;
        private Momoka.Services.ISettingsService? _settings = null; // Unused owner boundary; no saved settings in this fixture.
        private int _navigation;
        private int _searchPresentationVersion;
        private readonly LibraryLifetimeRequest _request = new();
        internal LibraryLifetimeViewModel ViewModel { get; } = new();
        internal MoviePilotSearchPanel MoviePilotPanel { get; } = new();
        internal PanelBoundary MoviePilotResources { get; } = new();
        internal PanelBoundary AniBtPanel { get; } = new();
        internal PickerBoundary SearchSitePicker { get; } = new();
        internal PickerBoundary FilterPane { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ComboBox SourceTabs { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement AniBtSourceTab { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement EmbyContent { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement ToolbarSurface { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox SearchInput { get; } = new();
        private double ContentScrollOffset => 73;
        internal int TypingCancellations { get; private set; }
        internal int ImeCancellations { get; private set; }
        internal int HeaderUpdates { get; private set; }
        internal double SavedOffset => _request.ScrollOffset;
        internal int SavedCount => _request.LoadedCount;
        internal int Navigation => _navigation;
        private void CancelTyping() { TypingCancellations++; }
        private void CancelImeSubmit() { ImeCancellations++; }
        private void ApplyHeaderLayout() { HeaderUpdates++; }
        private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) { }
        private void OnAniBtSettingChanged(object? sender, EventArgs args) { }
        internal void AttachFixture(Momoka.MoviePilot.MoviePilotService service)
        {
            if (_moviePilot is { } old) old.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
            _released = false;
            _moviePilot = service;
            service.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
            MoviePilotPanel.Attach(service, new Momoka.Tests.Launcher());
        }
        internal void SwitchFixture(int source) { SourceTabs.SelectedIndex = source; ApplySource(); }
        internal void NavigateFromFixture() => OnNavigatedFrom(new());
    }
    internal sealed class LibraryLifetimeRequest
    {
        public bool IsSearch { get; } = true;
        public double ScrollOffset { get; set; }
        public int LoadedCount { get; set; }
    }
    internal sealed class LibraryLifetimeViewModel
    {
        public Microsoft.UI.Xaml.Visibility MoviePilotTabsVisibility { get; set; } = Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility ToolbarVisibility { get; set; } = Microsoft.UI.Xaml.Visibility.Visible;
        public List<object> Cards { get; } = [new(), new(), new()];
        public bool NoticeOpen { get; set; }
        internal int Cancels { get; private set; }
        internal int PosterReleases { get; private set; }
        internal int Resumes { get; private set; }
        public void Cancel() { Cancels++; }
        public void ReleaseSearchPosters() { PosterReleases++; }
        public void Resume(bool reload)
        {
            if (reload) throw new InvalidOperationException("同页切来源不能自动读取");
            Resumes++;
        }
    }
    internal sealed class PanelBoundary : Microsoft.UI.Xaml.FrameworkElement
    {
        internal int Releases { get; private set; }
        public void Release() { Releases++; }
    }
    internal sealed class PickerBoundary
    {
        internal int Closes { get; private set; }
        public void ClosePopup() { Closes++; }
        public void Dismiss() { Closes++; }
    }
}
