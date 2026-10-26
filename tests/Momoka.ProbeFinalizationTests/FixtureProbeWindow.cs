// Inert window boundary for the extracted probe entry point. Production closes the shortcut probe
// windows it opened at the end of the run; the fixture only needs the same call to compile.
namespace Momoka.Shell;

internal sealed class FixtureProbeWindow
{
    internal bool Closed { get; private set; }

    internal void Close() => Closed = true;
}
