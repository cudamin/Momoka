// Only unused page controls/owners are boundaries. Release/OnNavigatedFrom/ApplySource/SearchSource
// come verbatim from the current production file at every build; the MoviePilot panel/VM are complete.
namespace Microsoft.UI.Xaml.Navigation
{
    public sealed class NavigationEventArgs : EventArgs;
    public enum NavigationCacheMode { Disabled, Required, Enabled }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.Navigation.NavigationCacheMode NavigationCacheMode { get; set; } = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        internal int Departures { get; private set; }
        protected virtual void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) { Departures++; }
    }
}
namespace Momoka.Shell.Views
{
    public sealed partial class LibraryPage
    {
        private bool _released;
        internal Momoka.Diagnostics.NavigationTimingRun? _timing;
        private Momoka.MoviePilot.MoviePilotService? _moviePilot;
        private Momoka.Services.ISettingsService? _settings = null; // Unused owner boundary; no saved settings in this fixture.
        private int _navigation;
        // 批选择（第三批多选）是另一份夹具的面，这里没有它的控件、命令与权限通道；Release 里那几句
        // 退订与收尾因此落在替身上：_session 永远为空，块体不执行，EndSelection 什么都不做。
        // 写成属性而不是字段：这份夹具从不为它赋值，字段会招来 CS0649，而「这里没有会话」正是要说的意思。
        private Momoka.Emby.EmbySession? _session => null;
        private void EndSelection() { }
        private void EndSelectionImmediately() { }
        // 这一份夹具不承载整墙动效；真实 Release 里的复位与淡入标记收在同名生产方法里。
        private void ResetWallReveal() { }
        private void OnBatchPermissionsChanged(object? sender, Momoka.Emby.EmbySessionScope source) { }
        private void OnBatchSourcesChanged(object? sender, EventArgs args) { }
        // Search never owns a library window anchor; real Release still clears these inactive slots.
        internal object? _pendingAnchor;
        internal double? _pendingScroll;
        internal bool _restoringAnchor;
        private readonly LibraryLifetimeRequest _request = new();
        internal LibraryLifetimeViewModel ViewModel { get; } = new();
        private MoviePilotSearchPanel? _moviePilotPanel;
        private PanelBoundary? _moviePilotResources;
        private PanelBoundary? _moviePilotSubtitles;
        private PanelBoundary? _aniBtPanel;
        private PickerBoundary? _filterPane;
        internal MoviePilotSearchPanel MoviePilotPanel => _moviePilotPanel ??= new();
        internal PanelBoundary MoviePilotResources => _moviePilotResources ??= new();
        internal PanelBoundary MoviePilotSubtitles => _moviePilotSubtitles ??= new();
        internal PanelBoundary AniBtPanel => _aniBtPanel ??= new();
        internal PickerBoundary SearchSitePicker { get; } = new();
        internal PickerBoundary FilterPane => _filterPane ??= new();
        private MoviePilotSearchPanel EnsureMoviePilotPanel() => MoviePilotPanel;
        private PanelBoundary EnsureMoviePilotResources() => MoviePilotResources;
        private PanelBoundary EnsureMoviePilotSubtitles() => MoviePilotSubtitles;
        private PanelBoundary EnsureAniBtPanel() => AniBtPanel;
        internal Microsoft.UI.Xaml.Controls.ComboBox SourceTabs { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement AniBtSourceTab { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement EmbyContent { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement ToolbarSurface { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement SettingsOverlay { get; } = new();
        // 浮层进出动画只属于真页面：这份夹具让 SurfaceMotion 的替身把进出直接落定，
        // 设置面板本身也是边界（Release 走「立即收起」，替身让它为空）。
        // 筛选那一对写成属性：提取的 Release 只读它们，字段会招来 CS0649。
        private SurfaceMotion? _filterScrimMotion => null;
        private SurfaceMotion? _filterMotion => null;
        private SurfaceMotion? _settingsScrimMotion;
        private SurfaceMotion? _settingsMotion;
        internal Microsoft.UI.Xaml.FrameworkElement FilterScrim { get; } = new();
        internal Microsoft.UI.Xaml.FrameworkElement SettingsScrim { get; } = new();
        private Microsoft.UI.Xaml.FrameworkElement? _settingsPane => null;
        internal Microsoft.UI.Xaml.FrameworkElement SearchUnavailableSurface { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox SearchUnavailableTitle { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox SearchUnavailableMessage { get; } = new();
        internal Microsoft.UI.Xaml.Controls.TextBox SearchInput { get; } = new();
        internal string? SavedSearchTerm => _request.SearchTerm;
        internal Momoka.Shell.ViewModels.MoviePilotSearchSnapshot? SavedMoviePilotSearch => _request.MoviePilotSearch;
        private double ContentScrollOffset => 73;
        internal int TypingCancellations { get; private set; }
        internal int HeaderUpdates { get; private set; }
        internal double SavedOffset => _request.ScrollOffset;
        internal int SavedCount => _request.LoadedCount;
        internal LibraryLifetimeRequest SavedSearch => _request;
        internal int Navigation => _navigation;
        private void CancelTyping() { TypingCancellations++; }
        private void ApplyHeaderLayout() { HeaderUpdates++; }
        private void RememberScrollAnchor(bool allowReleased = false) { }
        private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) { }
        private void OnAniBtSettingChanged(object? sender, EventArgs args) { }
        // 来源钉定的窗口只属于真页面：这份夹具没有来源标签的 LayoutUpdated，收手是空操作。
        private void StopSourcePin() { }
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
        public string? SearchTerm { get; set; }
        public int SearchSource { get; set; }
        public double MoviePilotScrollOffset { get; set; }
        public Momoka.Shell.ViewModels.MoviePilotSearchSnapshot? MoviePilotSearch { get; set; }
    }
    internal sealed class LibraryLifetimeViewModel
    {
        // This fixture owns search results, which never qualify for retained root navigation.
        public bool NavigationReuseEligible => false;
        public int WindowStart => 0;
        public Microsoft.UI.Xaml.Visibility MoviePilotTabsVisibility { get; set; } = Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility ToolbarVisibility { get; set; } = Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility ExtendedSearchCategoriesVisibility { get; set; } = Microsoft.UI.Xaml.Visibility.Collapsed;
        public List<object> Cards { get; } = [new(), new(), new()];
        public int LoadedCount => Cards.Count;
        public bool NoticeOpen { get; set; }
        public string SearchText { get; set; } = "";
        internal int Cancels { get; private set; }
        internal int PosterReleases { get; private set; }
        internal int Resumes { get; private set; }
        internal int EmbySourceChanges { get; private set; }
        public void Cancel() { Cancels++; }
        public void ReleaseSearchPosters() { PosterReleases++; }
        public void SetEmbySourceActive(bool active) { EmbySourceChanges++; }
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
        // 缓存预读属于真实 MoviePilotVersionsView；这一份边界只证明来源切换接上了它。
        internal System.Threading.Tasks.Task LoadCachedResultsAsync() => System.Threading.Tasks.Task.CompletedTask;
    }
    internal sealed class PickerBoundary
    {
        internal int Closes { get; private set; }
        public void ClosePopup() { Closes++; }
        public void Dismiss() { Closes++; }
    }
    /// <summary>这份夹具不播浮层动效：进出都当场落定，待落定的回调立即兑现。</summary>
    internal sealed class SurfaceMotion
    {
        internal SurfaceMotion(Microsoft.UI.Xaml.FrameworkElement element) { }
        internal bool IsAnimating => false;
        internal void Enter(int milliseconds, double offset) { }
        internal void Exit(int milliseconds, double offset, System.Action? landed = null) => landed?.Invoke();
        internal void Collapse() { }
        internal void Settle() { }
    }
}
