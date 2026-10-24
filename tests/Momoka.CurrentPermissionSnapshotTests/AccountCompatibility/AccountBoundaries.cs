using System.Text.Json.Nodes;

namespace Momoka.Emby;

/// <summary>Only event values and anonymous acknowledgement. No production session/client or IO.</summary>
public sealed class EmbySession
{
    private EventHandler<EmbySessionScope>? _permissions;
    private EventHandler? _sourcesChanged;
    internal EmbySession()
    {
        Sources = [new(this, "anonymous-a"), new(this, "anonymous-b")];
        Default = Sources[0];
    }
    public IReadOnlyList<EmbySessionScope> Sources { get; }
    internal EmbySessionScope Default { get; set; }
    public bool IsSignedIn => Default.IsAvailable;
    public EmbySessionScope Capture() => Default;
    internal int PermissionAdds { get; private set; }
    internal int PermissionRemoves { get; private set; }
    internal int SourceAdds { get; private set; }
    internal int SourceRemoves { get; private set; }
    public event EventHandler<EmbySessionScope>? PermissionsChanged
    {
        add { PermissionAdds++; _permissions += value; }
        remove { PermissionRemoves++; _permissions -= value; }
    }
    public event EventHandler? SourcesChanged
    {
        add { SourceAdds++; _sourcesChanged += value; }
        remove { SourceRemoves++; _sourcesChanged -= value; }
    }
    internal void Permission(EmbySessionScope scope, bool? administrator)
    {
        scope.IsAdministrator = administrator;
        scope.PermissionRevision++;
        _permissions?.Invoke(this, scope);
    }
    internal void NotifySources() => _sourcesChanged?.Invoke(this, EventArgs.Empty);

    // Acknowledgement seam, never production ChangeFnNasPasswordAsync. No password hashing,
    // token update, credentials, remembered profile or settings IO exists in this assembly.
    public Task ChangeFnNasPasswordAsync(EmbySessionScope scope, string password, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        scope.ThrowIfNotAvailable();
        return scope.Endpoint.PasswordAsync(password, token);
    }
}

public sealed class AnonymousConnection
{
    public bool IsFnNas => true;
}
public sealed class AnonymousNativeConnection
{
    public Uri ApiBase { get; } = new("https://account-compatibility.invalid/v/");
}
public sealed class EmbySessionScope
{
    private readonly EmbySession _owner;
    private readonly EmbyClient _client;
    internal EmbySessionScope(EmbySession owner, string key)
    {
        _owner = owner;
        Key = key;
        Endpoint = new();
        _client = new(Endpoint);
    }
    public string Key { get; }
    public bool IsAvailable { get; internal set; } = true;
    public bool IsCurrent => IsAvailable && ReferenceEquals(_owner.Default, this);
    public bool? IsAdministrator { get; internal set; } = true;
    public long PermissionRevision { get; internal set; }
    public AnonymousConnection Connection { get; } = new();
    internal AnonymousEndpoint Endpoint { get; }
    public void ThrowIfNotAvailable()
    {
        if (!IsAvailable) throw new OperationCanceledException("Anonymous source revoked");
    }
    public void ThrowIfNotCurrent()
    {
        if (!IsCurrent) throw new OperationCanceledException("Anonymous default replaced");
    }
    public async Task ExecuteAsync(Func<EmbyClient, CancellationToken, Task> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfNotAvailable();
        await action(_client, token);
    }
    public async Task<T> ExecuteAsync<T>(Func<EmbyClient, CancellationToken, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ThrowIfNotAvailable();
        return await action(_client, token);
    }
}
public sealed class EmbyClient(AnonymousEndpoint endpoint)
{
    public AnonymousEndpoint FnNas { get; } = endpoint;
    public AnonymousNativeConnection FnNasConnection { get; } = new();
}
public sealed class AnonymousEndpoint
{
    internal List<AnonymousSubmission> Submissions { get; } = [];
    internal int Confirmed { get; private set; }
    internal Action? OnConfirmed { get; set; }
    internal int Reads { get; private set; }
    public Task<JsonObject> GetUserAsync(AnonymousNativeConnection connection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Reads++;
        return Task.FromResult(new JsonObject { ["lan"] = "zh-CN", ["audio_lan"] = "en-US" });
    }
    public Task<JsonObject> GetConfigurationAsync(Uri address, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Reads++;
        return Task.FromResult(new JsonObject { ["supported_languages"] = new JsonArray("zh-CN", "en-US", "ja-JP", "ko-KR") });
    }
    public Task UpdateUserInfoAsync(AnonymousNativeConnection connection, JsonObject body, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return SubmitAsync(new("preferences", null, (JsonObject)body.DeepClone(), token));
    }
    internal Task PasswordAsync(string password, CancellationToken token) => SubmitAsync(new("password", password, null, token));
    private async Task SubmitAsync(AnonymousSubmission submission)
    {
        Submissions.Add(submission);
        // Simulate a confirmed write even after the page leaves. The VM alone owns UI currency.
        await submission.Acknowledged.Task;
        Confirmed++;
        OnConfirmed?.Invoke();
    }
}
internal sealed record AnonymousSubmission(string Kind, string? Password, JsonObject? Body, CancellationToken Token)
{
    internal TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
