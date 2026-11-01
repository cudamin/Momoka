using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Media;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 浮层上那几块读数：章节缩略图、按画面比例联动窗口、十赫兹那一跳、播放信息，以及共用的那两个小工具。
/// <para>
/// 这一片全是「每一跳都要跑一遍」的东西，所以它里面刻意不在每一跳里新建对象 —— 十赫兹乘上一部电影是几十万次分配。
/// </para>
/// <para>
/// 2026-09-05 从 <c>PlayerViewModel.cs</c>（当时 2227 行）切出来的一片，<b>正文一字节没动</b>：切的位置
/// 就是那个文件里本来就画好的分节线，所以这一次没有任何一处需要判断某个成员归谁。字段和构造函数留在主
/// 文件里 —— 它们是这一族共用的东西，散开就再也数不清谁在改哪个。
/// </para>
/// </summary>
public sealed partial class PlayerViewModel
{
    // ---- 章节缩略图预览 -----------------------------------------------------------

    /// <summary>
    /// Which chapter a hovered moment falls in, and its still if the server extracted one. The page owns
    /// where the box goes; this owns what is in it.
    /// </summary>
    /// <returns>Whether there is anything to preview at that moment, which there is for any open file.</returns>
    internal bool PeekChapterAt(double seconds)
    {
        if (!Status.HasDuration) return false;

        var moment = Math.Clamp(seconds, 0, Status.Duration);

        // Every pixel of movement, unlike the contents below: the time is the one part of the box that is
        // about where the pointer is rather than about which chapter it landed in. Written before anything
        // can return, because the readout is the half that is always available — plenty of servers extract
        // no chapters at all, and the slider's own tooltip only appears while the thumb is being dragged,
        // so hovering such a file used to show nothing whatsoever.
        ChapterClock = TimelineScale.Clock(moment, Status.Duration);

        // Two lookups against two lists, both correct — see ChapterTimeline. The name comes from whichever
        // marks the bar is currently drawing, which is mpv's once it has published them; the picture is
        // indexed against Emby's own list, because that index is what the image request means.
        var named = ChapterTimeline.IndexAt(ChapterMarks, moment);
        var still = ChapterTimeline.IndexAt(_embyMarks, moment);

        // Contents only when the chapter changes: the box follows the pointer along the bar, and reloading
        // the same still for every pixel of that would be absurd.
        ChapterCaption = TimelineChapters.CaptionAt(moment);
        if (named == _peekChapter && still == _peekStill) return true;

        _peekChapter = named;
        _peekStill = still;

        if (_nowPlaying is not null && still >= 0 && still < _nowPlaying.Chapters.Count
            && _nowPlaying.Chapters[still] is { HasImage: true } info)
            _ = LoadChapterStillAsync(_generation, _nowPlaying.Id, still, info.ImageTag);
        else
            ChapterStill = null;

        return true;
    }

    internal void ClearChapterPeek()
    {
        _peekChapter = -1;
        _peekStill = -1;
        ChapterStill = null;
        ChapterCaption = null;
        ChapterClock = null;
    }

