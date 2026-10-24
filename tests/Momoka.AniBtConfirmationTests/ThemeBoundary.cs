namespace Momoka.Shell;

internal static class ThemeHost
{
    internal static ThemeValue Current { get; } = new();
    internal sealed class ThemeValue
    {
        internal bool IsDark => true;
    }
}
