# 主页固定轮播与柔和接缝实施计划

> **For agentic workers:** Use executing-plans to implement the checked steps in this session.

**Goal:** 实装用户已确认的第二版真实数据 Demo：去掉慢缩放，全扩边高斯平滑过渡到原图。

**Architecture:** 沿用现有单张扩边位图和 ImageBrush；高斯衰减移入原图左8%，不加独立模糊层。保留自动换图、交叉淡入、文字动画和现有设置。

**Tech Stack:** .NET 10、WinUI 3、现有 CPU 高斯，无新增依赖。

**Spec:** ../specs/2026-11-07-home-carousel-static-design.md（用户于本轮明确批准实装）

## Global Constraints

- sigma12、原图15%扩边、原图8%过渡；过渡后原图和全部alpha不变，采用已批准Demo的smootherstep。
- 本树独立实现；主树已有后续修复先同步，整合/发布时重新检查写入会话与状态，保护其他工作。
- 不真实播放或写服务器；实装后实际WinUI宽窄截图，测试/提交后主树重验发布。无远程上传授权。

## Review Focus

- 延长区最末列不能再恢复清晰高频纹理；单测交错行逐列检查。
- 原图首列不能出现硬切，过渡末尾不能有新接缝；高反差单测和真实WinUI像素读数。
- 初始加载、自然换图、返页不得启动背景缩放；实际时钟探针测两轴单位缩放，不主动停camera。
- 1px图、alpha、取消与过渡后原图保护；单测保留与扩充。
- 缓存不能复用旧处理结果；更新规则版本，实际加载位图和宽窄覆盖检查。

### Task 1: 高斯完整覆盖并柔和接缝

- [ ] 更新HomeCarouselTests旧“完整原图不动/末列清晰”契约为“过渡后不动/全扩边高斯”；增加交错行与首列黑白阶跃、尾端/alpha/取消，跑本树当前Release主测试看红。
- [ ] CarouselBackdrop新增TransitionWidth(width)=max(1,round(width*0.08))；移植批准Demo卷积与smootherstep。BannerBackdropCache规则v4→v5。
- [ ] 重编译Tests/Core并跑主套件，预期0失败0跳过。

### Task 2: 图片静止与实际验证

- [ ] 在HomeBannerClock自然计时样本中断言背景两轴单位缩放；更新HomeBannerLibraries像素检查为扩边/首列/中段/过渡后/中央，删除手动1.045模拟。
- [ ] 移除HomeBanner的Push/PushScale、Nudge、ScaleBand及两个调用；保留单位变换以兼容已有定位句柄，更新过时注释与诊断说明。
- [ ] 构建Shell Release/x64，运行library分区实际WinUI检查与截图，预期所选检查无失败，新增断言可用反例验证；独立真实PNG比对正式算法与批准Demo一致。
- [ ] 独立审查最终diff、相关格式/差异检查通过后提交。

### Task 3: 主树交付

- [ ] 确认主树没有其他写入/文件锁，整合本次提交，保护主树新改动。
- [ ] 主树编译当前Tests/Core/Shell、主套件与受影响运行检查；发布到主树artifacts/publish/win-x64并核快捷方式/载荷/程序集hash。
- [ ] 更新PROGRESS记录最终证据与覆盖范围，保留本树Demo与日志至整合完成；不上传GitHub。
