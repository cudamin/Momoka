using Microsoft.UI.Xaml;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    private CancellationTokenSource? _extraLoad;
    private ExtraSelection? _extraSelection;
    private ExtraSelection? _preparedExtras;
    private bool _extrasReady;
    private bool _extrasChecked;
    private bool _extrasEmpty;
    private long _extraEpoch;

    public Visibility ExtraVisibility => Show(!_local && CanAct && _extrasReady && _extrasChecked && !_extrasEmpty
        && _detail is { } item && ItemExtras.CanRead(item, _scope!.Connection.ServerKind));

    private void ResetExtras()
    {
        _extraEpoch++;
        _extraSelection = null;
        _preparedExtras = null;
        _extraLoad?.Cancel();
        _extraLoad?.Dispose();
        _extraLoad = null;
        _extrasReady = false;
        _extrasChecked = false;
        _extrasEmpty = false;
        OnPropertyChanged(nameof(ExtraVisibility));
    }

    internal sealed class ExtraOption(EmbyItem item)
    {
        public EmbyItem Item { get; } = item;
        internal string ItemId { get; } = item.Id;
        public string Title { get; } = string.IsNullOrWhiteSpace(item.Name) ? "花絮与特典" : item.Name;
        public string Category { get; } = ItemExtras.Category(item.ExtraType);
    }

    internal sealed class ExtraSelection(EmbyItem subject, EmbySessionScope source, CancellationToken token, long epoch)
    {
        internal EmbyItem Subject { get; } = subject;
        internal string SubjectId { get; } = subject.Id;
        internal EmbySessionScope Source { get; } = source;
        internal CancellationToken Token { get; } = token;
        internal long Epoch { get; } = epoch;
        public List<ExtraOption> Options { get; } = [];
        public string? Error { get; internal set; }
    }

    internal bool OwnsExtras(ExtraSelection selection) => CanAct && _extrasReady
        && ReferenceEquals(_extraSelection, selection) && !selection.Token.IsCancellationRequested
        && ReferenceEquals(_detail, selection.Subject) && selection.Subject.Id == selection.SubjectId
        && ReferenceEquals(_scope, selection.Source) && selection.Source.IsAvailable
        && ItemExtras.CanRead(selection.Subject, selection.Source.Connection.ServerKind);

    internal bool CanRetryExtras(ExtraSelection selection) => CanAct && _extrasReady
        && selection.Epoch == _extraEpoch && ReferenceEquals(_detail, selection.Subject)
        && selection.Subject.Id == selection.SubjectId && ReferenceEquals(_scope, selection.Source)
        && selection.Source.IsAvailable && ItemExtras.CanRead(selection.Subject, selection.Source.Connection.ServerKind);

    /// <summary>Closing only ends this menu's selection and read, leaving the detail's entry available.</summary>
    internal void CloseExtras(ExtraSelection? selection)
    {
        if (selection is not null && !ReferenceEquals(_extraSelection, selection)) return;
        _extraSelection = null;
        _preparedExtras = null;
        _extraLoad?.Cancel();
        _extraLoad?.Dispose();
        _extraLoad = null;
    }

    internal async Task<ExtraSelection?> GetExtrasAsync()
    {
        if (ExtraVisibility != Visibility.Visible) return null;
        if (_preparedExtras is { } prepared && OwnsExtras(prepared))
        {
            _preparedExtras = null;
            return prepared;
        }
        return await ReadExtrasAsync().ConfigureAwait(true);
    }

    private async Task LoadExtrasAvailabilityAsync()
    {
        // 支持查询不等于有内容。进入详情先读一次，首个菜单复用结果，避免点击后空入口
        // 才消失、把旁边的连播挤到原位置；读取失败仍保留菜单中的原因和重试入口。
        var selection = await ReadExtrasAsync().ConfigureAwait(true);
        if (selection is not null && OwnsExtras(selection)) _preparedExtras = selection;
    }

    private async Task<ExtraSelection?> ReadExtrasAsync()
    {
        if (!CanAct || !_extrasReady || _detail is not { } subject || _scope is not { } scope
            || !ItemExtras.CanRead(subject, scope.Connection.ServerKind)) return null;
        _extraLoad?.Cancel();
        _extraLoad?.Dispose();
        _extraLoad = new CancellationTokenSource();
        var selection = new ExtraSelection(subject, scope, _extraLoad.Token, ++_extraEpoch);
        _extraSelection = selection;
        try
        {
            var extras = await scope.ExecuteAsync((client, ct) => client.GetSpecialFeaturesAsync(selection.SubjectId, ct),
                selection.Token).ConfigureAwait(true);
            if (!OwnsExtras(selection)) return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in extras)
            {
                // ExecuteAsync binds the request's owner. A conflicting server declaration in JSON still
                // cannot borrow that owner; the response did not come from a second trusted source.
                if (item is null || item.Id == selection.SubjectId || !ItemExtras.IsPlayable(item)
                    || !MatchesExtraSource(item, scope) || !MatchesExtraServer(item, scope) || !ids.Add(item.Id)) continue;
                selection.Options.Add(new ExtraOption(item));
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error)
        {
            if (!OwnsExtras(selection)) return null;
            selection.Error = "读取花絮与特典失败：" + Failure.Describe(error);
        }
        if (!OwnsExtras(selection)) return null;
        _extrasChecked = true;
        _extrasEmpty = selection.Options.Count == 0 && selection.Error is null;
        OnPropertyChanged(nameof(ExtraVisibility));
        return selection;
    }

    internal async Task OpenExtraAsync(ExtraSelection selection, ExtraOption option)
    {
        if (!OwnsExtras(selection) || !selection.Options.Contains(option) || _actions is null) return;
        var item = option.Item;
        if (item.Id != option.ItemId || item.Id == selection.SubjectId || !ItemExtras.IsPlayable(item)
            || !MatchesExtraSource(item, selection.Source) || !MatchesExtraServer(item, selection.Source)) return;
        try
        {
            // An extra owns its media, source, progress and tracks. Never pass the feature's selected
            // file, parent or queue; an explicit zero survives the player's resume preferences.
            await _actions.PlayAsync(item, choice: new PlaybackChoice(null, null, null, false, 0) { ExactStart = true })
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (OwnsExtras(selection)) Report("播放花絮与特典失败", error);
        }
    }

    // A same-identity retry binds a fresh scope wrapper. The live owner, account key and connection
    // identity remain authoritative; reference equality would discard its legitimate media response.
    private static bool MatchesExtraSource(EmbyItem item, EmbySessionScope scope) => item.Source is { IsAvailable: true } source
        && source.Key == scope.Key && ReferenceEquals(source.UserStateOwner, scope.UserStateOwner)
        && source.Connection.IsSameIdentityAs(scope.Connection);

    private static bool MatchesExtraServer(EmbyItem item, EmbySessionScope scope) =>
        string.IsNullOrEmpty(item.ServerId) || string.IsNullOrEmpty(scope.Connection.ServerId)
        || string.Equals(item.ServerId, scope.Connection.ServerId, StringComparison.Ordinal);
}
