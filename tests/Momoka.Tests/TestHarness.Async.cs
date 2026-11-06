namespace Momoka.Tests;

internal static partial class TestHarness
{
    // Only the main console suite compiles this bridge. Source-linked UI fixtures retain their
    // own completed-task guards/pumps and banned-API rules; do not inject blocking waits there.
    public static void Test(string name, Func<Task> run) =>
        Test(name, (Action)(() => run().GetAwaiter().GetResult()));
}
