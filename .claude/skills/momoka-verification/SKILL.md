---
name: "momoka-verification"
description: "Verify Momoka changes using the current gates in CLAUDE.md. Interpret self-check results, distinguish fresh Release tests from stale output, check capability-dependent hiding and restoration, inspect screenshots and pointer evidence, and report playback coverage gaps. Use at delivery-batch close, when editing verification tools, or when asked to 验证 / 跑一遍闸门 / 发新版本."
---

# Momoka — verification evidence

**Policy lives in [CLAUDE.md](../../../CLAUDE.md); commands and report paths live in [docs/开发与验证.md](../../../docs/开发与验证.md).** This skill explains what measurements mean, not a second gate schedule. Architecture is `momoka-winui-shell`; playback is `momoka-playback`; shaders are `mpv-shader-quality`.

## Delivery batches

Verification runs per delivery batch, not per small edit.

- Intermediate edits need a compile of the affected projects/tests and the tests covering that change. At batch close, run gate 2, the affected runtime checks/probes and gate 3 once, against the final code. For `--probe-shell`, select the affected areas with `--probe-section`; a partial run is not a full-probe pass.
- Documentation and progress-record commits do not trigger a build, probe or publish.
- A user request to test or publish closes the current batch at the requested commit; later edits start a new batch.
- A result is evidence only for the code it ran against. If later code changes a probed path, rerun the affected check; do not recycle earlier logs, screenshots or publish hashes.
- Do not rerun a full probe or self-check after every tweak. An environment-attributed red stays red in the report; do not repeat it each round while the relevant code and environment are unchanged, and never turn it green with a lucky rerun or a baseline edit.
- Verification-tool changes are the exception: run the affected success and failure paths in the same round, because the tool itself is under test.

## Before accepting a result

### Shipped runtime approval

`tools/verify-publish.ps1` delegates complete mpv-ui/native/license approval to
`verify-mpv-payload.ps1`; the repo-fixed approved-payload manifest is not rebuilt from the tested
DLLs. Dependency approval is separate from publish deps: selected pinned restore/package content and
current project outputs constrain DLL bytes and relative paths. SDK download/runtimepack records lack
content digests, so their approved identity/hash/asset mapping is explicitly pinned in
`assets/mpv-runtime/approved-runtimepack.json`, never taken from mutable cache metadata. Official
SDK NuGet content hashing/local signed-archive integrity remains offline; no signer-chain network
check is implied. A complete synthetic non-PE fixture tests this same verifier without proving that
real libmpv was supplied or a real publish/native run passed. Keep the missing real-native gate red;
changing an approved package or binary is a separate reviewed change, not automatic snapshot refresh.


- Confirm tested files and output belong to the current working tree. `--no-build` needs a successful build of the current test code and Core; `-c Release` alone can select stale output. Publishing the Shell does not refresh the test assembly.
- Report observed counts, failures and skips separately. Missing fixtures, unavailable input and known old failures are not passes.
- The Core test runner assigns each executed case exactly one outcome: passed, failed, or skipped. A runtime `Skip` suppresses its pass; an exception after `Skip` still fails. Repeated skip reasons within a case count once. Pre-registration skips also appear in the total. Optional skips preserve exit code 0 but never satisfy a required check. `TestHarnessTests` exercises the real runner in isolated test child processes.
- A `--probe-shell --probe-section` run covers only the selected areas. Read `shell-probe-scope.txt` and report the skipped areas; exit code 0 is not a full-probe pass.
- An isolated worktree's publish is not delivery to the desktop shortcut. Check the actual target as required by CLAUDE.md.
- The local cursor, motion and composition probes select the integrated pipeline. They do not validate the standalone window, uosc, standalone switching or the external mpv backend.
- Self-check uses copied settings and an isolated run directory, but can still sign in and read the real server. It is not an offline probe. Never inspect or print real tokens to validate redaction; use placeholders. `SelfCheckRun.Create` saves the isolated screenshot directory and non-maximized preference into the run copy; `ShellServices` reapplies the same policy on a strict reload. The production settings and backup stay untouched. A stalled/failing copy is not a fresh account. `selfcheck-output-isolation-fixture` uses fake configuration and the real copy/read/output code without launching the app; its user-level mutex must not overlap another self-check or full Core run.
- Before testing a rule, confirm the chosen probe executes it. In particular, `BeginMotionProbe` has stopped the player ticker; rules inside `OnTick` cannot be inferred from an unmodified motion run that does not tick. If temporary instrumentation is needed, separate its evidence from tests of the final uninstrumented build, and remove it before delivery.
- Helpers under ignored `work/` are local experiments, not clean-checkout prerequisites. Verify their existence and read them before reuse. Mouse probes on the live desktop must verify that their target is this process's window (or the intended desktop) before injecting input; a window covering the target can otherwise receive the click.

## Comparing self-check reports

Run `tools/selfcheck-diff.ps1`. It reads this run's isolated log and compares check names and statuses rather than volatile detail numbers. Non-maximized settings apply to the copy only; never edit and restore the real settings file as a workaround. Concurrent self-checks are rejected; coordinate other desktop-driving verification as well.

