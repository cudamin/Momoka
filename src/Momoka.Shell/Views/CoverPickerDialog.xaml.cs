using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// 候选封面里的一张。缩略图是后到的，所以这个类得会发通知 —— 同 <c>CardItem</c>。
/// <para>
/// 公开的，因为 <c>x:Bind</c> 在 DataTemplate 里是按类型名编译的。
/// </para>
/// </summary>
public sealed class CoverChoice(RemoteImageInfo info, bool canPreview = true, double previewHeight = 210) : INotifyPropertyChanged
{
    private BitmapImage? _picture;
    private readonly bool _canPreview = canPreview;
    private bool _loading;
    private bool _previewFailed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public RemoteImageInfo Info { get; } = info;
    public Visibility PreviewVisibility => _canPreview ? Visibility.Visible : Visibility.Collapsed;
    public string Caption { get; } = info.Describe();
    public double PreviewHeight { get; } = previewHeight;
    public double CellHeight => _canPreview ? PreviewHeight + 90 : 46;
    public bool IsLoading => _loading;
    public bool PreviewFailed => _previewFailed;
    public Visibility RetryVisibility => _previewFailed ? Visibility.Visible : Visibility.Collapsed;
    public string PreviewStatus => _loading ? "加载中…" : _previewFailed ? "预览加载失败" : "";
    public Visibility StatusVisibility => _canPreview && Picture is null && (_loading || _previewFailed)
        ? Visibility.Visible : Visibility.Collapsed;

    public BitmapImage? Picture
    {
        get => _picture;
        set
        {
            if (ReferenceEquals(_picture, value)) return;
            _picture = value;
            Raise(nameof(Picture));
            Raise(nameof(StatusVisibility));
        }
    }

    internal void SetPreviewState(bool loading, bool failed)
    {
        _loading = loading;
        _previewFailed = failed;
        Raise(nameof(IsLoading));
        Raise(nameof(PreviewFailed));
        Raise(nameof(RetryVisibility));
        Raise(nameof(PreviewStatus));
        Raise(nameof(StatusVisibility));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 「换一张」那张候选表。它回答的是<b>这一格用哪一张</b>，两种答案：从服务器列的候选里挑一个
/// （<see cref="Picked"/>），或者从本机传一张（<see cref="Uploaded"/>）—— 两者最多只有一个非空。
/// <para>
/// 缩略图是这张表自己取回来的：一次打开就那么十几二十张，取完就扔，没有必要进磁盘缓存（那一份是按「条目 +
/// 图片标签」存的，而这些图还不属于任何条目）。
/// </para>
/// <para>
/// 上传那一趟<b>不在这张表里做</b>：文件框和「往服务器写」都是外面那位的事，这里只把选中的字节带出去。
/// 这张表要知道服务器怎么连上，是从前那一版就立下的规矩。
/// </para>
/// </summary>
public sealed partial class CoverPickerDialog : ContentDialog
{
    private const string Category = "ui";

    /// <summary>一张慢图不阻挡整页，同时避免对服务器无上限并发。</summary>
    private const int PreviewConcurrency = 4;

    /// <summary>缩略图解到多宽。和模板里那一格的宽度一致，见 <see cref="PosterLoader.DecodeAsync"/>。</summary>
    private const int ThumbnailWidth = 150;

    private readonly List<CoverChoice> _choices = [];

    /// <summary>
    /// 外面那张面板。取候选缩略图的委托从它身上拿，免得同一份「按地址取图」的接线在这儿和那儿各写一遍 ——
    /// 那也保证了这一张表里所有网络上的事仍然只有那一个人在发。
    /// </summary>
    private readonly CoverDialog _owner;
    private bool _closed;
    private bool _picking;
    private readonly SemaphoreSlim _previewSlots = new(PreviewConcurrency);
    private readonly List<Task> _previewLoads = [];
    private readonly CancellationTokenSource _previewCancellation = new();
    private Task _loadTask = Task.CompletedTask;
    internal event Action? UploadCompleted;
    internal void CancelLoads()
    {
        if (_closed) return;
        _closed = true;
        _previewCancellation.Cancel();
        _ = DisposeLoadsAsync();
    }

    private async Task DisposeLoadsAsync()
    {
        try { await Task.WhenAll(_previewLoads).ConfigureAwait(true); }
        finally { _previewCancellation.Dispose(); _previewSlots.Dispose(); }
    }

    /// <param name="kind">正在换的是哪一种图。它只用来写标题 —— 剩下的事外面那位已经知道了。</param>
    /// <param name="found">服务器列的候选。</param>
    /// <param name="owner">外面那张面板，见 <see cref="_owner"/>。</param>
    public CoverPickerDialog(EmbyItem item, ArtworkKind kind, RemoteImageResult found, CoverDialog owner)
    {
        InitializeComponent();

        _owner = owner;

        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Title = $"{(kind.Many ? "另加一张" : "换一张")}{kind.Name} — {item.Name}";
        PrimaryButtonText = "使用这张";
        Closed += (_, _) => CancelLoads();

        var previewKind = ArtworkPreview.KindFor(item, kind);
        var previewHeight = previewKind is { ImageType: "Primary", Ratio: > 1 }
            ? ThumbnailWidth / previewKind.Ratio : 210;
        foreach (var image in found.Images.Where(image => !string.IsNullOrWhiteSpace(image.Url)))
            _choices.Add(new CoverChoice(image, owner.CanPreviewRemoteArtwork, previewHeight));

        Grid.ItemsSource = _choices;
        Grid.Visibility = _choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!owner.CanPreviewRemoteArtwork) Grid.Height = double.NaN;

        if (_choices.Count == 0)
        {
            // 一家刮削源都没答上来和「答了但没有这一种图」是两件事，用户能做的处置不同：前者要去服务器上装
            // 或者配刮削插件，后者只能等那一家有图。
            Notice.Text = found.Providers.Count == 0
                ? "服务器上没有能提供图片的刮削源，或者这个条目没有对上任何一家的记录。底下那颗「从本机传一张」照样能用。"
                : $"刮削源（{string.Join("、", found.Providers)}）上没有这个条目的{kind.Name}。底下那颗「从本机传一张」照样能用。";
            Notice.Visibility = Visibility.Visible;
        }

        IsPrimaryButtonEnabled = false;

        // 不 await：一张张图慢慢回来，这张表先画出来。
        if (owner.CanPreviewRemoteArtwork) _loadTask = LoadAsync();
    }

    /// <summary>挑中的那一张，没挑就是空。</summary>
    public RemoteImageInfo? Picked => (Grid.SelectedItem as CoverChoice)?.Info;

    /// <summary>从本机选中的那张图（字节 + 文件名），没选就是空。</summary>
    public PickedArtwork? Uploaded { get; private set; }

    private void OnPicked(object sender, SelectionChangedEventArgs e) => IsPrimaryButtonEnabled = !_picking && Picked is not null;

    /// <summary>
    /// 从本机传一张。选完就把答案收下并关掉这张表 —— 上传那一趟由外面那张面板发，因为要往服务器写。
    /// <para>
    /// 文件框弹不出来（拿不到窗口句柄）时退化成一句人话，而不是一个静默不动的按钮。
    /// </para>
    /// </summary>
    private async void OnUpload(object sender, RoutedEventArgs e)
    {
        if (_closed || _picking) return;
        _picking = true;
        UploadArtworkButton.IsEnabled = false;
        IsPrimaryButtonEnabled = false;
        try
        {
            var uploaded = await _owner.PickArtworkAsync().ConfigureAwait(true);
            if (_closed || uploaded is null) return;
            Uploaded = uploaded;
            Grid.SelectedItem = null;
            UploadCompleted?.Invoke();
        }
        catch (Exception error)
        {
            if (_closed) return;
            Log.Warn(Category, "选图片文件失败", error);
            Notice.Text = $"选择图片失败：{Failure.Describe(error)}";
            Notice.Visibility = Visibility.Visible;
        }
        finally
        {
            _picking = false;
            UploadArtworkButton.IsEnabled = true;
            IsPrimaryButtonEnabled = Picked is not null;
        }
    }

    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        if ((sender as FrameworkElement)?.Tag is CoverChoice choice)
            await QueuePreview(choice).ConfigureAwait(true);
        else
        {
            _loadTask = Task.WhenAll(_choices.Where(row => row.PreviewFailed && !row.IsLoading).Select(QueuePreview));
            await _loadTask.ConfigureAwait(true);
        }
    }

