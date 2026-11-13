using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSeasonPicker : ContentDialog
{
    public MoviePilotMediaViewModel ViewModel { get; } = new();

    /// <summary>用户点过「提交订阅」。点下去时窗就收了，写入要等收好之后才发（见 <see cref="ShowAsync"/>）。</summary>
    private bool _submitRequested;

    public MoviePilotSeasonPicker() => InitializeComponent();

    /// <summary>
    /// 开季选择弹窗，返回用户这一趟的结果。
    /// <para>
    /// 2026-11-13 用户令「点完订阅直接退出这个弹窗，然后再提示订阅是否成功」：点「提交订阅」不等于等结果，
    /// 窗立刻收起，写入在窗外继续；这个任务等写入走完才返回，调用方按拿到的报告在自己的页面上提示。
    /// 用户取消、点窗外或断线被收走，返回 <see cref="MoviePilotSubscribeReport.Cancelled"/>，一个请求都不发。
    /// </para>
    /// </summary>
    internal static async Task<MoviePilotSubscribeReport> ShowAsync(FrameworkElement owner, MoviePilotService service, MoviePilotMedia media, string group, int? season,
        CancellationToken token = default)
    {
        if (media.Type != "电视剧" || !service.Available || !service.IsCurrentConnection(media.ConnectionStamp)
            || owner.XamlRoot is null || token.IsCancellationRequested) return MoviePilotSubscribeReport.Cancelled;
        var dialog = new MoviePilotSeasonPicker { XamlRoot = owner.XamlRoot, RequestedTheme = owner.ActualTheme };
        dialog.ViewModel.Attach(service, media, group, picker: true, preselect: season, lifetimeToken: token);
        using var cancelled = token.Register(() => owner.DispatcherQueue.TryEnqueue(() =>
        {
            dialog.ViewModel.Cancel();
            dialog.Hide();
        }));
        try
        {
            _ = dialog.ViewModel.ReloadAsync();
            var result = await MoviePilotDialog.ShowAsync(dialog, service);
            if (token.IsCancellationRequested || !service.Available || !service.IsCurrentConnection(media.ConnectionStamp))
                return MoviePilotSubscribeReport.Cancelled;
            // 窗已经收好了，提交中「不许关」那条保护不再拦得住这一趟；这里才真的发请求，并等它走完。
            return await dialog.CompleteAsync(result);
        }
        finally { dialog.ViewModel.Cancel(); }
    }

    /// <summary>
    /// 收窗之后那一趟：只有用户真的按了「提交订阅」才发写入。单独留一个入口，是因为原生检查台要自己
    /// <c>ShowAsync()</c> 才能量真实控件，也走的是这同一段，不另抄一份提交。
    /// </summary>
    internal async Task<MoviePilotSubscribeReport> CompleteAsync(ContentDialogResult result)
    {
        if (result != ContentDialogResult.Primary || !_submitRequested) return MoviePilotSubscribeReport.Cancelled;
        return await ViewModel.SubmitAsync();
    }

    /// <summary>
    /// 只表示「要提交」，不在这里等结果：拦下关闭会让窗在提交期间一直停在屏上，正是这次要去掉的行为。
    /// 真正发请求由 <see cref="ShowAsync"/> 在收窗之后接着做。
    /// </summary>
    private void OnSubmit(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = !ViewModel.CanSubmit;
        _submitRequested = !args.Cancel;
    }
    private async void OnReview(object sender, RoutedEventArgs args) => await ViewModel.ReviewAsync();
    private async void OnRetrySeasonStates(object sender, RoutedEventArgs args) => await ViewModel.RetrySeasonStatesAsync();
}
