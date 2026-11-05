# Server and settings review regressions

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
