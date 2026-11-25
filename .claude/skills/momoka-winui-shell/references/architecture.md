# Architecture and binding ownership

Read only for the affected code path. Policy is in [CLAUDE.md](../../../../CLAUDE.md); these are implementation notes and historical evidence, not extra approval rules. Recheck named owners in the current tree.

## Project boundaries

- **`src/Momoka.Core`** — no UI references; currently no packages or project references. Package-free is a lightweight default, not a prerequisite for unit testing.
- **`src/Momoka.Shell`** — the WinUI shell. WindowsAppSDK is split into sub-packages rather than the meta-package to control payload size. The project file is authoritative for package selection and versions; review new runtime payloads during upgrades. `WindowsPackageType=None`, x64, `DISABLE_XAML_GENERATED_MAIN` with a hand-written `Program.cs`.
- **`tests/Momoka.Tests`** — currently package-free and referencing Core, with `OutputType=Exe` and a hand-written harness. Selected framework-free Shell sources are compile-linked into this project; `ActivationListener.cs` is tested as actual production code, not a copied Core model. `dotnet run` is the runner. Register new suites in `Program.cs`; compilation and run order are in CLAUDE.md.

## The shell's terrain

UI views are in `src/Momoka.Shell/Views/`, view models in `ViewModels/`, and registrations in `Composition/ShellServices.cs`. Entry points include the shell, home/library/detail/settings pages, server dashboard and player, with reusable controls such as `PosterCard`, `HomeBanner` and `EpisodeRow`. Enumerate the current files when a change spans the UI; an illustrative list here is not a completeness check.

`PlayerPage` is a sibling `UserControl` of the navigation shell rather than a page inside its `Frame`. It hosts the integrated composition pipeline; standalone playback uses mpv's own top-level window and uosc. See `momoka-playback`.

## Where capability lives

**`ShellServices.Build(AppPaths)` is the registration site.** It enables `ValidateOnBuild` and `ValidateScopes` and must run on the UI thread because `IUiDispatcher` captures that thread. Check the existing registration lifetimes before adding a dependency; the current use of singletons is not a reason to give every future object application lifetime.

Most non-UI capability sits in `Core/Emby`, `Core/Playback`, `Core/Mpv` and `Core/Configuration`; `Core/Services` contains selected services rather than all capability. Use dependencies and lifetime to decide whether a type needs registration. Moving a file into a folder named Services changes neither.

**Use the layering policy in CLAUDE.md.** Important business decisions, reusable calculations and regression-prone algorithms belong in testable Core code; local presentation predicates may remain near the UI. The current test-project reference is not a reason to extract every conditional. Existing reusable rules include:

`HomeLayout` (which home rows, in what order, and only writing settings back when something really changed), `ScreenPlacement` (which monitor, restoring geometry), `ItemArtwork.SamePictures`, `CardSize`, `ImageCachePolicy` and `LruCache` (eviction), `LoadGeneration` (which load is still the current one), `HomeRefresh` (whether a light refresh may keep the carousel), `LibraryWatch` (Home compares first-24 ID/order stamps after a library notification; the generic periodic media poll was removed, see [媒体库实时更新](../../../../docs/媒体库实时更新.md)), `ItemMenu.For` (which menu rows and their wording), `CardStrip`, `WrapLayout`, `HomeCarousel`, `ItemMetadataEdit`, `EpisodeNavigation`, `TrackSelection` / `TrackLanguagePriority`, `StartupArgs`.

## Source-linked page contracts

`tests/Momoka.PageContractTests` compile-links selected actual page code-behind and view models with tiny boundary substitutes for absent XAML/WinUI controls and external owners. It runs separately from `Momoka.Tests` and is included in the solution's build. The control-flow assertions use production `Attach`/navigation/release and diagnostic log/filter/copy methods, not copies of them. The diagnostic fixture uses the same pinned MVVM generator as Shell. Its Core cases overlap the main suite and must not be counted twice. These tests cannot establish real WinUI layout, input, scroll preservation or `Frame` history interaction; those remain runtime UI coverage. Commands and limitations are in its README and `docs/开发与验证.md`.

Diagnostics treats `RingBufferLogSink.Written` as a wake-up, not ordered delivery. `SnapshotAfter` returns retained writes and a sequence watermark under the same lock; a refresh or out-of-order queued notification therefore does not replay already-displayed entries. Repeated identical log content remains distinct writes. Normal updates append the unread tail and trim rows/categories to capacity rather than rebuilding every row. Reentry resets the watermark, and both old-sink notifications and paused/detached updates retain their lifetime guards.

Card image resizing uses a separate `_resizeWanted` flag from `_wanted`: a container may keep waiting for an old request while temporarily off-tree, but must not start a new request just because the window width changes. Explicit `EnsurePosterAsync` reopens resize demand; the current holder's release and page abandonment close it. Resize cancels the old generation and restarts a visible first image even when `Poster` is still null. Keep the weak holder for temporary off-tree releases because the same control's Loaded path only ensures the image, not claims it again. `CardImageResizeTests` compile-links actual card/cache sources with fake value/decode boundaries in the page contract project; it is not real Loaded, WinRT or scroll/render coverage.

## Pages, view models, `Attach`

Pages created by this project's `Frame.Navigate` path use parameterless constructors. **Pages construct their own view models; dependencies arrive afterwards through `Attach(...)`.** Keep guards for calls made before those dependencies arrive, and inspect the file's `Attach`, cancellation and load methods before adding work.

