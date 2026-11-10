using System.Collections;

namespace Momoka.Tests;

/// <summary>Actual settings VM and generated rows; inert save/picker delegates, no tools or real settings.</summary>
internal static class PlayerToolsSettingsUiTests
{
    internal static void Register(SettingsPageStateTests.ShellFixture fixture)
    {
        PlayerCaptureGeometryTests.Register(fixture);
        TestHarness.Test("播放工具设置：三张卡在统一分类，未配置也保留路径恢复入口", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var cards = Cards(vm);
            Assert.Equal("视频导出,字幕同步,AI 字幕", string.Join(',', cards.Select(card => fixture.Get<string>(card, "Title"))));
            Assert.True(cards.All(card => fixture.Get<string>(card, "Category") == "播放工具"));
            Assert.True(Rows(cards).Any(row => fixture.Get<string>(row, "Label") == "ffmpeg 路径"));
            Assert.True(Rows(cards).Any(row => fixture.Get<string>(row, "Label") == "字幕同步工具"));
            Assert.True(Rows(cards).Any(row => fixture.Get<string>(row, "Label") == "faster-whisper 路径"));
            Assert.False(Rows(cards).Any(row => fixture.Get<string>(row, "Label") == "识别模型"));
            Assert.Equal(0, settings.TrySaveCalls);
            Assert.Contains("\"播放工具\"", fixture.ViewModelSource);
        });
        TestHarness.Test("播放工具设置：路径清理与保存失败保留偏好和重试", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings { NextSaveSucceeds = false };
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var cards = Cards(vm);
            foreach (var card in cards) fixture.List(vm, "Sections").Add(card);
            var row = Rows(cards).Single(item => fixture.Get<string>(item, "Label") == "ffmpeg 路径");
            fixture.Set(row, "Text", "  \"C:\\播放器工具\\ffmpeg.exe\"  ");
            Assert.Equal("C:\\播放器工具\\ffmpeg.exe", fixture.Get<string>(row, "Text"));
            Assert.Equal("C:\\播放器工具\\ffmpeg.exe", ToolValue(settings, "FfmpegPath"));
            Assert.True(fixture.Get<bool>(vm, "HasUnsavedChanges"));
            Assert.True(fixture.Command(vm, "RetrySaveCommand").CanExecute(null));
            Assert.Equal(1, settings.TrySaveCalls);
            Assert.True(Rows(cards).Where(item => fixture.Get<string>(item, "Label") == "ffmpeg 状态")
                .All(item => fixture.Get<string>(item, "Value").Contains("未找到", StringComparison.Ordinal)));
            settings.NextSaveSucceeds = true;
            fixture.Command(vm, "RetrySaveCommand").Execute(null);
            Assert.False(fixture.Get<bool>(vm, "HasUnsavedChanges"));
            Assert.Equal("C:\\播放器工具\\ffmpeg.exe", ToolValue(settings, "FfmpegPath"));
        });
        TestHarness.Test("播放工具设置：识别程序可用后显示参数，隐藏恢复保留选择", () =>
        {
            var directory = Path.Combine(fixture.Root, "artifacts", "player-tools-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "faster-whisper-xxl.exe");
            File.WriteAllText(path, "inert test file; never executed");
            try
            {
                var settings = new SettingsPageStateTests.FakeSettings();
                SetTool(settings, "FasterWhisperPath", path);
                SetTool(settings, "WhisperModel", "medium");
                SetTool(settings, "WhisperDevice", "cuda");
                SetTool(settings, "WhisperLanguage", "ja");
                var vm = fixture.NewViewModel();
                fixture.Attach(vm, settings, fixture.NewLauncher());
                var cards = Cards(vm);
                foreach (var card in cards) fixture.List(vm, "Sections").Add(card);
                var model = Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "识别模型");
                Assert.Equal("medium", fixture.Get<string>(fixture.Get<object>(model, "Selected"), "Label"));
                var program = Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "faster-whisper 路径");
                fixture.Set(program, "Text", "");
                Assert.False(Rows(cards).Any(row => fixture.Get<string>(row, "Label") == "识别模型"));
                Assert.Equal("medium", ToolValue(settings, "WhisperModel"));
                fixture.Set(program, "Text", path);
                Assert.True(Rows(cards).Any(row => fixture.Get<string>(row, "Label") == "识别设备"));
                Assert.Equal("cuda", ToolValue(settings, "WhisperDevice"));
                Assert.Equal("ja", ToolValue(settings, "WhisperLanguage"));
            }
            finally { File.Delete(path); Directory.Delete(directory); }
        });
        TestHarness.Test("播放工具设置：选择取消不保存，离页后的迟到文件选择不改配置", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var picked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes = 0;
            fixture.Invoke(vm, "UsePlayerToolsPickers", (Func<Task<string?>>)(() => Task.FromResult<string?>(null)),
                (Func<Task<string?>>)(() => Task.FromResult<string?>(null)));
            ((Task)fixture.Invoke(vm, "PickPlayerToolPathAsync", false, (Action<string>)(_ => writes++))!).GetAwaiter().GetResult();
            Assert.Equal(0, writes);
            Assert.Equal(0, settings.TrySaveCalls);
            fixture.Invoke(vm, "UsePlayerToolsPickers", (Func<Task<string?>>)(() => picked.Task),
                (Func<Task<string?>>)(() => Task.FromResult<string?>(null)));
            var delayed = (Task)fixture.Invoke(vm, "PickPlayerToolPathAsync", false, (Action<string>)(_ => writes++))!;
            fixture.Invoke(vm, "Cancel");
            fixture.Invoke(vm, "Activate");
            picked.SetResult("C:\\unknown\\late.exe");
            delayed.GetAwaiter().GetResult();
            Assert.Equal(0, writes);
            Assert.Equal(0, settings.TrySaveCalls);
            Assert.False(fixture.Get<bool>(vm, "NoticeOpen"));
        });
        TestHarness.Test("播放工具设置：程序选择更新现有路径行，输出目录状态不创建文件", () =>
        {
            var directory = Path.Combine(fixture.Root, "artifacts", "player-tools-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "ffmpeg.exe");
            File.WriteAllText(path, "inert picker result; never executed");
            try
            {
                var settings = new SettingsPageStateTests.FakeSettings();
                var vm = fixture.NewViewModel();
                fixture.Attach(vm, settings, fixture.NewLauncher());
                fixture.Invoke(vm, "UsePlayerToolsPickers", (Func<Task<string?>>)(() => Task.FromResult<string?>(path)),
                    (Func<Task<string?>>)(() => Task.FromResult<string?>(directory)));
                var cards = Cards(vm);
                foreach (var card in cards) fixture.List(vm, "Sections").Add(card);
                var pathRow = Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "ffmpeg 路径");
                var program = Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "ffmpeg 状态");
                fixture.Command(program, "RunCommand").Execute(null);
                Assert.Equal(path, ToolValue(settings, "FfmpegPath"));
                Assert.Equal(path, fixture.Get<string>(pathRow, "Text"));
                Assert.True(ReferenceEquals(pathRow, Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "ffmpeg 路径")));
                var folder = Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "保存目录状态");
                fixture.Command(folder, "RunCommand").Execute(null);
                Assert.Equal(directory, ToolValue(settings, "OutputDirectory"));
                Assert.Equal("已找到本地文件夹", fixture.Get<string>(folder, "Value"));
                Assert.Equal(1, Directory.GetFiles(directory).Length);
                var output = Rows(cards).Single(row => fixture.Get<string>(row, "Label") == "导出保存目录");
                fixture.Set(output, "Text", "C:\\invalid\0directory");
                Assert.Equal("未找到可用的本地文件夹", fixture.Get<string>(folder, "Value"));
            }
            finally { File.Delete(path); Directory.Delete(directory); }
        });

        object[] Cards(object vm) => ((IEnumerable)fixture.Invoke(vm, "PlayerToolsSections")!).Cast<object>().ToArray();
        IEnumerable<object> Rows(IEnumerable<object> cards) => cards.SelectMany(card => fixture.List(card, "Rows").Cast<object>());
        static string ToolValue(SettingsPageStateTests.FakeSettings settings, string property)
        {
            var tools = settings.Settings.GetType().GetProperty("PlayerTools")!.GetValue(settings.Settings)!;
            return (string)tools.GetType().GetProperty(property)!.GetValue(tools)!;
        }
        static void SetTool(SettingsPageStateTests.FakeSettings settings, string property, string value)
        {
            var tools = settings.Settings.GetType().GetProperty("PlayerTools")!.GetValue(settings.Settings)!;
            tools.GetType().GetProperty(property)!.SetValue(tools, value);
        }
    }
}
