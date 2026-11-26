namespace Momoka.Shell;

internal static partial class ShellSelfCheck
{
    // Value-only endpoints for the verbatim ScheduleFor writer-binding block.
    private static Action<string>? _writePlaybackReturnTrace;
    private static Action<string>? _writeInfoGeometry;
    private static Action<Exception>? _infoGeometryFailure;
    private static readonly List<object> InfoGeometryReadings = [];
    private static int _notificationsLayoutWaits;
    private static Action<string>? _writeNotificationsLayout;
    private static Action<Exception>? _notificationsLayoutFailure;
    private static readonly List<object> NotificationsLayoutReadings = [];

    internal static int NotificationsLayoutWaitsForFixture => _notificationsLayoutWaits;

    internal static Action<string> EvidenceWriterForFixture(int index) => index switch
    {
        0 => _writePlaybackReturnTrace!,
        1 => _writeInfoGeometry!,
        2 => _writeNotificationsLayout!,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
