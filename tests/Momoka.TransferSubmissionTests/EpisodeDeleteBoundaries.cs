using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Emby;
using Momoka.MoviePilot;

namespace Microsoft.UI.Xaml.Navigation
{
    public sealed class NavigatingCancelEventArgs : EventArgs;
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class ContentControl : FrameworkElement
    {
        public static object ContentProperty { get; } = new();
    }

    // Only the native Frame/event boundary is substituted; the linked Shell method owns cancellation.
    public sealed class Frame : FrameworkElement
    {
        private readonly Dictionary<long, Action<object, object>> _callbacks = [];
        private long _next;
        private object? _content = new object();
        public delegate void NavigatingHandler(object sender, NavigatingCancelEventArgs args);
        public event NavigatingHandler? Navigating;
        public int CallbackCount => _callbacks.Count;
        public int NavigationSubscribers => Navigating?.GetInvocationList().Length ?? 0;
        public object? Content
        {
            get => _content;
            set
            {
                if (ReferenceEquals(_content, value)) return;
                _content = value;
                foreach (var callback in _callbacks.Values.ToArray()) callback(this, ContentControl.ContentProperty);
            }
        }
        public long RegisterPropertyChangedCallback(object property, Action<object, object> callback)
        {
            _callbacks.Add(++_next, callback);
            return _next;
        }
        public void UnregisterPropertyChangedCallback(object property, long token) => _callbacks.Remove(token);
        internal void NavigateTo(object page)
        {
            Navigating?.Invoke(this, new());
            Content = page;
        }
    }
}

namespace Momoka.Shell.Views
{
    public interface IShellActions
    {
        void ShowMoviePilotHistoryAction(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action);
    }

    public sealed partial class ShellPage : FrameworkElement, IShellActions
    {
        private readonly MoviePilotService? _moviePilotService;
        private readonly EmbySession? _session;
        private ContentDialog? _moviePilotDialog;
        internal Frame ContentFrame { get; } = new();
        internal List<(string Message, InfoBarSeverity Severity)> Notices { get; } = [];
        internal bool HistoryShowing => _moviePilotHistoryShowing;
        internal bool HoldsDelete => _moviePilotEpisodeDelete is not null;
        internal ShellPage(MoviePilotService service, EmbySession session)
        {
            _moviePilotService = service;
            _session = session;
            XamlRoot = new();
        }
        internal Task InvokeHistory(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action) =>
            ShowMoviePilotHistoryActionAsync(scope, item, action);
        internal void StopForFixture() => _moviePilotEpisodeDelete?.Cancel();
        private void Notify(string message, InfoBarSeverity severity) => Notices.Add((message, severity));
        // 页面重读归外壳探针；这里只验证提交与防重，不让主页/详情页类型进入提取范围。
        private Task RefreshAfterMoviePilotLibraryDeleteAsync(EmbyItem item,
            IReadOnlyCollection<string> removedPaths, bool subjectRemoved) => Task.CompletedTask;
    }

    // Native dialog boundary only: observing cancellation must never submit a request.
    public sealed class MoviePilotMediaManagementDialog : ContentDialog
    {
        internal static int Constructions { get; private set; }
        internal bool LibraryFilesRemovalSubmitted { get; set; }
        internal IReadOnlyList<string> RemovedLibraryPaths { get; } = [];
        internal bool LibrarySubjectRemoved { get; }
        internal MoviePilotHistoryResult? CompletedResult { get; }
        internal MoviePilotMediaManagementDialog(MoviePilotService service, EmbySessionScope scope, EmbyItem item,
            MoviePilotHistoryAction action, Frame frame, EmbySession? sourceSession, MoviePilotHistoryPlan? preparedPlan = null,
            CancellationToken ownerLifetime = default)
        {
            Constructions++;
        }
        internal Task ShowAsync() => Task.CompletedTask;
    }
}

namespace System.Threading.Tasks
{
    internal static class FixtureTaskExtensions
    {
        internal static Task AsTask(this Task task) => task;
    }
}
