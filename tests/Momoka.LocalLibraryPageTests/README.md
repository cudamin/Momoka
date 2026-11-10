# 本地媒体库页面离线契约

编译链接实际 LocalLibraryViewModel、LocalLibraryRow、PageViewModel 和 LocalLibraryArtwork，使用与Shell相同的MVVM生成器。协调器、WinUI值类型与图片解码是受控边界；Core配置与条目模型使用实际源代码。

验证当前库过滤、稳定集合与行复用、通知排队后换库或离页、属性通知内重入、加载状态通知内取消及缺失目录不交给会创建目录的launcher。另运行首页导航与XAML源码接线契约，以及本机海报路径、12MiB读取上限和取消的实际临时文件测试。

不读取用户设置或媒体，不启动WinUI窗口、Explorer或播放器，不连接服务器。该夹具不能证明真实布局、图片解码、输入、滚动、Frame返回前进或外部文件夹打开结果。

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.LocalLibraryPageTests/Momoka.LocalLibraryPageTests.csproj -c Release
```
