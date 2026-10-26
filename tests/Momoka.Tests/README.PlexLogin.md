# R-005：Plex v2 顶层登录响应

## 根因与修复

实际入口是 `PlexMediaAdapter.SignInAsync`，不是独立的 `PlexServerGateway.cs`。客户端调用 v2账号登录，旧实现从顶层读 token、从嵌套 `user` 读 id／name；官方静态 auth-form 使用的合法成功对象是同一顶层账号对象，旧解析因此拒绝成功响应。

现在从同一顶层对象读取正整数 `id`、字符串 `authToken` 和 `username`／`title`；显示名称可缺省，错误类型明确拒绝。目标 PMS 二次核对、用户／服务器完整身份、取消和迟到结果守卫均保留。没有仅凭 token Adopt，也没有无证兼容嵌套 envelope／字符串 id，PMS XML数字字符串转换不是账号登录契约。

官方来源是2026-10-24匿名静态前端 auth-form与UserModel；位置、页面hash与标记hash见 [已入库证据](../../.claude/skills/momoka-plex-api/references/v2-login-official-evidence.json)。没有向官方登录接口提交密码，也没有把静态字段定义冒充真实成功服务响应。

## 当前树执行

先按 `CLAUDE.md` 成功编译 Release Tests／Core，再：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- plex-login-fixture
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- plex-protocol-fixture
```

专项43项，Plex完整146项含专项；全Core经 `PlexMediaTests.Register()` 已注册，不再次注册。三者重叠，不能相加。

## 测试与整合边界

实际 Core session、Plex客户端和 adapter／原传输策略；HTTP 为匿名内存handler，保护器与 settings IO只用本树隔离占位文件。顶层成功先通过目标PMS再存档；目标拒绝、缺id/token、非整数id／非字符串token、空／控制字符、混层或错误用户名字段、401/403、取消、退出迟到、账号切换、服务器身份和保存令牌/密码恢复均保留拒绝或正例。没有真实外部HTTP、用户凭据或PMS数据。

来源实现提交 `fbab57acb352083b96fb18786a4c89b0d705d1e2`，只合8份题内源码／测试／Plex技能与reference，来源PROGRESS提交 `581ce2553ff5c74ccaf127221bf9d9f1bd5f6ff8` 不复制主合。主线程另写fixture入口；复制Cs后更新时间戳使current真正编译，不改文件字节、不借来源DLL。

- 来源目录 `C:/Users/89400/.codex/worktrees/r005-plex-v2-final/Momoka/artifacts/staged-repair/batch07/r005/`：专项43项28过15败→43全过，Plex146项124过22败→146全过；Core原2687=2666过21败，在额外迟到退出case加入前，后2688全过，不称同数量对比。最初fixture-host工具错误独立保留，不计业务红。
- 本树 `artifacts/staged-repair/batch07/r005/`：`integration-source.json`记录8个源码Gitblob／SHA；主current43、146和共享R00386／R03142／R00137重验仅用本树编译输出。批次最终统一报告另取最终源码、DLL和日志hash。

## 未验证

没有真实 plex.tv 登录／PMS访问、现有 JSON 请求正文的服务接受度、2FA／PIN／legacy、生产 DPAPI、WinUI或媒体播放。仅解析支持与离线调用链闭环，真实 libmpv 门禁红、旧快捷方式不变，未发布推送，不称完整交付。
