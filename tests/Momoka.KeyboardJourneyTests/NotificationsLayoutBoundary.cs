namespace Momoka.Shell.Views
{
    internal sealed class SettingsPage
    {
        internal NotificationsPage? Notifications { get; set; }
        internal int NextCalls { get; set; }
    }
    internal sealed class NotificationsPage
    {
        internal bool IsReady { get; set; }
        internal bool IsLoaded { get; set; } = true;
        internal double ActualWidth { get; set; } = 600;
        internal double ActualHeight { get; set; } = 700;
        internal (int Bound, int Drawn) Realised { get; set; }
        internal (bool Enabled, bool Bound) StopReport => (false, true);
        internal (bool Hidden, bool Follows) NoteState => (false, true);
        internal NotificationsVm ViewModel { get; } = new();
        internal int LayoutCalls { get; private set; }
        internal Action? OnLayout { get; set; }
        internal void UpdateLayout() { LayoutCalls++; OnLayout?.Invoke(); }
    }
    internal sealed class NotificationsVm
    {
        internal bool CanConfirm => true;
    }
}

namespace Momoka.Shell
{
    internal static partial class ShellSelfCheck
    {
        private static int _notificationsStep;
        private static NotificationsState? _notifications;
        private sealed record NotificationsState(bool Ready, int Bound, int Drawn, bool CanConfirm, bool StopReport, bool NoteHidden, bool NoteFollows);
        private static void OpenConsoleOrReturn(Views.SettingsPage page) => page.NextCalls++;
        internal static void BeginNotificationsFixture(Action<string> write)
        {
            _notificationsStep = 1;
            _notificationsLayoutWaits = 0;
            _notifications = null;
            _writeNotificationsLayout = write;
            _notificationsLayoutFailure = null;
            NotificationsLayoutReadings.Clear();
            _requiredEvidenceFailed = false;
        }
        internal static bool VisitNotificationsFixture(Views.SettingsPage page)
        {
            _ticks++;
            return VisitNotifications(page);
        }
        internal static (int Bound, int Drawn)? NotificationsFixtureReading => _notifications is null ? null : (_notifications.Bound, _notifications.Drawn);
        internal static int NotificationsFixtureWaits => _notificationsLayoutWaits;
    }
}
