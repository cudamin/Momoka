using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell;

namespace Momoka.Tests
{
    internal static partial class Program
    {
        private static void RegisterVerificationReads()
        {
            foreach (var foreign in new[] { false, true })
                Test("R17-006-screen-colors-foreign-" + foreign, () =>
                {
                    Native.Reset();
                    Native.ForeignSides = foreign;
                    var reading = VerificationReadsFixture.SampleScreen(new(), 120, 100);
                    Equal(!foreign, reading.Rendered, "foreign side colors must not establish our rendering");
                    return Task.CompletedTask;
                });
            Test("R17-006-invalid-pixels", () =>
            {
                Native.Reset();
                Native.InvalidPixels = true;
                Ensure(!VerificationReadsFixture.SampleScreen(new(), 120, 100).Rendered, "invalid pixel is no rendering evidence");
                return Task.CompletedTask;
            });
        }

        private static void RegisterPlacementRead()
        {
            Test("R17-008-directed-same-size-different-placement", () =>
            {
                Native.Reset();
                var settings = new AppSettings();
                settings.Ui.WindowWidth = 1200;
                settings.Ui.WindowHeight = 700;
                settings.Ui.WindowLeft = 300;
                settings.Ui.WindowTop = 300;
                var window = new ProbeHostWindow { Placement = (new WindowBounds(40, 40, 1240, 740), false) };
                var reading = VerificationReadsFixture.ReportRememberedWindow(window, settings, new(Paths("placement")) { Screen = 1 });
                Ensure(reading.Ok, "identical saved dimensions do not prove saved placement was used: " + reading.Detail);
                window.UsedSavedPlacement = true;
                Ensure(!VerificationReadsFixture.ReportRememberedWindow(window, settings, new(Paths("placement")) { Screen = 1 }).Ok,
                    "actual forbidden saved-placement adoption is rejected");
                return Task.CompletedTask;
            });
        }
    }
}

namespace Momoka.Shell
{
    internal struct NativePoint { internal int X; internal int Y; }
    internal struct NativeRect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
    internal static class Native
    {
        internal const uint GaRoot = 2;
        internal static bool ForeignSides { get; set; }
        internal static bool InvalidPixels { get; set; }
        internal static void Reset() { ForeignSides = false; InvalidPixels = false; }
        internal static bool ClientToScreen(IntPtr window, ref NativePoint point) => true;
        internal static IntPtr GetDC(IntPtr window) => new(1);
        internal static int ReleaseDC(IntPtr window, IntPtr dc) => 1;
        internal static uint GetPixel(IntPtr dc, int x, int y) => InvalidPixels ? uint.MaxValue : x == 24 ? 0xFFFFFFu : 0u;
        internal static IntPtr WindowFromPoint(NativePoint point) => ForeignSides && point.X == 24 ? new(3) : new(1);
        internal static IntPtr GetAncestor(IntPtr window, uint flags) => window;
        internal static int GetClassName(IntPtr window, char[] buffer, int length) { buffer[0] = 'W'; return 1; }
        internal static bool GetWindowRect(IntPtr window, out NativeRect rect)
        { rect = new() { Left = 40, Top = 40, Right = 1240, Bottom = 740 }; return true; }
    }

    internal static partial class VerificationReadsFixture
    {
        private static string ColorOf(string key) => "#FF000000";
    }
}
