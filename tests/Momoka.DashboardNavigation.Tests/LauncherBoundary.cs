namespace Momoka.Shell.Platform;

internal interface ISystemLauncher
{
    void OpenUrl(string url);
}

internal sealed class ForbiddenLauncher : ISystemLauncher
{
    public void OpenUrl(string url) => throw new InvalidOperationException("R-019 must not launch a browser");
}
