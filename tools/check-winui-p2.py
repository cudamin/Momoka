"""Offline P2 report/source/evidence checks. Never builds, launches or changes settings."""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import re
from urllib.parse import unquote

REPORT = "docs/WinUI-P2材质与主题.md"
OWNERS = ("src/Momoka.Shell/Windowing/HostWindow.Backdrop.cs", "src/Momoka.Shell/Windowing/IslandMicaBackdrop.cs",
          "src/Momoka.Shell/Views/DetailMenuBackdrop.cs", "src/Momoka.Shell/Views/DetailMenuSurface.cs",
          "src/Momoka.Shell/Views/SettingsWindow.Appearance.cs")
TERMS = ("P2a", "P2b", "P2c", "CS0029", "Microsoft.UI.Composition", "Windows.UI.Composition",
         "HighContrast", "AdvancedEffectsEnabledChanged", "D01", "D07", "P1", "GPU", "Narrator", "DPI")


def need(ok: bool, message: str) -> None:
    if not ok:
        raise ValueError(message)


def anchors(text: str) -> set[str]:
    found, counts = set(), {}
    for title in re.findall(r"^#{1,6}\s+(.+?)\s*#*\s*$", text, re.M):
        slug = re.sub(r"\s+", "-", re.sub(r"[^\w\s-]", "", title.casefold()))
        found.add(slug + (f"-{counts[slug]}" if slug in counts else ""))
        counts[slug] = counts.get(slug, 0) + 1
    return found | set(re.findall(r'<a\s+(?:id|name)=["\']([^"\']+)', text))


def report_text(text: str) -> None:
    need("\r" not in text and text.endswith("\n"), "Report must have LF and final newline")
    need(not re.search(r"\bTBD\b|待根代理|根代理收尾填入", text), "Unresolved report placeholders")
    need(all(term in text for term in TERMS), "Missing material/system/version/remaining evidence boundary")
    need("实色" in text and "回退" in text and "恢复" in text, "Missing fallback/restoration result")
    need("候选" in text and ("未覆盖" in text or "没有测" in text), "Missing remaining scope and coverage limits")
    need("模拟" in text and "native" in text and ("旧红" in text or "红项" in text), "Mock/native or historical failures were omitted")
    need(not re.search(r"(?m)^\s*(?:\*\*)?(?:全部\s*P2|整个\s*P2)\s*(?:已)?(?:全部)?完成", text), "Do not claim the entire P2 completed")


