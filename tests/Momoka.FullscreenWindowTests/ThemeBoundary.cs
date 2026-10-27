namespace Momoka.Infrastructure;

// ConfigureTitleBar can attach its unchanged handler without constructing the app/theme runtime.
internal static class ThemeHost
{
    internal static event Action<object>? Changed;
    internal static void Notify() => Changed?.Invoke(new object());
}
