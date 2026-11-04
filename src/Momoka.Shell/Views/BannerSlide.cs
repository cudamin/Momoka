using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// One slide of the home page's carousel: what it says, and the two pictures it says it over.
/// <para>
/// The same split as <see cref="CardItem"/>, and for the same reason — the control is one band that every
/// slide takes a turn in, so the artwork has to belong to the slide rather than to the band, or moving on
/// would drop whatever the previous slide had decoded. Two pictures rather than one, because this band is
/// both halves of 需求 4 at once: 背景图 behind it (<see cref="ItemArtwork.Banner"/>) and 徽标 on top of it
/// (<see cref="ItemArtwork.Plate"/>).
/// </para>
/// <para>
/// Public because <c>x:Bind</c> compiles against the type by name.
/// </para>
/// </summary>
public sealed class BannerSlide : INotifyPropertyChanged
{
    private const string Category = "主页轮播";

    /// <summary>
    /// What the picture is decoded at. 1280 is <see cref="EmbyImageStore.RequestWidth"/>'s own ceiling, so
    /// asking for more would only upscale a 1280-wide download — and this band is the width of the window,
    /// which on this machine is past that ceiling before it is maximised.
    /// </summary>
    private const int PictureWidth = 1280;

    /// <summary>Same as the detail page's name plate: the logo is drawn a few inches wide at most.</summary>
    private const int LogoWidth = 320;

    private readonly EmbyImageStore _images;
    private readonly ArtworkRef? _picture;
    private readonly ArtworkRef? _logo;

    /// <summary>
    /// 这张背景在内存缓存里的键。来源在构造时就不可用时留空 —— 不可用的来源既不该命中旧图，也不该写入。
    /// </summary>
    private readonly string? _backdropKey;

