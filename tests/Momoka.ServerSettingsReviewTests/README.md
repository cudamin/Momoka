# Server and settings review regressions

2026-11-07 notification in-flight UI acceptance: `--probe-shell --probe-section metadata --screen 1` invokes actual Save/Test/Close controls with anonymous pending tasks, confirms Closing blocks both operations, and confirms failure retains the draft/error and restores cancel. It never sends a notification. Screen evidence is `notification-{save,test}-{pending,failed}-screen.png`; Close/Hide checks are not pointer-outside evidence. This executable links the entire new probe and actual dialog, and exercises normal completion plus separate Save/Test capture-failure cleanup in inert controls. Full-probe desktop execution remains the parent delivery's final step.

2026-11-07 fixture compatibility: the complete production IO-helper range still includes `RequirePermission`. The narrow shell boundary now supplies explicit permission values and records the checked scope. Metadata command coverage has six cases: revoking permission before saving sends no write, keeps the draft/error/retry state, and does not publish `changed`. Current Release/x64 compiles with zero warnings/errors and the full standalone executable passes; no production source or previous assertion was removed.

This fixture extracts the complete current `ServersViewModel.TestAsync` member at build time, refusing missing or ambiguous boundaries. It executes that method with inert UI load/notice endpoints, the actual Core session, gateway and isolated settings IO. Only anonymous placeholder secrets are available and all HTTP ends at an in-memory handler.

R28 covers unchanged and replaced server identities, with and without a preceding connection test, then executes the actual saved-token/password restore chain. It also covers missing IDs and profiles without accounts. The test must preserve a known credential binding and must send no credential in the anonymous test.

Run after restore and a fresh Release build:

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project tests/Momoka.ServerSettingsReviewTests/Momoka.ServerSettingsReviewTests.csproj -c Release --no-build --no-restore
```

This is not real WinUI, DPAPI, configured-server or playback evidence. No desktop or real account is used.

R29 executes the actual gateway for bare hosts, HTTP bare hosts, explicit 32400, explicit 80, HTTPS, unreachable-all and canceled probes. It retains the original protocol routes and tests only the candidate control flow.

The same executable also runs R1 typed Plex paging through actual single/aggregate consumption, R17 permission-cache relogin and delayed responses, R20 artwork reauthentication, and R12/R13/R16 display persistence/null loading/profile selection. R30/R31 links the complete notification dialog and extracts current catalog-error/context definitions. R24/R25 links the complete metadata dialog and extracts current EditAsync/GuardAsync/AskAsync/TellAsync, preserving actual session and HTTP saves; inert controls emulate button deferrals and closing, not WinUI pixels or input.

Jellyfin list evidence was read from the official v10.10.7 `ItemUpdateController.cs` (Tags assigned as an array) and `SqliteItemRepository.cs` (tags joined with `|`), from the fixed review source copies. Thus a comma is valid inside a tag; local list input now uses double-quoted entries and doubled quotes, and unchanged arrays stay untouched. Save failure retries build a new JSON clone, so failed edits cannot become false no-ops. No real server metadata was written.

The official `--probe-shell --probe-section metadata` section now has two separate review-editor cases in `ShellNavigationProbe.ReviewEditors.cs`. They open the real `MetadataDialog` and `NotificationEditDialog`, use anonymous delegates, invoke actual buttons through UIA, toggle actual event checkboxes through UIA and assign text properties. The cases verify failure drafts/retry/cancel/confirmed closing, event IDs and unavailable/missing restriction entries at 1100 and 760 DIP, saving `review-metadata-*-screen.png` and `review-notification-*-screen.png`. They do not send pointer input or contact any server. The console source contract checks registration and forbids acquiring server/session boundaries; desktop execution and screenshot inspection remain a separate step.
