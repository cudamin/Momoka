namespace Momoka.Shell;

/// <summary>Probe-only startup readiness; later position samples remain strict.</summary>
internal static class CompositionPositionReadiness
{
    internal static async Task<double> WaitAsync(Func<CancellationToken, Task<double?>> read,
        Func<bool> playbackEnded, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var budget = timeout ?? TimeSpan.FromSeconds(10);
        if (budget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (playbackEnded()) throw new InvalidOperationException("首个有效位置出现前播放已结束");
                var position = await read(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                if (playbackEnded()) throw new InvalidOperationException("首个有效位置出现前播放已结束");
                if (position is { } value && double.IsFinite(value) && value >= 0) return value;
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("等待首个有效播放位置超时");
        }
    }
}
