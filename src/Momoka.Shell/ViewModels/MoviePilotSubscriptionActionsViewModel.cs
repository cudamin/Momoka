using System.Collections.ObjectModel;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>封面菜单弹窗的访问期；所有网络调用仍经同一 MoviePilot 服务与订阅身份守卫。</summary>
public sealed partial class MoviePilotSubscriptionActionsViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private MoviePilotSubscription? _subscription;
    private Func<bool>? _owns;
    private CancellationTokenSource? _lifetime;
    private string? _next;
    private int? _days = 7;
    private string? _kind;
    public ObservableCollection<MoviePilotSubscriptionActivity> Activities { get; } = [];
    public bool HasMore => _next is not null;
    private bool Live => _lifetime is { IsCancellationRequested: false } && _service is { Available: true }
        && _subscription is { } subscription && _service.IsCurrentSubscription(subscription) && _owns?.Invoke() == true;

    public override Task ReloadAsync() => LoadActivityAsync(_days, _kind);

    internal void Attach(MoviePilotService service, MoviePilotSubscription subscription, Func<bool> owns)
    {
        Cancel();
        _service = service;
        _subscription = subscription;
        _owns = owns;
        _lifetime = new();
        IsReady = false;
    }

    internal async Task<bool> ShareAsync(string comment, string user)
    {
        if (!Live || Busy) return false;
        var token = _lifetime!.Token;
        Busy = true;
        ClearNotice();
        try
        {
            await _service!.ShareSubscriptionAsync(_subscription!, comment, user, token);
            return Live && !token.IsCancellationRequested;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) { if (Live && !token.IsCancellationRequested) Report("分享失败", error); return false; }
        finally { if (!token.IsCancellationRequested) Busy = false; }
    }

    internal async Task LoadActivityAsync(int? days, string? kind, bool more = false)
    {
        if (!Live || Busy || more && !HasMore) return;
        var token = _lifetime!.Token;
        Busy = true;
        ClearNotice();
        if (!more) { _days = days; _kind = kind; _next = null; Activities.Clear(); }
        try
        {
            var page = await _service!.SubscriptionActivityAsync(_subscription!, _days, _kind, more ? _next : null, token);
            if (!Live || token.IsCancellationRequested) return;
            foreach (var item in page.Items)
                if (!Activities.Any(existing => existing.EventId == item.EventId)) Activities.Add(item);
            _next = page.NextCursor;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Live && !token.IsCancellationRequested) Report("读取订阅活动失败", error); }
        finally { if (!token.IsCancellationRequested) { Busy = false; IsReady = true; } }
    }

    internal async Task<string?> LoadDetailsAsync(MoviePilotSubscriptionActivity activity)
    {
        if (!Live || Busy || !Activities.Contains(activity)) return null;
        var token = _lifetime!.Token;
        Busy = true;
        ClearNotice();
        try
        {
            var detail = activity.DetailsLoaded ? activity
                : await _service!.SubscriptionActivityDetailAsync(_subscription!, activity.EventId, token);
            return Live && !token.IsCancellationRequested ? detail.Details : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) { if (Live && !token.IsCancellationRequested) Report("读取活动详情失败", error); return null; }
        finally { if (!token.IsCancellationRequested) Busy = false; }
    }

    internal async Task<MoviePilotSubscriptionFiles?> LoadFilesAsync()
    {
        if (!Live || Busy) return null;
        var token = _lifetime!.Token;
        Busy = true;
        ClearNotice();
        try
        {
            var files = await _service!.SubscriptionFilesAsync(_subscription!, token);
            return Live && !token.IsCancellationRequested ? files : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) { if (Live && !token.IsCancellationRequested) Report("读取文件统计失败", error); return null; }
        finally { if (!token.IsCancellationRequested) Busy = false; }
    }

    public override void Cancel()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _owns = null;
        _next = null;
        Activities.Clear();
        Busy = false;
        base.Cancel();
    }

    public override void Dispose()
    {
        Cancel();
        base.Dispose();
    }
}
