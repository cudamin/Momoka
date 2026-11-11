# Mica surfaces implementation plan

> Execute in the existing isolated worktree. The user requested direct implementation against Microsoft's Mica guidance, including compact media details.

**Goal:** Use a single system backdrop per window, with the documented command/content layers on home, libraries, settings and compact details.

**Architecture:** Retain the main XAML island's Mica Alt. Give the settings Window Mica Base. Scope the translucent solid layer brushes to these surfaces, leaving unrelated global palette roles intact. Preserve immersive artwork and playback backdrop suspension.

**Tech stack:** Existing WinUI 3 / Windows App SDK, C# and XAML; no dependency changes.

**Spec:** https://learn.microsoft.com/zh-cn/windows/apps/design/style/mica (documentation commit `1b116d52a415fb84b45d26219181a4d25949a760`). SDK implementation reference: microsoft-ui-xaml `ba3a8d59e6de4e2b86ca4f61c78bc26ccf129afd`, `src/controls/dev/CommonStyles/Common_themeresources_any.xaml` and `src/controls/dev/Materials/MicaBackdrop/MicaBackdrop.cpp`.

## Constraints and review focus

- One backdrop per window; do not stack independent Mica samplers on information cards.
- BaseAlt uses `LayerOnMicaBaseAltFillColorDefault` command layer, then `LayerFillColorDefault` content layer.
- High contrast brushes bind system colors, never the SDK's sentinel color tokens.
- Preserve media artwork, navigation state, theme accents and playback behavior.
- The settings window must remain transparent through its root, Frame, caption and page; hosted native pages follow the same scope.
- Theme changes, cached navigation, compact/immersive transitions and settings hide/show must retain the right layers.

## Tasks

- [ ] Add and run regression checks for layer resources, page coverage and high contrast.
- [ ] Add scoped Mica resources and update home/library/settings surfaces plus shell background routing.
- [ ] Replace compact-detail per-element backdrops with command/content layers in an isolated agent worktree.
- [ ] Build current Release Core/Shell/tests, run the complete main suite and affected regressions.
- [ ] Run isolated UI checks and inspect real screen captures across all five live themes and relevant window sizes; review the final diff independently.
- [ ] Commit, integrate into master when safe, repeat affected validation there, and publish to the actual delivery directory; record any concrete blocker.

## Execution record

- Baseline Shell Release build at `8dea65c`: 0 warnings, 0 errors.
- Detail implementation delegated to isolated `mica-detail` worktree; all shared shell/resources written in `415c`.
