using Momoka.Shell.Interop;

namespace Momoka.Diagnostics;

internal static class Log
{
    internal static void Info(string category, string message) => Native.State.Call("Info:" + message);
    internal static void Warn(string category, string message, Exception? error = null) => Native.State.Call("Warn:" + message);
}
