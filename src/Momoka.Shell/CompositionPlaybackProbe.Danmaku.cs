using System.Text.Json;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Windowing;

namespace Momoka.Shell;

internal static partial class CompositionPlaybackProbe
{
    /// <summary>实际本地后端、独立 Lua 层、播放时钟和截图；没有凭据、服务器请求或播放上报。</summary>
    private static async Task CheckDanmakuAsync(IPlaybackHandle handle, HostWindow window, string output,
        CancellationToken token, Action<string> write)
    {
        if (handle is not IPlayerControl control || handle is not IPlayerHostMessages host)
            throw new InvalidOperationException("本地播放器没有弹幕控制与回执通道");
        var ready = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receipt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string key, string value)
        {
            if (key == MpvDanmaku.Ready && long.TryParse(value, out var serial)) ready.TrySetResult(serial);
            if (key == MpvDanmaku.Loaded) receipt.TrySetResult(value);
        }
        host.HostMessageReceived += Message;
        try
        {
            await Send("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Ping);
            var serial = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            var file = serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await handle.SetPropertyAsync("pause", true, token);
            var position = await handle.GetPositionAsync(token) ?? throw new InvalidOperationException("本地视频没有播放时钟");
            var sid = await handle.GetTextAsync("sid", token);
            var opts = JsonSerializer.Serialize(new { show = true, opacity = 85, fontSize = 36, speed = 1, area = 50, density = 40 });
            var comments = JsonSerializer.Serialize(new[]
            {
                new { time = Math.Max(0, position - 0.5), mode = 1, color = 0xFFFFFF, text = "Momoka 弹幕测试：独立图层，不占用字幕轨道" },
                new { time = Math.Max(0, position - 0.5), mode = 5, color = 0xFFCC66, text = "顶部弹幕 · 半屏区域" },
                new { time = Math.Max(0, position - 0.5), mode = 4, color = 0xFFFFFF, text = "底部弹幕 · 暂停和拖动按视频时间同步" },
            });
            await Send("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Begin, file, "1", opts);
            await Send("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Chunk, file, "1", comments);
            await Send("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Commit, file, "1");
            var loaded = await receipt.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            if (loaded != $"{serial}:1:3") throw new InvalidOperationException("弹幕图层没有确认本次三条测试弹幕");
            if (sid != await handle.GetTextAsync("sid", token)) throw new InvalidOperationException("弹幕加载改变了字幕轨道");
            write("通过：实际 libmpv 弹幕图层已确认三种模式，字幕 sid 未改变");
            await Task.Delay(200, token);
            var shown = CapturePixels(window);
            await SaveScreenAsync(window, Path.Combine(output, "composition-danmaku-visible-screen.png"));
            await Send("cycle", "sub-visibility");
            await Task.Delay(120, token);
            var subtitlesOff = CapturePixels(window);
            if (changed(shown, subtitlesOff) > 0.001) throw new InvalidOperationException("字幕开关改变了独立弹幕层");
            await Send("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Clear, file, "2");
            await Task.Delay(200, token);
            var cleared = CapturePixels(window);
            if (changed(shown, cleared) < 0.0001) throw new InvalidOperationException("没有检测到弹幕图层实际呈现的像素");
            await SaveScreenAsync(window, Path.Combine(output, "composition-danmaku-hidden-screen.png"));
            write("通过：弹幕确有实屏像素，字幕开关不影响弹幕，清除图层后测试文字消失");
            await handle.SetPropertyAsync("pause", false, token);
        }
        finally { host.HostMessageReceived -= Message; }

        async Task Send(params string[] command)
        {
            if (!await control.CommandAsync(command, token)) throw new InvalidOperationException("弹幕/快捷键探针命令被拒绝：" + command[0]);
        }

        static double changed(ScreenPixels left, ScreenPixels right)
        {
            if (left.Width != right.Width || left.Height != right.Height) throw new InvalidOperationException("截图期间窗口尺寸发生变化");
            var count = 0;
            for (var offset = 0; offset < left.Bytes.Length; offset += 4)
                if (Math.Abs(left.Bytes[offset] - right.Bytes[offset]) + Math.Abs(left.Bytes[offset + 1] - right.Bytes[offset + 1])
                    + Math.Abs(left.Bytes[offset + 2] - right.Bytes[offset + 2]) > 30) count++;
            return (double)count / (left.Width * left.Height);
        }
    }
}
