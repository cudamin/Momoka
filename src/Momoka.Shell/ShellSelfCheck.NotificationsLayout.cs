using System.Text.Json;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal static partial class ShellSelfCheck
{
    private static int _notificationsLayoutWaits;
    private static Action<string>? _writeNotificationsLayout;
    private static Action<Exception>? _notificationsLayoutFailure;
    private static readonly List<object> NotificationsLayoutReadings = [];

    private static void RecordNotificationsLayout(NotificationsPage page, int bound, int drawn)
    {
        if (_writeNotificationsLayout is not { } write) return;
        try
        {
            NotificationsLayoutReadings.Add(new
            {
                tick = _ticks,
                waits = _notificationsLayoutWaits,
                bound,
                drawn,
                page.IsLoaded,
                page.ActualWidth,
                page.ActualHeight
            });
            write(JsonSerializer.Serialize(NotificationsLayoutReadings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            _requiredEvidenceFailed = true;
            _notificationsLayoutFailure?.Invoke(error);
        }
    }
}
