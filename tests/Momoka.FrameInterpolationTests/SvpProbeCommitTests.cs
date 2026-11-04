using System.Security.Cryptography;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Shell;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SvpProbeCommitTests
{
    internal static void Register()
    {
        VerifyInputs();
        Test("SVP 提交：运行源码哈希必须对应当前工作树", VerifyInputs);
        Test("SVP 提交：真实文本行规范化、一次保存及退订", () => Run(async () =>
        {
            using var fixture = new FocusFixture();
            await fixture.Run();
            fixture.Passed();
        }));
        Test("SVP 提交：Focus 返回成功但焦点未到时不得编辑", () => Run(async () =>
        {
            using var fixture = new FocusFixture { DelayFocus = true };
            var running = fixture.Run();
            try
            {
                Assert.Equal(0, fixture.Editor.TextWrites);
                Assert.Equal(0, fixture.NextCalls);
                Assert.False(running.IsCompleted);
            }
            finally { FocusManager.Current = fixture.Editor; await running; }
            fixture.Passed();
        }));
        Test("SVP 提交：目标已有焦点但真实失焦未到时不得验证", () => Run(async () =>
        {
            using var fixture = new FocusFixture { DelayLost = true };
            var running = fixture.Run();
            try
            {
                await fixture.NextRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(0, fixture.VerifyCalls);
                Assert.False(running.IsCompleted);
            }
            finally { fixture.CommitLost(); await running; }
            fixture.Passed();
        }));
        Test("SVP 提交：缺少真实焦点严格超时且零编辑", () => Run(async () =>
        {
            using var fixture = new FocusFixture { DelayFocus = true };
            await Failure<TimeoutException>(fixture.Run());
            Assert.Equal(0, fixture.Editor.TextWrites);
            Assert.Equal(0, fixture.VerifyCalls);
            Assert.Equal(0, fixture.Editor.LostSubscribers);
        }));
        Test("SVP 提交：缺失焦事件严格超时并退订", () => Run(async () =>
        {
            using var fixture = new FocusFixture { DelayLost = true };
            await Failure<TimeoutException>(fixture.Run());
            Assert.Equal(0, fixture.VerifyCalls);
            Assert.Equal(0, fixture.Editor.LostSubscribers);
        }));
        Test("SVP 提交：保存或模型失败保留原异常，不轮询重试", () => Run(async () =>
        {
            using var fixture = new FocusFixture();
            var error = new IOException("anonymous save denied");
            fixture.Verify = () => Task.FromException(error);
            Assert.True(ReferenceEquals(error, await Failure<IOException>(fixture.Run())));
            Assert.Equal(1, fixture.VerifyCalls);
            Assert.Equal(1, fixture.NextCalls);
            Assert.Equal(0, fixture.Editor.LostSubscribers);
            Assert.False(fixture.Stages.Contains("commit-verified"));
        }));
        Test("SVP 提交：拒绝移交焦点仍退订", () => Run(async () =>
        {
            using var fixture = new FocusFixture { RejectNext = true };
            await Failure<InvalidOperationException>(fixture.Run());
            Assert.Equal(0, fixture.VerifyCalls);
            Assert.Equal(0, fixture.Editor.LostSubscribers);
        }));
        Test("SVP 提交：异步验证完成前不能报告成功", () => Run(async () =>
        {
            using var fixture = new FocusFixture();
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Verify = () => complete.Task;
            var running = fixture.Run();
            Assert.Equal(1, fixture.VerifyCalls);
            Assert.False(running.IsCompleted);
            Assert.False(fixture.Stages.Contains("commit-verified"));
            complete.SetResult();
            await running;
            Assert.Equal(0, fixture.Editor.LostSubscribers);
        }));
    }

    private static void VerifyInputs()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string Hash(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root!.FullName, "src/Momoka.Shell", name))));
        Assert.Equal(Hash("ShellNavigationProbe.Svp.cs"), ShellNavigationProbe.SvpHash);
        Assert.Equal(Hash("ShellNavigationProbe.cs"), ShellNavigationProbe.SharedHash);
    }

    // This console harness has no UI SynchronizationContext; synchronously unwrap each bounded test.
#pragma warning disable RS0030
    private static void Run(Func<Task> test) => test().WaitAsync(TimeSpan.FromSeconds(6)).GetAwaiter().GetResult();
#pragma warning restore RS0030

    private static async Task<T> Failure<T>(Task task) where T : Exception
    {
        try { await task; }
        catch (T error) { return error; }
        throw new AssertionException("Expected failure " + typeof(T).Name);
    }

    private sealed class FocusFixture : IDisposable
    {
        internal TextBox Editor { get; } = new();
        internal ToggleSwitch Next { get; } = new();
        internal SettingsViewModel Vm { get; } = new();
        internal SettingTextRow Row { get; }
        internal int NextCalls, VerifyCalls;
        internal bool DelayFocus, DelayLost, RejectNext;
        internal Func<Task>? Verify;
        internal TaskCompletionSource NextRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Stages { get; } = [];

        internal FocusFixture()
        {
            FocusManager.Current = null;
            Vm.Settings.Mpv.SvpEnabled = true;
            Vm.Settings.Mpv.SvpVapourSynthPath = "initial-anonymous-component";
            Row = (SettingTextRow)Vm.BuildCard().Rows.Single(row => row.Label == "VapourSynth 组件");
            Editor.FocusRequested = () => { if (!DelayFocus) FocusManager.Current = Editor; return true; };
            Next.FocusRequested = () =>
            {
                NextCalls++;
                NextRequested.TrySetResult();
                if (RejectNext) return false;
                FocusManager.Current = Next;
                if (!DelayLost) CommitLost();
                return true;
            };
        }

        internal Task Run() => ShellNavigationProbe.CommitSvpForFixture(Editor, Next,
            " \"unavailable-anonymous-component\" ", async () =>
            {
                VerifyCalls++;
                if (Verify is not null) { await Verify(); return; }
                Assert.Equal("unavailable-anonymous-component", Row.Text);
                Assert.Equal(Row.Text, Vm.Settings.Mpv.SvpVapourSynthPath);
                Assert.Equal(1, Vm.SaveCount);
            }, Stages.Add);

        // The framework binding endpoint is the substitute; row cleanup/save execute real sources.
        internal void CommitLost() { Row.Text = Editor.Text; Editor.RaiseLost(); }
        internal void Passed()
        {
            Assert.Equal(1, VerifyCalls);
            Assert.Equal(1, NextCalls);
            Assert.Equal(0, Editor.LostSubscribers);
            Assert.True(Stages.Contains("commit-verified"));
        }
        public void Dispose() => FocusManager.Current = null;
    }
}
