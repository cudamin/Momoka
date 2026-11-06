using System.Text;
using Momoka.Tests;

Console.OutputEncoding = Encoding.UTF8;
ShaderTransactionTests.Register();
ShaderCommandGenerationTests.Register();
ShaderBatchTests.Register();
NativeThreadDiscipline.Register();
SvpPlaybackTests.Register();
return TestHarness.Run();
