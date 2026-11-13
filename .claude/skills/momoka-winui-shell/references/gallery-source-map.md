# Official evidence and Gallery source map

Policy: [CLAUDE.md](../../../../CLAUDE.md#winui-官方优先与偏离确认). Read this when selecting a control or asserting an official recommendation. The source inventory below is evidence from 2026-11-12, not a dependency lock for the application.

## Evidence recorded for this refactor

- Momoka planning base: `da64fe3a87c758c51c0654a3ba73c17101fe56dd`. Other sessions had uncommitted settings/Mica work; reacquire source state before implementation.
- Gallery: [microsoft/WinUI-Gallery at 6de91feccadfbb0a796b71caedead5a2284f730e](https://github.com/microsoft/WinUI-Gallery/tree/6de91feccadfbb0a796b71caedead5a2284f730e), fetched 2026-11-12. Its `standalone.props` requests `2.6.6-experimental`. Do not copy that package selection into Momoka's stable-only dependency policy.
- Momoka's Shell project at the planning base uses the Windows App SDK split packages, including WinUI `2.3.9` and Runtime `[2.5.1]`. Read the csproj again at implementation; subpackage versions are not interchangeable with meta-package release numbers.
- Gallery is MIT licensed. Retain appropriate attribution/license when copying code; reading a pattern does not require vendoring the whole repository or shipping its sample data. When a sample matches the requested behavior, copying it is the default under CLAUDE's copy-first rule: keep the pinned path and commit in a comment, and re-read the sample when Gallery or our SDK version changes.

## Concrete source routes

Paths below are relative to that pinned Gallery checkout. Build permalinks by appending the path to `https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/`. Reconfirm symbols if selecting a newer revision.

| Topic | Files actually inspected | What the implementation establishes |
| --- | --- | --- |
| Shell and title bar | `WinUIGallery/MainWindow.xaml`; `Samples/TitleBar/` under `WinUIGallery` is the next detailed route | MainWindow uses MicaBackdrop, TitleBar, AutoSuggestBox and NavigationView. These choices do not establish suitability for Momoka's HWND/XAML-island host. |
| Navigation | `WinUIGallery/Samples/NavigationView/NavigationViewPage.xaml` and `.xaml.cs` | Native selection, back state and pane modes; a VisualState with AdaptiveTrigger changes the top navigation sample's PaneDisplayMode. Keep the associated navigation handlers when adapting it. |
| Command groups | `WinUIGallery/Samples/CommandBar/CommandBarPage.xaml` | Primary commands, SecondaryCommands and keyboard accelerators. Overflow is a platform capability to test with Momoka's command/permission model. |
| Simple material setup | `WinUIGallery/Samples/SystemBackdrops/SystemBackdropsPage.xaml.cs`, `WinUIGallery/SampleSupport/SamplePages/SampleBuiltInSystemBackdropsWindow.xaml.cs` | The entry opens different sample windows. `SetBackdrop` clears the old backdrop; TrySet methods check support; the sample falls from Mica to Acrylic to a solid default. That sequence is an example, not a required product fallback. |
| Controller setup | `WinUIGallery/SampleSupport/SamplePages/SampleSystemBackdropsWindow.xaml.cs` | `TrySetMicaBackdrop`/`TrySetAcrylicBackdrop`, target/configuration setup, activation/theme updates, Window_Closed disposal. Review subscription symmetry for repeated switching; sample code is not a production lifecycle guarantee. |
| Application Acrylic | `WinUIGallery/Samples/Acrylic/AcrylicPage.xaml` and `.xaml.cs` | The default example uses AcrylicInAppFillColorDefaultBrush. Custom examples change TintOpacity, TintColor, FallbackColor and TintLuminosityOpacity separately. Colors and 0.8 sample values demonstrate controls, not recommended Momoka defaults. |
| Local system material | `WinUIGallery/Samples/SystemBackdropElement/SystemBackdropElementPage.xaml` and `.xaml.cs` | A SystemBackdropElement is a sibling behind content; code swaps Mica/MicaAlt/Acrylic and corner radius. The page notes that app/OS theme changes drive it, not RequestedTheme on an arbitrary parent. |
| Repeater lifecycle and focus | `WinUIGallery/Samples/ItemsRepeater/ItemsRepeaterPage.xaml.cs` | FlowRepeater_ElementPrepared/ElementClearing track realized containers; focus handlers use TryGetElement and StartBringIntoView. Do not assume every new layout in this experimental branch exists in the current stable package. |
| Connected animations | `WinUIGallery/Samples/ConnectedAnimation/ConnectedAnimationPage.xaml.cs`, `WinUIGallery/SampleSupport/SamplePages/SamplePage1.xaml.cs` | PrepareToAnimate before navigation, GetAnimation/TryStart at the destination, suppress duplicate navigation transitions. Continue into SamplePage2 and collection pages when implementing a corresponding route. |
| Theme changes | `WinUIGallery/Helpers/ThemeHelper.cs` | RootTheme updates active window roots and persists the preference. Momoka's role palette and manual island host need their own source-aware adaptation. |
| Deployment | `WinUIGallery/WinUIGallery.csproj`, `standalone.props` | Both packaged and unpackaged configurations exist. Neither the sample's experimental dependencies nor self-contained choices are a reason to change Momoka's deployment scope. |

Typography, resource dictionaries, ListView, InfoBar, ContentDialog, accessibility announcements, lifecycle and notification implementations must be read in their own sample routes before actual changes. A route listed here is not a claim that every referenced helper was audited.

## How to decide from evidence

1. State the requested surface and behavior; find the live Momoka owner and applicable existing decision.
2. Read the relevant Microsoft Learn recommendation/API contract. Record page, retrieval date and Windows/SDK qualifiers.
3. Trace a Gallery sample from XAML to events, helpers, resources and cleanup. Use the runnable source rather than its displayed text snippet alone.
4. Check the referenced public API in the exact restored package metadata. XML documentation can be incomplete: absence from an XML comment index does not prove absence from `.winmd`.
5. For a framework edge case, inspect the corresponding framework implementation/version and the actual target hierarchy. A latest-main implementation is supporting evidence, not proof that the installed binary matches it.
6. Classify the result as technical limit, recommendation, supported option, project decision or unverified. Explain conflicting documents rather than selecting the most restrictive sentence.
7. Follow CLAUDE's confirmation rule before implementing a conflicting choice. Record the decision and the validation that will exercise it. Recheck on package/OS changes or broader use.

## Material documentation routes

- [Mica design](https://learn.microsoft.com/en-us/windows/apps/design/style/mica): base layering and older design recommendations.
- [Acrylic design](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic): transient surfaces, fixed vs overlay panes, seams, contrast and power cost.
- [System backdrop implementation](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/system-backdrop-controller): Window.SystemBackdrop, controllers, customization and SystemBackdropElement.
- [Current material API overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials) and [in-app Acrylic](https://learn.microsoft.com/en-us/windows/apps/develop/ui/in-app-acrylic): current API distinctions and caveats.

The older Mica design page says not to apply the backdrop to UI elements or apply it more than once; the newer implementation page explicitly supports SystemBackdropElement in bounded regions together with the window backdrop. Treat this as a versioned API/design distinction. A local element can be technically supported while repeated decorative layers remain a poor design. The [materials guide](materials.md) records the actionable distinction, including conflicting battery-saver descriptions.
