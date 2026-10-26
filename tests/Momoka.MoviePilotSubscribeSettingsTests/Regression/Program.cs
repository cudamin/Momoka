namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("相关既有匿名 MoviePilot 只读/写夹具；不运行播放、配置或Shell探针");
        MoviePilotTests.Register();
        MoviePilotAvailabilityTests.Register();
        MoviePilotMediaDetailTests.Register();
        MoviePilotSubscriptionTests.Register();
        MoviePilotSafetyTests.Register();
        MoviePilotQuerySafetyTests.Register();
        return TestHarness.Run();
    }
}
