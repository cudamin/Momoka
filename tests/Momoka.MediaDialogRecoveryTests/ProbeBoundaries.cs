using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private readonly FrameworkElement _root = new() { XamlRoot = new() };
    internal bool FailPendingCapture { get; init; }
    internal int Captures { get; private set; }
    internal Task IdentifyForFixtureAsync() => IdentifyRecoveryAsync();
    internal Task FnNasForFixtureAsync() => FnNasMetadataRecoveryAsync();
    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message) => Momoka.Tests.Assert.True(condition, message);
    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static Task LayoutAsync(FrameworkElement element) => Task.CompletedTask;
    private static Task SettleAsync() => Task.Delay(1);
    private static async Task UntilAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (ready()) return;
            await Task.Delay(1);
        }
        throw new TimeoutException("Source-linked probe did not settle");
    }
    private Task SaveProbeScreenAsync(string name)
    {
        Captures++;
        return FailPendingCapture && name.Contains("pending", StringComparison.Ordinal)
            ? Task.FromException(new IOException("anonymous capture failure")) : Task.CompletedTask;
    }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement element) =>
        element is IdentifyDialog dialog ? dialog.Candidates.Select(dialog.PickButton) : [];
    private static Button MetadataButton(ContentDialog dialog, string name) => new()
    {
        Invoke = name == "CloseButton" ? dialog.Hide : () => ((FnNasMetadataDialog)dialog).Save()
    };
    private static void MetadataInvoke(Button button)
    {
        Require(button.IsEnabled, "Cannot invoke a disabled source-linked control");
        button.Invoke!();
    }
}

internal static class ProbeTaskBoundary
{
    // WinRT's AsTask is replaced only at the ShowAsync boundary; the probe body remains unchanged.
    internal static Task<T> AsTask<T>(this Task<T> task) => task;
}