`-Log` compares an existing report without launching the app. This proves only what is in that report, not that the current source was built or exercised. Report the source executable and run directory. Missing/incomplete reports and baseline failures are not green.

The [baseline](../../../docs/selfcheck-baseline.txt) can evolve through reviewed additions, renames, merges and retirements under CLAUDE.md. Keep evidence of the old/new coverage and update only the intended entries. Do not use `-UpdateBaseline` to accept a failing run or silently remove someone else's checks.

### Establishing a before/after comparison

The publish script clears old output; it does **not** automatically archive a pre-change build. If a comparison is needed, preserve a complete known pre-change build before replacing it, including native libraries and assets. Record its commit, uncommitted changes and artifact hashes. The previous publish may already contain earlier edits from the same task; different hashes prove different files, not the before/after relationship.

Keep both reports and run under comparable account, server-data and window conditions. Reproduction in the old build is evidence that a symptom existed earlier, not blanket proof that the new change cannot worsen it. A nondeterministic difference needs more evidence than one run per build. If the old executable predates isolated self-check support, do not pass an unknown switch and assume isolation; avoid running it against production data and report that comparison as unavailable until a safe method is established.

Historical diagnoses in PROGRESS.md describe their own runs. They do not waive a current red gate or justify a baseline update. See the development document's “逐行比报告：每次都会变的行” for raw-report interpretation.

**Answering “is this red mine?”** — run `tools/selfcheck-diff.ps1 -Exe <verified pre-change executable>` only after establishing the artifact's source and isolation support as above. An old build reproducing the symptom shows that it predates the change; it does not by itself identify the cause or exclude a regression in unexercised paths. Quote both reports, identify differing runtime conditions, and use separate evidence before attributing the failure to the environment. If no trustworthy pre-change artifact is available, report that limit rather than treating a directory timestamp as provenance. The current gate remains red.

### Retired cursor checks

The three ordinary self-checks retired on 2026-09-22 depended on an undisturbed physical mouse: `藏鼠标真的到了系统`, `鼠标真等两秒就藏`, and `藏匿期净位移走不够一百像素不醒`. Do not register or run them as required gates again. This does not retire `--probe-cursor`, `--hide-cursor`, or valid cursor assertions in other checks.

For the isolated cursor probe, preserve the failing assertion and input evidence before retrying. A diagnostic counter or an internal hidden flag is not proof of visible system-cursor behavior. “指针没挪动，只作参考” means missing coverage, not success.

## Moving the pointer, and proving it moved

Navigation and authorization follow CLAUDE.md. These are measurements from this machine, not permanent guarantees; confirm the current target received input.

| Attempt | Observed messages | Pointer movement |
| --- | --- | --- |
| `SendInput` relative move of (0,0) | No `WM_MOUSEMOVE` or `WM_SETCURSOR`, despite return value 1 | No |
| `SetCursorPos` to the existing position | One of each, including from a background process | No |
| `SendInput` with real displacement | Delivery varied between runs | Must measure |
| `SetCursorPos` with real displacement | Coordinate changed, including across monitors | Yes in the measured runs |

- Moving a coordinate does not prove the XAML island received input. A one-pixel `SendInput` out and back has reached the input queue in earlier runs; check pointer-event counts now. `GetCursorInfo` measures the cursor, not input delivery.
- Do not base screenshots on a parked pointer. The user can move the same mouse; hover-only states use the documented display switches.
- Restore pointer position and input-thread attachments when a probe exits. A denied tool is not permission to try another injection mechanism.

## Screenshots — what property checks cannot see

Correct properties do not prove correct pixels. A clipped poster or blank background can pass geometry checks; pair relevant assertions with actual screen evidence.

