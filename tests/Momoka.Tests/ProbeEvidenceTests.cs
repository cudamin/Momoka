using Momoka.Shell;
using System.Security.Cryptography;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ProbeEvidenceTests
{
    internal static void Register()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (var (path, hash) in ProbeEvidenceInputs.Hashes)
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root!.FullName, path)))),
                "建后源码发生变化，拒绝旧探针回归产物：" + path);
        Test("光标探针：外国目标即使本窗在前台也不得注入", () => Run(async () =>
        {
            var state = NewState();
            state.Owner = ProbeEvidenceState.Foreign;
            var error = await Failure(() => Input(state, 30));
            Assert.NotNull(error);
            Assert.Equal(0, state.Moves.Count);
            Assert.Equal(0, state.Warps);
            Assert.Equal(0, state.Nudges);
        }));
        Test("光标探针：相同根句柄但不同进程拒绝且零注入", () => Run(async () =>
        {
            var state = NewState();
            state.OwnerProcess = (uint)Environment.ProcessId + 1;
            Assert.NotNull(await Failure(() => Input(state, 30)));
            Assert.Equal(0, state.Moves.Count);
        }));
        Test("光标探针：等待实际前台，尚未取得时不发送移动", () => Run(async () =>
        {
            var state = NewState();
            state.Foreground = ProbeEvidenceState.Foreign;
            using var stopping = new CancellationTokenSource();
            var input = Input(state, 500, stopping.Token);
            try
            {
                Assert.Equal(0, state.Moves.Count, "前台仍属于外国窗口");
                Assert.False(input.IsCompleted, "准备必须等待真实前台");
                state.Foreground = ProbeEvidenceState.Own;
                await input;
                Assert.Equal(1, state.Moves.Count);
                Assert.Equal((25, 40), state.Moves[0]);
            }
            finally
            {
                stopping.Cancel();
                await Failure(() => input);
            }
        }));
        Test("光标探针：前台截止和取消均不注入", () => Run(async () =>
        {
            var state = NewState();
            state.Foreground = ProbeEvidenceState.Foreign;
            Assert.True(await Failure(() => Input(state, 20)) is TimeoutException);
            Assert.Equal(0, state.Moves.Count);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.True(await Failure(() => Input(state, 20, canceled.Token)) is OperationCanceledException);
            Assert.Equal(0, state.Moves.Count);
        }));
        Test("光标探针：移动中被外国窗口盖住后不再输入", () => Run(async () =>
        {
            var state = NewState();
            await CursorVisibilityProbe.PrepareForFixture(state.Window, [new() { X = 25, Y = 40 }],
                state.Log.Add, CancellationToken.None, 30);
            state.AfterMove = () => state.Owner = ProbeEvidenceState.Foreign;
            var error = await Failure(async () =>
            {
                await CursorVisibilityProbe.MoveForFixture(state.Window, 25, 40, state.Log.Add, CancellationToken.None);
                await CursorVisibilityProbe.MoveForFixture(state.Window, 35, 40, state.Log.Add, CancellationToken.None);
            });
            Assert.NotNull(error);
            Assert.Equal(1, state.Moves.Count);
        }));
        Test("光标探针：输入未到位且失去归属时禁止后备搬运", () => Run(async () =>
        {
            var state = NewState();
            state.MoveReachesTarget = false;
            state.AfterMove = () => state.Owner = ProbeEvidenceState.Foreign;
            Assert.NotNull(await Failure(() => Input(state, 30)));
            Assert.Equal(1, state.Moves.Count);
            Assert.Equal(0, state.Warps);
            Assert.Equal(0, state.Nudges);
        }));
        Test("光标探针：位置读取失败不能当作到位或开始后备搬运", () => Run(async () =>
        {
            var state = NewState();
            state.PositionReadSucceeds = false;
            var failure = await Failure(() => Input(state, 30));
            Assert.NotNull(failure);
            Assert.Equal(1, state.Moves.Count);
            Assert.Equal(0, state.Warps);
            Assert.Equal(0, state.Nudges);
        }));

        Test("保留帧证据：颜色RGB和PNG来自唯一一次捕获", () => Run(async () =>
        {
            var state = NewState();
            var first = Bars();
            state.Frames.Enqueue(first);
            state.Frames.Enqueue(new(100, 100, new byte[40000]));
            var read = await CompositionPlaybackProbe.ReadEdgeEvidenceAsync(state.Window, "retained.png", state.Log.Add);
            Assert.Equal("RGMC", read);
            Assert.Equal(1, state.Captures);
            Assert.True(ReferenceEquals(first.Bytes, state.EncodedBytes));
            Assert.Contains("#00FF00", string.Join("\n", state.Log));
        }));
        Test("保留帧证据：四角正常而真实25%采样点被遮挡也拒绝", () => Run(async () =>
        {
            var state = NewState();
            state.Frames.Enqueue(Bars());
            state.Frames.Enqueue(Bars());
            state.OwnerAt = point => point.X == 1025 && point.Y == 240
                ? ProbeEvidenceState.Foreign : ProbeEvidenceState.Own;
            Assert.NotNull(await Failure(() => CompositionPlaybackProbe.ReadEdgeEvidenceAsync(
                state.Window, "covered.png", state.Log.Add)));
            Assert.NotNull(state.EncodedBytes, "失败那一帧仍须保存");
            Assert.Contains("1025,240", string.Join("\n", state.Log));
            Assert.Equal(1, state.Captures);
        }));
        Test("保留帧证据：实际采样点按客户区原点换成屏幕坐标", () => Run(async () =>
        {
            var state = NewState();
            state.Frames.Enqueue(Bars());
            state.Frames.Enqueue(Bars());
            Assert.Equal("RGMC", await CompositionPlaybackProbe.ReadEdgeEvidenceAsync(
                state.Window, "owned.png", state.Log.Add));
            Assert.Equal("1008,240;1025,240;1074,240;1091,240",
                string.Join(';', state.OwnerQueries.Select(point => $"{point.X},{point.Y}")));
        }));
        Test("保留帧证据：本窗黑点仍被正式原断言拒绝，完整彩条通过", () => Run(async () =>
        {
            var state = NewState();
            state.Frames.Enqueue(Bars(blackSecond: true));
            state.Frames.Enqueue(Bars(blackSecond: true));
            var read = await CompositionPlaybackProbe.ReadEdgeEvidenceAsync(state.Window, "black.png", state.Log.Add);
            Assert.Equal("RKMC", read);
            Assert.Throws<InvalidOperationException>(() => RetainedEvidenceCaller.Validate(read, state.Log.Add));
            RetainedEvidenceCaller.Validate("RGMC", state.Log.Add);
            Assert.NotNull(state.EncodedBytes);
        }));
        Test("保留帧证据：捕获后窗口位置变化仍检查捕获原点", () => Run(async () =>
        {
            var state = NewState();
            state.Frames.Enqueue(Bars());
            state.CurrentOriginX = 1500;
            state.CurrentOriginY = 500;
            Assert.Equal("RGMC", await CompositionPlaybackProbe.ReadEdgeEvidenceAsync(state.Window, "moved.png", state.Log.Add));
            Assert.Equal("1008,240;1025,240;1074,240;1091,240",
                string.Join(';', state.OwnerQueries.Select(point => $"{point.X},{point.Y}")));
        }));
        Test("保留帧证据：捕获或保存失败不能返回成功", () => Run(async () =>
        {
            var state = NewState();
            state.CaptureFailure = new IOException("capture failed");
            Assert.True(await Failure(() => CompositionPlaybackProbe.ReadEdgeEvidenceAsync(
                state.Window, "capture.png", state.Log.Add)) is IOException);
            state.CaptureFailure = null;
            state.Frames.Enqueue(Bars());
            state.Frames.Enqueue(Bars());
            state.EncodeFailure = new IOException("encode failed");
            Assert.True(await Failure(() => CompositionPlaybackProbe.ReadEdgeEvidenceAsync(
                state.Window, "encode.png", state.Log.Add)) is IOException);
        }));
    }

    private static ProbeEvidenceState NewState()
    {
        var state = new ProbeEvidenceState();
        ProbeEvidenceNative.State = state;
        return state;
    }

    private static async Task Input(ProbeEvidenceState state, int timeout, CancellationToken token = default)
    {
        await CursorVisibilityProbe.PrepareForFixture(state.Window, [new() { X = 25, Y = 40 }], state.Log.Add, token, timeout);
        await CursorVisibilityProbe.MoveForFixture(state.Window, 25, 40, state.Log.Add, token);
    }

    private static ProbeEvidenceFrame Bars(bool blackSecond = false)
    {
        var bytes = new byte[40000];
        foreach (var (x, red, green, blue) in new[] { (8, 255, 0, 0), (25, 0, blackSecond ? 0 : 255, 0),
                     (74, 255, 0, 255), (91, 0, 255, 255) })
        {
            var offset = (40 * 100 + x) * 4;
            bytes[offset] = (byte)blue;
            bytes[offset + 1] = (byte)green;
            bytes[offset + 2] = (byte)red;
        }
        return new(100, 100, bytes);
    }

    private static void Run(Func<Task> run) => run().GetAwaiter().GetResult();

    private static async Task<Exception?> Failure(Func<Task> run)
    {
        try { await run(); return null; }
        catch (Exception error) { return error; }
    }
}

