using Momoka.Tests;

namespace Microsoft.UI.Dispatching;

// Signature verified in the current Shell project metadata, not a real WinUI dispatcher.
public sealed class DispatcherQueue
{
    internal bool Accept { get; set; } = true;
    public bool TryEnqueue(Action callback)
    {
        if (!Accept) return false;
        if (SynchronizationContext.Current is not FixtureUiQueue queue)
            throw new InvalidOperationException("R-019 requires its isolated single-thread UI queue");
        queue.Post(_ => callback(), null);
        return true;
    }
}
