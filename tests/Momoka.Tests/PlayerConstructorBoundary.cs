using System.Reflection;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>Checks the real Shell constructor without loading UI or silently nulling a new dependency.</summary>
internal static class PlayerConstructorBoundary
{
    private static readonly string[] Names = ["playback", "settings", "session", "images", "shaders", "ui", "dandanPlay",
        "fonts", "playerCapabilities"];

    internal static ConstructorInfo Require(Type viewModel, Type dispatcher)
    {
        var constructors = viewModel.GetConstructors();
        Assert.Equal(1, constructors.Length, "the actual PlayerViewModel constructor must be unambiguous");
        RequireParameters(Shape(constructors[0]), dispatcher);
        for (var index = 7; index < Names.Length; index++)
        {
            var optional = constructors[0].GetParameters()[index];
            Assert.True(optional.IsOptional && optional.DefaultValue is null,
                "the reviewed optional player dependency must remain optional with null default: " + Names[index]);
        }
        return constructors[0];
    }

    private static (string? Name, Type Type)[] Shape(ConstructorInfo constructor) =>
        constructor.GetParameters().Select(parameter => (parameter.Name, parameter.ParameterType)).ToArray();

    private static void RequireParameters(IReadOnlyList<(string? Name, Type Type)> parameters, Type dispatcher)
    {
        Type[] types = [typeof(PlaybackService), typeof(ISettingsService), typeof(EmbySession), typeof(EmbyImageStore),
            typeof(ShaderGroupResolver), dispatcher, typeof(DandanPlayClient), typeof(FontLibrary)];
        Assert.Equal(Names.Length, parameters.Count, "an unreviewed mandatory constructor parameter must fail closed");
        for (var index = 0; index < Names.Length; index++)
        {
            Assert.Equal(Names[index], parameters[index].Name, "constructor parameter order/name must remain exact");
            if (index < types.Length)
                Assert.Equal(types[index], parameters[index].Type, "constructor parameter type must remain exact");
            else
                Assert.True(MatchesOptionalSlot(index, parameters[index].Type),
                    "constructor optional parameter type must remain exact: " + Names[index]);
        }
    }

    internal static object Create(ConstructorInfo constructor, params object?[] arguments)
    {
        Assert.True(arguments.Length >= Names.Length - 2 && arguments.Length <= Names.Length,
            "preserve seven mandatory dependencies and the reviewed optional player dependencies");
        Assert.True(arguments[6] is DandanPlayClient, "a null or unrelated seventh dependency must be rejected before invoking the VM");
        for (var index = 7; index < arguments.Length; index++)
        {
            if (arguments[index] is null) continue;
            Assert.True(MatchesOptionalSlot(index, arguments[index]!.GetType()),
                "only the reviewed optional dependency can occupy its slot: " + Names[index]);
        }
        var filled = new object?[Names.Length];
        Array.Copy(arguments, filled, arguments.Length);
        return constructor.Invoke(filled);
    }

    // Shell 侧的两个可选依赖在纯夹具工程里不引用程序集，只能按完整类型名核对。
    private static bool MatchesOptionalSlot(int index, Type type) => index switch
    {
        7 => type == typeof(FontLibrary),
        8 => type.FullName == "Momoka.Services.IServerCapabilities",
        _ => false
    };

    internal static void Register(Type viewModel, Type dispatcher)
    {
        TestHarness.Test("播放器构造边界：七项必需依赖与两项可选依赖的名称类型顺序", () => _ = Require(viewModel, dispatcher));
        TestHarness.Test("播放器构造边界：旧七参数签名与未审核新增尾参数均拒绝", () =>
        {
            var parameters = Shape(Require(viewModel, dispatcher));
            Assert.Throws<AssertionException>(() => RequireParameters(parameters[..^1], dispatcher));
            Assert.Throws<AssertionException>(() => RequireParameters([.. parameters, ("future", typeof(object))], dispatcher));
        });
        TestHarness.Test("播放器构造边界：改名错序与错误尾参数类型均拒绝", () =>
        {
            var parameters = Shape(Require(viewModel, dispatcher));
            parameters[^1] = ("otherClient", typeof(DandanPlayClient));
            Assert.Throws<AssertionException>(() => RequireParameters(parameters, dispatcher));
            parameters[^1] = ("dandanPlay", typeof(object));
            Assert.Throws<AssertionException>(() => RequireParameters(parameters, dispatcher));
            parameters = Shape(Require(viewModel, dispatcher));
            (parameters[0], parameters[1]) = (parameters[1], parameters[0]);
            Assert.Throws<AssertionException>(() => RequireParameters(parameters, dispatcher));
        });
        TestHarness.Test("播放器构造边界：null或非真实第七依赖不能借反射启动VM", () =>
        {
            var constructor = Require(viewModel, dispatcher);
            Assert.Throws<AssertionException>(() => Create(constructor, new object?[Names.Length]));
            var arguments = new object?[Names.Length];
            arguments[^1] = new object();
            Assert.Throws<AssertionException>(() => Create(constructor, arguments));
        });
    }
}

/// <summary>Real client, disabled anonymous preferences, rejecting HTTP and an invalid cache path.</summary>
internal sealed class AnonymousDanmakuDependency : IDisposable
{
    private readonly RejectHttp _http = new();
    internal DandanPlayClient Client { get; }

    internal AnonymousDanmakuDependency(AppSettings settings, string sourceRoot)
    {
        Assert.False(settings.Danmaku.Enabled, "this constructor fixture must not enable a real Danmaku request");
        var cache = Path.Combine(sourceRoot, "work", "player-constructor", "no-danmaku-IO|fixture");
        Client = new DandanPlayClient(settings, cache, _http);
    }

    public void Dispose()
    {
        try
        {
            Assert.Equal(0, _http.Calls, "unrelated player tests must never issue Danmaku HTTP");
        }
        finally { Client.Dispose(); }
    }

    private sealed class RejectHttp : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            throw new AssertionException("anonymous player constructor forbids all HTTP");
        }
    }
}
