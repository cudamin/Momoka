# 第四批 Plex 元数据修复计划

> 执行方式：本会话连续实施，不按任务暂停；回归采用先红后绿，最后独立复审。

**目标：** 修复仍在当前主树存在的 R05-004、R05-005。

**范围依据：** [R05 原始审查](../reviews/2026-11-05-full-code-review/R05.md)。本批基点 c253b00555e65abba6fb76bc265a8b310af1b22d；复用当前托管工作树，已快进到主树。只在 Plex 映射边界修改，不引入新服务或共享后端改造。

**约束：** 按当前 CLAUDE.md；SDK 10.0.400、Release/x64、单节点构建。只用匿名内存传输，不读真实凭据或登录/播放/写服务器。每项产品修复与测试单独提交，中文正文加规定署名。最终合入 master、重验、刷新实际交付目录及快捷方式；不上传。

## R05-004：码率

- [x] 在 PlexMediaTests 的新局部分文件中补 Media 与 Stream 的单位及实际 ItemDetail 格式化测试；覆盖数字、字符串、零、缺值、非法值和 int 乘法边界。
- [x] 编译当前 Tests/Core，运行 plex-protocol-fixture，确认原映射失败并保存完整原红。
- [x] Models.cs 的两处映射共同调用局部转换，将 Plex kbps 转为共享 bit/s；无法表示的值返回 null，沿用现有数值解析语义。
- [x] 重编并跑同一专项，确认通过后提交。

## R05-005：剧/季观看状态

- [x] 增加 show/season 已看完、部分看过、完全未看、空容器、缺字段及数值字符串回归；movie/episode 保持 viewCount 语义。断言实际 ItemMenu 反向命令与未看集数。
- [x] 编译并跑同一专项，保存观看状态原红。
- [x] 在 Item 映射中用有效 leafCount/viewedLeafCount 决定 show/season 的 Played、UnplayedItemCount；不将空或未知容器标为已看完。
- [x] 重编并跑同一专项，确认通过后提交。

## 收尾

- [x] 一次独立复审最终产品差异、回读调用与边界；不重复扩大全库审查。
- [ ] 核实主树无其他会话写入，整合后重新构建 Tests/Core/Shell，并跑完整主测试、Plex 专项及变更文本检查。
- [ ] 版本沿用当前 0.2.5.0。发布到主树 artifacts/publish/win-x64；按发布编译参数重新核验测试 Core 与实际发布件一致，核快捷方式、载荷与许可。
- [ ] 更新 FOURTH-REPAIRS.md 和 PROGRESS.md，保留原红与最终绿；记录真实 Plex/WinUI 未覆盖边界。当前树受 primary 保护，保留并记录，不手工删除。
