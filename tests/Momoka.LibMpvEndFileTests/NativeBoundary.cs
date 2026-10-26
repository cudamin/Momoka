namespace Momoka.Tests;

internal sealed class NativeBoundary
{
    internal List<string[]> Commands { get; } = [];
    internal List<string> Reads { get; } = [];
    internal List<string> Writes { get; } = [];
    internal Action<string[]>? OnCommand { get; set; }
    internal int DestroyCalls { get; set; }
    internal int QuitCalls => Commands.Count(command => command[0] == "quit");
    internal int LoadCalls => Commands.Count(command => command[0] == "loadfile");
}
