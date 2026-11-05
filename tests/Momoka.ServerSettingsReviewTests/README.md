# Server and settings review regressions

This fixture extracts the complete current `ServersViewModel.TestAsync` member at build time, refusing missing or ambiguous boundaries. It executes that method with inert UI load/notice endpoints, the actual Core session, gateway and isolated settings IO. Only anonymous placeholder secrets are available and all HTTP ends at an in-memory handler.

R28 covers unchanged and replaced server identities, with and without a preceding connection test, then executes the actual saved-token/password restore chain. It also covers missing IDs and profiles without accounts. The test must preserve a known credential binding and must send no credential in the anonymous test.

Run after restore and a fresh Release build:

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project tests/Momoka.ServerSettingsReviewTests/Momoka.ServerSettingsReviewTests.csproj -c Release --no-build --no-restore
```

This is not real WinUI, DPAPI, configured-server or playback evidence. No desktop or real account is used.

R29 executes the actual gateway for bare hosts, HTTP bare hosts, explicit 32400, explicit 80, HTTPS, unreachable-all and canceled probes. It retains the original protocol routes and tests only the candidate control flow.
