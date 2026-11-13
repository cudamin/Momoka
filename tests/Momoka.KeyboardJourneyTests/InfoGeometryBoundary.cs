using Momoka.Shell.Views;

namespace Momoka.Shell;

internal static partial class ShellSelfCheck
{
    private static int _ticks;
    private static int _stage;
    private static bool _returned;
    private static bool _detailReloaded;
    private static int _infoLooks;
    private static bool _requiredEvidenceFailed;
    internal static bool InfoFixtureFailed => _requiredEvidenceFailed;
    internal static void BeginInfoFixture(Action<string> write)
    {
        _writeInfoGeometry = write;
        _infoGeometryFailure = null;
        _requiredEvidenceFailed = false;
        _ticks = 0;
        _stage = 2;
        _infoLooks = 0;
        InfoGeometryReadings.Clear();
    }
    internal static void ReadInfoFixture(DetailPage page, bool reloaded, bool returned)
    {
        _ticks++;
        _infoLooks++;
        _detailReloaded = reloaded;
        _returned = returned;
        RecordInfoGeometry(page);
    }
}
