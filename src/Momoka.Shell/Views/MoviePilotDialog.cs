using Momoka.MoviePilot;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

internal static class MoviePilotDialog
{
    internal static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, MoviePilotService? service)
    {
        if (service is null) return await dialog.ShowAsync();
        if (!service.Available) return ContentDialogResult.None;
        var connection = service.ConnectionStamp;
        var dispatcher = dialog.DispatcherQueue;
        var invalidated = false;
        void Changed(object? sender, EventArgs args)
        {
            if (service.Available && service.IsCurrentConnection(connection)) return;
            invalidated = true;
            // 用托管回调包装 WinRT 方法；直接传 dialog.Hide 会在委托封送时抛 InvalidCastException。
            dispatcher.TryEnqueue(() => dialog.Hide());
        }
        service.AvailabilityChanged += Changed;
        try
        {
            if (!service.Available || !service.IsCurrentConnection(connection)) return ContentDialogResult.None;
            var result = await dialog.ShowAsync();
            return !invalidated && service.Available && service.IsCurrentConnection(connection) ? result : ContentDialogResult.None;
        }
        finally { service.AvailabilityChanged -= Changed; }
    }
}