**Service availability is only part of a session-bound page's lifetime check.** `DetailViewModel.Attached` requires `_active`, non-null settings and a current captured `_scope`; `HomeViewModel.Live` and `LibraryViewModel.LoadAsync` likewise check activity and session identity. Their `Cancel()` methods leave service fields populated, so a null check alone does not reject work after leaving the page or changing accounts. Preserve the existing entry guards and the operation's cancellation, request-generation and item/identity checks when applying asynchronous results. Simpler service guards in `SettingsViewModel` and `ServersViewModel` have different responsibilities; do not use them to replace the stronger checks in browsing workflows.

**Playback view models live in the container**: `PlayerViewModel` and `MusicPlaybackViewModel` retain playback state across navigation. `ShellPage.AttachWindow` resolves the video VM; `AttachMusic` connects the shared music VM and player bar. All music backends use `MusicLibraryPage` and the same bar; backend differences belong to adapters and capability visibility, per CLAUDE.md. `HomePage` marks its single resolution site with a comment saying it is the one place the page resolves anything — keep that habit, because a page reaching into the container from five places is how this rule rots.

Known deviation, not a template: `FilterPanelViewModel` is `new`ed with no `Attach` at all.

## Interfaces

**Don't invent an interface just because a class goes into DI** — with no second implementation and nothing to substitute or isolate, register the concrete class, which is what most registrations here do.

`ISecretProtector` separates platform protection from Core. `ISettingsService`, `IServerCapabilities`, `IUiDispatcher`, `IClipboard`, `ISystemLauncher` and `IShellActions` narrow access to capabilities: a page that needs settings should not acquire unrelated session or player operations. Keep that boundary even when only one production implementation exists; inspect current registrations and tests instead of maintaining counts here.

For a single operation, a method delegate may express the boundary more clearly than a new interface. Settings receives `PlaybackService.ApplySubtitleStyleAsync` without receiving play/stop/seek capabilities; the backend factory is also passed as a delegate. Use an interface when several related operations or a substitutable contract warrant it.

## Code-behind

The rule — pure UI behaviour may stay there, business capability may not — is in `CLAUDE.md`. **The test is "did business logic leak in", not "is there a binding".** Live examples: `ShellPage.xaml.cs` (the `_trail` / `_forward` breadcrumb stacks, library items built from what the server returned), `PlayerPage.Input.cs` (the three window buttons, which exist only on the player's title bar), `PosterCard.xaml.cs` (the hover layer). The counter-example: page-level busy/notice is *not* a code-behind toggle — it is a derived property on `PageViewModel` with `[NotifyPropertyChangedFor]`.

`Views/ItemCommands.cs` and `ItemCommands.Server.cs` are existing code-behind debt, not a template. They drive card commands through `EmbyClient`; some operations use `session.ExecuteAsync`, while downloads capture an `EmbySessionScope` so later work remains bound to the original identity. Inspect the relevant path rather than replacing every scope with the current session. Metadata field rules already live in `Core/Emby/ItemMetadataEdit.cs` with tests. Refactoring follows CLAUDE.md: extract a service when it improves responsibility, lifetime or testability, preserving confirmation and identity boundaries; being a large static class is not sufficient reason on its own.

The metadata date box represents the timestamp's own calendar day. Its unchanged-date guard compares that same day, not the UTC day, preserving the original time/offset/precision even across UTC midnight. A changed date retains the existing parsing and UTC serialization policy. `metadata-date-fixture` exercises actual Core rules and anonymous client JSON IO with an in-memory HTTP boundary; its conditional save driver is not the real WinUI dialog or proof that a deployed server returns offset timestamps. See the [date regression](../../../../tests/Momoka.Tests/README.MetadataDate.md).

Card watched/favorite commands share `ItemUserStateOrder` across live cards with the same online identity owner and item ID. The identity registry and card lists retain weak references; a new login cannot inherit an earlier identity's operations. Confirmed state is copied to each DTO and signals every card. A request captures the source confirmation revision before awaiting; only DTOs from an earlier read inherit a later confirmed snapshot when joining a group. A later fresh read remains authoritative for its new card, so refresh and server playback progress are not frozen by a local cache. Watched owns Played plus position/percentage/count/date fields; favorite owns IsFavorite. Same-field stale responses cannot undo a newer success, and a failed newer edit must not suppress an earlier success. Always complete pending bookkeeping in finally and retain source/item guards. Home resolves a command's item to its current resume card; Home and Library next-up continuations check the shared watched revision after awaiting, including an unwatched/watched round trip from another shelf. The actual-command tests and actual Home/Library VM fixture cover these paths with anonymous transport; they do not establish real UI input, remote processing order, cross-device push synchronization or the detail page's separate primary-action flow.

For native FnNas no-body state writes, card commands acknowledge their own field before a separate supplementary read. `RunUserState` has an awaited apply tail while unrelated one-step `Run` callers keep their old action contract. Read-back uses `RefreshConfirmed`, so an older refresh cannot undo a later same-field confirmation or another overlapping field group. Detail watched applies the confirmed target before reloading server-derived data; a refresh error is labelled saved-but-refresh-failed, with permissions/protocol errors still errors. Detail favorite remains a one-bit update without unnecessary read-back. Tests of source-linked commands and a reflected real detail VM do not prove actual WinUI feedback or server processing order.

Page vertical scrolling follows DetailPage: keep the native ScrollBar template and default margin, extend the primary viewport to the page right edge, and put content clearance inside it. ScrollView/ScrollViewer Padding becomes presenter Margin in WinUI 2.3.9 generic.xaml; using it as a background mask clips the whole content surface. Do not reintroduce the retired home-only narrow template or banner-height scrollbar offset. Horizontal shelves, table scrolling and popup scroll owners stay independent.