    /// <summary>候选只经原来的服务器代理预览；失败重试不提交或改动媒体图片。</summary>
    private async Task LoadAsync()
    {
        if (_closed) return;
        await Task.WhenAll(_choices.Where(choice => choice.Picture is null).Select(QueuePreview)).ConfigureAwait(true);
    }

    private Task QueuePreview(CoverChoice choice)
    {
        if (_closed || choice.IsLoading || choice.Picture is not null) return Task.CompletedTask;
        choice.SetPreviewState(true, false);
        UpdatePreviewNotice();
        var load = LoadChoiceAsync(choice, _previewCancellation.Token);
        _previewLoads.Add(load);
        return load;
    }

    private async Task LoadChoiceAsync(CoverChoice choice, CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            await _previewSlots.WaitAsync(cancellationToken).ConfigureAwait(true);
            entered = true;
            cancellationToken.ThrowIfCancellationRequested();
            var picture = await ArtworkPreview.RemoteAsync(choice.Info, async (address, token) =>
            {
                var bytes = await _owner.FetchRemoteAsync(address, token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                if (bytes is not { Length: > 0 }) return null;
                var decoded = await PosterLoader.DecodeAsync(bytes, ThumbnailWidth).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                return decoded;
            }, cancellationToken).ConfigureAwait(true);
            if (_closed) return;
            choice.Picture = picture;
            choice.SetPreviewState(false, picture is null);
        }
        catch (OperationCanceledException) when (_closed || cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_closed) return;
            choice.SetPreviewState(false, true);
            Log.Debug(Category, $"候选封面取不回来（{choice.Info.ProviderName}）：{error.Message}");
        }
        finally
        {
            if (entered) _previewSlots.Release();
            if (!_closed) UpdatePreviewNotice();
        }
    }

    private void UpdatePreviewNotice()
    {
        var failures = _choices.Count(choice => choice.PreviewFailed);
        PreviewNotice.Text = failures > 0 ? $"{failures} 张图片的预览未能加载，可以重试。" : "";
        PreviewNotice.Visibility = failures > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryArtworkPreviews.Visibility = failures > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
