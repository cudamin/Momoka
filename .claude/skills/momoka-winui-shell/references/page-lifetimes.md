# Page lifetimes and regression contracts

Read only for the affected code path. Policy is in [CLAUDE.md](../../../../CLAUDE.md); these are implementation notes and historical evidence, not extra approval rules. Recheck named owners in the current tree.

## Frame destination tags

`ShellPage.AdoptContentTag` derives the current top-level destination from the restored page's `Tag`; returning from a cached or recreated page must obey the same contract as initial navigation. `MusicLibraryPage` uses `LibraryRequest.Tag`, retaining empty tags for album/playlist drill-downs. Playlist items themselves open through the ordinary `LibraryPage` with `ParentType = Playlist`; that page keeps the server's list order. `Momoka.NavigationStateTests` extracts the actual page visits/releases and shell navigation methods with strict source boundaries. Its Frame and view-model substitutes do not prove native event order, caching, input, focus or scroll restoration.

## Resume paging and displayed next episodes

`LibraryViewModel` uses the existing `AggregateQueryCursor` for both aggregated queries and a single-source Resume page. Resume consumption belongs to server records, not `Cards.Count`: a watched episode can be replaced by a synthetic NextUp card that the Resume route does not contain. `_resumeRead` records which item identities actually came from that route; only removing one of those identities rolls back its source cursor and server total. Record the confirmed removal before awaiting NextUp, and cancel the in-flight page without changing the page's query identity.

Page reads consume a cursor `Copy()` and publish it only after the UI continuation confirms both query and load generations. Otherwise a network-complete page waiting in the dispatcher queue can consume an offset even when its displayed rows are later rejected. Removal refreshes also build their cursor privately. `allowPartialFailure: false` retains a single source's normal failure/retry behavior; multi-source reads keep the existing partial-result policy. `PlayableList.CollectAsync` accepts an optional raw start index for Resume lists containing synthetic cards and does not mutate the displayed page's cursor.

`library-resume-paging-fixture` explicitly loads this tree's current Release Shell DLL and runs the real VM/CardItem/ResumeSlot with a fake HTTP handler and a single-thread queue. It installs only in-memory source identity; no login, secret IO, window, actual network or playback. Commands and runtime coverage limits are in `docs/开发与验证.md`.

Favorite account selection reuses `RestoreSort`, `RestoreFilters`, `RestoreDisplay` and `RefreshBrowseChrome` after changing `_scope`, rather than sending the old product's conditions into another adapter. The flight/selection/rows of the native FnNas filter panel belong to that source: reset and cancel them on source changes. Successful/error/finally callbacks must check their captured filter lifetime; an old completion cannot clear a new source's loading flag or replace its options. `library-favorite-source-fixture` shares the actual VM fixture and adds only fake read-only favorite/tag endpoints. Existing saved library preferences and the stable card collection are not replaced by a same-page source change.

## Cached settings and directory actions

`SettingsPage`'s same-container `built` path calls `SettingsViewModel.SyncSaveState` before the existing device refresh. Initial reload uses the same method. It only reads shared `HasUnsavedChanges` and uses the generated property/command notifications; it must not save or rebuild rows just to refresh the banner. Half-typed input, categories and collection identities survive reentry.

The four About directory actions call one awaited `OpenDirectoryAsync` path with a captured `ISystemLauncher`. Directory creation belongs to the launcher once, and launch/IO/permission failures return through the existing page `Report` path. A screenshot directory is read at click time. `SettingsPageStateTests` can explicitly load this tree's current Release Shell assembly with a fake settings service and launcher; the standalone test switch is documented in `docs/开发与验证.md`. Actual VM/row-command tests are not actual SettingsPage navigation, focus, WinUI input or Explorer verification.

Restoring defaults and importing preferences both call `ReapplyAndReloadAsync`; its shared `ReapplyPlaybackPreferencesAsync` tail awaits subtitle appearance and the current screenshot option group before success feedback. A subtitle failure does not suppress the screenshot attempt, and callback failure/cancellation must not be overwritten with success. Individual screenshot rows call the same guarded push helper. `settings-screenshot-restore-fixture` executes this actual tail and the actual service writer with an in-memory active handle; it does not run theme/full-page reload or a file picker, and source contracts separately check the two production restore callers. Actual image encoding and window interaction remain runtime verification.

## Home fold completion ownership

