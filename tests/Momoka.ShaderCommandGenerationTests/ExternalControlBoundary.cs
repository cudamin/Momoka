using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

// An ordinary IPlayerControl-only endpoint like the external-process backend contract.
// No process, pipe, server, native API or new scoped-control capability.
internal sealed class ExternalControlBoundary : IPlaybackHandle, IPlayerControl
{
    internal Dictionary<string, string> Values { get; } = ShaderGroupCatalog.NeutralOptions
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    internal List<string[]> Commands { get; } = [];
    internal bool Accepted { get; set; } = true;
    public bool HasControlChannel => true;
    public bool IsPaused => false;
    public PlayerStatus Status => new();
    public event Action<bool>? PauseChanged { add { } remove { } }
    public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
    public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Commands.Add([.. arguments]);
        if (Accepted && arguments.Count == 3 && arguments[0] == "set") Values[arguments[1]] = arguments[2];
        return Task.FromResult(Accepted);
    }
    public Task<string?> GetTextAsync(string name, CancellationToken token)
    {
        if (name == "profile-list") return Task.FromResult<string?>("[]");
        const string prefix = "option-info/";
        const string suffix = "/default-value";
        return Task.FromResult<string?>(name.StartsWith(prefix, StringComparison.Ordinal)
            ? ShaderGroupCatalog.NeutralOptions.First(pair => pair.Key == name[prefix.Length..^suffix.Length]).Value
            : Values.GetValueOrDefault(name));
    }
    public Task<IReadOnlyList<string>?> GetStringListAsync(string name, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<string>?>(Values[name].Length == 0 ? [] : [Values[name]]);
    public Task SetPropertyAsync(string name, object? value, CancellationToken token) => throw new AssertionException("外部夹具只走原命令入口");
    public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => throw new AssertionException("外部夹具禁止进程启动");
    public Task StopAsync() => Task.CompletedTask;
    public Task<double?> GetPositionAsync(CancellationToken token) => Task.FromResult<double?>(null);
    public Task ShowMessageAsync(string text) => Task.CompletedTask;
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
    public Task<double?> GetNumberAsync(string name, CancellationToken token) => Task.FromResult<double?>(null);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class ShaderCommandGenerationTests
{
    private static async Task ExternalControlAsync()
    {
        var settings = new AppSettings();
        var secrets = new DenySecrets();
        using var http = new DenyHttp();
        using var session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [settings, new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "r051-external|no-io")), secrets),
                new CredentialVault(secrets), new DeviceIdentity("r051", "r051", "r051", "0"), http], null)!;
        var service = new PlaybackService(session, settings, () => throw new AssertionException("禁止外部进程"),
            new PlaybackPlanner(settings, new ShaderGroupResolver(settings.Shaders)), allowPlayback: false);
        var handle = new ExternalControlBoundary();
        typeof(PlaybackService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, handle);
        Assert.False((object)handle is IFileScopedPlayerControl);
        Assert.True(await service.SetShaderGroupAsync(Group));
        Assert.Equal(8, handle.Commands.Count);
        Assert.True(await service.CommandAsync("seek", "-2.5", "relative"));
        Assert.Equal("seek|-2.5|relative", string.Join('|', handle.Commands[^1]));
        Assert.True(await service.SetShaderGroupAsync(null));
        Assert.Equal("", handle.Values["glsl-shaders"]);
        handle.Accepted = false;
        Assert.False(await service.CommandAsync("seek", "1", "relative"));
        Assert.Equal(0, http.Calls);
    }
}
