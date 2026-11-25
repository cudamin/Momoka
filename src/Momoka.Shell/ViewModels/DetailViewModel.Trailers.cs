using Microsoft.UI.Xaml;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Shell.Platform;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    private ISystemLauncher? _trailerLauncher;
    private CancellationTokenSource? _trailerLoad;
    private TrailerSelection? _trailerSelection;
    private bool _trailersReady;
    private bool _trailersEmpty;

    public Visibility TrailerVisibility => Show(CanAct && _trailersReady && !_trailersEmpty
        && _detail is { } item && ItemTrailers.HasAny(item, _scope!.Connection.ServerKind));

    private void ResetTrailers()
    {
        _trailerSelection = null;
        _trailerLoad?.Cancel();
        _trailerLoad?.Dispose();
        _trailerLoad = null;
        _trailersReady = false;
        _trailersEmpty = false;
        OnPropertyChanged(nameof(TrailerVisibility));
        OnPropertyChanged(nameof(TailMinHeight));
        OnPropertyChanged(nameof(TrailerButtonVisibility));
    }

    internal sealed class TrailerOption(string title, EmbyItem? item = null, string? url = null)
    {
        public string Title { get; } = title;
        public EmbyItem? Item { get; } = item;
        public string? Url { get; } = url;
        public string? ItemId { get; } = item?.Id;
        public string Destination => Item is not null || TrailerPreview.YouTubeId(Url) is not null ? "在 Momoka 播放" : "在浏览器打开";
    }

    internal sealed class TrailerSelection(EmbyItem subject, EmbySessionScope source, CancellationToken token)
    {
        internal EmbyItem Subject { get; } = subject;
        internal EmbySessionScope Source { get; } = source;
        internal CancellationToken Token { get; } = token;
        public List<TrailerOption> Options { get; } = [];
        public string? Error { get; internal set; }
    }

    internal bool OwnsTrailers(TrailerSelection selection) => CanAct && _trailersReady
        && ReferenceEquals(_trailerSelection, selection) && !selection.Token.IsCancellationRequested
        && ReferenceEquals(_detail, selection.Subject) && ReferenceEquals(_scope, selection.Source)
        && selection.Source.IsAvailable;

    internal async Task<TrailerSelection?> GetTrailersAsync()
    {
        if (TrailerVisibility != Visibility.Visible || _detail is not { } subject || _scope is not { } scope) return null;
        _trailerLoad?.Cancel();
        _trailerLoad?.Dispose();
        _trailerLoad = new CancellationTokenSource();
        var selection = new TrailerSelection(subject, scope, _trailerLoad.Token);
        _trailerSelection = selection;
        var remote = ItemTrailers.Remote(subject);
        if (subject.LocalTrailerCount > 0)
        {
            try
            {
                var local = await scope.ExecuteAsync((client, ct) => client.GetLocalTrailersAsync(subject.Id, ct),
                    selection.Token).ConfigureAwait(true);
                if (!OwnsTrailers(selection)) return null;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in local)
                {
                    if (string.IsNullOrWhiteSpace(item.Id) || item.Id == subject.Id || !item.IsPlayable
                        || item.Source?.Key != scope.Key || !ids.Add(item.Id)) continue;
                    selection.Options.Add(new TrailerOption(string.IsNullOrWhiteSpace(item.Name) ? "预告片" : item.Name, item));
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception error)
            {
                if (!OwnsTrailers(selection)) return null;
                selection.Error = "读取服务器预告片失败：" + Failure.Describe(error);
            }
        }
        if (!OwnsTrailers(selection)) return null;
        foreach (var link in remote)
            selection.Options.Add(new TrailerOption(link.Name ?? "预告片", url: link.Url));
        _trailersEmpty = selection.Options.Count == 0 && selection.Error is null;
        if (_trailersEmpty) Notify("暂无预告片", "服务器未返回可用的预告片", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        OnPropertyChanged(nameof(TrailerVisibility));
        OnPropertyChanged(nameof(TailMinHeight));
        OnPropertyChanged(nameof(TrailerButtonVisibility));
        return selection;
    }

    internal async Task OpenTrailerAsync(TrailerSelection selection, TrailerOption option)
    {
        if (!OwnsTrailers(selection) || !selection.Options.Contains(option) || _actions is null) return;
        try
        {
            if (option.Item is { } item)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || item.Id != option.ItemId || item.Id == selection.Subject.Id
                    || !item.IsPlayable || !ReferenceEquals(item.Source, selection.Source)) return;
                // The trailer owns its session and progress. Never borrow the feature's resume point or queue.
                await _actions.PlayAsync(item, choice: new PlaybackChoice(null, null, null, false, 0) { ExactStart = true })
                    .ConfigureAwait(true);
            }
            else
            {
                var checkedLinks = ItemTrailers.Remote(new EmbyItem
                {
                    RemoteTrailers = [new() { Url = option.Url, Name = option.Title }]
                });
                if (checkedLinks.Count != 1) return;
                // YouTube 远程预告走 Momoka 内的官方播放窗：和设置页登录共用一份 Cookie，
                // 不再把用户丢进系统浏览器（那里没有 Momoka 的登录态）。
                if (TrailerPreview.YouTubeId(checkedLinks[0].Url) is { } youTubeId)
                {
                    _actions.OpenYouTubeTrailer(youTubeId, option.Title);
                    return;
                }
                if (_trailerLauncher is null) throw new InvalidOperationException("浏览器启动服务不可用");
                _trailerLauncher.OpenUrl(checkedLinks[0].Url!);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (OwnsTrailers(selection)) Report("打开预告片失败", error);
        }
    }
}
