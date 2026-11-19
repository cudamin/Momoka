"""Check relative Markdown links and anchors in tracked documentation.

Run: py tools/check-docs.py
Test: py tools/check-docs.py --self-test
This only reads files. It never builds, launches the application, publishes, or changes a branch.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import tempfile
from pathlib import Path
from urllib.parse import unquote

LINK = re.compile(r"!?\[([^\]]*)\]\(([^)]+)\)")
INLINE_CODE = re.compile(r"`[^`]*`")
HEADING = re.compile(r"^(#{1,6})\s+(.*)$")
SKILL_LINK = re.compile(r"\]\(\.claude/skills/([^/()]+)/SKILL\.md(?:#[^)]*)?\)")
SCHEMES = ("http://", "https://", "mailto:", "computer:", "file:")


def slug(text: str) -> str:
    value = text.strip().lower()
    value = re.sub(r"[^\w\u4e00-\u9fff\- ]", "", value)
    return value.replace(" ", "-")


def headings(path: Path) -> set[str]:
    values: set[str] = set()
    in_fence = False
    for line in path.read_bytes().decode("utf-8-sig").splitlines():
        if line.lstrip().startswith(("```", "~~~")):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        match = HEADING.match(line)
        if match:
            values.add(slug(match.group(2)))
    return values


def tracked_docs(root: Path) -> list[Path]:
    try:
        output = subprocess.check_output(
            ["git", "-C", str(root), "-c", "core.quotepath=false", "ls-files", "-z", "*.md"],
            stderr=subprocess.DEVNULL,
        )
        return [root / name for name in output.decode("utf-8").split("\0") if name]
    except Exception:
        return sorted(path for path in root.rglob("*.md") if ".git" not in path.parts)


def check_skill_index(root: Path) -> list[str]:
    policy = root / "CLAUDE.md"
    if not policy.is_file():
        return []
    indexed = set(SKILL_LINK.findall(INLINE_CODE.sub("", policy.read_text(encoding="utf-8-sig"))))
    directory = root / ".claude" / "skills"
    actual = {
        path.parent.name
        for path in directory.glob("*/SKILL.md")
        if path.is_file()
    } if directory.is_dir() else set()
    return [f"CLAUDE.md: skill index missing {name}" for name in sorted(actual - indexed)] + [
        f"CLAUDE.md: skill index names missing skill {name}" for name in sorted(indexed - actual)
    ]


def check_file(path: Path) -> list[str]:
    failures: list[str] = []
    lines = path.read_bytes().decode("utf-8-sig").splitlines()
    in_fence = False
    for number, line in enumerate(lines, 1):
        if line.lstrip().startswith(("```", "~~~")):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for match in LINK.finditer(INLINE_CODE.sub("", line)):
            target = match.group(2).strip()
            if not target:
                continue
            if target.startswith("<") and target.endswith(">"):
                target = target[1:-1]
            elif " " in target:
                target = target.split()[0]
            if target.startswith(SCHEMES) or target.startswith("#"):
                continue
            relative, _, anchor = target.partition("#")
            if not relative:
                continue
            resolved = (path.parent / unquote(relative)).resolve()
            if not resolved.exists():
                failures.append(f"{path}:{number}: missing {target}")
                continue
            if anchor and resolved.suffix.lower() == ".md":
                if unquote(anchor).lower() not in headings(resolved):
                    failures.append(f"{path}:{number}: missing anchor {target}")
    return failures


def self_test() -> int:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "b.md").write_text("# 目标\n", encoding="utf-8")
        (root / "a.md").write_text(
            "# 自测\n\n"
            "[ok](b.md)\n"
            "[anchor](b.md#目标)\n"
            "[web](https://example.com)\n"
            "[skip](file:///C:/missing)\n"
            "`[inline](missing-inline.md)`\n"
            "\n"
            "```text\n[fenced](missing-fenced.md)\n```\n"
            "\n"
            "[bad](missing.md)\n"
            "[bad-anchor](b.md#不存在)\n",
            encoding="utf-8",
        )
        skills = root / ".claude" / "skills"
        for name in ("alpha", "orphan"):
            (skills / name).mkdir(parents=True)
            (skills / name / "SKILL.md").write_text("---\nname: fixture\n---\n", encoding="utf-8")
        (root / "CLAUDE.md").write_text(
            "[alpha](.claude/skills/alpha/SKILL.md)\n[gone](.claude/skills/gone/SKILL.md)\n",
            encoding="utf-8",
        )
        index_failures = check_skill_index(root)
        if not any("missing orphan" in failure for failure in index_failures):
            raise AssertionError("self-test did not reject an unindexed skill")
        if not any("names missing skill gone" in failure for failure in index_failures):
            raise AssertionError("self-test did not reject a stale skill index entry")
        failures = [item for path in tracked_docs(root) for item in check_file(path)]
        expected = ("missing.md", "missing anchor b.md#不存在", "missing .claude/skills/gone/SKILL.md")
        for marker in expected:
            if not any(marker in failure for failure in failures):
                raise AssertionError(f"self-test did not reject {marker}")
        if len(failures) != 3:
            raise AssertionError(f"self-test expected 3 failures, got {len(failures)}: {failures}")
    print("Document link self-test passed: valid, external, fenced and inline links handled; broken file and anchor rejected.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    root = args.root.resolve()
    docs = tracked_docs(root)
    failures = [item for path in docs for item in check_file(path)]
    failures.extend(check_skill_index(root))
    for failure in failures:
        print(failure)
    print(f"Document link check: {len(docs)} files, {len(failures)} broken links or skill-index gaps.")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
