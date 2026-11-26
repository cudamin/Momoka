namespace Momoka.Shell.Media;

/// <summary>Only the login window is substituted; the page's post-login check runs verbatim.</summary>
internal sealed class DoubanSession
{
    internal Func<CancellationToken, Task> Login { get; set; } = _ => Task.CompletedTask;
    internal Task LoginAsync(object owner, CancellationToken token) => Login(token);
    internal Task LogoutAsync(CancellationToken token) => Task.CompletedTask;
}
