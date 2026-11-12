using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests
{
    internal static class SettingsSelfCheckReadTests
    {
        internal static void Register()
        {
            foreach (var supported in new[] { false, true })
                foreach (var contrast in new[] { false, true })
                    foreach (var effects in new[] { false, true })
                    {
                        var expectedAlpha = (byte)(supported && !contrast && effects ? 0x99 : 0xFF);
                        Test($"原样自检控制流：supported={supported}/HC={contrast}/effects={effects}", () =>
                        {
                            AppearanceFaults.Reset();
                            Microsoft.UI.Composition.SystemBackdrops.MicaController.Supported = supported;
                            AppearanceFaults.HighContrast = contrast;
                            AppearanceFaults.Effects = effects;
                            var good = Read(supported, expectedAlpha);
                            Assert.Equal("设置窗口底材", good.Name);
                            Assert.True(good.Passed, good.Detail);
                            Assert.Contains($"{expectedAlpha}/255", good.Detail);
                            // Re-run the actual production check with the wrong live reading. Do not
                            // compute a second implementation of SettingsBackdropCheck in this fixture.
                            var badAlpha = Read(supported, (byte)(expectedAlpha == 0x99 ? 0xFF : 0x99));
                            Assert.False(badAlpha.Passed, "真实caller没有拒绝错误画布alpha");
                            var badAssignment = Read(!supported, expectedAlpha);
                            Assert.False(badAssignment.Passed, "真实caller没有拒绝错误Mica赋值读数");
                            Assert.Equal(0, AppearanceFaults.Subscribers);
                        });
                    }
        }
        private static (string Name, bool Passed, string Detail) Read(bool assigned, byte alpha)
        {
            var calls = new List<(string, bool, string)>();
            var shell = new SettingsReadShell(assigned, alpha);
            SettingsSelfCheckReadFixture.Run(shell, (name, passed, detail) => calls.Add((name, passed, detail)));
            Assert.Equal(1, shell.Reads, "自检必须取得当前窗口读数");
            Assert.Equal(1, calls.Count, "原block必须只报告这一条判据");
            return calls.Single();
        }
    }
}
namespace Momoka.Shell
{
    internal sealed class SettingsReadShell(bool mica, byte alpha)
    {
        internal int Reads { get; private set; }
        internal (bool Mica, byte CanvasAlpha) SettingsBackdrop
        {
            get { Reads++; return (mica, alpha); }
        }
    }
}
namespace Microsoft.UI.Composition.SystemBackdrops
{
    // Current Mica support only; no controller or HWND is instantiated by this read-only fixture.
    public static class MicaController
    {
        public static bool Supported { get; set; }
        public static bool IsSupported() => Supported;
    }
}
