using System.Reflection;
using Momoka.Diagnostics;
using Momoka.Tests;

Log.UseSink(NullLogSink.Instance);
if (args.Length == 2 && args[0] == "file-tail-child") return FileLogBudgetTests.WriteTailChild(args[1]);
var suite = args.Length == 0 ? "all" : args[0];
if (args.Length > 1 || suite is not ("all" or "budgets" or "websocket" or "logs" or "fonts" or "compatibility"))
{
    Console.Error.WriteLine("用法：all | budgets | websocket | logs | fonts | compatibility");
    return 2;
}

if (suite is "all" or "compatibility")
{
    DiagnosticsTests.Register();
    EmbyRealtimeTests.Register();
    // Run the existing anonymous parser/catalogue/search tests, not the machine font scan.
    foreach (var part in new[] { "RegisterParser", "RegisterCatalogue", "RegisterSearch" })
        typeof(FontTests).GetMethod(part, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
}
if (suite is "all" or "budgets" or "websocket") EmbyRealtimeTests.RegisterBudgets();
if (suite is "all" or "budgets" or "logs") FileLogBudgetTests.Register();
if (suite is "all" or "budgets" or "fonts") FontDecodeBudgetTests.Register();
return TestHarness.Run();