    private CancellationTokenSource? _loading;
    private bool _asked;
    private BitmapImage? _pictureImage;
    private BitmapImage? _logoImage;
    private readonly EmbySessionScope? _scope;
    public BannerSlide(EmbyItem item, EmbyImageStore images)
    {
        Item = item;
        _images = images;
        _scope = images.CaptureScope(item);
        if (item.Source is null) _scope?.Bind(item);
        _picture = ItemArtwork.Banner(item);
        _logo = ItemArtwork.Plate(item);

        // 返回主页会重建一批幻灯片；同一来源、同一张图、同一套处理规则就不必再解一次。这里直接装进字段，
        // 不经过属性通知：构造期间还没有人订阅，而页面第一次 Show 会读到这张图并立即上屏。
        if (_picture is { } picture && _scope is { IsAvailable: true } scope)
        {
            _backdropKey = BannerBackdropCache.Key(scope.Connection, picture, PictureWidth);
            if (BannerBackdropCache.TryGet(_backdropKey, out var cached)) _pictureImage = cached;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 这一张真的取不到宽图：服务器没有、下载失败，或者解码解不出来。带子收到这一句就把这张从轮播里摘掉，
    /// 然后由页面把同一件从清单里也去掉 —— 「刚入库的资源如果没有背景图或可用于轮播图的图片就不要放到轮播
    /// 列表里」（2026-11-02 用户令）。
    /// <para>
    /// 只在图片这一趟走完之后报一次，徽标不算：它是角上一枚图章，没有徽标的那张照样是一条完整的幻灯片。
    /// 取消（离树、换批、换账号）不算失败，不会报。
    /// </para>
    /// </summary>
    public event EventHandler? PictureUnavailable;

    /// <summary>The item behind the slide, for whoever opens or plays it.</summary>
    public EmbyItem Item { get; }

    /// <summary>The show's name on an episode, which is where the reader thinks they are.</summary>
    public string Title => ItemDetail.Title(Item);

    public string Caption => HomeCarousel.Caption(Item);

    public string Synopsis => HomeCarousel.Synopsis(Item);

    /// <summary>「继续播放 12:34」 on a half-watched item, which is most of what this band holds.</summary>
    public string PlayText => ItemDetail.PlayText(Item);

    public Visibility CaptionVisibility => Show(Caption.Length > 0);

    public Visibility SynopsisVisibility => Show(Synopsis.Length > 0);

    /// <summary>What a reader who cannot see the band is told it is: 「打开 命运石之门」.</summary>
    public string OpenTip => $"打开 {Title}";

    public BitmapImage? Picture
    {
        get => _pictureImage;
        private set
        {
            if (ReferenceEquals(_pictureImage, value)) return;
            _pictureImage = value;
            Raise();
        }
    }

    public BitmapImage? Logo
    {
        get => _logoImage;
        private set
        {
            if (ReferenceEquals(_logoImage, value)) return;
            _logoImage = value;
            Raise();
            Raise(nameof(LogoVisibility));
        }
    }

    public Visibility LogoVisibility => Show(_logoImage is not null);

    /// <summary>
    /// 这一张画出来的东西。整页重读会重建一批 <see cref="BannerSlide"/>，带子拿这个句子判断「还是同一张，
    /// 只是换了个对象」并沿用旧对象 —— 台上那张的图、字幕和倒计时因此不会因为一次刷新回到第一张
    /// （2026-11-02「进度条跑到一半回退」）。
    /// </summary>
    internal string Signature => string.Join('\u001f',
        Item.IdentityKey, Title, Caption, Synopsis, PlayText,
        _picture?.ItemId ?? "", _picture?.ImageType ?? "", _picture?.Tag ?? "",
        _logo?.ItemId ?? "", _logo?.ImageType ?? "", _logo?.Tag ?? "");

    /// <summary>
    /// Starts both loads if they have not been asked for. Idempotent and asked once per slide: the band calls
    /// this on the slide it is about to show and on its two neighbours, so a slide the reader is walking
    /// towards is already decoded when it arrives, and every slide is asked at most once no matter how many
    /// times the carousel comes back around to it.
    /// <para>
    /// The picture is the slide: when it cannot be had, <see cref="PictureUnavailable"/> says so and the band
    /// drops the slide. The logo is optional and is not part of that verdict.
    /// </para>
    /// </summary>
    public async Task EnsureAsync()
    {
        // 来源已经失效：连构造时从缓存装进来的那张也不能继续画 —— 「不能沿用旧图」说的是这个。
        if (_scope?.IsAvailable != true)
        {
            Picture = null;
            Logo = null;
            return;
        }

        if (_asked || _loading is not null) return;

        var cts = new CancellationTokenSource();
        _loading = cts;
        _asked = true;
        var unavailable = false;

        try
        {
            // 被 Release 放掉之后又转回来时再查一次：缓存里还留着同一张就不必走一遍取字节和解码。
            if (Picture is null && _backdropKey is { } cachedKey
                && BannerBackdropCache.TryGet(cachedKey, out var cached))
                Picture = cached;

            if (Picture is null)
            {
                // Sequential, and the picture first: it is the whole band, the logo is a stamp in one corner of
                // it, and the two together would otherwise race for the one connection the session holds.
                var bytes = await FetchAsync(_picture, PictureWidth, cts.Token).ConfigureAwait(true);
                if (!StillWanted(cts)) return;

                if (bytes is { Length: > 0 })
                {
                    var picture = await BannerPictureLoader.DecodeAsync(bytes, PictureWidth, cts.Token).ConfigureAwait(true);
                    if (!StillWanted(cts)) return;
                    Picture = picture;

                    // 只在这一趟仍然有效时写入：来源在解码途中失效的，不该把画面留给下一次。
                    if (picture is not null && _backdropKey is { } key) BannerBackdropCache.Remember(key, picture);
                }
            }

            if (Picture is null)
            {
                unavailable = true;
            }
            else
            {
                var logo = await DecodeAsync(_logo, LogoWidth, cts.Token).ConfigureAwait(true);
                if (!StillWanted(cts)) return;
                Logo = logo;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            // 带 exception 而不只是 Message：这句文案（「没有检测到已安装的组件」）是 WinUI 的 0x800F1000
            // 误报 —— CBS 错误码和 XAML 撞了号，消息本身说不清是哪一层的事，只有堆栈说得清（2026-09-12 记）。
            Log.Warn(Category, $"加载轮播图片失败（{Item.Name}）：{error.Message}", error);

            // 图已经到手、只是徽标那一趟失败的不算：这张幻灯片照样站得住。
            unavailable = Picture is null && StillWanted(cts);
        }
        finally
        {
            if (ReferenceEquals(_loading, cts)) _loading = null;
            cts.Dispose();
        }

        if (unavailable) PictureUnavailable?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>这一趟还是这一张在等：没被换下、没被取消，账号也还在。取消不是失败，不报。</summary>
    private bool StillWanted(CancellationTokenSource cts) =>
        ReferenceEquals(_loading, cts) && !cts.IsCancellationRequested && _scope?.IsAvailable == true;

    /// <summary>
    /// Drops the decoded picture and the logo, and abandons anything in flight. Called on the slides the band
    /// has moved away from: a backdrop is a megabyte-scale surface, and eight of them held at once is the whole
    /// reason this is a method rather than a load that happens once and stays.
    /// <para>
    /// <see cref="_asked"/> is cleared too, so a slide that comes back around asks again — the decoded
    /// picture is in <see cref="BannerBackdropCache"/> by then, so the second ask is an in-memory lookup
    /// rather than another decode.
    /// </para>
    /// </summary>
    public void Release()
    {
        _loading?.Cancel();
        _loading = null;
        _asked = false;
        Picture = null;
        Logo = null;
    }

    /// <summary>取字节那一半（缓存命中的话是一次磁盘读），解码那一半在调用方。</summary>
    private async Task<byte[]?> FetchAsync(ArtworkRef? artwork, int width, CancellationToken token)
    {
        if (artwork is not { } reference || _scope is not { IsAvailable: true } scope) return null;

        return await _images
            .GetAsync(scope, reference.ItemId, reference.ImageType, reference.Tag, EmbyImageStore.RequestWidth(width), token)
            .ConfigureAwait(true);
    }

    private async Task<BitmapImage?> DecodeAsync(ArtworkRef? artwork, int width, CancellationToken token)
    {
        var bytes = await FetchAsync(artwork, width, token).ConfigureAwait(true);

        return bytes is { Length: > 0 } && !token.IsCancellationRequested
            ? await PosterLoader.DecodeAsync(bytes, width).ConfigureAwait(true)
            : null;
    }

    private void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property ?? string.Empty));

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
