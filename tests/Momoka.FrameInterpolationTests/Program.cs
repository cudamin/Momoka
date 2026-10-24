using Momoka.Tests;

if (args.Length > 0 && args[0] == "native")
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("native 仅接受一个本地 libmpv-2.dll 绝对路径，不接受媒体或 URL。");
        return 2;
    }
    return NativeNoMediaProbe.Run(args[1]);
}
if (args is ["regression"])
{
    VideoOutputTests.Register();
    InlineSwitchTests.Register();
    return TestHarness.Run();
}
if (args.Length > 0)
{
    Console.Error.WriteLine("仅支持无参规则测试、regression 或 native <本地DLL路径>。");
    return 2;
}
FrameInterpolationTests.Register();
FrameInterpolationUiTests.Register();
return TestHarness.Run();
