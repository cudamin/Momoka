# Applying WinUI features to Momoka

Use for controls, layout, binding, collections, motion, accessibility and platform integration. Read [policy](../../../../CLAUDE.md#winui-官方优先与偏离确认) and the matching [source route](gallery-source-map.md). Materials have their own [detailed guide](materials.md). The techniques here are defaults to evaluate, not a blanket migration instruction.

## Native controls and command surfaces

| Need | Starting control/pattern | Preserve when adapting | Avoid |
| --- | --- | --- | --- |
| Top-level navigation | NavigationView + Frame, native pane states | Current destination, history, focus, pane toggle, account/source identity and capability visibility | Separate custom navigation state that disagrees with Frame; restoring a removed mode without a product decision |
| A group of page/batch commands | CommandBar with primary and secondary commands | Ordering, labels, accelerators, overflow, disabled/busy state where semantically appropriate and service guards | A custom StackPanel of buttons solely to recreate built-in overflow; replacing a single button with a toolbar |
| Menus | MenuFlyout, MenuFlyoutItem/Toggle/SubItem | Keyboard navigation, selection semantics, submenu boundaries, dismiss/focus return | Manually positioned Popup with no equivalent focus and accessibility support |
| Editable settings | ToggleSwitch, ComboBox, NumberBox, TextBox and appropriate selectors | Validation timing, draft values, units, error recovery, app/source lifetime | Raw pointer handlers for standard actions; saving invalid half-typed input on every keystroke |
| Short contextual choices | Flyout/TeachingTip as appropriate | Light-dismiss, keyboard reachability and clear relationship to its anchor | Using a modal dialog for every small choice |
| Confirmation or a substantial modal form | ContentDialog | Default/cancel/close semantics, focus containment, async submit protection, actual XamlRoot | Treating a light-dismiss recommendation for flyouts as automatic authorization to alter dialog semantics |
| Progress / status / failure | ProgressRing/ProgressBar, InfoBar and accessible feedback | Distinguish loading, empty, partial, failure, retry and confirmed success | Hiding a failed operation or giving success before a write/queue acceptance is confirmed |
| Selectable rows | ListView/GridView/ItemsView if available and suitable | Selection, keyboard, UIA patterns, virtualized focus and stable item identity | Recreating selection with Border taps without providing the same semantics |
| A custom high-volume arrangement | ItemsRepeater + suitable VirtualizingLayout | Realization/recycling, focus/selection ownership, viewport and image lifetimes | Assuming ItemsRepeater supplies ListView selection automatically |

Inspect native templates before overriding: resource aliases often suffice. A template replacement owns the full hover/pressed/checked/disabled/focused/contrast matrix. Keep custom visuals that have a real product purpose, but document where native behavior is retained or replaced. CommunityToolkit.Mvvm already serves a different purpose from adding a new visual Toolkit dependency.

## Responsive layout and scroll ownership

1. Identify the actual available content width after sidebar, padding, title bar and music bar. Breakpoints use DIP; OS scale is not a second layout breakpoint.
2. Define wide, medium and minimum supported window behavior in terms of visible commands, column count, navigation mode and text wrapping. Establish the supported minimum with the current host; do not promise phone width if the application cannot resize that far.
3. Use Grid sizing, VisualStateManager/AdaptiveTrigger or a focused computed layout policy. Keep one owner for each derived width/state and notify every dependent binding; avoid several competing SizeChanged handlers rebuilding the same tree.
4. Each scroll axis needs a clear owner. Inspect the viewport constraint before selecting a nested ItemsRepeater/GridView. Infinite measurement can defeat virtualization; a horizontal shelf within a vertical page is a different layout from a full collection grid.
5. Keep a bounded vertical scroll region for long forms and menus. Ensure primary/recovery actions remain reachable with a short window and expanded text. Popups must not be clipped below the taskbar or outside their root.
6. Preserve item identity, selection, anchor and focused control on resize. Do not rebuild every card or settings section simply to update spacing.
7. Exercise breakpoint minus one / exact / plus one, rapid repeated crossings, source changes while loading and open popup states. A static wide/narrow screenshot misses transition bugs.

Current anchors: ShellPage/HostWindow, HomePage/CardShelf/CardStrip, LibraryPage, DetailPage and DetailViewModel's PageWidth/AnnounceShape, SettingsPage/SettingSection, MusicLibraryPage and the shared music bar. Check real filenames and current thresholds before editing.

## DPI and text scaling are independent

- Use DIP for XAML layout, physical pixels for native HWND geometry and swapchains; make conversions explicit at a narrow boundary. Verify `XamlRoot.RasterizationScale`, monitor DPI and host state agree when relevant.
- A 125% or 150% monitor is not the same test as increasing the Windows text-size setting. Both can clip labels or displace click targets differently.
- On per-monitor DPI change, honor the correct suggested placement and update native hit regions, overlays, image decode demand and any physical-pixel caches. Do not double-scale XAML dimensions.
- Check fractional scaling for one-pixel borders, icon alignment, title-bar resize bands, flyout anchor position and corner clips. Measure against current DPI rather than assuming “8 px” always means 8 physical pixels.
- Test restore/maximize/fullscreen, a move between monitors, monitor work-area changes and DPI changes while a secondary window/menu is open. Missing hardware is a reported coverage gap, not a simulated pass.
- Preserve text reflow and localized string growth. Avoid fixed heights that clip larger text, and avoid shrinking fonts merely to keep a desktop arrangement.

## Binding, state and architecture

Use [architecture](architecture.md) and [page lifetimes](page-lifetimes.md) for the current ownership contracts.

- Prefer typed x:Bind for stable page/VM/template relationships. Specify the mode intentionally: default OneTime is correct for immutable data, not for changing playback, draft or availability state.
- Use Binding for a dynamic DataContext when it actually fits; do not convert all bindings mechanically. TwoWay is for user-editable state, OneWay for display derived from the source.
- Decide TextBox update timing from the interaction: instant search and a validated numeric draft need different commit rules. PropertyChanged on every field is not a universal requirement.
- Retain PropertyChanged/dependent notifications, collection identity and incremental notifications. Do not Clear/refill a list for one changed card.
- Keep business commands in services/testable rules and presentation transitions near the UI. Use interfaces for substitution or restricted access, not merely because a type is registered in DI.
- Preserve Attach, Release/Cancel/Dispose, captured account/source, permission revision, request generation and cancellation checks. A non-null service field does not establish an active page.
- Defer expensive work with cancellation and owner checks; only mutate bound collections on their owning UI thread. Moving CPU work to Task.Run does not make a captured UI object safe.
- Use x:Load only after auditing named references and animation targets. Materialize before access and release resources when actually detached. Do not declare the feature universally unsafe from an old missing-namescope failure.

## Large collections and images

1. Distinguish network pagination, DTO storage, view-model creation, realized UI elements and decoded image memory. A virtualized panel solves only some of these costs.
2. Verify a finite viewport and an appropriate virtualizing layout. Count realized containers during a deterministic scroll, not just the presence of an ItemsRepeater tag.
3. Attach/detach item work using the actual item/container ownership path. Account for ElementPrepared/ElementClearing, recycled DataContext/item changes and asynchronous completions. Historical Loaded/Unloaded workarounds must be retested against the current layout/version.
4. Cap image decode requests to intended display size and scale, cancel stale loads, keep bounded caches and release no-longer-owned holders. A BitmapSource.PixelWidth reading can describe source metadata instead of decoded memory; use the right evidence.
5. Preserve the same item after insertion, removal, paging, sort/filter changes and back navigation. Test failed/partial source loads and same-page account changes.
6. Bound long-lived subscriptions and queued DispatcherQueue updates; coalesce repeated notifications where safe. Avoid heavy I/O, sorting or synchronous waits on the UI thread.
7. Compare CPU/frame time, realized count, allocations/working set and retained images under the same dataset and machine state. Use WPR/WPA or available equivalent tooling after validating the capture configuration; absence of a trace is not an improvement claim.

## Motion and composition

| Scenario | Prefer | Essential behavior |
| --- | --- | --- |
| Page navigation | Native navigation transitions | Avoid duplicate page/custom transforms; preserve focus, history and reduced motion |
| Same item moves from card to detail | ConnectedAnimation when source/destination really correspond | Prepare before navigation; start when target is laid out; handle missing/recycled/cancelled target and TryStart failure with a normal navigation fallback |
| Add/remove/reposition items | Appropriate theme transitions | Keep collection/item identity, virtualization and cancellation correct |
| Brief status or overlay appearance | Platform transition or a small existing opacity/translation primitive | Interrupt from current state, close immediately when preference is off, remove hit testing when finished |
| Playback/video composition | Existing composition surface and tested control motion | Avoid layout work per frame, duplicate effect pipelines and any change to intended video pixels |

Reuse HomeMotion, SurfaceMotion, GlyphPulse and current settings motion only where their responsibility fits. Do not replace their tested interruption behavior solely to obtain a native API name. Inspect system and application animation preferences, cached-window activity and event cleanup. Verify zero-animation mode before counting polish as complete.

## Accessibility and input

- Keep keyboard navigation logical and visible: Tab/Shift+Tab, arrows in lists/navigation, Enter/Space, Escape, accelerators and context menus. Avoid focus traps and accidental playback from a navigation key.
- A stable AutomationId is a locator, not an accessible name or role. Prefer built-in peers and expose Name, state, value, selection and live feedback appropriate to the actual control.
- A custom item must communicate its selected/checked/busy/disabled state and position in a collection when needed. Confirm with the runtime UIA tree and Narrator; a source attribute count is insufficient.
- Focus must return meaningfully after flyout/dialog dismissal, a collection update, modal cancellation and back navigation. Hidden capability rows must leave the tab sequence.
- Provide a way to reach hover actions by keyboard and touch. Tooltips alone do not label essential actions. Do not shrink touch targets only to match a screenshot's density.
- Use system high-contrast brushes and visible focus treatment; test status without color as the only signal. Text, selected states and error icons must remain distinguishable.
- Keep strings in the existing localization path, tolerate longer text and RTL where relevant to the supported product scope. Do not add new supported locales merely to demonstrate localization capability.

## Windowing, activation, notifications and deployment

- Prefer AppWindow and framework Window where compatible. Main HWND/XAML-island migration is a separate engineering decision from native control adoption. First prove the existing composition/fullscreen/minimize behavior can be preserved.
- Centralize lifetime and activation ownership rather than routing it through page constructors. Current ActivationListener, Program/App and settings hide/close semantics have specific tests; preserve behavior while evaluating AppLifecycle samples.
- Do not confuse `NotificationsPage`/Emby server notification configuration or the shell InfoBar named Toast with Windows App SDK desktop notifications. Adding OS notifications requires an explicit useful scenario and supported activation/registration path for the actual distribution model.
- For any accepted OS notification feature, cover foreground/background activation, missing/expired source identity, dismissal, opt-out and safe handling of action data. Do not put credentials or sensitive media URLs into notification activation payloads.
- Maintain unpackaged x64 unless the user chooses another distribution model. Verify package identity requirements, runtime/bootstrap behavior, merged PRI resources, native assets/licenses and install/update behavior from actual output.
- Do not adopt Gallery's experimental SDK, broad meta-package or self-contained settings as a side effect of a UI refactor. Dependency upgrades follow the separate project procedure and payload review.

Use the [validation matrix](../../momoka-verification/references/winui-validation.md) and [phased plan](../../../../docs/WinUI重构计划.md) to turn these methods into scoped work with measurable exit criteria.
