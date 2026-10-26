using System.Text;
using Momoka.Tests;

Console.OutputEncoding = Encoding.UTF8;
ShaderTransactionTests.Register();
ShaderCommandGenerationTests.Register();
return TestHarness.Run();
