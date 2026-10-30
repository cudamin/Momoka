namespace Momoka.Tests;

internal sealed class NativeBoundary
{
    internal List<string[]> Commands { get; } = [];
    internal List<string> Reads { get; } = [];
    internal List<string> Writes { get; } = [];
    internal Action<string[]>? OnCommand { get; set; }
    internal Func<IntPtr>? WaitEvent { get; set; }
    internal Action<string, string>? OnWrite { get; set; }
    internal Action? OnDestroy { get; set; }
    internal List<(string Name, string Text)> WrittenValues { get; } = [];
    internal int DestroyCalls { get; set; }
    internal int QuitCalls => Commands.Count(command => command[0] == "quit");
    internal int LoadCalls => Commands.Count(command => command[0] == "loadfile");
}
