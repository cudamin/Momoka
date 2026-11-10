namespace Momoka.Mpv;

/// <summary>Tracks only the host's screenshot commands; call inside the owning native API gate.</summary>
internal sealed class EachFrameScreenshot
{
    private string? _mode;

    internal bool Execute(IReadOnlyList<string> arguments, Func<string[], bool> send)
    {
        if (arguments.Count == 0 || arguments[0] != "screenshot") return send(arguments.ToArray());
        var flags = arguments.Count > 1 ? arguments[1] : "subtitles";
        var toggles = flags.Split('+').Contains("each-frame", StringComparer.Ordinal)
            || arguments.Count > 2 && arguments[2] == "each-frame";
        var wasRunning = _mode is not null;
        var success = send(arguments.ToArray());
        if (!toggles)
        {
            // mpv clears its repeating command before attempting a single-frame write, even on failure.
            _mode = null;
            return success;
        }
        if (wasRunning)
        {
            // An each-frame command while active only clears mpv's command; no extra image is written.
            if (success) _mode = null;
            return success;
        }
        _mode = flags.Replace("+each-frame", "", StringComparison.Ordinal);
        if (!success) Stop(send);
        return success;
    }

    internal bool Stop(Func<string[], bool> send)
    {
        if (_mode is not { } mode) return true;
        if (!send(["screenshot", mode + "+each-frame"])) return false;
        _mode = null;
        return true;
    }
}
