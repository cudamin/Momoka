# WinUI validation matrix

Policy and gate timing belong to [CLAUDE.md](../../../../CLAUDE.md). Commands and argument syntax belong to [development documentation](../../../../docs/开发与验证.md). This matrix selects evidence for affected features; it does not require every combination for a text edit or every small UI change.

## Evidence types

| Evidence | Establishes | Does not establish |
| --- | --- | --- |
| Source / fixed Gallery commit / package metadata | Actual implementation route, API declaration and version assumptions | Rendered behavior in Momoka |
| Core or source-linked contract test | Decision/lifetime/ordering behavior within its stated boundary | Actual XAML layout, composition, focus, power use or native input |
| Runtime UIA / property readback | The observed live tree/state/geometry | Correct system material pixels or user-visible cursor |
| Real screen capture | Visible result for a particular app, state, OS, DPI and desktop | Correct event lifecycle, performance or another monitor configuration |
| RenderTargetBitmap / --dump-ui | XAML render/tree evidence inside that path | Desktop-composited Mica or a system cursor |
| Frame/resource trace | Measured performance for the recorded configuration | General improvement across all datasets/hardware |

Record executable and commit, any uncommitted changes, Windows/build, runtime/package versions, data fixture, window state, dimensions in DIP and pixels, scale, text size, theme, effects/contrast and run directory. Keep credentials absent. Do not reuse a prior screenshot when the source or relevant configuration changed.

## Materials

Read the [material guide](../../momoka-winui-shell/references/materials.md). Use a deterministic bright/dark/busy fixture behind in-app Acrylic and verified desktop context for system material. Do not silently change the user's wallpaper, theme, accessibility or power settings to produce a test image.

| Case | Required observations | False pass to reject |
| --- | --- | --- |
| Initial creation | Correct target, backdrop/resource, first-frame foreground and no opaque accidental cover | Only `SystemBackdrop != null` |
| Theme change while visible | Backdrop, custom tint/fallback, text, border and all interaction states agree | Current ThemeHost palette value with a stale controller |
| Active→inactive→active | Observe policy result, retained readability and correct restoration | Forcing IsInputActive permanently true |
| Effects disabled | Readable actual fallback and recovery | Only manually setting a mocked flag |
| High contrast | System foreground/background/highlight, visible focus, readable popup/disabled state | Palette dictionary exists but custom surfaces bypass it |
| Battery saver / low support / remote session | Record actual API/OS result and continued functionality | One universal Mica fallback expectation despite conflicting official docs |
| Popup and nested submenu | Material ownership, bounded placement, theme/activation, keyboard, close/reopen and no orphan targets | Main menu works while submenu leaks or clips |
| Resize / scale / clip | Material follows bounds/corners with no stale strip, seam or input interception | A single static-size screenshot |
| Video on/off | Correct video pixels, control readability and restored browsing background | A XAML-only bitmap or integrated test claimed as standalone coverage |
| Disposal / repeated use | Controllers/targets/handlers return to the expected lifetime baseline | Merely finding `Dispose()` in source |

Exercise every live product palette when colors change. Light/system mode is tested as a product mode only after it exists by approved design; isolated alternate resource fixtures must be labeled as fixtures. A missing OS/hardware state stays an explicit coverage gap. Use screenshots plus state/log evidence to explain fallback; do not claim measured energy consumption without a power measurement.

## Layout, DPI and typography

- Cover representative wide, medium and minimum supported sizes and relevant short heights. Test threshold−1, threshold and threshold+1 for each changed breakpoint, including repeated transitions with data still loading.
- For a broad refactor plan use 100%, 125%, 150% and 200% monitor scale as target coverage, plus text size 100% and 200% or the supported test environment's explicit equivalent. These are validation targets, not claims this machine provides every state.
- Include multi-monitor movement between different scales, maximize/restore and changed work area when native geometry changes. Distinguish DIP, physical pixel and text scale assertions.
- Inspect wrapped labels, expanded errors, long localized text, empty/loading states, menu placement, scrolling to the final action and focus visibility. Keep the music bar from obscuring the final list row.
- Use source-linked layout tests for calculations and real runtime views for measure/arrange, clipping and input. Changing fixed constants in a snapshot is not layout validation.

