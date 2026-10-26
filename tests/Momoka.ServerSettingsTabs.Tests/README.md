# Embedded server settings tabs — offline regression fixture

This project references the current Core project and source-links only `ServerSettingsTests`, its test
harness and fake HTTP handler. It does **not** run the full Core suite or acquire `SelfCheckRun`'s
same-user mutex. It never starts Momoka, signs in, reads account settings, plays media or writes server
configuration. It does not wire a switch into the main `Momoka.Tests/Program.cs`.

The exporter calls the actual built `EmbyWebConsole` skin/drawer methods. The browser fixture runs those
production scripts at document creation in their production order. It retains the fixture's original
tab nodes, event handlers and scrollers. Output includes source/Core SHA256 hashes and anonymous
browser-rendered screenshots; these are **not** actual WinUI/WebView2 or logged-in server screenshots.

## Run in one isolated worktree

`Run-Fixture.ps1` restores and builds the dedicated project in **Release**, runs the 14 server-settings
contracts, exports the built production scripts, then runs the DOM regression cases. Its output must
be inside that same worktree. Supply an installed Node executable, Playwright package directory and a
Chromium-family browser; it does not install/download dependencies or use the user's browser profile.

```powershell
& .\tests\Momoka.ServerSettingsTabs.Tests\Run-Fixture.ps1 `
  -Dotnet "$HOME\.dotnet\dotnet.exe" `
  -Node '<absolute Node executable>' `
  -NodeModules '<absolute node_modules containing playwright>' `
  -Browser '<absolute msedge.exe or chrome.exe>'
```

The optional `-EmbyManager '<absolute anonymous emby-maintabs.js>'` runs the existing anonymous Emby
upstream snapshot verbatim (AMD dependency boundaries are inert). It creates the real upstream tab
markup, updates `withHeaderTabs`/header classes, owns panel switching and the existing `hide` permission
rules. **Only use the anonymous source snapshot, never account exports or captured authentication data.**
Without it, the deterministic fixture creates structurally equivalent markup; the report records
whether an upstream snapshot was used and its hash. No upstream source is copied into this project.

The DOM page intentionally stubs the unavailable server tab web component/MUI component boundary.
It proves the production embedding scripts do not remove/clone tabs or cancel their click/keyboard
handlers, not that every real upstream component or version was exercised.

## Coverage

- Emby `headerMiddle` inside or beside `headerTop`, dynamically inserted by the tabs manager.
- Jellyfin React/MUI AppBar and legacy `headerTabs`, including absolute-positioned old route views.
- All ancestor branches that contain page tablists, not a route/name whitelist; duplicate global
  title, navigation, drawer/backdrop and footer stay hidden.
- SPA insertion after startup retry timers, removal, page reentry and repeated script installation.
- Upstream per-tab `.hide`, `hidden`, inline/style-sheet hiding, parent permission changes and restore;
  zero available tabs collapse their header and offset. React's hidden legacy mirror remains hidden.
- 320px horizontal scrolling/keyboard activation, 1000px desktop layout and 1920px legacy header margin.
- Content below the measured tab bar; no full-document horizontal overflow; local content AppBar and
  dialog backdrop are not mistaken for the global navigation.

The page has a restrictive CSP, blocks HTTP(S)/WebSocket routes and uses only anonymous inline data.
Browser runtime profiles and evidence are isolated under the selected worktree output directory.
Page-request and page-error counts must both be zero.

Actual embedded-server GUI, real upstream tab web components, server permissions, authentication,
WinUI/WebView2 rendering and the final full Core/mutex suite remain the integrating agent's serial
verification. This fixture is not a publish, a full application probe or a full-gate pass.
