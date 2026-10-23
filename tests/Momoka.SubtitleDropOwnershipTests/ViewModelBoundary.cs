using System.ComponentModel;
using System.Reflection;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Tests;

namespace Momoka.Shell.ViewModels;

// Only constructor wiring, state setup and irrelevant UI events are substitutes.
// Drop execution, InteractionContext capture/currentness and OnTracksChanged are production nodes.
public sealed partial class PlayerViewModel
{
    internal PlayerViewModel(PlaybackService playback, ISettingsService settings)
    {
        _playback = playback;
        _settings = settings;
        _preparingPictureInHost = null;
    }
    internal bool CoverUp => false;
    internal Func<ShaderSurface>? MeasureSurface { get; set; }
    internal Func<double>? MeasureRefreshHz { get; set; }
    internal Func<Task>? PrepareStopAsync { get; set; }
    internal event Action? PlayerShown { add { } remove { } }
    internal event Action? PlayerHidden { add { } remove { } }
    internal event Action? PlaybackStarted { add { } remove { } }
    internal event Action? ChaptersChanged { add { } remove { } }
    internal event Action<PlayerStatus>? StatusApplied { add { } remove { } }
    internal event Action<double>? PictureAspectChanged { add { } remove { } }
    internal event Action<double>? SourceAspectChanged { add { } remove { } }
    internal event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    internal IReadOnlyList<MpvTrack> Tracks { get; private set; } = [];
    internal long TracksRevision => _tracksRevision;
    internal void Connect() { }
    internal void Shutdown() => _lifetime.Cancel();
    private static void OnUi(Action action) => action();
    internal void DeliverTracks(PlaybackUpdate<IReadOnlyList<MpvTrack>> update) => OnTracksChanged(update);
    internal void SeedItem(EmbyItem item) => _nowPlaying = item;
    internal void ChangeVmGeneration() => _generation++;
    internal void ChangeAttempt() => _playbackAttempt++;
    internal void ChangeInteractionVersion() => _interactionVersion++;
    internal void CancelLifetime() => _lifetime.Cancel();

    // Identical tests can run the baseline one-argument method and the repaired, explicitly owned
    // method. Signature selection is only a call adapter; it does not implement a guard or loop.
    internal Task AddCaptured(IReadOnlyList<string> paths, InteractionContext context, Func<bool> isCurrentDrop)
    {
        var methods = typeof(PlayerViewModel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.Name == "AddExternalSubtitlesAsync").ToArray();
        if (methods.Length != 1) throw new AssertionException("实际生产字幕入口缺失或不唯一");
        var method = methods[0];
        var parameters = method.GetParameters();
        object?[] arguments;
        if (parameters.Select(parameter => parameter.ParameterType).SequenceEqual([typeof(IReadOnlyList<string>)]))
            arguments = [paths];
        else if (parameters.Select(parameter => parameter.ParameterType)
            .SequenceEqual([typeof(IReadOnlyList<string>), typeof(InteractionContext), typeof(Func<bool>)]))
            arguments = [paths, context, isCurrentDrop];
        else throw new AssertionException("生产字幕入口签名改变；必须复核夹具边界，不能继续旧测试");
        try { return (Task)method.Invoke(this, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
