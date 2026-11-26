namespace Momoka.Shell.Platform;

/// <summary>Starts presentation before registering cancellation, including cancellation inside ShowAsync.</summary>
internal static class CancelableDialog
{
    internal static async Task<T> ShowAsync<T>(Func<Task<T>> show, Action hide, Action<Action> dispatch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var showing = show();
        var cancelled = 0;
        using var registration = token.Register(() =>
        {
            Interlocked.Exchange(ref cancelled, 1);
            dispatch(hide);
        });
        var result = await showing;
        if (Volatile.Read(ref cancelled) != 0) throw new OperationCanceledException(token);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