    /// <summary>
    /// Fetches and decodes one chapter still, once. Cached by index in <c>_chapterStills</c> including the
    /// misses, because a scrub back and forth over a chapter the server has no picture for would otherwise
    /// ask again on every pass. The index is Emby's, so what it is checked against is <c>_peekStill</c>.
    /// </summary>
    private async Task LoadChapterStillAsync(int generation, string itemId, int chapter, string? tag)
    {
        if (_chapterStills.TryGetValue(chapter, out var cached))
        {
            if (_peekStill == chapter) ChapterStill = cached;
            return;
        }

        // Nothing on screen while it arrives rather than the previous chapter's frame, which would be a
        // picture of the wrong moment — worse than no picture at all.
        ChapterStill = null;

        try
        {
            var scope = _playbackScope;
            if (scope is null) return;
            var width = EmbyImageStore.RequestWidth(ChapterPeekWidth);
            var bytes = await _images
                .GetChapterAsync(scope, itemId, chapter, tag, width, _lifetime.Token)
                .ConfigureAwait(true);

            if (generation != _generation) return;

            var bitmap = bytes is null
                ? null
                : await PosterLoader.DecodeAsync(bytes, ChapterPeekWidth).ConfigureAwait(true);
            if (generation != _generation) return;

            _chapterStills[chapter] = bitmap;
            if (_peekStill == chapter) ChapterStill = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"读取章节缩略图失败：{error.Message}");
        }
    }

    // ---- 缩放窗口时按画面比例联动 --------------------------------------------------

    /// <summary>
    /// Hands a picture shape to the page, which is what reshapes the window and holds it there. Two
    /// things know the shape — the server before the file is open and mpv once it is — and both come
    /// through here; <paramref name="learnedFrom"/> is which one, for the log.
    /// <para>
    /// Only on a change, so switching episodes inside one series does not shuffle the window the viewer
    /// has already placed, and so the server's answer is not re-applied when mpv confirms it.
    /// </para>
    /// </summary>
    private void AdoptAspect(double aspect, string learnedFrom)
    {
        // 画面不在本窗口就不整形：独立管线（内置后端）的画面在 mpv 自建窗口里，外部 mpv.exe 更是
        // 如此 —— 拿一部看不到的片子整形本窗口，窗口只会白挪。判的是画面位置（<see cref="PictureInHostWindow"/>），
        // 不是后端 —— 内置后端走独立管线时同样不画进本窗口，从前的「内置才整形」在那一档是错的。
        if (!PictureInHostWindow || aspect <= 0) return;
        if (Math.Abs(aspect - _aspect) <= 0.001) return;

        _aspect = aspect;
        PictureAspectChanged?.Invoke(aspect);
        Log.Debug(
            Category,
            $"画面比例 {aspect.ToString("0.000", CultureInfo.InvariantCulture)}（{learnedFrom}），缩放已联动");
    }

    /// <summary>
    /// The shape the server already reported, adopted before the player is even shown — 进播放器时画面两边
    /// 各有一条黑边. The poll below cannot answer in time for the first frames: it waits until the video is
    /// configured before it reads anything at all (the gate at <see cref="ApplyAspectAsync"/>), so the
    /// picture's first half-second lands in whatever shape the window was browsing in — and a window
    /// the user has dragged to any shape at all is one mpv has no choice but to letterbox.
    /// （侧边栏还在的时候连开窗那一档都是：窗口天生比 16:9 宽出一条栏。2026-09-06 那条栏删掉之后开窗那一档
    /// 正好是 16:9，可拖过的窗口照旧不是，所以这一步照旧要做。）
    /// <para>
    /// The stored size, so anamorphic and rotated files are a guess that <see cref="ApplyAspectAsync"/>
    /// corrects a moment later. A guess that is right for every ordinary file beats half a second of bands
    /// for all of them.
    /// </para>
    /// </summary>
    private void AdoptServerAspect(EmbyItem item)
    {
        var video = item.DefaultMediaSource?.PrimaryVideoStream;

        // Zeroes for mpv's pair: nothing is decoded yet, which is the case Ratio's fallback is for.
        AdoptAspect(
            AspectLock.Ratio(0, 0, streamWidth: video?.Width ?? 0, streamHeight: video?.Height ?? 0),
            "服务器");
    }

    /// <summary>
    /// Corrects the shape to the picture's own once mpv has one: its decoder-level display aspect, not the
    /// VO's <c>dwidth</c>/<c>dheight</c>.
    /// <para>
    /// Polled for the same reason the track list is: there is no notification to wait for. Silent when it
    /// agrees with <see cref="AdoptServerAspect"/>, which is the ordinary case.
    /// </para>
    /// <para>
    /// <b>Why not <c>dwidth</c>/<c>dheight</c> — two incidents.</b> They are VO-level values, and before the
    /// decoder has configured the video they fall back to the video output's size. The 2026-09-12 incident
    /// caught that fallback answering with the window's own shape (0.889 from a 1230×1384 client) and the
    /// window was reshaped to itself. Gating the read on <c>video-params/w</c> fixed that case, but on
    /// 2026-11-01 it happened again at <c>playback-restart</c> — the gate passed, <c>dwidth</c>/<c>dheight</c>
    /// still answered 0.889 while the film was 16:9, and the window came out of fullscreen as a tall strip
    /// with a black band above and below the picture. <c>video-params/aspect</c> (and <c>w/h</c> + <c>par</c>
    /// when it is absent) is the decoder's own display aspect: it has no window size to borrow. An explicit
    /// <c>video-aspect-override</c> from the 画面比例 menu still wins, because that is the viewer's choice.
    /// </para>
    /// </summary>
    private Task ApplyAspectAsync(int generation)
    {
        // 画面不在本窗口（独立管线/外部后端）就整趟不轮询：这些比例是给「按画面整形本窗口」用的，
        // 没有画面进来，每一问都是白问。
        if (!PictureInHostWindow) return Task.CompletedTask;

        return PollAsync(generation, true, async () =>
        {
            // The gate comes before the display pair: without it mpv answers about the file it has not
            // configured yet. The gate alone is not enough — see the method comment.
            var decodedWidth = await _playback.GetNumberAsync("video-params/w").ConfigureAwait(true);
            if (decodedWidth is null or 0) return false;

            var decodedHeight = await _playback.GetNumberAsync("video-params/h").ConfigureAwait(true);
            var pixelAspect = await _playback.GetNumberAsync("video-params/par").ConfigureAwait(true);
            var videoAspect = await _playback.GetNumberAsync("video-params/aspect").ConfigureAwait(true);
            var overrideAspect = await _playback.GetNumberAsync("video-aspect-override").ConfigureAwait(true);
            if (generation != _generation) return true;

            var aspect = AspectLock.FromVideoParams(
                videoAspect ?? 0, decodedWidth.Value, decodedHeight ?? 0, pixelAspect ?? 0);
            if (overrideAspect is { } forced && forced > 0) aspect = forced;
            if (aspect <= 0) return false;

            AdoptAspect(aspect, "mpv");
            return true;
        });
    }

    // ---- the ten-hertz tick ------------------------------------------------------

    /// <summary>
    /// The share of the page's ticker that is not about drawing: the coalesced seek, the 统计 refresh and
    /// the standing 跳过 offer. Driven by the page's timer rather than one of its own, because all three are
    /// only wanted while the player is up, which is exactly when that timer runs.
    /// </summary>
    internal void Tick()
    {
        FlushTimelineSeek();

        ApplySkipOffer();

        // The 400 ms a window size has to hold still before the 档位 is judged again. Here rather than on a
        // timer of its own because this ticker already runs exactly while the player is up, and a resize that
        // has settled is precisely a thing that expires rather than happens.
        if (_outputWatch is { } watch) Judge(watch.Tick(DateTimeOffset.UtcNow), "窗口尺寸变化");
    }

    // ---- 播放信息 ----------------------------------------------------------------

    /// <summary>
    /// The 播放信息 body: what is playing, then what was decided about how to play it — the quality preset,
    /// the shader group and why it was chosen, the backend, and every mpv option the launch actually set,
    /// in the order mpv itself resolved them. The page puts it in a dialog; the text is all from here
    /// because every line of it is a playback fact.
    /// </summary>
    internal string MediaInfoText()
    {
        var lines = new List<string>();

        if (_nowPlaying is { } item)
        {
            lines.Add($"标题：{item.ToPlaybackTitle()}");

            // 说的是**正在放的那一版**，且名字与画质一起写。这一页存在的意义是事后能说清「当时到底放的是
            // 什么」，而一个条目挂两版时（4K HDR / 1080p），只写画质那一句答不出是哪一份文件；
            // 只写名字又答不出是什么分辨率 —— 两句都要。问第一个媒体源是旧写法，换过版就报错了文件。
            if (PlayingSource is { } playing)
            {
                var quality = playing.ToQualityLabel();
                lines.Add(quality.Length > 0
                    ? $"媒体源：{ItemDetail.SourceLabel(playing)}（{quality}）"
                    : $"媒体源：{ItemDetail.SourceLabel(playing)}");
            }
        }

        if (Status.HasDuration) lines.Add($"时长：{Status.DurationClock}");
        if (_playback.LaunchQualityPreset is { Length: > 0 } preset) lines.Add($"画质预设：{preset}");
        lines.Add($"着色器档位：{(ShaderStateKnown ? ActiveShader?.DisplayName ?? "未启用" : "状态未知")}");
        if (ShaderStateKnown && ActiveShader is { } chain) lines.Add($"着色器链：{chain.Description}");

        // 任务书 3.7's line, on screen: the launch decision, then the current one when the window has moved
        // since. 「开播时是这一条，现在是这一条」 is exactly what a report about frame drops needs to say.
        if (_playback.LaunchShaderReason is { Length: > 0 } reason) lines.Add($"着色器判定：{reason}");
        if (CurrentShaderLine() is { Length: > 0 } current) lines.Add($"当前判定：{current}");
        lines.Add($"后端：{(Embedded ? "内置 libmpv" : "外部 mpv.exe")}");
        if (_playback.AudioDeviceInUse is { Length: > 0 } device) lines.Add($"音频输出设备：{device}");

        var options = _playback.LaunchOptions.Count == 0
            ? "（没有额外参数）"
            : string.Join("\n", _playback.LaunchOptions.Select(option => $"  {option.Key} = {option.Value}"));

        return string.Join("\n", lines) + "\n\nmpv 参数：\n" + options;
    }

    /// <summary>
    /// The 任务书 3.7 line as it stands right now, or an empty string when nothing has moved since the launch.
    /// Rebuilt rather than remembered, because the output size is the half of it that changes.
    /// </summary>
    private string CurrentShaderLine()
    {
        if (_outputWatch is not { } watch || _shaderContext is not { } context) return "";
        if (watch.Output == _launchOutput) return "";

        var video = context.Source.PrimaryVideoStream;
        var (width, height) = watch.Output;
        var measure = ShaderTier.Measure(video?.Width ?? 0, video?.Height ?? 0, width, height, watch.Tier);
        if (!ShaderStateKnown) return "着色器状态未知，请重新选择或重新播放";
        return ShaderTier.Explain(measure, ActiveShader?.Animated ?? false, (_shaderSettings ?? Settings.Shaders).Gpu, width, height, ActiveShader);
    }

    // ---- plumbing ---------------------------------------------------------------

    /// <summary>
    /// Marshals onto the UI thread. Every one of the player's events arrives from mpv's own loop, and all
    /// of them end in a bound property or an event the page answers by touching the visual tree.
    /// </summary>
    private void OnUi(Action action) => _ui.Run(() =>
    {
        if (!_lifetime.IsCancellationRequested) action();
    });

    private static string Glyph(int codepoint) => char.ConvertFromUtf32(codepoint);
}
