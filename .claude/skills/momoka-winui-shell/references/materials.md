# Mica, Acrylic and surface composition

Read for every material/background change, including a new opacity slider or a restyled control. Approval policy is in [CLAUDE.md](../../../../CLAUDE.md#winui-官方优先与偏离确认); evidence sources and the Gallery revision are in the [source map](gallery-source-map.md). This guide specifies selection, implementation and verification methods. It does not silently authorize replacing existing product choices.

## 1. Name the material correctly

| Material | What it renders | Intended starting point | What it does not establish |
| --- | --- | --- | --- |
| Mica / MicaKind.Base | An opaque, theme-aware foundation derived from desktop wallpaper | Long-lived application/settings window foundation; let it remain visible around content and in the title region | It is not live frosted glass over other windows, and does not blur an application's poster or video behind a control. |
| Mica Alt / MicaKind.BaseAlt | A different Mica foundation treatment, providing stronger differentiation for layered/tabbed chrome | A hierarchy needing distinction between title/tabs/command areas and content | It is not exclusively legal for tabbed applications, and not a fixed brightness/opacity formula. Verify both themes and actual wallpaper. |
| Desktop Acrylic / DesktopAcrylicBackdrop | System background Acrylic, with translucency, blur, tint and texture | Transient menus, flyouts and appropriate light-dismiss surfaces | A public API allowing it on a Window does not make it the recommended background for a large permanent application. |
| In-app Acrylic / AcrylicBrush | Frosted material sampling content behind it within the XAML composition context | Overlaid supporting panes where preserving underlying page context is useful | In WinUI 3 it has no UWP BackgroundSource switch and does not promise sampling of unrelated HWNDs, WebView2 or an mpv swapchain. |
| Solid or low-opacity theme brush | A painted color layer, possibly alpha-blended | Content cards, persistent panes, controls, readable text surfaces and deterministic fallback | A translucent SolidColorBrush alone is neither Mica nor Acrylic. |
| Scrim/gradient or image blur | Contrast support over known image/video content | Hero captions, playback controls, transitions where the content source is known | A processed bitmap or blurred screenshot is an application effect, not evidence of a system material. |

Mica's wallpaper personalization can be subtle. Do not increase layers or disable fallback just because a screenshot looks nearly solid. `MicaBackdrop` being assigned proves configuration, not current pixels or the absence of an opaque covering layer.

## 2. Technical support versus design advice

Use these labels in a proposal:

- **Technical constraint:** the target/API/version/compositor cannot do the requested operation. Confirm with the actual package and implementation. User agreement cannot make an unsupported combination work.
- **Official recommendation:** technically possible, but Microsoft recommends a different usage. Present the reason and ask under CLAUDE before adopting the conflicting request.
- **Supported option:** a documented choice such as controller tint/luminosity or unpackaged deployment. Assess costs; a non-default value is not automatically against guidance.
- **Momoka choice:** product-specific behavior, defaults or retained workaround. Identify its scope and history; it is not a Microsoft rule.
- **Unverified:** incomplete/version-conflicting evidence. Investigate or use a supported alternative; do not invent certainty.

### Version-sensitive local backdrops

The older Mica design page says “Don't apply backdrop material to a UI element” and “Don't apply backdrop material more than once in an application.” The newer system-backdrop documentation describes `SystemBackdropElement` for a bounded card/panel and explicitly allows coexistence with a window backdrop. It states Windows App SDK 2.0 or later. Gallery demonstrates the element. Read the new API as an explicit supported mechanism; do not turn the older broad recommendation into a permanent API prohibition.

For the 2026-11-12 planning base, public `SystemBackdropElement`, `SystemBackdrop` and `CornerRadius` members were found by metadata inspection in Momoka's restored WinUI 2.3.9 `.winmd`; its XML comments did not list the type. This establishes a reference API, not successful rendering in Momoka. The framework source at `2a73274618c3f1042ee617dca5f64be782f93261`, `controls/dev/SystemBackdropElement/SystemBackdropElement.cpp`, sizes a placement visual, applies a rectangular/corner clip and connects/disconnects its target with lifecycle. That framework revision is not proven to be the exact installed binary source.

Use a bounded system backdrop only for an identified need after checking the package, target, theme propagation, lifetime, clipping and cost. Avoid instantiating one controller/target for every recycled poster. Its existence does not make per-card Mica or a wall of Acrylic the preferred design. Gallery's note says theme changes come from app/OS changes, not arbitrary parent RequestedTheme; Momoka's custom role updates therefore need an explicit runtime test.

## 3. Surface-by-surface decisions

“Default” below is a recommendation for new/reviewed work. Existing differing surfaces remain current product behavior until the corresponding decision is made.

| Surface / state | Default choice | Avoid / requires explanation | Momoka owner and review point |
| --- | --- | --- | --- |
| Main browsing window | One coherent Mica Base foundation for its target, content layered above it | Full-window Desktop Acrylic; independent wallpaper materials behind every child; opaque roots that accidentally conceal the whole backdrop | `Windowing/HostWindow.cs` ApplyBackdrop; `Views/ShellPage.xaml` ContentHost. Native frame DWM and island are distinct targets; inspect their actual extent before claiming double-painting. |
| Title bar | Reveal the same foundation; native title/command contrast and hit regions | A decorative layer covering drag/caption regions; making the whole title bar client input without equivalent behavior | Shell title strip and HostWindow's non-client regions; test drag, maximize, snap, focus and DPI. |
| Mica Alt option | Evaluate for a hierarchy that benefits from differentiated title/tabs and content | Calling BaseAlt universally brighter/darker or forbidding it without tabs; copying Gallery's sample values as product requirements | Current main window chooses Base. Changing its choice must include all foreground/content layers in the comparison. |
| Separate settings/MoviePilot window | Its own appropriate long-lived foundation and readable content layer | Assuming another HWND's backdrop supplies this window; forcing owned/topmost behavior merely for visual consistency | `Views/SettingsWindow.cs`, `Views/MoviePilotWindow.cs`; inspect the latest source because a parallel settings/Mica change exists. |
| Root of a content page inside the shell | Expose the shell foundation or use the designated content layer once | Applying both a page-wide alpha layer and an identical shell alpha layer; treating transparency as a substitute for readable grouping | Current root policy uses Transparent with `EgCanvasBrush` on ContentHost. Maintain until a reviewed redesign replaces it. |
| Permanently docked navigation pane | Solid or suitable theme layer with clear hierarchy over the foundation | Desktop Acrylic as a permanent large pane; adjoining multiple independent frosted panes | `SidebarHost`, NavigationView. Current project tint layer is a choice, not a universal official opacity. |
| Overlay/compact navigation pane | Native NavigationView overlay treatment; in-app Acrylic if it preserves useful context | Hand-building a blur when the native template already handles it; making text hard to read over moving content | Inspect runtime pane mode, not just its expanded screenshot; keep focus containment/dismissal. |
| Settings cards, metadata panels, dense reading areas | Native/system card or solid/low-opacity theme surface | Frosting every nested card; several borders/shadows/blur layers doing the same job | Palette/Styles, settings row templates. Remove only genuinely redundant layering after measuring readability. |
| Poster, person image, album artwork | Preserve the image; use a local readable caption/selection treatment | Mica over the artwork; blur obscuring the very image the user is selecting | PosterCard, EpisodeRow, MusicLibraryPage. Test white/dark/busy/missing artwork. |
| Hero/detail backdrop | Keep the known image; use a scrim/gradient or reviewed bounded treatment behind text | Calling image blur Mica; applying Desktop Acrylic when the intent is to blur the page's hero | DetailPage.Backdrop, BackdropInk, detail resources. Sampling existing hero pixels and choosing foreground is separate from a backdrop controller. |
| Buttons, switches, check boxes, text boxes | Platform visual states and brushes first | Controller per button; Acrylic everywhere; changing only idle state so hover/pressed/disabled/high contrast breaks | `Theme/ActionButtons.xaml`, `Theme/SettingsInputs.xaml`. Existing detail glass treatment is a product-specific option to review, not a mandatory new-control style. |
| ComboBox and AutoSuggestBox open popups | Native popup surface/theme resources | Extra blur added over an already Acrylic template; incorrect popup XamlRoot, clipping or keyboard dismissal | EgPicker workaround and input templates; closed field and open popup are different surfaces. |
| MenuFlyout / flyout / context menu | Native transient Acrylic treatment when supported and readable | Stacking a custom backdrop over the template's material; assuming theme resource changes repaint controller overrides | `Views/DetailMenuBackdrop.cs` creates a controller per connected menu target; verify parent and submenu, open-theme changes and cleanup. |
| Tooltip / TeachingTip / InfoBar | Native surface for its semantic role; InfoBar for persistent in-app status | Treating every small transient control as a reason to create a controller; using a notification effect to hide required recovery actions | Keep content, focus, live announcements and error actions readable in fallback. |
| Modal ContentDialog | Default readable dialog surface and a separate smoke layer | Conflating light-dismiss flyouts with modal dialogs; Acrylic or low alpha that exposes distracting page text behind a long form | Current Momoka uses opaque dialog surfaces plus outside-click cancellation. Both are explicit product choices; changing dismissal/material needs separate analysis. |
| Light-dismiss filter/settings overlay | Native flyout/pane if it meets interaction and size needs; reviewed in-app material otherwise | Desktop material used to simulate an in-app blur; multiple adjacent frosted sheets; a visually invisible but hit-testable mask | Library overlays have custom motion and state. Preserve draft, cancel, focus return and no click-through. |
| Integrated video image and black bars | Exact video rendering and stable background required by the playback pipeline | Material on top of video that changes intended pixels; pretending Mica reveals video; reviving screenshot-and-blur capture without a reviewed requirement | CompositionVideoTarget and PlayerPage. HostWindow disables the browsing backdrop in video state; verify return restores it. |
| Playback control bar/timeline/subtitle/OSD | Readable scrim or existing theme/contrast treatment, minimal occlusion | Assuming AcrylicBrush can sample mpv; high GPU-cost blur over every frame; hiding subtitles under animated glass | `PlayerPage.TimelineMaterial.cs` retired its capture/blur path at user request. Altering this is a product and playback change. |
| Standalone mpv/uosc | Its own rendering/theming system | Injecting WinUI material into the independent native window or claiming integrated probes cover it | Read playback skill and uosc source; no actual playback without the relevant authorization. |
| WebView2 content/admin console | Preserve its own page composition; coordinate surrounding native chrome | Assuming CSS blur equals WinUI Acrylic or that XAML blur captures WebView pixels correctly | Keep trusted-origin/credential and native-host boundaries; assess actual surface behavior. |
| High-contrast or effects-disabled UI | System colors / readable opaque fallback | Manually forcing translucency to defeat the user's accessibility/system choice | HighContrast dictionary and relevant controller configuration; verify all control states and popups. |

## 4. Selecting the API and wiring it

### Framework Window

1. Confirm the actual owner is `Microsoft.UI.Xaml.Window`. Prefer its `SystemBackdrop` with MicaBackdrop or DesktopAcrylicBackdrop when the scenario fits; the implementation guide recommends this simpler path for most apps.
2. Let a deliberate root/content layering model reveal it. An opaque child does not disable the API; it covers the result. Count visual layers before changing opacity.
3. Use per-window state. Opening a second window must not move the first window's controller/target or assume the same XamlRoot.
4. Keep title bar and caption colors coherent with actual theme and activation. Do not set input-active true forever just to keep the material bright.
5. Confirm unavailable/failed material still yields readable content. Gallery's Mica→Acrylic→solid chain is optional; a direct solid fallback can be the appropriate product choice.

### Momoka's HWND and XAML island

1. Read `Windowing/HostWindow.cs`, its `DesktopWindowXamlSource`, DWM attributes, `ApplyBackdrop`, `UseBackdrop` and `VideoVisible` together with `CompositionVideoTarget`.
2. Apply the backdrop to the real supported island/target path. Do not paste `Window.As<ICompositionSupportsSystemBackdrop>()` into a host that is not that object.
3. Separate native frame treatment from client-area layers and playback visuals. Old comments about a child video HWND can describe retired pipelines; follow actual source before restructuring.
4. Keep browse→video→browse, minimize/restore, activation and disposal behavior explicit. An exception that logs “solid fallback” is not proof a solid brush has been painted; inspect final code and pixels.
5. A proposed migration to framework Window requires a focused compatibility investigation for composition, resizing, fullscreen, HDR/color output and state restoration. Do not bundle it into a brush cleanup.

### Custom system-backdrop controller

Use when the simple SystemBackdrop abstraction cannot express a justified target/configuration. Follow Gallery's supporting window source, then complete production lifetime handling:

1. Check `MicaController.IsSupported` or `DesktopAcrylicController.IsSupported` for the actual runtime.
2. Use a valid `ICompositionSupportsSystemBackdrop` target and the required dispatcher queue on its UI thread; check the current package's setup API.
3. Create/configure the controller, add its target, set a SystemBackdropConfiguration reflecting the real active/theme/high-contrast state. In a SystemBackdrop subclass, assess the framework-provided default configuration before building a parallel observer system.
4. Handle initialization failure by removing any attached target and disposing the newly created controller. A failed attachment cannot count as success.
5. On activation/theme/contrast changes, synchronize relevant state. Store removable handlers; repeated open/switch operations must not accumulate subscriptions.
6. On target disconnect, remove that target, release configuration ownership, unsubscribe and dispose exactly once. Avoid sharing a controller across unrelated lifetimes merely because they share a brush color.
7. Custom tint/luminosity/fallback properties change who owns theme adaptation. Update every overridden value for the actual theme; do not assume setting configuration.Theme will restore the stock defaults.
8. For menus, track each connected target and nested popup. Verify open→close→reopen and theme changes while open, including the transition to a solid system fallback.

### In-app AcrylicBrush

1. Prefer the appropriate built-in theme Acrylic resource. Inspect the installed template/resource rather than hardcoding a UWP resource name.
2. Use an actual XAML background behind the brush; no useful source or an occluding opaque layer can make the result look flat.
3. Scope a custom brush to the component that needs it, with theme-aware TintColor and readable FallbackColor. Keep disabled/high-contrast resources available.
4. Do not add UWP's `BackgroundSource=HostBackdrop` to WinUI 3 AcrylicBrush. Background/system Acrylic uses the SystemBackdrop/controller route.
5. Test animation, scrolling and repeated containers for GPU/visual cost. Applying it to an element is supported; applying it to hundreds of moving items may be an unsuitable product choice.

## 5. Parameters: what each setting actually controls

| Property | Meaning / default ownership | Implementation precautions |
| --- | --- | --- |
| MicaKind Base / BaseAlt | Selects a system Mica treatment | Start with the system choice, not a hand-built imitation. Compare hierarchy rather than assigning a universal transparency percentage. |
| DesktopAcrylicKind Base / Thin | Selects a system Acrylic treatment | “Thin” is a supported appearance variant, not a guarantee of readable text or lower GPU cost. |
| TintColor | Color contributing to the material's tint | Alpha participates in some brush calculations; inspect the implementation. Use theme roles and test all states. |
| TintOpacity | Strength of the color tint | Typical API input range is 0–1; verify setters/clamping and reject invalid saved values. It is not whole-element opacity. |
| AcrylicBrush.TintLuminosityOpacity | Explicit luminosity tint setting; nullable behavior allows system-derived treatment when unset | Do not confuse it with controller.LuminosityOpacity. Reset must distinguish “unset/system-derived” from numeric zero. |
| Controller.LuminosityOpacity | Controller's luminosity treatment | Once custom properties are set, manually maintain their theme values as the implementation guide requires. |
| FallbackColor | Color shown when material effects cannot/should not render | Treat it as a first-class state, with sufficient foreground contrast and an appropriate solid appearance. A transparent fallback can defeat readability. |
| Brush.Opacity / UIElement.Opacity | Opacity of the whole brush/element; element opacity includes descendants | Not a material tint control. Lowering an entire button's opacity also fades its label/focus cues. Do not label it “Acrylic tint.” |
| AlwaysUseFallback, where supported on AcrylicBrush | Forces the brush's fallback for a deliberate scenario | Useful for a controlled fixture or product fallback; it is not proof of actual battery-saver/system-policy behavior. |

The API supports custom settings; exposing every parameter as a product preference is a separate usability decision. Group only settings the user can understand, hide ineffective dependents under CLAUDE and preserve saved values when hiding. A reset must return to the real chosen default, not the last previewed value. Temporary previews must use isolated/app-owned state and not silently persist. `AlwaysUseFallback=false` does not override the system's decision to disable effects; it only stops explicitly forcing the brush to fallback.

Do not use a formula such as `(1 - luminosity) * (1 - tint)` as an exact Acrylic transparency or contrast measurement. The inspected framework [AcrylicBrush.cpp](https://github.com/microsoft/microsoft-ui-xaml/blob/2a73274618c3f1042ee617dca5f64be782f93261/controls/dev/Materials/Acrylic/AcrylicBrush.cpp) combines tint, luminosity, blur/noise and blending. System materials are composited effects. A tint number and a screenshot sample answer different questions. The bounded-material lifecycle reference is [SystemBackdropElement.cpp](https://github.com/microsoft/microsoft-ui-xaml/blob/2a73274618c3f1042ee617dca5f64be782f93261/controls/dev/SystemBackdropElement/SystemBackdropElement.cpp).

Current Momoka canvas/sidebar alpha constants and DetailMenuBackdrop's 0.65/0.45 are application choices. They are not Microsoft recommendations. First-frame Palette.xaml values and ThemeHost updates must agree; do not add an independent “global opacity” setting to multiply the same layers again.

## 6. System states and fallback

| Condition | Required engineering behavior | Evidence limitation |
| --- | --- | --- |
| Light/dark/selected Momoka palette | Readable background, foreground, borders, selected/hover/pressed states and caption buttons | All current shipped Momoka palettes are dark. Light/system mode requires the product decision; an isolated light resource test is not a shipped light theme. |
| High contrast | System foreground/background/highlight roles; no forced decorative tint defeating contrast | A nonempty HighContrast dictionary does not prove every popup/custom controller uses it. |
| Transparency effects off | Respect the actual API/system fallback, maintain contrast | Do not force active material or fake blur to defeat the preference. A simulated bool is only logic coverage. |
| Inactive desktop window | Preserve the system's material policy and readable controls; restore on reactivation | Background Acrylic and in-app Acrylic need not behave identically. |
| Battery/energy saver | Respect the API's actual policy; verify Acrylic fallback and observe Mica separately | Older Mica design docs list a solid fallback in Battery Saver; the newer material API overview says Battery Saver disables Acrylic while Mica is unaffected. Record OS/build/API and actual result; do not hardcode one universal Mica outcome from conflicting pages. |
| Windows version/support unavailable, low-end GPU, remote session | Functional readable fallback; no crash/retry loop or transparent unreadable window | Different APIs/support checks have different limits. In-app Acrylic, Mica and bounded system backdrop are not interchangeable. Record untested environments. |
| Resize/DPI/monitor move | Correct bounds, clip, corner scaling, text and input region alignment; no stale backdrop target | A static screenshot on one monitor does not prove transitions between scales. |
| Hidden/minimized/closed/rapidly reopened window or popup | No orphan target/controller/observer; correct material on reopen | Checking Dispose source alone does not establish no runtime leak. |
| Video active then stopped | Video pixels and controls remain correct; browsing material restored according to current state | Integrated local probes do not validate standalone uosc or external-mpv behavior. |

An explicitly chosen application policy may use a more conservative solid background, for example during energy saver. Treat that as an application feature with its own decision and tests, not a correction to a supposed universal OS rule. It must still respect transparency-off/high-contrast choices, and no energy saving can be claimed without measurement.

Never change the user's global Windows accessibility/power settings silently for a screenshot. Prefer an isolated fixture for application logic; when real system-state verification is needed, use an agreed test environment or ask for the required system-state change and record what was actually exercised.

## 7. Combinations to reject or pause

- Reject a nonexistent API/unsupported target rather than manufacturing a successful fallback claim. Offer a supported route and its visual difference.
- When no valid informed confirmation already covers the same proposal, scope and conditions, pause for the user's choice before using Desktop Acrylic as the entire long-lived app background, tiling adjacent frosted panes, or placing low-contrast accent text over Acrylic contrary to the design guidance. Reuse a still-valid confirmation under CLAUDE.
- Do not layer blur-on-blur merely to intensify an effect. First inspect native template Acrylic, custom backdrop, solid overlay and element opacity separately.
- Do not create decorative geometry that steals hit testing, clips focus, hides a disabled state, makes a dialog click-through or allows unsafe activation behind a modal surface.
- Do not use a material change to reinstate removed themes, restore the retired video-sampling timeline, upgrade to experimental packages or alter server-side settings.
- Do not categorize a normal documented controller parameter, Mica Alt on a nontabbed window, or a correctly supported SystemBackdropElement as impossible solely from an old broad recommendation.

## 8. Verification and change record

Use the detailed [WinUI validation matrix](../../momoka-verification/references/winui-validation.md). For a material change record the surface and owner, background source, selected API/version, template/resource layers, default and overridden properties, lifecycle/fallback behavior, official rationale or confirmed deviation, and current screenshots/measurements.

At minimum inspect initial render, theme change, active/inactive state, fallback, repeated opening/closing, relevant sizes/scales, text and input states. For pictures/video include dark, bright and busy synthetic/local content. Use real screen captures for system material; RenderTargetBitmap does not prove desktop composition. Match screenshots to the executable/commit and environment. Tests of tint constants, resource keys or mocked policy cannot be reported as actual material, GPU, power or accessibility verification.