def local_links(root: Path, text: str) -> int:
    count = 0
    for target in re.findall(r"\]\((<[^>]+>|[^)\s]+)(?:\s+\"[^\"]*\")?\)", text):
        target = unquote(target.strip("<>"))
        if re.match(r"[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("//"):
            continue
        path, _, anchor = target.partition("#")
        linked = (root / REPORT).parent.joinpath(path).resolve() if path else root / REPORT
        need(linked.is_file(), "Missing local link: " + target)
        if anchor:
            need(anchor in anchors(linked.read_text(encoding="utf-8-sig")), "Missing anchor: " + target)
        count += 1
    return count


def table_shape(text: str) -> None:
    blocks = re.findall(r"(?m)(?:^\|[^\n]*\n){2,}", text)
    need(blocks, "Missing evidence tables")
    for block in blocks:
        lines = block.splitlines()
        width = len(lines[0].strip().split("|")) - 2
        need(width >= 2 and all(len(line.strip().split("|")) - 2 == width for line in lines), "Malformed table columns")
        for line in lines[2:]:
            need(all(cell.strip() for cell in line.strip().split("|")[1:-1]), "Empty evidence table field")


def source_contract(root: Path) -> None:
    sources = {name: (root / name).read_text(encoding="utf-8") for name in OWNERS}
    need("BackdropConnected" in sources[OWNERS[0]] and "_source?.SystemBackdrop is null" in sources[OWNERS[0]], "Lost actual-target retry boundary")
    need("AddSystemBackdropTarget" in sources[OWNERS[1]] and "new MicaController()" in sources[OWNERS[1]], "Lost controller ownership/callback path")
    menu = sources[OWNERS[2]]
    need("DesktopAcrylicKind.Thin" in menu and "0.65f" in menu and "0.45f" in menu, "This batch changed the retained menu parameters")
    need("SurfaceChanged" in menu and "NeedsSolidSurface" in sources[OWNERS[3]], "Lost presenter/controller fallback separation")
    state = sources[OWNERS[4]]
    need("ApiInformation.IsEventPresent" in state and "StopSystemAppearanceWatch" in state and "TryEnqueue" in state, "Lost system observer lifecycle/dispatch")
    window = (root / "src/Momoka.Shell/Views/SettingsWindow.cs").read_text(encoding="utf-8")
    need("CanvasAlpha = 0x99" in window and "MicaKind.Base" in window and "titleBar.BackgroundColor = Colors.Transparent" in window, "This batch changed retained window parameters")


def evidence(data: dict) -> None:
    need(re.fullmatch(r"[0-9a-fA-F]{40}", data["sourceCommit"]) is not None and re.fullmatch(r"[0-9a-fA-F]{64}", data["sourceSha256"]) is not None, "Invalid source receipt")
    need(data["builds"] and all(all(type(row[key]) is int and row[key] >= 0 for key in ("warnings", "errors")) and row.get("id") and row.get("sourceSha256") for row in data["builds"]), "Missing build result fields")
    need(data["tests"] and data["runtime"], "Tests must remain separate from native UI receipts")
    attempts = data.get("runtimeAttempts", [])
    for row in [*data["tests"], *data["runtime"], *attempts]:
        need(all(type(row[key]) is int and row[key] >= 0 for key in ("checks", "passed", "failed", "skipped")) and row["checks"] > 0, "Invalid evidence counts")
        need(row["checks"] == row["passed"] + row["failed"] + row["skipped"], "Evidence counts do not sum")
    need(all(row["evidenceKind"] in ("source-linked", "core") and row.get("native") is False for row in data["tests"]), "Simulated tests cannot be native evidence")
    need(all(row["evidenceKind"] == "native-ui" and type(row["pid"]) is int and row["pid"] > 0 and row.get("runDirectory") and re.fullmatch(r"[0-9a-fA-F]{64}", row["binarySha256"]) for row in data["runtime"]), "Missing actual native UI receipt")
    need(all(row["evidenceKind"] == "native-ui" and type(row["pid"]) is int and row["pid"] > 0 and type(row.get("exitCode")) is int and row.get("log") for row in attempts), "Missing attempted native run receipt")
    recovery = data["systemRecovery"]
    need(recovery["authorized"] is True and recovery["restored"] is True and recovery["before"] == recovery["after"] and recovery.get("evidence"), "System restoration must be observed, not just attempted")
    need(data["remaining"] and all(isinstance(item, str) and item.strip() for item in data["remaining"]), "Missing remaining P2 scope")
    if data.get("delivered") is True:
        need(all(row["errors"] == row["warnings"] == 0 for row in data["builds"]) and all(row["failed"] == row["skipped"] == 0 for row in [*data["tests"], *data["runtime"], *attempts]) and all(row["exitCode"] == 0 for row in attempts), "Required red/skipped checks cannot be delivered")


def self_test() -> int:
    text = "P2a P2b P2c CS0029 Microsoft.UI.Composition Windows.UI.Composition HighContrast AdvancedEffectsEnabledChanged D01 D07 P1 GPU Narrator DPI 实色回退 恢复 模拟 native 旧红 候选 未覆盖\n"
    report_text(text)
    base = dict(sourceCommit="a" * 40, sourceSha256="b" * 64, builds=[dict(id="shell", warnings=0, errors=0, sourceSha256="b" * 64)],
                tests=[dict(id="mock", checks=55, passed=55, failed=0, skipped=0, evidenceKind="source-linked", native=False)],
                runtime=[dict(id="real", checks=1, passed=1, failed=0, skipped=0, evidenceKind="native-ui", pid=1, runDirectory="anonymous", binarySha256="c" * 64)],
                systemRecovery=dict(authorized=True, restored=True, before=dict(highContrast=False, effects=True), after=dict(highContrast=False, effects=True), evidence="state receipt"), remaining=["not measured"], delivered=True)
    evidence(base)
    rejected = 0
    for mutate in (lambda d: d.update(runtime=[]), lambda d: d["tests"][0].update(native=True), lambda d: d["runtime"][0].update(evidenceKind="source-linked"), lambda d: d["systemRecovery"]["after"].update(effects=False), lambda d: d.update(remaining=[]), lambda d: d["runtime"][0].update(passed=0, failed=1), lambda d: d.update(sourceCommit="old"), lambda d: d.update(runtimeAttempts=[dict(checks=3, passed=1, failed=2, skipped=0, evidenceKind="native-ui", pid=2, exitCode=1, log="failed run")])):
        broken = copy.deepcopy(base)
        mutate(broken)
        try:
            evidence(broken)
        except (KeyError, TypeError, ValueError):
            rejected += 1
        else:
            raise AssertionError("Invalid evidence passed")
    for broken in (text + "TBD\n", text.replace("CS0029", ""), text + "整个P2已完成\n"):
        try:
            report_text(broken)
        except ValueError:
            rejected += 1
        else:
            raise AssertionError("Invalid report passed")
    table_shape("| 检查 | 结果 |\n| --- | --- |\n| case | passed |\n")
    try:
        table_shape("| 检查 | 结果 |\n| --- | --- |\n| case | |\n")
    except ValueError:
        rejected += 1
    else:
        raise AssertionError("Empty evidence cell passed")
    return rejected


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--self-test-only", action="store_true")
    args = parser.parse_args()
    count = self_test() if args.self_test or args.self_test_only else 0
    if args.self_test_only:
        print(f"P2 in-memory tests passed: {count} rejected fixtures; no app/system action.")
        return 0
    root = args.root.resolve()
    raw = (root / REPORT).read_bytes()
    need(not raw.startswith(b"\xef\xbb\xbf"), "Report BOM is unexpected")
    text = raw.decode("utf-8")
    report_text(text)
    table_shape(text)
    links = local_links(root, text)
    source_contract(root)
    receipt = root / "docs/winui-p2/verification.json"
    if receipt.is_file():
        evidence(json.loads(receipt.read_text(encoding="utf-8")))
    print(f"P2 document/source check passed: {links} local links, {count} rejected fixtures; runtime receipt {'checked' if receipt.is_file() else 'absent, not a UI pass'}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
