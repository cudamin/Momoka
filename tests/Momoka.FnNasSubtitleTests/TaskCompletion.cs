namespace Momoka.Tests;

internal static class TaskCompletion
{
    internal static T Finish<T>(this Task<T> task)
    {
        T result = default!;
        using var queue = new UiQueue();
        Exception? failure = null;
        queue.Run(async () =>
        {
            try { result = await task; }
            catch (Exception error) { failure = error; }
        });
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    internal static void Finish(this Task task)
    {
        using var queue = new UiQueue();
        Exception? failure = null;
        queue.Run(async () =>
        {
            try { await task; }
            catch (Exception error) { failure = error; }
        });
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