A retained `HomeFoldMotion` object does not mean its storyboard is still running. Its active/complete state and removable Completed handler retain a single run; the page clears only the matching owner on completion and releases ownership before Stop on cancellation. Keep the active return-flight carrier until handback, but a completed climb must not block later carousel disablement from clearing the overlay host. The delayed second phase uses one visit version so main completion does not discard its valid late phase, while a replacement/cancellation rejects old completion and queued work. Existing timings and easing remain unchanged. `Momoka.HomeFoldCompletionTests` uses complete production motion source and verbatim page methods with controlled Storyboard/layout values; it does not establish rendered WinUI, pointer hit testing or real frame timing.

## Library directory handoff to an early home page

`ShellPage.EnterShellAsync` exposes navigation while library views are loading. When the valid directory result is committed, an already-open current HomePage still owns its earlier empty HomeRequest snapshot. Fill an empty frame normally or use the existing forced-home navigation to refresh only a still-current home; do not pull a user back from another page. The identity load and captured scope guards still precede this handoff, and ordinary directory retry remains unchanged. This intentionally performs a one-time normal home reload and may reset its scroll/selection state. `Momoka.HomeDirectoryTests` executes verbatim Shell navigation/load methods, the actual SourceLibraries and request/plan values with inert Frame/HTTP boundaries; it cannot prove downstream HomeViewModel reads or rendered rows.

## Settings administration and cached-window activity

Confirmed same-identity client replacements have a separate `PermissionsChanged` path; a scope's
`Connection` remains the original token/media snapshot while `IsAdministrator` and
`PermissionRevision` read the current online identity. Capabilities and administration results
check that revision after awaits. FnNas administration VMs revoke only permission-owned operations;
account/password saves do not subscribe to this administrator lifetime. Settings permission events
and R010 local-save events keep separate handlers and the same visit guards. A released/disposed base
must also stop a derived password Attach before it subscribes to persistence again. The integrated
[permission fixture](../../../../tests/Momoka.CurrentPermissionSnapshotTests/README.md) and
[local-save fixture](../../../../tests/Momoka.SettingsPersistenceTests/README.md) exercise anonymous
updates, actual VMs/generators and real nonsecret file saves, not production reauthentication,
credentials/DPAPI, rendered WinUI or server permissions.


`SettingsViewModel` has a dedicated administration `LoadGeneration` plus activity state. A directory refresh captures the capability owner, source keys and current identity; after Probe/plugin waits it checks those owners again and rereads permissions before publishing. An empty result from an old same-product account must not clear a new account’s plugin list. `Cancel` invalidates reads, `Activate` restores only the activity of a cached page, and `Dispose` prevents reactivation; none clears half-typed settings merely to reactivate monitoring.

`SettingsPage` also scopes posted source/MoviePilot callbacks to its active visit. Window Hide/X does not cause Frame navigation, so `ReleaseHosted` explicitly ends that visit and unsubscribes source, MoviePilot and motion notifications before releasing hosted content. Reopening subscribes afresh; queued work from before hiding cannot borrow the new visit. If the synchronous administration refresh already rebuilt a hosted page, the navigation tail avoids a duplicate creation/GET but still shows an unchanged or newly selected category. `Momoka.SettingsLifetimeTests` executes the real current Shell VM and verbatim page/window methods with inert UI/HTTP boundaries; its repository README documents commands and why this is not actual WinUI window/input verification.

## Confirmed detail subject deletion

The detail More menu captures removal/reload callbacks for its visit, source and subject, rather than calling whatever page is current after a dialog finishes. A confirmed subject or final-file deletion cancels the old detail/target/version owners, clears the executable target and picker data, and leaves a readable deleted notice; an ordinary failed GET must never be treated as deletion. Non-final FnNas version removal edits only the confirmed file ID in the captured DTO. The current detail remembers those IDs per visit, filters late same-subject reads and prunes all matching discovered picker entries even when a valid foreign-source copy is selected; foreign copies with the same file ID remain independent. Direct playback rejects a stale picker row. `DetailSubjectRemovalTests` and `Momoka.DetailDeleteCommandTests` separately exercise the real VM and actual command/confirmation/HTTP callback path with inert UI and anonymous sources, not real deletion or rendered WinUI.

## Hosted console navigation ownership