internal readonly record struct ProbeEvidenceFrame(int Width, int Height, byte[] Bytes)
{
    internal int OriginX { get; init; } = 1000;
    internal int OriginY { get; init; } = 200;
}

internal struct ProbeEvidencePoint
{
    internal int X;
    internal int Y;
}

internal sealed class ProbeEvidenceWindow
{
    internal IntPtr Handle => ProbeEvidenceState.Own;
    internal bool TopMost { get; set; }
    internal void Activate() => ProbeEvidenceNative.State.Activations++;
}

internal sealed class ProbeEvidenceState
{
    internal static readonly IntPtr Own = new(0x101);
    internal static readonly IntPtr Foreign = new(0x202);
    internal ProbeEvidenceWindow Window { get; } = new();
    internal IntPtr Foreground { get; set; } = Own;
    internal IntPtr Owner { get; set; } = Own;
    internal uint OwnerProcess { get; set; } = (uint)Environment.ProcessId;
    internal Func<ProbeEvidencePoint, IntPtr>? OwnerAt { get; set; }
    internal Action? AfterMove { get; set; }
    internal bool MoveReachesTarget { get; set; } = true;
    internal bool PositionReadSucceeds { get; set; } = true;
    internal ProbeEvidencePoint Pointer;
    internal List<(int X, int Y)> Moves { get; } = [];
    internal List<ProbeEvidencePoint> OwnerQueries { get; } = [];
    internal List<string> Log { get; } = [];
    internal Queue<ProbeEvidenceFrame> Frames { get; } = new();
    internal byte[]? EncodedBytes { get; private set; }
    internal Exception? CaptureFailure { get; set; }
    internal Exception? EncodeFailure { get; set; }
    internal int Activations;
    internal int Warps;
    internal int Nudges;
    internal int Captures;
    internal int CurrentOriginX = 1000;
    internal int CurrentOriginY = 200;

