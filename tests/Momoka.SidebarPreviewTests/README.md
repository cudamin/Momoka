# 侧边栏媒体库预览回归测试

构建时读取当前工作树完整的 `ShellPage.LibraryPreview.cs`，只将单调时钟读取替换为可控时间；`PosterHoverDwell.cs` 直接源码链接。预览的事件处理、计时器接线、候选/显示归属、查询与图片返回检查、原生按钮创建、点击处理和释放均执行生产代码。

同时按唯一的首尾标记原样提取 `ShellNavigationProbe.Sidebar.cs` 中新增的封面按钮语义检查段，标记缺失或歧义立即让构建失败。检查正向路径及按钮数量、名称、定位句柄、条目身份和 Invoke 模式反例；惰性的按钮自动化边界不证明实际 UIA 模式存在。

另外三项源码接线契约按唯一方法边界检查：初始化监听处理过的 PointerPressed，库重建注册处理过的 PointerMoved 并创建每项独立状态；Invoke/SelectionChanged 将实际导航目标交给抑制方法；独立预览探针显式采用商店侧栏并归入 Shell 分区，宽窄检查调用唯一共用展示 helper。它们补充真实行为测试的调用者覆盖，不证明平台事件投递。

WinUI 控件、鼠标系统读数、调度器、位图解码与 `PlayAsync` 是惰性边界。真实 Core 会话在内存中采用匿名身份，HTTP handler 只接受 `preview.invalid` 的匿名 GET 并返回内存数据，拒绝任何其他地址或写入操作。测试不登录服务器、不操作真实鼠标、不打开窗口、不加载媒体或上报播放记录。布局和动画替身不证明实际 WinUI 渲染；导航 invoke 测试覆盖调用的预览抑制方法，不证明平台事件投递。

音乐封面回归按库作用域检查：`music` 的三种大小写均使用正方形和音符占位，覆盖普通封面与按钮两条路径；切回电影恢复 2:3，即使原图为正方形也维持影视海报规格。测试按现有「封面＋标题」单元遍历，继续核对旧按钮失效、图片释放和原生探针的按钮／标题反例。

```powershell
& "$HOME/.dotnet/dotnet.exe" restore tests/Momoka.SidebarPreviewTests/Momoka.SidebarPreviewTests.csproj --disable-parallel
& "$HOME/.dotnet/dotnet.exe" build tests/Momoka.SidebarPreviewTests/Momoka.SidebarPreviewTests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.SidebarPreviewTests/Momoka.SidebarPreviewTests.csproj -c Release --no-build
```
