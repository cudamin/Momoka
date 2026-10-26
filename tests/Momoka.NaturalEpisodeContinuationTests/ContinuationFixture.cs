using System.Reflection;
using System.Runtime.InteropServices;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

internal sealed partial class ServiceFixture
{
    internal Task<PlaybackResult> BeginContinuable(string file = "A", bool retain = true, CancellationToken token = default,
        PlaybackContinuation? continuation = null, bool alternateSource = false)
    {
        var ticket = Ticket(file) with { RetainForAutomaticContinuation = retain };
        if (alternateSource)
            ticket.Item.MediaSources.Add(new Momoka.Emby.MediaSource { Id = file + "-alternative", Container = "mkv", RunTimeTicks = ticket.Item.RunTimeTicks });
        var task = Service.PlayAsync(ticket, Scope, token,
            continuationCancellation: continuation?.Lifetime ?? default, expectedContinuation: continuation);
        _plays.Add(task);
        return task;
    }
    internal void ObservedExpectedFailure(Task<PlaybackResult> task)
    {
        Assert.True(task.IsCompleted && !task.IsCompletedSuccessfully, "only an explicitly observed failed/canceled task may leave cleanup tracking");
        Assert.True(_plays.Remove(task));
    }
    internal void RemoveObservedExpectedFailureByState()
    {
        var failed = _plays.Where(task => task.IsCanceled).ToArray();
        Assert.Equal(1, failed.Length, "only the single observed superseded request may be canceled");
        Assert.True(_plays.Remove(failed[0]));
    }
    internal Task WaitUntilStarted(string file = "A") => UntilAsync(() => Subjects.Contains(file, StringComparer.Ordinal));
    internal void RevokeSource() => ((CancellationTokenSource)_identityType.GetProperty("Lifetime")!.GetValue(_identity)!).Cancel();
    internal void EndNative(int reason = LibMpvNative.EndFileEof) => End(NativeHandle, reason);
    internal static void End(HandleFixture fixture, int reason)
    {
        var bytes = Marshal.AllocHGlobal(Marshal.SizeOf<LibMpvNative.MpvEventEndFile>());
        try
        {
            Marshal.StructureToPtr(new LibMpvNative.MpvEventEndFile { Reason = reason }, bytes, false);
            fixture.Gate.Run(() =>
            {
                var method = typeof(LibMpvHandle).GetMethod("ProcessEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
                method.Invoke(fixture.Handle, [new LibMpvNative.MpvEvent { EventId = LibMpvNative.EventEndFile, Data = bytes }]);
                return true;
            });
        }
        finally { Marshal.FreeHGlobal(bytes); }
    }
}