    internal ProbeEvidenceFrame Capture(IntPtr handle)
    {
        Assert.Equal(Own, handle);
        Captures++;
        if (CaptureFailure is { } error) throw error;
        return Frames.Dequeue();
    }

    internal Task Encode(byte[] bytes, string file)
    {
        if (EncodeFailure is { } error) return Task.FromException(error);
        EncodedBytes = bytes;
        return Task.CompletedTask;
    }
}

internal static class ProbeEvidenceNative
{
    internal const uint GaRoot = 2;
    internal static ProbeEvidenceState State { get; set; } = null!;
    internal static IntPtr GetForegroundWindow() => State.Foreground;
    internal static IntPtr WindowFromPoint(ProbeEvidencePoint point)
    {
        State.OwnerQueries.Add(point);
        return State.OwnerAt?.Invoke(point) ?? State.Owner;
    }
    internal static IntPtr GetAncestor(IntPtr handle, uint flags) => handle;
    internal static uint GetWindowThreadProcessId(IntPtr handle, out uint process)
    {
        process = handle == ProbeEvidenceState.Own ? State.OwnerProcess : 0;
        return 1;
    }
    internal static bool ClientToScreen(IntPtr handle, ref ProbeEvidencePoint point)
    {
        point.X += State.CurrentOriginX;
        point.Y += State.CurrentOriginY;
        return true;
    }
    internal static int GetClassName(IntPtr handle, char[] name, int capacity)
    {
        const string value = "FixtureWindow";
        value.CopyTo(0, name, 0, value.Length);
        return value.Length;
    }
    internal static bool GetCursorPos(out ProbeEvidencePoint point) { point = State.Pointer; return State.PositionReadSucceeds; }
    internal static bool MovePointerTo(int x, int y)
    {
        State.Moves.Add((x, y));
        if (State.MoveReachesTarget) State.Pointer = new() { X = x, Y = y };
        State.AfterMove?.Invoke();
        return State.MoveReachesTarget;
    }
    internal static bool SetCursorPos(int x, int y)
    {
        State.Warps++;
        State.Pointer = new() { X = x, Y = y };
        return true;
    }
    internal static bool NudgeCursorState() { State.Nudges++; return true; }
}
