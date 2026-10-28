using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual playback service and handle with the existing managed native seam; zero media/HTTP.</summary>
internal static class SvpPlaybackTests
{
    internal static void Register()
    {
        Test("SVP 实际播放服务：起播票据固定接入状态，菜单与命令不跟随后改的偏好", () =>
            ActiveOwnershipAsync().GetAwaiter().GetResult());
        Test("SVP 实际句柄：拒绝同窗换片，零原生 loadfile 命令", () =>
            NoInlineSwitchAsync().GetAwaiter().GetResult());
    }

    private static async Task ActiveOwnershipAsync()
    {
        await using var fixture = new ServiceFixture();
        fixture.Settings.Mpv.SvpEnabled = true;
        fixture.Settings.Mpv.SvpVapourSynthPath = "anonymous-local-component";
        fixture.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.MotionCompensated;
        fixture.Settings.Video.HardwareDecoding = "d3d11va";
        fixture.Settings.Video.Interpolation = true;
        fixture.Settings.Video.VideoSync = "display-resample";
        await fixture.StartAsync();
        Assert.True(fixture.Service.PlayingWithSvp);
        Assert.Equal("d3d11va-copy", fixture.Service.LaunchOptions.Last(pair => pair.Key == "hwdec").Value);
        Assert.Equal("no", fixture.Service.LaunchOptions.Last(pair => pair.Key == "interpolation").Value);
        Assert.False(fixture.Service.LaunchOptions.Any(pair => pair.Value.Contains("minterpolate", StringComparison.Ordinal)));
        fixture.Settings.Mpv.SvpEnabled = false;
        Assert.True(fixture.Service.PlayingWithSvp, "修改下次播放的设置不撤销当前滤镜所有权");
        // These two properties are the bounded native endpoints needed by this SVP case.
        fixture.NativeHandle.Native.Values["hwdec"] = "d3d11va-copy";
        fixture.NativeHandle.Native.Values["pause"] = "no";
        var commands = fixture.NativeHandle.Native.Commands.Count;
        Assert.False(await fixture.Service.CommandAsync("vf", "clr", ""));
        Assert.False(await fixture.Service.CommandAsync("set", "interpolation", "yes"));
        await fixture.Service.SetPropertyAsync("hwdec", "d3d11va");
        Assert.Equal(commands, fixture.NativeHandle.Native.Commands.Count, "禁用命令没有传入实际 native seam");
        Assert.True(await fixture.Service.CommandAsync("set", "hwdec", "d3d11va-copy"));
        Assert.True(await fixture.Service.CommandAsync("set", "pause", "yes"));
        Assert.Equal(commands + 2, fixture.NativeHandle.Native.Commands.Count);
        await fixture.Service.StopAsync();
        Assert.False(fixture.Service.PlayingWithSvp);
    }

    private static async Task NoInlineSwitchAsync()
    {
        await using var fixture = new HandleFixture(svp: true);
        var commands = fixture.Native.Commands.Count;
        Assert.False(fixture.Handle.CanSwapTo(HandleFixture.Request("B")));
        fixture.Handle.HandOver();
        Assert.False(await fixture.Handle.SwapToAsync(HandleFixture.Request("B"), CancellationToken.None));
        Assert.Equal(commands, fixture.Native.Commands.Count);
    }
}
