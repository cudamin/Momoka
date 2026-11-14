"""Validate the P1 documents and anonymous evidence summary; no build, app or network.

Run: py tools/check-winui-p1.py --self-test
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import re
from urllib.parse import unquote
import xml.etree.ElementTree as ET

FILES = ("docs/WinUI-P1基线.md", "docs/WinUI-P1材质与窗口清单.md",
         "docs/WinUI-P1页面与控件清单.md", "docs/winui-p1/baseline.json")


def require(condition: bool, detail: str) -> None:
    if not condition:
        raise ValueError(detail)


def anchors(text: str) -> set[str]:
    found, counts = set(), {}
    for title in re.findall(r"^#{1,6}\s+(.+?)\s*#*\s*$", text, re.M):
        slug = re.sub(r"\s+", "-", re.sub(r"[^\w\s-]", "", title.casefold()))
        duplicate = counts.get(slug, 0)
        found.add(slug + (f"-{duplicate}" if duplicate else ""))
        counts[slug] = duplicate + 1
    return found | set(re.findall(r'<a\s+(?:id|name)=["\']([^"\']+)', text))


def documents(root: Path) -> tuple[dict[str, str], int]:
    texts, links = {}, 0
    for name in FILES:
        raw = (root / name).read_bytes()
        require(not raw.startswith(b"\xef\xbb\xbf") and b"\r" not in raw and raw.endswith(b"\n"), f"UTF-8/LF/BOM/final newline: {name}")
        texts[name] = raw.decode("utf-8")
    for name, text in texts.items():
        for target in re.findall(r"\]\((<[^>]+>|[^)\s]+)(?:\s+\"[^\"]*\")?\)", text):
            target = unquote(target.strip("<>"))
            if re.match(r"[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("//"):
                continue
            path, _, anchor = target.partition("#")
            linked = (root / name).parent.joinpath(path).resolve() if path else root / name
            require(linked.is_file(), f"Missing link: {name} -> {target}")
            if anchor:
                require(anchor in anchors(linked.read_text(encoding="utf-8-sig")), f"Missing anchor: {name} -> {target}")
            links += 1
    return texts, links


def inventory(root: Path) -> dict[str, int]:
    counts = dict.fromkeys(("Page", "UserControl", "ContentDialog"), 0)
    for file in (root / "src/Momoka.Shell/Views").rglob("*.xaml"):
        tag = ET.fromstring(file.read_bytes()).tag.rsplit("}", 1)[-1]
        if tag in counts:
            counts[tag] += 1
    return counts


def validate(data: dict, actual_inventory: dict[str, int]) -> None:
    for field in ("sourceCommit", "integratedCommit"):
        require(re.fullmatch(r"[0-9a-fA-F]{40}", data[field]) is not None, f"Invalid {field}")
    require(data["inventory"] == actual_inventory, "XAML root inventory differs from evidence")
    runtime = data["runtime"]
    require(isinstance(runtime["compile"], dict) and all(runtime["compile"].get(key) for key in ("winui", "runtimePackage")), "Missing compile package versions")
    require(isinstance(runtime["requested"], dict) and runtime["requested"].get("bootstrapLine"), "Missing requested bootstrap line")
    require(isinstance(runtime["loaded"], list) and runtime["loaded"] and all(isinstance(item, dict) and all(item.get(key) for key in ("name", "runtimePackage", "fileVersion")) for item in runtime["loaded"]), "Loaded binaries must be separate version observations")
    runs = data["performance"]["runs"]
    measured = [*runs, *data["performance"].get("exploratoryRuns", [])]
    require(len(runs) >= 3 and len({run["id"] for run in runs}) == len(runs), "Need at least three distinct performance runs")
    require(len({run["id"] for run in measured}) == len(measured), "Exploratory and comparative run IDs overlap")
    for entry in [*measured, *data["surfaces"]]:
        require(all(type(entry[key]) is int and entry[key] >= 0 for key in ("checks", "passed", "failed")), "Invalid check counts")
        require(entry["checks"] > 0 and entry["checks"] == entry["passed"] + entry["failed"], "Check outcomes do not sum")
        require(type(entry["exitCode"]) is int and (entry["exitCode"] == 0) == (entry["failed"] == 0), "Exit code conceals failed checks")
        require(re.fullmatch(r"[0-9a-fA-F]{40}", entry["sourceCommit"]) is not None and entry["sourceCommit"] in (data["sourceCommit"], data["integratedCommit"]), "Run source is outside recorded revisions")
        require(entry.get("runDirectory") and len(entry["failures"]) == entry["failed"] and all(all(isinstance(item.get(key), str) and item[key].strip() for key in ("name", "reason", "classification")) for item in entry["failures"]), "Missing named failures or run receipt")
    require(all(type(run["samples"]) is int and run["samples"] > 0 and run["sourceCommit"] == data["sourceCommit"] for run in measured), "Performance runs need samples and baseline source")
    if any(run["failed"] for run in measured):
        require(data["performance"].get("productPassed") is False, "Red performance cannot be product-passed")
    require(data["surfaces"], "Missing surface results")
    for entry in data["surfaces"]:
        require(entry["status"] == ("red" if entry["failed"] else "green"), "Surface red was washed green")
    music = data["music"]
    require(music["observer"] is True and type(music["samples"]) is int and music["samples"] > 0 and len(music["measurements"]) == music["samples"], "Missing music observations")
    require(type(music["knownGap"]) is bool and type(music["requirementPass"]) is bool and type(music["productPassed"]) is bool, "Music outcomes must be explicit")
    if music["knownGap"]:
        require(music["requirementPass"] is False and music["productPassed"] is False, "Known music gap was called passed")
        require(all(item.get("requirementPass") is False and item.get("measurementOnly") is True for item in music["measurements"]), "Music measurement washed a known gap green")
    require(data["instrumentationRemoved"] is True and data["productionChanges"] is False, "P1 evidence must leave production unchanged")
    require(data["verificationCompleted"] is True and data["evidenceLimits"] and all(isinstance(item, str) and item.strip() for item in data["evidenceLimits"]), "Missing completion boundary or evidence limits")
    require(set(data["decisions"]) == {f"D{number:02}" for number in range(1, 8)} and all(item.get("status") == "deferred" and item.get("productChanged") is False for item in data["decisions"].values()), "P1 must not implement deferred D01-D07")


def self_test() -> int:
    base = dict(sourceCommit="a" * 40, integratedCommit="b" * 40, inventory=dict(Page=1, UserControl=2, ContentDialog=3),
                runtime=dict(compile=dict(winui="2.3.9", runtimePackage="2.5.1"), requested=dict(bootstrapLine="2"), loaded=[dict(name="Xaml.dll", runtimePackage="2.6.5.0", fileVersion="3.2.0.2511")]),
                performance=dict(runs=[dict(id=str(i), sourceCommit="a" * 40, checks=2, passed=1, failed=1, samples=2, exitCode=1, runDirectory="anonymous", failures=[dict(name="sample", reason="observed gap", classification="unresolved")]) for i in range(3)], exploratoryRuns=[], productPassed=False),
                surfaces=[dict(id="library", sourceCommit="a" * 40, runDirectory="anonymous", checks=2, passed=1, failed=1, status="red", exitCode=1, failures=[dict(name="sample", reason="observed gap", classification="unresolved")])],
                music=dict(observer=True, knownGap=True, requirementPass=False, productPassed=False, samples=1, measurements=[dict(requirementPass=False, measurementOnly=True)]), instrumentationRemoved=True, productionChanges=False,
                verificationCompleted=True, evidenceLimits=["Anonymous only"], decisions={f"D{i:02}": dict(status="deferred", productChanged=False) for i in range(1, 8)})
    validate(base, base["inventory"])
    def omit_exploratory_failure(data: dict) -> None:
        entry = copy.deepcopy(data["performance"]["runs"][0])
        entry.update(id="exploratory", failures=[])
        data["performance"]["exploratoryRuns"].append(entry)
    mutations = (lambda d: d.pop("runtime"), lambda d: d["music"].update(requirementPass=True), lambda d: d["performance"]["runs"][0].update(samples=0), lambda d: d["runtime"].update(loaded=d["runtime"]["compile"]), lambda d: d.update(productionChanges=True), lambda d: d["surfaces"][0].update(status="green"), lambda d: d["surfaces"][0].update(failures=[]), lambda d: d["decisions"]["D01"].update(productChanged=True), omit_exploratory_failure, lambda d: d["music"]["measurements"][0].update(requirementPass=True), lambda d: d["performance"]["runs"][0].update(sourceCommit="b" * 40))
    for mutate in mutations:
        broken = copy.deepcopy(base)
        mutate(broken)
        try:
            validate(broken, base["inventory"])
        except (ValueError, KeyError, TypeError):
            continue
        raise AssertionError("Invalid P1 evidence passed")
    require("测试-章节" in anchors("## 测试 章节\n"), "Anchor fixture failed")
    return len(mutations)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    rejected = self_test() if args.self_test else 0
    texts, links = documents(args.root.resolve())
    validate(json.loads(texts[FILES[-1]]), inventory(args.root.resolve()))
    print(f"P1 evidence check passed: {len(texts)} files, {links} local links, {rejected} rejected fixtures; no build/app/network.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
