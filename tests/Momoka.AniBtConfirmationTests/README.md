# AniBT R-037 / R-039：真实源码、无窗口的确认边界验证

此专项已最小注册到方案构建，仍需单独运行；不修改共享测试 Program。只运行 AniBT 本题及相关既有匿名测试。

## 覆盖与边界

- 默认项目直接 compile-link 本树 Core、实际 AniBtScrapeViewModel、PageViewModel、AniBtScrapeDialog code-behind；使用与 Shell 相同的 CommunityToolkit.Mvvm **8.4.2** 生成器。
- 实际 Dialog XAML 作为资源输入；边界只替代控件值、InitializeComponent 与事件投递。通用绑定求值不包含预览/确认业务状态机，全部状态转移仍执行生产源码。
- AnonymousFixture 安装内存 EmbySessionScope 和无密钥 cached MoviePilot 身份；请求由虚拟 HttpMessageHandler 白名单接收。NoSecrets 的保护/解密入口一律抛错。不登录、不测试连接、不恢复/重认证、不读取配置/DPAPI、不创建或触碰 JSON 中的媒体路径，无实际网络/服务器操作。
- 默认 **53 项**覆盖精确/无关/年份候选、实际预览零写入、取消/改选/迟到读取、实际 Emby 电影/整剧目标、sealed MoviePilot source/target/方式/旧目标、混合批次提前准备、历史变化、连接变化、unknown/停止/防重，以及 R-024 的原 Apply+Refresh 欠项不被重发掩盖。
- [Legacy 专项项目](Legacy/Momoka.Tests.csproj) 只注册实际 AniBtTests、AniBtIntegrationTests、AniBtReceiptStateTests（**87 项**），引用实际 Core。项目名沿用仓库对同步测试运行器的既有分析器条件；不加 NoWarn、不更新约定基线，不运行 full Core、自检或用户级 mutex。
- 确认提交前增加整批历史快照/旧目标依赖只读重核；既有 MoviePilot TransferSubmit 的逐文件临发送核验也保留。所有规划/预览先于第一个 write；临发送核验仍可以在已处理前项之后拒绝后项并保留原逐项结果。客户端重核不是服务器原子事务，不能把 preview、accepted 或 Emby Refresh 请求当完成/回读证明。

## 离线命令

从本树根目录运行，替换成当前树的绝对路径，不借另一树输出：

~~~powershell
$root = (Get-Location).Path
& (Join-Path $root 'tests/Momoka.AniBtConfirmationTests/Run-Fixture.ps1') -Baseline -Label 'final-red'
& (Join-Path $root 'tests/Momoka.AniBtConfirmationTests/Run-Fixture.ps1') -SkipRestore -Label 'final-green'
& (Join-Path $root 'tests/Momoka.AniBtConfirmationTests/Run-Fixture.ps1') -LegacyRegression -Label 'legacy-green'
~~~

脚本固定 SDK `C:\Users\89400\.dotnet\dotnet.exe`；第一次 restore 仅复制本机已有的四个固定 nupkg 到本树 local-feed，源只指向此目录，缺包立即停。NuGetAudit=false 仅当次 restore。CLIhome/temp/packages/http-cache/log 全在 `artifacts/r037-r039/`，证书生成关闭。当前 Release 0 警告/0 错误才允许 no-build；每次失败日志均保留。

`-Baseline` 精确读取 `3f86b51f8202cac0328d9a10f9945ce9df15969a` 四个 Git blob 到 artifacts 下，不 checkout/restore/reset 或覆盖工作源码。同一夹具的版本桥只选择并调用实际旧 Submit 或新 Prepare/Submit，不模拟修好的状态机。旧源码红测是预期退出 1，不是绿。

## 真正 XAML 编译与生成器契约

实际 x64 Release Shell 构建成功以后：

~~~powershell
& (Join-Path $root 'tests/Momoka.AniBtConfirmationTests/Test-GeneratedBindings.ps1')
& (Join-Path $root 'tests/Momoka.AniBtConfirmationTests/Test-GeneratorBoundary.ps1')
~~~

前者检查当前实际 `AniBtScrapeDialog.g.cs` 的 **11 项**绑定/setter/通知/事件；后者将生成文件复制到独立证据目录，测试 **6 个**非法/缺失输入均拒绝，每个子进程预期退出 1，不改生成原件或产品。此检查曾抓到 `x:Name` 与 VM 属性同名的遮蔽，控件句柄最终是 `PreviewHeadingText` / `PreviewDetailsText`；不能把通用反射绑定测试代替真实 XAML 编译。

## 取舍与不声称通过的范围

- 只自动准备规范化精确中文/原文标题；同年优先，保留原有 ±1 年发行/首播差异及精确标题但未知年份候选。全部明确年份不符、仅包含标题、无关首条都拒绝，引导普通识别；不猜 ProviderId。
- 用户每次先点“预览计划”，核对真实内容后再点“确认执行”。更改选择或取消使原凭证永久失效。确认后的提交/read failure 沿原保守策略锁住本次界面，需核对服务器后关闭重开，不能靠改选或重新预览解锁已发送文件。
- 额外真实目标详情读取和确认前整批历史/依赖重核增加只读请求；它们不改变已确认候选、路径或方式。MoviePilot 原逐项临发送核验、账本、unknown、半成功、停止和 R-038 扫描判定没有被替代。
- **未运行**真实应用/窗口、WinUI 输入/布局/滚动/焦点/UIA/像素/主题截图、服务器、媒体文件、凭据、播放、libmpv、系统配置、发布或推送。模板内地址仍需主线程在获授权的运行环境核验。
- R-042 条件、次级来源写权限、R-024 完成后回读不在本题修复范围。主线程整合后串行 full Core/SelfCheck/UI/发布另验；桌面快捷方式仍是旧版。

## 主树限定整合结果

来源 `18c86056788b4c445cbee940f36786c44f7c9433`；主树只复制四产品、三相关旧测试及本正式项目，不搬来源进度／bin／obj／缓存。主树53项和Legacy87项各自全过，完整Core2549全过／0失败／0跳过；当前方案与最终x64ShellRelease零警告错误。匿名夹具10处初始化换行按方案格式定点修，最后全方案Release/x64空白0修改／退出0无加载诊断。首次设计期format让本树g.cs空壳，生成绑定检查如实报红；format结束后当前Shell重编译，实际11生成绑定／6非法输入拒绝检查均通过，不读取backup冒当前结果。证据 `artifacts/staged-repair/batch05/r037-r039/`。

生成绑定与源码格式检查串行：设计期XAML加载会改obj生成文件，须先完成format，再成功编译当前Shell，然后运行两份生成器检查；空生成输入仍必须失败，不放宽验证。
