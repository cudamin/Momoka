---
name: "momoka-winui-shell"
description: "Plan, implement or review Momoka C# and WinUI XAML: native controls, Gallery source comparisons, Mica/Acrylic placement and fallback, themes, DPI, adaptive layout, bindings, virtualization, input, animation and window lifetimes. Use for UI refactoring and feature/settings design; platform-deviation confirmation is governed by CLAUDE.md. Backend protocol details belong to the server skills."
---

# Momoka — WinUI shell

Read [CLAUDE.md](../../../CLAUDE.md), especially [official guidance and deviation confirmation](../../../CLAUDE.md#winui-官方优先与偏离确认), before proposing a UI change. Versions and runtime behavior come from current source, not old comments or this skill. Use the current worktree; enumerate affected views instead of assuming an old inventory is complete.

## Start with the affected experience

1. Identify the surface, lifetime, users, input modes and current behavior. Trace XAML, code-behind, VM, service and tests only as far as the task needs. Preserve server-specific video-library capabilities and the shared music UI.
2. Consult the [Microsoft Learn Windows app design guidelines](https://learn.microsoft.com/en-us/windows/apps/design/) for the affected experience, as requested in CLAUDE, and read the appropriate references below. Apply the available `winui-app` skill for generic guidance; project references remain usable without a particular global installation.
3. Inspect the matching Gallery XAML, code-behind and called resources/helpers at a fixed commit. If a sample already implements the interaction, copy that implementation rather than rewriting it: keep its resource keys, attached properties, event wiring, focus and automation semantics, and change only namespaces, project text styles, bindings and lifetime/cancellation ownership. Record the Gallery path and pinned commit in a comment. Compare its SDK version with Momoka, and compose or customize only when no sample fits or the sample needs packages Momoka does not ship. Use [source evidence](references/gallery-source-map.md) to distinguish design recommendations, supported options and technical limits.
4. When a conflict triggers the CLAUDE confirmation rule, finish the read-only comparison and present the actual tradeoff before implementation. A general request to modernize does not approve a new deviation or the reversal of an existing product choice.
5. Make the smallest coherent change with its behavioral tests. Preserve Attach/identity/cancellation ownership, stable collections, input semantics and settings migrations. Avoid mechanical replacement just to increase the count of native APIs.
6. Select verification from CLAUDE and [momoka-verification](../momoka-verification/SKILL.md). Source presence and unit tests do not establish material pixels, focus, native windowing or rendered virtualization.

## Read by task

| Task | Required reference |
| --- | --- |
| Mica, Acrylic, transparency, materials on any surface, fallback or backdrop settings | [Detailed materials guide](references/materials.md), then the affected entries of the [WinUI validation matrix](../momoka-verification/references/winui-validation.md) |
| Choose controls, adapt window sizes, DPI/text scaling, keyboard/Narrator, motion, deployment | [WinUI feature guidance](references/winui-features.md) |
| Find official examples, compare versions or explain a recommendation | [Gallery source map](references/gallery-source-map.md) |
| DI, view models, bindings, source-linked contracts or moving business rules | [Architecture and binding ownership](references/architecture.md) |
| Async paging, session changes, settings reentry, startup or report completion | [Page lifetimes and regression contracts](references/page-lifetimes.md) |
| Capability visibility, dialog dismissal, images, namescopes, recycling or popup geometry | [Measured XAML pitfalls](references/xaml-pitfalls.md) |
| Plan further UI work | Start from the current request and source, then use the [Gallery source map](references/gallery-source-map.md) and [validation matrix](../momoka-verification/references/winui-validation.md). Historical phase reports are evidence, not an active task list. |

## Current source owners

- `src/Momoka.Core`: framework-independent domain rules. Read current project references before making package claims.
- `src/Momoka.Shell/Composition/ShellServices.cs`: dependency registration and lifetimes.
- `Views/`, `ViewModels/`: presentation and page/identity ownership. Pure UI behavior may remain in code-behind under CLAUDE.
- `Windowing/HostWindow.cs`, `HostWindow.Backdrop.cs`, `IslandMicaBackdrop.cs`: Win32 HWND plus `DesktopWindowXamlSource`, caller intent, target ownership and HWND activation. Integrated video uses `CompositionVideoTarget`. `Views/SettingsWindow.cs` and `SettingsWindow.Appearance.cs` own a framework Window, its canvas/caption and system observations.
- `Theme/Palette.xaml`, `Theme/Styles.xaml`, `Theme/ThemeHost.cs`, Core `Theming/UiThemes.cs`: first-frame resources, shared styles, live updates and available palettes.
- `src/Momoka.Shell/ActivationListener.cs`: framework-free activation listener. Do not use the obsolete `Shell/ActivationListener.cs` path.
- `tests/Momoka.Tests`, `tests/Momoka.PageContractTests` and focused test projects have different boundaries; read their actual runners and the [development commands](../../../docs/开发与验证.md).

## Capability-dependent visibility

Read the [implementation and failure cases](references/xaml-pitfalls.md#capability-dependent-visibility), then the [runtime validation](../momoka-verification/SKILL.md#capability-dependent-visibility). Product policy and exceptions are in CLAUDE; preserve command-side guards as well as UI hiding.

## Dialogs close on outside click

This is an existing Momoka requirement, not a claim about default ContentDialog behavior. Read [overlay ownership, cancellation and lifecycle details](references/xaml-pitfalls.md#dialogs-close-on-outside-click). Changing this behavior requires the policy decision first; retaining it does not authorize additional custom modal behavior.

## Themes and material ownership

Read `DetailMenuBackdrop` with `DetailMenuSurface`: successful controller attachment, system Fallback and a presenter's XAML solid fallback are separate results. Copied canvas/caption colors need UI-dispatched system observations. Read the material guide before changing these paths; diagnostics are not pixel evidence.

Read [materials](references/materials.md) before touching a brush or backdrop. `Palette.xaml` currently uses `Dark`, `Light`, `HighContrast`; the first two are repainted, while high contrast uses system roles. This is a project contract, not a platform prohibition on a `Default` dictionary. Five fixed dark palettes and system mode are available (system mode requested 2026-11-15); system mode preserves its preference id and resolves light/dark via UISettings on the UI queue. Keep theme keys, first-frame values and runtime updates aligned.

Use [momoka-playback](../momoka-playback/SKILL.md) for the integrated/standalone and backend boundaries. A XAML material must not be assumed to sample native mpv video or a WebView surface. Do not restore the retired timeline capture/blur chain as a side effect of UI modernization.
