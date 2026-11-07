using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private readonly FrameworkElement _root = new();
    internal string? FailPendingCaptureMode { get; init; }
    internal int Captures { get; private set; }
    internal Task NotificationForFixture() => NotificationInFlightRecoveryAsync();
    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static T Get<T>(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (T)(target.GetType().GetField(name, flags)?.GetValue(target)
            ?? target.GetType().GetProperty(name, flags)!.GetValue(target))!;
    }
    private static Task LayoutAsync(FrameworkElement _) => Task.CompletedTask;
    private static Task SettleAsync() => Task.Delay(1);
    private static void ResizeProbeClient(int width, int height) { }
    private static void ResizeInspect(int width, int height) { }
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var count = 0; count < 100; count++) { if (condition()) return; await Task.Delay(1); }
        throw new TimeoutException("Notification probe failed to settle");
    }
    private Task SaveProbeScreenAsync(string name)
    {
        Captures++;
        return FailPendingCaptureMode is { } mode && name.Contains(mode + "-pending", StringComparison.Ordinal)
            ? Task.FromException(new IOException("anonymous pending capture failure")) : Task.CompletedTask;
    }
    private static Button MetadataButton(ContentDialog dialog, string name) => new()
    {
        IsEnabled = name == "CloseButton" || dialog.IsPrimaryButtonEnabled,
        Invoke = name == "CloseButton" ? dialog.Hide : dialog.RaisePrimary
    };
    private static void MetadataInvoke(Button button) { Require(button.IsEnabled, "Disabled control invoked"); button.Invoke!(); }
}

internal static class ProbeTaskBoundary
{
    internal static Task<T> AsTask<T>(this Task<T> task) => task;
}
