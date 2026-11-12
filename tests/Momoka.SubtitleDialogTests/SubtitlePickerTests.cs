using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SubtitlePickerTests
{
    internal static void Register()
    {
        foreach (var stage in new[] { "before", "pick", "properties", "open", "load", "read" })
            Test("生产字幕选择器 页面失效阶段 " + stage, () =>
            {
                using var ui = new UiQueue();
                ui.Run(async () =>
                {
                    SubtitlePickerBoundary.Reset();
                    SubtitlePickerBoundary.Pick = () => Task.FromResult<ServerSubtitleFile?>(File());
                    var current = stage != "before";
                    Action invalidate = () => current = false;
                    switch (stage)
                    {
                        case "pick": SubtitlePickerBoundary.AfterPick = invalidate; break;
                        case "properties": SubtitlePickerBoundary.AfterProperties = invalidate; break;
                        case "open": SubtitlePickerBoundary.AfterOpen = invalidate; break;
                        case "load": SubtitlePickerBoundary.AfterLoad = invalidate; break;
                        case "read": SubtitlePickerBoundary.AfterRead = invalidate; break;
                    }
                    var picked = await ServerSubtitlePicker.PickAsync(new XamlRoot(), () => current);
                    Assert.Null(picked);
                    Assert.Equal(stage == "before" ? 0 : 1, SubtitlePickerBoundary.Picks);
                    Assert.Equal(stage is "properties" or "open" or "load" or "read" ? 1 : 0, SubtitlePickerBoundary.Properties);
                    Assert.Equal(stage is "open" or "load" or "read" ? 1 : 0, SubtitlePickerBoundary.Opens);
                    Assert.Equal(stage is "load" or "read" ? 1 : 0, SubtitlePickerBoundary.Loads);
                    Assert.Equal(stage == "read" ? 1 : 0, SubtitlePickerBoundary.Reads);
                    Assert.Equal(SubtitlePickerBoundary.Opens, SubtitlePickerBoundary.StreamsDisposed);
                    Assert.Equal(SubtitlePickerBoundary.Loads, SubtitlePickerBoundary.ReadersDisposed);
                    SubtitlePickerBoundary.Reset();
                });
            });

        Test("生产字幕选择器 原字节正常读取并释放", () =>
        {
            using var ui = new UiQueue();
            ui.Run(async () =>
            {
                SubtitlePickerBoundary.Reset();
                SubtitlePickerBoundary.Pick = () => Task.FromResult<ServerSubtitleFile?>(File());
                var picked = await ServerSubtitlePicker.PickAsync(new XamlRoot(), () => true);
                Assert.Equal(File().Data, picked!.Data);
                Assert.Equal(1, SubtitlePickerBoundary.Reads);
                Assert.Equal(1, SubtitlePickerBoundary.StreamsDisposed);
                Assert.Equal(1, SubtitlePickerBoundary.ReadersDisposed);
                SubtitlePickerBoundary.Reset();
            });
        });

        foreach (var size in new[] { 0UL, (ulong)ServerSubtitleFile.MaxBytes + 1 })
            Test("生产字幕选择器 超界尺寸不打开读取 " + size, () =>
            {
                using var ui = new UiQueue();
                ui.Run(async () =>
                {
                    SubtitlePickerBoundary.Reset();
                    SubtitlePickerBoundary.Pick = () => Task.FromResult<ServerSubtitleFile?>(File());
                    SubtitlePickerBoundary.Size = size;
                    try
                    {
                        await ServerSubtitlePicker.PickAsync(new XamlRoot(), () => true);
                        throw new Exception("Invalid size was accepted");
                    }
                    catch (InvalidOperationException) { }
                    Assert.Equal(0, SubtitlePickerBoundary.Opens);
                    Assert.Equal(0, SubtitlePickerBoundary.Loads);
                    SubtitlePickerBoundary.Reset();
                });
            });

        Test("生产字幕选择器 短读取不创建上传快照", () =>
        {
            using var ui = new UiQueue();
            ui.Run(async () =>
            {
                SubtitlePickerBoundary.Reset();
                SubtitlePickerBoundary.Pick = () => Task.FromResult<ServerSubtitleFile?>(File());
                SubtitlePickerBoundary.Loaded = 1;
                try
                {
                    await ServerSubtitlePicker.PickAsync(new XamlRoot(), () => true);
                    throw new Exception("Short read was accepted");
                }
                catch (InvalidOperationException) { }
                Assert.Equal(0, SubtitlePickerBoundary.Reads);
                Assert.Equal(1, SubtitlePickerBoundary.StreamsDisposed);
                Assert.Equal(1, SubtitlePickerBoundary.ReadersDisposed);
                SubtitlePickerBoundary.Reset();
            });
        });
    }

    private static ServerSubtitleFile File() => new("原字节.srt", [0xff, 0xfe, 0x2d, 0x4e, 0x87, 0x65, 0, 0x0a]);
}