`DashboardPage` binds full-page Starting/Completed to NavigationId and the owning load generation. Navigate returns no ID; until Starting or a completion identifies the single issued pending request, later app requests are coalesced into the newest queued route, including an identical URL refresh. Keep previously observed IDs until Release so an old completion cannot consume a new pending slot; do not assume ID order or guess cancellation from elapsed time. Dispatch the queued request through the UI queue, checking generation/source again and retaining queue/dispatch failure feedback. A current successful completion explicitly restores Web and clears the notice layer. Already committed same-document inline/hash routes remain available, while full-document starts invalidate old sample/render/inline continuations. `Momoka.DashboardNavigation.Tests` executes verbatim production control flow with inert browser/dispatcher endpoints and validates its source hash, not actual WebView2 or credential/UI behavior.

`Momoka.AniBtSourceContractTests` links the current Core/session, actual scrape VM and dialog with memory settings IO and inert HTTP/UI endpoints. Aggregated SelectSource retains the old available scope; the caller continuation checks current source, connection and draft/confirmation version before further identify writes. R037/R039 already provide these guards. A fixed-source task without that callback may still continue deliberately; do not globally require IsCurrent on all scopes. Embedded outer entry anchors are source contracts, not actual modal/WinUI execution or deployment evidence. See the [R042 fixture](../../../../tests/Momoka.AniBtSourceContractTests/README.md).

## Verification report finalization

Self-check's final report must return from its writer before exit code 0 is published. The scheduled callback uses `ShellSelfCheck.FinishAsync` to retain failure and always reach the window close. Subtitle and Shell navigation probes keep their real Record/Case and report evidence in small Shell-owned report types; their common `ProbeFinalization` independently attempts scope/report IO, then disposal and application exit. Do not put report IO before cleanup with no nested finally, or let a caught write failure leave success feedback. The independent `Momoka.ProbeFinalizationTests` project exercises the actual source and callers with UI/IO endpoint substitutes; it cannot replace real window/WinRT validation. See the verification skill and development commands.

## Startup registration read failures

`StartupRegistration.IsEnabled` reports registry access/IO failures as exceptions, distinct from absent Run/Approval values. Its Apply contract still returns false for an unconfirmed write/readback. The actual settings row uses the existing two-state control and Error feedback: accurate readback reseeds without a second write; unreadable state retains the last confirmed reading (or the old initial false default) with an explicit inability-to-confirm error. This fallback is not evidence of disabled startup.

Reload resets a narrow startup-read failure cause. Since row construction is synchronous, `ReapplyAndReloadAsync` snapshots that cause before awaiting font/device work and passes it to the existing playback-preference tail so this reload's read failure is not replaced by restore/import success. Other push failure/cancellation and the normal successful/no-read-error path remain unchanged. `startup-state-contract-fixture` executes the full linked platform implementation and actual anonymous Shell VM/row; `startup-reapply-notice-fixture` executes verbatim shared methods with inert reload/theme boundaries then the actual VM tail. Neither performs real registry, reset/import/file picker, WinUI, native or server verification. See the [startup contract](../../../../tests/Momoka.Tests/StartupStateContractTests.md).

## Startup activation lifetime

`App` passes a queue delegate to the framework-free `src/Momoka.Shell/ActivationListener.cs`; only the UI boundary knows `DispatcherQueue`. The listener's `Dispose` requests stop once and never joins on the UI thread. Its worker owns the signal handles until its `finally` releases them, including when disposal precedes the first worker instruction. Stop has priority over activation, queued callbacks check the lifetime again, and a rejected queue or worker exception ends only the listener. A startup exception releases the handles immediately. `ActivationListenerTests` compile-links that exact source and exercises it with private random named events in disposable test child processes; it does not consume the real application activation signal or prove window activation visually.

## Artwork candidate failures

`DetailViewModel` sends its hero and both poster candidate loops through `Core/Emby/ArtworkCandidates.FirstAsync`, preserving the existing ordering and the caller's synchronization context for WinUI decoding. Only explicit bad-image/format data, a completed candidate's unreachable/5xx failure, or missing-image result can downgrade to the next candidate. Cancellation, source/identity rejection, permissions, low memory and arbitrary program/stream errors still propagate. The WIC code list is a narrow allow-list cross-checked against Microsoft's documented constants; missing codec allows another candidate, not cache eviction. This fixes candidate selection, not corrupt-cache invalidation. Core tests do not establish the actual WinRT exception shape or rendered fallback image.
