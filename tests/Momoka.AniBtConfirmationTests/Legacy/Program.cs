namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        // Only these anonymous AniBT suites. Never run shared Program/full Core/user mutex.
        AniBtTests.Register();
        AniBtIntegrationTests.Register();
        AniBtReceiptStateTests.Register();
        return TestHarness.Run();
    }
}
