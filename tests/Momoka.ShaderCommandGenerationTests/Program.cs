using System.Text;
using Momoka.Tests;

Console.OutputEncoding = Encoding.UTF8;
ShaderTransactionTests.Register();
ShaderCommandGenerationTests.Register();
NativeThreadDiscipline.Register();
SvpPlaybackTests.Register();
return TestHarness.Run();
