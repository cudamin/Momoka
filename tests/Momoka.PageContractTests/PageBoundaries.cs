using Momoka.MoviePilot;

// These are deliberately tiny framework boundaries for source-linked control flow, not WinUI fakes
// claiming rendered layout/input coverage. Nothing creates a window, contacts a server or reads config.
namespace Microsoft.UI.Xaml
{
    public class FrameworkElement { public object? Tag { get; set; } }
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
    public sealed class MoviePilotSubscriptionsViewModel
    {
        public string? PageCategory { get; private set; }
        internal void UsePageCategory(string key) => PageCategory = key;
        public int Reloads { get; private set; }
        public Task ReloadAsync() { Reloads++; return Task.CompletedTask; }
    }
}

namespace Momoka.Shell.Views
{
    internal sealed class MoviePilotHomeSubscriptions
    {
        internal ViewModels.MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
        public object Movies { get; } = new();
        public object Series { get; } = new();
        public int Releases { get; private set; }
        public void Release() => Releases++;
    }

    internal sealed class MoviePilotSubscriptionsView
    {
        public ViewModels.MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
        public int Attaches { get; private set; }
        public int Detaches { get; private set; }
        public int Releases { get; private set; }
        public void AttachRow(MoviePilotHomeSubscriptions owner, object row, MoviePilotService service,
            Action<MoviePilotSubscription> open) => Attaches++;
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