- `tools/shot.ps1` can launch, capture and close the main window; pass an explicit executable path rather than relying on its historical default. Its `-WindowTitle` selector requires an exact, nonempty native title. `SettingsWindow` currently sets `Title = string.Empty`, so `-WindowTitle 设置` cannot find it, and passing an empty title just selects the main-window path. For settings, use the available official Computer Use capability to identify the owning application and the settings window by its actual content before capturing it; do not substitute a guessed title or the main window. If no reliable window capture is available, report the screenshot gap. Prefer a non-primary display when available; single-monitor handling follows CLAUDE.md.
- An ignored resize helper may support chosen window dimensions, but must be inspected before reuse; do not assume a previous session's scratch file exists.
- `--dump-ui` writes the final page's PNG and visual tree in the isolated run's log directory. It does not composite Mica; use a real screen capture for colors and `shot.ps1` for per-page photography.
- Color changes cover every live theme; spacing/type changes cover the affected page and relevant window sizes. Read the live list from `UiThemes.cs`, not an old screenshot list.
- “有没有箭头” needs cursor evidence: `tools/cursor-watch.ps1` records `GetCursorInfo` and draws the visible cursor into a capture. It does not change foreground, z-order or pointer position. `shot.ps1` raises a window, which can change which input queue owns the cursor, so the tools are not interchangeable.
- Match the sampling time shown on each cursor capture to a successful `GetCursorInfo` sample in the console log; the filename uses the scheduled capture time, which can differ. The current [cursor-watch.ps1](../../../tools/cursor-watch.ps1) capture branch can label a failed sample “屏幕上没有光标” even though the log says “问不出 GetCursorInfo”; neither the image label nor exit code 0 proves a valid observation. If the matching sample is missing or failed, preserve the log and image and report missing coverage for that observation. To establish automatic hiding, require successful observations of the cursor first visible and then hidden; an already-hidden starting state does not prove that transition.
- Read-only UI Automation can inspect without moving the pointer. In templates, locate the owning item then its local handle; the source scanner skips templates and cannot prove runtime addressability. Interacting verbs remain subject to playback authorization.
- **Measuring a row of controls: do not derive the cells from “flush against the right edge”.** A measurement script that numbers cells that way is off by the whole shift the moment that row is moved sideways, and it silently folds a neighbour's background into the cell being measured. Locate cells from the layout actually reported (uosc's `size` × `buttons`) or, better, use connected components over the region — that also separates two stacked layers (an application glyph under a system caption glyph) that a fixed grid merges into one bounding box.

## Capability-dependent visibility

Verify [CLAUDE.md's visibility policy](../../../CLAUDE.md#功能可用性与显隐) through the real controls in an isolated fixture. Use [the Shell skill](../momoka-winui-shell/SKILL.md#capability-dependent-visibility) to locate the notification or rebuild path.

1. Check both initial states, then change the prerequisite through the actual toggle or selector: available → unavailable → restored, without reopening the page. Also check reentry, import and reset when they affect that prerequisite. For an asynchronous service, ensure late results cannot reopen an unavailable surface.
2. Inspect the rendered rows, menus and containers. Hidden controls must leave no dependent note, separator, empty card or layout gap, and keyboard focus must not reach them. Check that the enabling switch or mode selector remains usable and that unrelated content stays visible. `IsEnabled=false` and off-screen placement do not prove hiding.
3. Set nondefault dependent values before hiding, then restore availability and check that the values survived. Verify stored preferences separately from effective backend values; hiding does not prove a runtime option was reset or applied. Do not alter real user settings for this check.
4. Capture the affected visible, hidden and restored layouts at relevant window sizes. Report the assertions and screenshots actually obtained; an existing probe for a similar callback does not cover a new toggle automatically.

Reference transitions:

| Scenario | Expected UI |
| --- | --- |
| MoviePilot available → disabled or disconnected → connected again | Dependent search/subscription entries disappear and recover; configuration remains reachable. Inspect `ShellNavigationProbe.MoviePilotAvailability.cs` for current assertions. |
| WebP lossy → lossless → lossy | Quality and compression disappear together, then return with their previous values; the lossless switch remains reachable. |
| WebP → PNG → WebP | All WebP-only rows disappear, then return according to the preserved lossless preference; format selection remains reachable. |

These are acceptance scenarios, not a claim that every transition already has an automated assertion. A documentation-only update needs document checks under CLAUDE.md, not a desktop run solely to exercise these examples.

## Tool changes

A verification-tool edit needs tests of its affected success and failure paths as well as the applicable gates. `tools/check-scripts.ps1` checks PowerShell encoding/line endings/syntax, not runtime correctness. `smoke.ps1` checks process survival, not pages, login or playback. It returns nonzero for early exit (including child code 0), launch failure or unreadable new logs, and preserves a nonzero child exit code. It does not stop the launched process by default; `-StopAfterCheck` is an explicit opt-in to clean up only that retained process handle, not permission to kill an existing user instance. `tools/test-smoke.ps1` compiles a disposable local .NET 10 console helper with fake `LOCALAPPDATA`, exercises those success/failure paths, and cleans up only its own helper receipts. It never launches Momoka or reads production logs. Keep command help and messages consistent with the current policy.


Report completion is a distinct result boundary. `ShellSelfCheck.CompleteReport` publishes exit code 0 only after the final writer returns successfully and the check failure count is zero; the scheduled callback still closes through `FinishAsync`. `ProbeFinalization` executes report/scope attempts and disposal without allowing one IO exception to short-circuit the other report or application exit. The subtitle and Shell probes use `SubtitleProbeReport` / `ShellProbeReport` for their real Record/Case and final paths. A recovered final write must retain an earlier failure, not turn it green.

`Momoka.ProbeFinalizationTests` compile-links those actual implementations and extracts the real self-check final callback and probe entry/Record/Case delegates at build time. It fails if source boundaries are missing/ambiguous; only UI readings/endpoints are substitutes. Its writer/real-lock success and failure results verify the production finalization code, not a real WinUI run or HWND destruction. The official comparator may be run separately with `-Log` and an isolated fixture baseline against this run's generated reports; do not run its default saved-account application path or modify the repository baseline. Commands and count/coverage boundaries are in `docs/开发与验证.md` and the project's README.
