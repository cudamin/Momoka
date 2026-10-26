# R-003：自动恢复密码回退的最终身份

## 最小修复

`EmbySession.RestoreAttemptAsync` 保留 `ResolveRestoreConnectionAsync` 已通过匿名原入口确认、完成合法产品纠正的预期连接。密码回退返回后，在原锁、取消、代次与原配置地址守卫内要求 `IsSameIdentityAs(expectedConnection)`；预期服务器 ID 已确认且非空时，最终 ID 必须精确相等，不能利用共享身份规则对旧配置空 ID 的兼容绕过。

匹配后才 `SaveCredentials`、`Adopt`、`Persist`。不改共享同身份判据和 Gateway 的显式登录授权，不用旧 Emby 误标签比较合法 Jellyfin 纠正，不限制同身份的新令牌、名称或版本变化。

## 主树执行

先按 `CLAUDE.md` 在当前树成功编译 Release 测试及 Core，然后：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- restore-identity-fixture
```

完整 Core 已通过 `SessionTests.Register()` 注册本专项；不要在完整路径再次注册。同专项 86 项和 Session／Identity／Jellyfin 回归有重叠，分套不能相加。

## 覆盖与实际边界

- 真实 Core session、产品 gateway、同身份判据、settings store、vault；传输为 `.invalid` 原来源的内存脚本，保护器只接受固定占位凭据／包装值。真实配置文件 IO 限本树 `artifacts/staged-repair/batch07/r003/fixtures/<随机>`，退出前确认完整绝对边界再清理自己的夹具。
- 有保存令牌的 401 密码回退／只有密码分支；AuthB 与同用户、Auth 缺／空 ServerId 再 PublicB、后登录产品／API 基址变化、用户 ID 错配均拒绝，且保原 profile/凭据、保护器零新写、零来源采用，隔离落盘与内存一致。
- 原 profile 不带 ID 但本次 PublicA 确认时仍拒 PublicB；确认 ID 丢失也拒绝。匹配 A、缺认证 ID 由 PublicA 补齐、新令牌／名字／版本变化正常恢复。
- 旧 Emby→Jellyfin 合法纠正和反代路径仍恢复，同身份探针 403／503 不回退；密码认证 403／503、匿名预探测异服、取消、退出改代次、await 原地址变动和并发迟到结果均覆盖。
- 单维 API 基址测试直接调用实际 `EmbyConnection.IsSameIdentityAs`，不宣称完整服务器真实改变入口的传输链已经观测。

## 证据与整合

来源提交 `a133df892d5b16e205303ab7c5039999ed1f4dc5`，只合 `EmbySession.cs`、`SessionTests.cs`、`RestoreIdentityR003Tests.cs` 三个源码 blob。主线程另加明确 fixture 入口，不复制独立树 `PROGRESS.md` 或 DLL。

- 来源证据目录：`C:/Users/89400/.codex/worktrees/r003-restore-identity-final/Momoka/artifacts/staged-repair/batch07/r003/`。修前70项49过21败／退出1，最终70全过；来源 Session＋Identity＋Jellyfin229全过、完整Core2715全过。原 format 退出2保留，修正后重新编译和运行，不抹掉旧失败。
- 独立只读复核在来源70绿后保留一个P1：同产品初次公开信息 `Id=""` 会以 null 合并擦掉原已知A。主线程增加16项（两产品／有无token／初ID空或缺／B拒绝或A允许）后，86项 **82过4败**，空串4反例实际复现；随后只让非空公开ID覆盖保存ID，86项全过。来源70/229/2715仍只代表旧来源补丁，不能代替补边界后的主树读数。
- 本树重验目录：`artifacts/staged-repair/batch07/r003/`，`integration-source.json` 固定3个 Git blob/sha，当前 build/专项/R001共享回归与全Core均只使用主树输出。最终批次另外固定源码与DLL全套 hash，来源绿色不冒充主树绿色。

## 未验证

未真实登录 Emby／Jellyfin、读取保存用户配置、DPAPI、WinUI 恢复流程、服务器反代漂移部署、native 或播放。这里只保证实际产品路径的匿名离线契约，不声称所有部署已验；真实 libmpv 仍缺且 runtime 红、快捷方式旧版，未发布／推送。
