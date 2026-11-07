using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.PlaybackLifecycleTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ExternalSubtitleSelectionTests
{
    internal static void Register()
    {
        Test("R13-003 外挂字幕在起播先禁用预测编号", () =>
        {
            using var f = new Fixture();
            var ticket = SelectionTicket();
            var request = new PlaybackPlanner(f.Settings, new ShaderGroupResolver(f.Settings.Shaders)).Plan(ticket, f.Session.Connection!);
            Assert.Null(request.SubtitleId, "外部轨不能按挂载前预测编号选择");
            Assert.Contains("--sid=no", string.Join(' ', MpvArgumentBuilder.Build(request, "fixture")));
            var overridden = request with { PlayerOptions = [.. request.PlayerOptions, new("sid", "2")] };
            Assert.Equal("--sid=no", MpvArgumentBuilder.Build(overridden, "fixture").Last(), "ordinary options cannot revive a predicted ID");
            Assert.Equal("no", InlineSwitch.PerFile(request).Single(option => option.Key == "sid").Value);
        });
        foreach (var missingTarget in new[] { false, true })
            Test("R13-003 前一外部字幕失败按实际文件选择 " + missingTarget, () => Run(async () =>
            {
                using var f = new Fixture();
                var ticket = SelectionTicket();
                var planned = new PlaybackPlanner(f.Settings, new ShaderGroupResolver(f.Settings.Shaders)).Plan(ticket, f.Session.Connection!);
                var command = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var writes = new List<string[]>();
                var target = planned.ExternalSubtitles[1].AbsoluteUri;
                var third = planned.ExternalSubtitles[2].AbsoluteUri;
                var handle = new Handle
                {
                    CurrentTracks = missingTarget
                        ? [new(1, "sub", null, null, false, false) { External = true, ExternalFilename = third }]
                        : [new(1, "sub", null, null, false, false) { External = true, ExternalFilename = target },
                           new(2, "sub", null, null, false, false) { External = true, ExternalFilename = third }],
                    OnCommand = (args, _) => { writes.Add(args.ToArray()); command.TrySetResult(); return Task.FromResult(true); }
                };
                handle.Publish(0);
                f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var play = f.Service.PlayAsync(ticket, default);
                await handle.Started.Task;
                if (!missingTarget) await command.Task.WaitAsync(TimeSpan.FromSeconds(1));
                else await Task.Delay(50);
                Assert.Equal(missingTarget ? 0 : 1, writes.Count);
                if (!missingTarget) Assert.Equal("set sid 1", string.Join(' ', writes.Single()));
                handle.End(); await play;
            }));
        foreach (var manual in new[] { false, true })
            Test("R13-003 延迟目标出现只初次选轨，手动选择以后不覆盖 " + manual, () => Run(async () =>
            {
                using var f = new Fixture();
                var ticket = SelectionTicket();
                var planned = new PlaybackPlanner(f.Settings, new ShaderGroupResolver(f.Settings.Shaders)).Plan(ticket, f.Session.Connection!);
                var writes = new List<string[]>();
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var handle = new Handle { OnCommand = (args, _) => { writes.Add(args.ToArray()); signal.TrySetResult(); return Task.FromResult(true); } };
                handle.Publish(0);
                f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var play = f.Service.PlayAsync(ticket, default); await handle.Started.Task;
                if (manual) await f.Service.SetPropertyAsync("sid", 7);
                MpvTrack[] tracks = [new(1, "sub", null, null, false, false) { External = true, ExternalFilename = planned.ExternalSubtitles[1].AbsoluteUri }];
                handle.PublishTracks(tracks);
                if (!manual) await signal.Task.WaitAsync(TimeSpan.FromSeconds(1));
                handle.PublishTracks(tracks);
                await Task.Delay(40);
                Assert.Equal(manual ? 0 : 1, writes.Count);
                handle.End(); await play;
            }));
        foreach (var mode in new[] { "duplicate", "manual", "changed-file", "rejected" })
            Test("R13-003 实际选轨拒绝模糊目标与旧文件 " + mode, () => Run(async () =>
            {
                using var f = new Fixture(); var ticket = SelectionTicket();
                var planned = new PlaybackPlanner(f.Settings, new ShaderGroupResolver(f.Settings.Shaders)).Plan(ticket, f.Session.Connection!);
                var tracks = new List<MpvTrack> { new(1, "sub", null, null, false, false) { External = true, ExternalFilename = planned.ExternalSubtitles[1].AbsoluteUri } };
                if (mode == "duplicate") tracks.Add(new(2, "sub", null, null, false, false) { External = true, ExternalFilename = planned.ExternalSubtitles[1].AbsoluteUri });
                if (mode == "manual") tracks.Add(new(7, "sub", null, null, false, true) { MainSelection = 0 });
                var writes = 0;
                var handle = new Handle { OnCommand = (_, _) => { writes++; return Task.FromResult(mode != "rejected"); } };
                handle.Publish(0);
                f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
                var play = f.Service.PlayAsync(ticket, default); await handle.Started.Task;
                if (mode == "changed-file") handle.FileGeneration++;
                handle.PublishTracks(tracks); await Task.Delay(50);
                handle.PublishTracks(tracks); await Task.Delay(30);
                Assert.Equal(mode == "rejected" ? 1 : 0, writes);
                if (mode == "changed-file") Assert.Equal(0L, handle.FileCommands.Single());
                handle.End(); await play;
            }));
        Test("R13-003 尚未file-loaded的旧轨道不能成为本片选择", () => Run(async () =>
        {
            using var f = new Fixture(); var ticket = SelectionTicket();
            var planned = new PlaybackPlanner(f.Settings, new ShaderGroupResolver(f.Settings.Shaders)).Plan(ticket, f.Session.Connection!);
            var writes = 0;
            MpvTrack[] tracks = [new(4, "sub", null, null, false, false) { External = true, ExternalFilename = planned.ExternalSubtitles[1].AbsoluteUri }];
            var handle = new Handle { CurrentTracks = tracks, OnCommand = (_, _) => { writes++; return Task.FromResult(true); } };
            f.Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(handle);
            var play = f.Service.PlayAsync(ticket, default); await handle.Started.Task;
            Assert.Equal(0, writes);
            handle.Publish(0); handle.PublishTracks(tracks); await Task.Delay(50);
            Assert.Equal(1, writes);
            handle.End(); await play;
        }));
    }

    private static PlaybackTicket SelectionTicket()
    {
        var source = new MediaSource
        {
            Id = "source", Container = "mkv", RunTimeTicks = 100 * TimeSpan.TicksPerSecond,
            MediaStreams = [
                new() { Index = 1, Type = "Subtitle", Codec = "srt", IsExternal = true },
                new() { Index = 2, Type = "Subtitle", Codec = "srt", IsExternal = true },
                new() { Index = 3, Type = "Subtitle", Codec = "srt", IsExternal = true }
            ]
        };
        return new() { Item = new EmbyItem { Id = "item", Name = "item", Type = EmbyItemType.Movie, MediaSources = [source] }, Source = source, SubtitleStreamIndex = 2 };
    }
    private static void Run(Func<Task> test) => test().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
}
