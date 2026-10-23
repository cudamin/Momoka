namespace Momoka.Shell;

public static class ThemeHost
{
    public static ThemeValue Current { get; } = new();
    public sealed class ThemeValue
    {
        public bool IsDark => true;
    }
}
