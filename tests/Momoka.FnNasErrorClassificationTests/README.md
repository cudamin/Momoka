# R-004 飞牛权限错误分类

独立、已注册方案的离线控制台项目，21项回归执行当前FnNasApiException的分类与实际只读client/HTTP错误解析；不运行登录、恢复、密码、令牌重认证或凭据链。

- -16文件/操作权限不是密码或token拒绝；-5帐号权限同样不得降级。
- -2/-15/HTTP401仍为身份拒绝；403保权限，签名/未知业务不忽略。
- 即使错误同时带503/传输暂不可用标志，明确权限码也不能补读降级。
- 两个匿名GET只允许user/info或sys/config返回code=-16，断言一次请求、零写/零禁止端点。地址.invalid仅虚拟handler，无实际网络、Session、DPAPI或配置文件。

## 运行

当前树首次fixed本机缓存restore，SDK按global.json。成功编译后才no-build：

~~~powershell
$root = (Get-Location).Path
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet restore (Join-Path $root 'tests/Momoka.FnNasErrorClassificationTests/Momoka.FnNasErrorClassificationTests.csproj') --source (Join-Path $HOME '.nuget/packages') --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw '固定缓存还原失败' }
& $dotnet build (Join-Path $root 'Momoka.sln') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前构建失败，不跑旧输出' }
& $dotnet run --project (Join-Path $root 'tests/Momoka.FnNasErrorClassificationTests/Momoka.FnNasErrorClassificationTests.csproj') -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw '错误分类回归失败' }
~~~

主树基点1b37ca1的17项14过3败；修后加权限与传输组合4项，21全过/0失败/0跳过。最初新测试同步awaiter两条警告保留，改真实async await+现有UiQueue后零警告错误，没有压制分析器。当前方案/Shell/Core构建零警告错误，完整Core2549全过；格式0修改/退出0无加载诊断。证据artifacts/staged-repair/batch05/r004。

只修业务错误分类，不运行停止的R001/003/005/031链。分类使SignIn错误出口不再将权限包装为凭据拒绝，这是当前源码因果结论；真实重新登录/来源退出的端到端运行没有作为本题验收，真实服务器与界面/native/播放/凭据持久化未验，不称完整交付。
