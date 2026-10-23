using Momoka.Playback;
using Momoka.Mpv;
using Momoka.Configuration;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Case("R046 Natural EOF hands an ended reusable context to caller without destroying it", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.Equal(PlaybackEndReason.EndOfFile, result.Exit.Reason, "EOF must not become Stopped");
            Assert.NotNull(result.Continuation, "caller owns the ended context until successor or dispose");
            Assert.Equal(0, f.NativeHandle.Native.DestroyCalls);
            await result.Continuation!.DisposeAsync();
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 Natural EOF next request swaps the exact handle and old lease cannot stop B", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.NotNull(result.Continuation);
            var handle = f.NativeHandle;
            var next = f.BeginContinuable("B", retain: false);
            await f.WaitUntilStarted("B");
            f.NativeHandle.Ready();
            await result.Continuation!.DisposeAsync();
            Assert.Equal(1, f.BackendStarts);
            Assert.True(ReferenceEquals(handle, f.NativeHandle));
            Assert.Equal("B", handle.Native.File);
            Assert.Equal(0, handle.Native.DestroyCalls);
            f.EndNative();
            await next;
            Assert.Equal(1, handle.Native.DestroyCalls);
        });
        Case("R046 final episode/no successor caller disposal is idempotent", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.NotNull(result.Continuation);
            await result.Continuation!.DisposeAsync();
            await result.Continuation.DisposeAsync();
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 disabled auto never retains EOF", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = false;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.Null(result.Continuation);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 opt-out caller keeps old disposal contract", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable(retain: false);
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.Null(result.Continuation);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        foreach (var reason in new[] { LibMpvNative.EndFileQuit, LibMpvNative.EndFileStop, LibMpvNative.EndFileError })
            Case("R046 non-natural end " + reason + " never retained", async () =>
            {
                await using var f = new ServiceFixture();
                f.Settings.Playback.AutoPlayNextEpisode = true;
                var running = f.BeginContinuable();
                await f.WaitUntilStarted();
                f.EndNative(reason);
                var result = await running;
                Assert.Null(result.Continuation);
                Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            });
        Case("R046 source revoke releases parked context without another request", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.NotNull(result.Continuation);
            f.RevokeSource();
            await ServiceFixture.UntilAsync(() => f.NativeHandle.Native.DestroyCalls == 1);
            await result.Continuation!.DisposeAsync();
            Assert.False(f.Service.IsPlaying);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 StopAsync closes retained EOF and caller late dispose is safe", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.NotNull(result.Continuation);
            await f.Service.StopAsync();
            await result.Continuation!.DisposeAsync();
            Assert.False(f.Service.IsPlaying);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 EOF then Shutdown preserves A EOF but rejects retention and swap", async () =>
        {
            await using var f = new HandleFixture();
            ServiceFixture.End(f, LibMpvNative.EndFileEof);
            f.Dispatch(LibMpvNative.EventShutdown);
            var exit = await f.Handle.WaitForExitAsync(CancellationToken.None);
            Assert.Equal(PlaybackEndReason.EndOfFile, exit.Reason);
            Assert.False(f.Handle.TryRetainAfterEndOfFile());
            Assert.False(f.Handle.CanSwapTo(HandleFixture.Request("B")));
            Assert.False(await f.Handle.SwapToAsync(HandleFixture.Request("B"), CancellationToken.None));
        });
        Case("R046 parked Q cancels preparation and never reopens B", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            Assert.NotNull(a.Continuation);
            f.NativeHandle.Dispatch(LibMpvNative.EventShutdown);
            var rejected = f.BeginContinuable("B", retain: false, continuation: a.Continuation);
            await ExpectCanceled(rejected);
            f.ObservedExpectedFailure(rejected);
            await a.Continuation!.DisposeAsync();
            Assert.Equal(1, f.BackendStarts);
            Assert.False(f.Service.IsPlaying);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 incompatible successor intentionally replaces rather than canceling on own cleanup", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            Assert.NotNull(a.Continuation);
            f.Settings.Mpv.Backend = Momoka.Configuration.MpvBackendKind.ExternalMpv;
            var replacement = new EndedHandle();
            f.NextBackendStart = (_, _) => Task.FromResult<IPlaybackHandle>(replacement);
            var next = f.BeginContinuable("B", retain: false, continuation: a.Continuation);
            await f.WaitUntilStarted("B");
            await a.Continuation!.DisposeAsync();
            Assert.Equal(2, f.BackendStarts);
            Assert.False(replacement.Stopped);
            replacement.End();
            await next;
            Assert.Equal(1, replacement.Disposes);
        });
        Case("R046 rejected loadfile fallback failure still releases retained old handle", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            Assert.NotNull(a.Continuation);
            f.NativeHandle.Native.LoadResult = _ => -12;
            f.NextBackendStart = (_, _) => Task.FromException<IPlaybackHandle>(new IOException("anonymous fallback fails"));
            var next = f.BeginContinuable("B", retain: false, continuation: a.Continuation);
            try { await next; throw new AssertionException("expected failed fallback"); }
            catch (IOException) { }
            f.ObservedExpectedFailure(next);
            await a.Continuation!.DisposeAsync();
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 old lease disposal after B parks cannot stop B", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            var next = f.BeginContinuable("B", continuation: a.Continuation);
            await f.WaitUntilStarted("B");
            f.NativeHandle.Ready();
            f.EndNative();
            var b = await next;
            Assert.NotNull(b.Continuation);
            await a.Continuation!.DisposeAsync();
            Assert.Equal(0, f.NativeHandle.Native.DestroyCalls);
            Assert.False(b.Continuation!.Lifetime.IsCancellationRequested);
            await b.Continuation.DisposeAsync();
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 Stop and dispose and source revocation concurrent close exactly once", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            var stopping = Task.Run(() => f.Service.StopAsync());
            var disposing = Task.Run(async () => await a.Continuation!.DisposeAsync());
            f.RevokeSource();
            await Task.WhenAll(stopping, disposing);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 parked handle refuses stale scoped shader transaction", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            var writes = f.NativeHandle.Native.SetAttempts;
            Assert.False(await f.Service.SetShaderGroupAsync(null));
            Assert.Equal(writes, f.NativeHandle.Native.SetAttempts);
            await a.Continuation!.DisposeAsync();
        });
        Case("R046 canceled successor before service admission leaves caller cleanup", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            var next = f.BeginContinuable("B", retain: false, token: new CancellationToken(true), continuation: a.Continuation);
            await ExpectCanceled(next);
            f.ObservedExpectedFailure(next);
            Assert.Equal(1, f.BackendStarts);
            await a.Continuation!.DisposeAsync();
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 custom configuration context never opts into EOF retention", async () =>
        {
            await using var f = new HandleFixture();
            var signature = new PlaybackLaunchSignature(VideoPipelineKind.Standalone, [], [], [], []) { UsesCustomConfig = true };
            var context = new LibMpvHandle(f.Native.Context, null, signature, _ => signature);
            var status = typeof(LibMpvHandle).GetField("_status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            status.SetValue(context, new PlayerStatus { Loaded = true });
            var exit = (TaskCompletionSource<PlaybackExit>)typeof(LibMpvHandle).GetField("_exit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(context)!;
            exit.SetResult(new PlaybackExit(PlaybackEndReason.EndOfFile, 100, 0, null));
            typeof(LibMpvHandle).GetField("_finishedReason", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, PlaybackEndReason.EndOfFile);
            Assert.False(context.TryRetainAfterEndOfFile(), "custom configs do not use same-signature inline reuse");
        });
        Case("R046 source canceled in retention boundary does not leak or deadlock registration", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            f.Handle.BeforeRetention = f.RevokeSource;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            if (result.Continuation is { } lease) await lease.DisposeAsync();
            Assert.Equal(PlaybackEndReason.EndOfFile, result.Exit.Reason);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 lease Dispose while next request waits for gate cannot wait for B duration", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            using var afterSwap = new CommandPause();
            f.Handle.AfterSwap = afterSwap;
            var next = Task.Run(() => f.BeginContinuable("B", retain: false, continuation: a.Continuation));
            await afterSwap.WaitAsync();
            var release = a.Continuation!.DisposeAsync().AsTask();
            await release.WaitAsync(TimeSpan.FromSeconds(2));
            afterSwap.Release();
            await f.WaitUntilStarted("B");
            Assert.Equal(0, f.NativeHandle.Native.DestroyCalls);
            f.NativeHandle.Ready();
            f.EndNative();
            await next;
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 superseded delayed successor cannot consume or stop newer parked B", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var first = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await first;
            using var held = new CommandPause();
            f.Handle.AfterHandOver = held.Block;
            var old = Task.Run(() => f.BeginContinuable("superseded", retain: false, continuation: a.Continuation));
            await held.WaitAsync();
            var latest = f.BeginContinuable("B", continuation: a.Continuation);
            await f.WaitUntilStarted("B");
            f.NativeHandle.Ready();
            f.EndNative();
            var b = await latest;
            Assert.NotNull(b.Continuation);
            held.Release();
            await ExpectCanceled(old);
            // Task.Run unwraps the actual task; remove only that observed expected failure from cleanup.
            f.RemoveObservedExpectedFailureByState();
            Assert.True(f.Service.IsPlaying, "late old request must not clear parked B");
            Assert.Equal(0, f.NativeHandle.Native.DestroyCalls);
            Assert.False(b.Continuation!.Lifetime.IsCancellationRequested);
            await a.Continuation!.DisposeAsync();
            await b.Continuation.DisposeAsync();
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 lease queued behind a successor stops waiting when the successor claims it", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            var gate = (SemaphoreSlim)typeof(PlaybackService).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Service)!;
            await gate.WaitAsync();
            Task<PlaybackResult> next;
            Task release;
            try
            {
                next = f.BeginContinuable("B", retain: false, continuation: a.Continuation);
                release = a.Continuation!.DisposeAsync().AsTask();
            }
            finally { gate.Release(); }
            await release.WaitAsync(TimeSpan.FromSeconds(2));
            await f.WaitUntilStarted("B");
            Assert.Equal(0, f.NativeHandle.Native.DestroyCalls);
            f.NativeHandle.Ready();
            f.EndNative();
            await next;
        });
        Case("R046 external backend EOF retains neither handle nor lease", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            f.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var result = await running;
            Assert.Null(result.Continuation);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
        });
        Case("R046 integrated composition handle never opts into natural window retention", async () =>
        {
            await using var f = new HandleFixture();
            var integrated = new LibMpvHandle(f.Native.Context, new InertSurface(), null, null);
            Assert.False(integrated.TryRetainAfterEndOfFile());
            Assert.Equal(0, f.Native.ForbiddenCalls);
        });
        Case("R046 EOF then Q before retention returns an admission veto instead of reopening B", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            f.Handle.BeforeRetention = () => f.NativeHandle.Dispatch(LibMpvNative.EventShutdown);
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            Assert.Equal(PlaybackEndReason.EndOfFile, a.Exit.Reason);
            Assert.NotNull(a.Continuation, "a closed native context must veto automatic continuation even when not retained");
            Assert.True(a.Continuation!.Lifetime.IsCancellationRequested);
            var next = f.BeginContinuable("B", retain: false, continuation: a.Continuation);
            await ExpectCanceled(next);
            f.ObservedExpectedFailure(next);
            await a.Continuation.DisposeAsync();
            Assert.Equal(1, f.BackendStarts);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.False(f.Service.IsPlaying);
        });
        Case("R046 admitted successor startup error still tries its second candidate", async () =>
        {
            await using var f = new ServiceFixture();
            f.Settings.Playback.AutoPlayNextEpisode = true;
            var running = f.BeginContinuable();
            await f.WaitUntilStarted();
            f.EndNative();
            var a = await running;
            var replacement = new EndedHandle();
            string? requestedSource = null;
            f.NextBackendStart = (request, _) =>
            {
                requestedSource = request.MediaSourceId;
                replacement.End();
                return Task.FromResult<IPlaybackHandle>(replacement);
            };
            var next = f.BeginContinuable("B", retain: false, continuation: a.Continuation, alternateSource: true);
            await f.WaitUntilStarted("B");
            f.NativeHandle.Dispatch(LibMpvNative.EventStartFile);
            f.EndNative(LibMpvNative.EndFileError);
            PlaybackResult result;
            try { result = await next; }
            catch (OperationCanceledException)
            {
                f.ObservedExpectedFailure(next);
                throw new AssertionException("normal failed-candidate cleanup was mistaken for user closing the predecessor");
            }
            Assert.Equal(PlaybackEndReason.EndOfFile, result.Exit.Reason);
            Assert.Equal("B-alternative", requestedSource);
            Assert.Equal(2, f.BackendStarts);
            Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
            Assert.Equal(1, replacement.Disposes);
            await a.Continuation!.DisposeAsync();
        });
        return Run();
    }
    private static async Task ExpectCanceled(Task work)
    {
        try { await work; throw new AssertionException("expected cancellation"); }
        catch (OperationCanceledException) { }
    }

    private static void Case(string name, Func<Task> work) => Test(name, () => work().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult());
}
