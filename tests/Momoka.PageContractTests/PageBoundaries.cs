using Momoka.MoviePilot;

// These are deliberately tiny framework boundaries for source-linked control flow, not WinUI fakes
// claiming rendered layout/input coverage. Nothing creates a window, contacts a server or reads config.
namespace Microsoft.UI.Xaml
{
    public class FrameworkElement : DependencyObject
    {
        public object? Tag { get; set; }
        public object? DataContext { get; set; }
        public double ActualHeight { get; set; } = 100;
        public double Y { get; set; }
        public List<DependencyObject> Children { get; } = [];
        public LibraryAnchorTransform TransformToVisual(FrameworkElement? _) => new(Y);
    }
    public sealed class LibraryAnchorTransform(double y)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point _) => new(0, y);
    }
    public class RoutedEventArgs : EventArgs;
    public enum Visibility { Visible, Collapsed }
    public sealed class Application
    {
        public static Application? Current => null;
        public Dictionary<string, object> Resources { get; } = [];
    }
}

namespace Microsoft.UI.Xaml.Navigation
{
    public sealed class NavigationEventArgs(object? parameter) : EventArgs
    {
        public object? Parameter { get; } = parameter;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        protected virtual void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) { }
        protected virtual void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) { }
    }
}

namespace Momoka.Shell.ViewModels
{
    public sealed class MoviePilotSubscriptionsViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string? PageCategory { get; private set; }
        internal void UsePageCategory(string key) => PageCategory = key;
        public int Reloads { get; private set; }
        public int Cancels { get; private set; }
        public bool Busy => false;
        public bool Working => false;
        public bool NoticeOpen => false;
        public double RowHeight => 300;
        public Microsoft.UI.Xaml.Visibility Visible => Microsoft.UI.Xaml.Visibility.Visible;
        public List<MoviePilotSubscriptionCard> Movies { get; } = [];
        public List<MoviePilotSubscriptionCard> Series { get; } = [];
        public void Attach(MoviePilotService _) { }
        public void Cancel() => Cancels++;
        public void RefreshAvailability() => PropertyChanged?.Invoke(this, new(nameof(Visible)));
        public bool Owns(MoviePilotSubscriptionCard card) => Movies.Contains(card) || Series.Contains(card);
        public Task ReloadAsync() { Reloads++; return Task.CompletedTask; }
    }
    public sealed class MoviePilotSubscriptionCard(MoviePilotSubscription subscription)
    {
        public MoviePilotSubscription Subscription { get; } = subscription;
    }
}

namespace Momoka.Shell.Views
{
    internal sealed class MoviePilotSubscriptionsView
    {
        public ViewModels.MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
        public int Attaches { get; private set; }
        public int Detaches { get; private set; }
        public int Releases { get; private set; }
        public string? RowKey { get; private set; }
        public void AttachRow(MoviePilotHomeSubscriptions owner, object row, MoviePilotService service,
            Action<MoviePilotSubscription> open)
        {
            Attaches++;
            RowKey = ((MoviePilotHomeRow)row).Key;
        }
        public void DetachRow() => Detaches++;
        public void Release() => Releases++;
    }

    public sealed partial class MoviePilotSubscriptionsPage
    {
        internal sealed class TextBoundary { public string Text { get; set; } = ""; }
        internal sealed class BindingBoundary
        {
            public int Updates { get; private set; }
            public void Update() => Updates++;
        }
        private readonly MoviePilotSubscriptionsView Subscriptions = new();
        private readonly TextBoundary PageTitle = new();
        private readonly TextBoundary EmptyText = new();
        private readonly BindingBoundary Bindings = new();
        private void InitializeComponent() { }

        internal string ContractTitle => PageTitle.Text;
        internal string ContractEmptyText => EmptyText.Text;
        internal string? ContractRowKey => Subscriptions.RowKey;
        internal string? ContractPageCategory => _owner?.ViewModel.PageCategory;
        internal int ContractAttaches => Subscriptions.Attaches;
        internal int ContractDetaches => Subscriptions.Detaches;
        internal void ContractNavigateTo(object? request) => OnNavigatedTo(new(request));
        internal void ContractNavigateFrom() => OnNavigatedFrom(new(null));
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush;
}

namespace Momoka.Shell.Platform
{
    public interface IUiDispatcher
    {
        bool Post(Action work);
        void Run(Action work);
    }
    internal interface IClipboard { void SetText(string text); }
    internal interface ISystemLauncher
    {
        void OpenFolder(string path);
        void OpenUrl(string url);
    }
}

namespace Momoka.Shell.Views
{
    internal interface IShellActions
    {
        void Notify(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity);
    }
}