## Navigation, commands and accessibility

- Complete the changed flow by keyboard, including open/dismiss/return, menu accelerators and selection. Check mouse/touch or pen where the changed surface supports them.
- Inspect accessible name, role, value, selection/checked/busy state and important live feedback. Verify Narrator announcements for a representative flow, separately from an AutomationId scan.
- Test focus after resize, recycling, data changes, navigation back, account switch and cancelled dialogs. Hidden controls must not remain keyboard-reachable.
- Verify CommandBar overflow at narrow widths, visible labels/tooltips, ordering and permission changes while the menu is open. A command hidden by policy still needs a service-side guard.
- Test outside-dialog press only under the existing pointer safety workflow. A TryDismiss call proves a method, not delivery of real pointer input, cancellation semantics or no click-through.
- For play/seek/delete and server writes, use anonymous inert fixtures by default. Keyboard reachability is not authorization to execute the live action.

## Binding and async state

- Change the actual bound property or collection after first render; verify the visible target updates. Cover TwoWay edit timing, invalid draft, save failure, reset and reentry where affected.
- Exercise stale request after leave/reenter, account/source swap, permission change, cancellation and disposal. Verify the real production owner/generation checks, not a parallel test implementation.
- Preserve collection identity, selected item, scroll anchor and draft values for incremental changes. Whole-list refresh is tested separately.
- Register tests in the actual runner and compile the current test targets before using --no-build. Source extracts need checks that reject missing/ambiguous production boundaries.

## Virtualization and performance

Use anonymous data and repeatable local images. For library scale work establish datasets such as 100, 1,000 and 10,000 metadata items without creating an unbounded decoded-image cache. Separate item count from realized elements.

Record cold/warm first-content time, frame timing during a fixed scroll/resize, realized element count, working set/allocation/retained image evidence and subscription/queue counts as applicable. Compare the same fixture and route before/after. Set numerical acceptance budgets from the recorded baseline before optimizing; do not invent a percentage improvement target without a baseline.

Check forward/back scroll, repeated page visits, append/remove/sort/filter, missing/failed images and late async results. Realized count should reflect viewport/cache behavior rather than the entire dataset; its exact ceiling depends on the selected layout. Metadata dimensions alone do not measure decoded texture memory. A drop in source lines or element tags is not a performance result.

## Motion

Cover start/middle/end, rapid repeated action, direction reversal, navigation during animation, virtualized source disappearance, disconnected target and both system/application animation preferences off. Verify fallback when ConnectedAnimation cannot start. State and focus must be correct even with zero frames rendered. Check reduced-motion settings without changing global settings silently.

Use the actual motion primitives' tests for interruption/lifecycle and live frames for duplicate transforms, layout churn and smoothness. Playback motion uses its isolated local probe; confirm the chosen probe executes the ticker path being claimed.

## Window, activation and distribution

Verify each actual window type separately: main HostWindow/XAML island, framework settings window, other native/web hosts and independent mpv window. Check close/hide/reopen, focus/ownership, single-instance activation, minimize/restore, fullscreen and non-client hit testing at relevant scales.

For a newly accepted Windows notification feature, exercise registration/activation under the actual unpackaged identity model, stale payload/source, opt-out and duplicate activation. Server NotificationsPage and an in-app InfoBar do not count as an OS notification test.

Follow existing publish/PRI/native-payload tests for distribution changes. A running developer build does not prove the desktop shortcut, clean install or all runtime prerequisites. Unsupported OS/hardware coverage must be named. Updating only documentation does not require application build or publish.

## Completion record

For each phase record: task and source/decision IDs; final code revision; tests with actual pass/fail/skip counts; selected runtime sections and explicitly omitted sections; screenshots/traces; unresolved defects and unavailable environments; whether merged into master and whether application delivery was required/completed. Preserve the original failure evidence. Missing required evidence blocks that product phase's completion; it must not be relabeled as an intentional exception without the policy decision.
