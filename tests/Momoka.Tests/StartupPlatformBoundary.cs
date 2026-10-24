namespace Momoka.Shell.Platform;

/// <summary>
/// Namespace-local test endpoints shadow imported platform names in the complete linked source only.
/// No global alias changes other test suites, and neither endpoint can call registry/WinRT APIs.
/// </summary>
internal static class Registry
{
    internal static Momoka.Tests.MemoryStartupRegistry CurrentUser => Momoka.Tests.StartupRegistryBoundary.CurrentUser;
}

internal static class Windows
{
    internal static class ApplicationModel
    {
        internal static class Package
        {
            internal static object Current => Momoka.Tests.StartupRegistryBoundary.Packaged
                ? new object()
                : throw new InvalidOperationException("anonymous unpackaged fixture; no WinRT call");
        }
    }
}
