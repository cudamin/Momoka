# 本地媒体库页面离线契约

编译链接实际 LocalLibraryViewModel、LocalLibraryRow、PageViewModel 和 LocalLibraryArtwork，使用与Shell相同的MVVM生成器。协调器、WinUI值类型与图片解码是受控边界；Core配置与条目模型使用实际源代码。

2026-11-14 完整链接生产 `LocalLibraryPage.xaml.cs` 与 `IShellContent`，用 AppBarButton 边界调用原刷新和打开目录处理器；XAML 契约核对原生命令栏、有限列、标签和能力显隐。受控 SynchronizationContext 先等真实刷新续体到达，再在所属线程执行，覆盖重复提交、离页/移除、目录失效及错误已到达但切库后才执行的情况。源码接线和受控焦点边界不能证明真实命令栏溢出、键盘或系统文件夹结果。

验证当前库过滤、稳定集合与行复用、通知排队后换库或离页、属性通知内重入、加载状态通知内取消及缺失目录不交给会创建目录的launcher。另运行首页导航与XAML源码接线契约，以及本机海报路径、12MiB读取上限和取消的实际临时文件测试。

不读取用户设置或媒体，不启动WinUI窗口、Explorer或播放器，不连接服务器。该夹具不能证明真实布局、图片解码、输入、滚动、Frame返回前进或外部文件夹打开结果。

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.LocalLibraryPageTests/Momoka.LocalLibraryPageTests.csproj -c Release
```
