#!/usr/bin/env python3
"""Scan XAML files for Fluent design token violations.

Rules:
  F001  hard-coded color in a visual attribute
  F002  literal FontSize (should come from a type-ramp style)
  F003  literal CornerRadius (should use ControlCornerRadius / OverlayCornerRadius)
  F004  Bold/Black/ExtraBold font weight, or italic style
  F005  FontSize below 12 (unreadable in some languages)

Usage:
  python audit_fluent.py <path> [<path> ...] [--format text|json]
                         [--rules F001,F002] [--exclude bin,obj] [--quiet]

Exit codes: 0 = clean, 1 = findings, 2 = error.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

COLOR_ATTRS = (
    "Background",
    "Foreground",
    "BorderBrush",
    "Fill",
    "Stroke",
    "Color",
    "TintColor",
    "FallbackColor",
)

NAMED_COLORS = {
    "white",
    "black",
    "red",
    "green",
    "blue",
    "yellow",
    "orange",
    "purple",
    "gray",
    "grey",
    "magenta",
    "cyan",
    "brown",
    "lime",
    "navy",
    "teal",
    "silver",
    "maroon",
    "olive",
    "aqua",
    "fuchsia",
}

COLOR_ATTR_RE = re.compile(
    r"\b(?:" + "|".join(COLOR_ATTRS) + r")\s*=\s*\"([^\"]*)\""
)
FONT_SIZE_RE = re.compile(r"\bFontSize\s*=\s*\"([0-9]+(?:\.[0-9]+)?)\"")
CORNER_RADIUS_RE = re.compile(r"\bCornerRadius\s*=\s*\"([0-9.,\s]+)\"")
FONT_WEIGHT_RE = re.compile(
    r"\bFontWeight\s*=\s*\"(Bold|Black|ExtraBold|Heavy)\"", re.IGNORECASE
)
FONT_STYLE_RE = re.compile(r"\bFontStyle\s*=\s*\"Italic\"", re.IGNORECASE)

HEX_RE = re.compile(r"^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")

DEFAULT_EXCLUDES = {"bin", "obj", ".git", "node_modules", "artifacts"}


def strip_comments(text: str) -> str:
    """Replace XML comment characters with spaces, preserving line numbers."""
    out = []
    in_comment = False
    i = 0
    while i < len(text):
        if not in_comment and text.startswith("<!--", i):
            in_comment = True
            out.append("    ")
            i += 4
        elif in_comment and text.startswith("-->", i):
            in_comment = False
            out.append("   ")
            i += 3
        else:
            ch = text[i]
            out.append(" " if in_comment and ch != "\n" else ch)
            i += 1
    return "".join(out)


def is_token_definition(line: str) -> bool:
    return "x:Key=" in line


def scan_file(path: Path, rules: set[str]) -> list[dict]:
    try:
        raw = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        raw = path.read_text(encoding="utf-8", errors="replace")

    cleaned = strip_comments(raw)
    findings: list[dict] = []

    for lineno, line in enumerate(cleaned.splitlines(), start=1):
        if not line.strip():
            continue
        original = raw.splitlines()[lineno - 1].strip()

        if "F001" in rules and not is_token_definition(line):
            for match in COLOR_ATTR_RE.finditer(line):
                value = match.group(1).strip()
                if not value or value.startswith("{"):
                    continue
                if HEX_RE.match(value) or value.lower() in NAMED_COLORS:
                    findings.append(
                        {
                            "file": str(path),
                            "line": lineno,
                            "rule": "F001",
                            "message": f"hard-coded color '{value}'",
                            "text": original,
                        }
                    )

        if "F002" in rules or "F005" in rules:
            for match in FONT_SIZE_RE.finditer(line):
                if is_token_definition(line):
                    continue
                size = float(match.group(1))
                if size < 12 and "F005" in rules:
                    findings.append(
                        {
                            "file": str(path),
                            "line": lineno,
                            "rule": "F005",
                            "message": f"FontSize {size:g} is below the 12px minimum",
                            "text": original,
                        }
                    )
                elif "F002" in rules:
                    findings.append(
                        {
                            "file": str(path),
                            "line": lineno,
                            "rule": "F002",
                            "message": f"literal FontSize {size:g}; use a type-ramp style",
                            "text": original,
                        }
                    )

        if "F003" in rules and not is_token_definition(line):
            for match in CORNER_RADIUS_RE.finditer(line):
                findings.append(
                    {
                        "file": str(path),
                        "line": lineno,
                        "rule": "F003",
                        "message": (
                            f"literal CornerRadius '{match.group(1).strip()}'; "
                            "use ControlCornerRadius or OverlayCornerRadius"
                        ),
                        "text": original,
                    }
                )

        if "F004" in rules:
            if FONT_WEIGHT_RE.search(line) or FONT_STYLE_RE.search(line):
                findings.append(
                    {
                        "file": str(path),
                        "line": lineno,
                        "rule": "F004",
                        "message": "Bold/Black/Italic is not part of the Windows type ramp; use SemiBold",
                        "text": original,
                    }
                )

    return findings


def collect_files(paths: list[str], excludes: set[str]) -> list[Path]:
    files: list[Path] = []
    for raw in paths:
        p = Path(raw)
        if not p.exists():
            raise FileNotFoundError(raw)
        if p.is_file():
            if p.suffix.lower() == ".xaml":
                files.append(p)
            continue
        for root, dirs, names in os.walk(p):
            dirs[:] = [d for d in dirs if d.lower() not in excludes]
            for name in names:
                if name.lower().endswith(".xaml"):
                    files.append(Path(root) / name)
    return sorted(files)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Scan XAML for Fluent design token violations."
    )
    parser.add_argument("paths", nargs="+", help="XAML files or directories")
    parser.add_argument(
        "--format", choices=("text", "json"), default="text", help="output format"
    )
    parser.add_argument(
        "--rules",
        default="F001,F002,F003,F004,F005",
        help="comma-separated rule ids (default: all)",
    )
    parser.add_argument(
        "--exclude",
        default=",".join(sorted(DEFAULT_EXCLUDES)),
        help="comma-separated directory names to skip",
    )
    parser.add_argument("--quiet", action="store_true", help="only print the summary")
    args = parser.parse_args(argv)

    rules = {r.strip().upper() for r in args.rules.split(",") if r.strip()}
    unknown = rules - {"F001", "F002", "F003", "F004", "F005"}
    if unknown:
        print(f"error: unknown rules: {', '.join(sorted(unknown))}", file=sys.stderr)
        return 2

    excludes = {e.strip().lower() for e in args.exclude.split(",") if e.strip()}

    try:
        files = collect_files(args.paths, excludes)
    except FileNotFoundError as exc:
        print(f"error: path not found: {exc}", file=sys.stderr)
        return 2

    findings: list[dict] = []
    for path in files:
        findings.extend(scan_file(path, rules))

    by_rule: dict[str, int] = {}
    for item in findings:
        by_rule[item["rule"]] = by_rule.get(item["rule"], 0) + 1

    summary = {
        "files": len(files),
        "findings": len(findings),
        "byRule": dict(sorted(by_rule.items())),
    }

    if args.format == "json":
        print(
            json.dumps(
                {"summary": summary, "findings": findings},
                ensure_ascii=False,
                indent=2,
            )
        )
    else:
        if not args.quiet:
            for item in findings:
                print(
                    f"{item['file']}:{item['line']}: {item['rule']} "
                    f"{item['message']}"
                )
                print(f"    {item['text']}")
        if findings:
            print(
                f"\n{summary['findings']} finding(s) in {summary['files']} file(s): "
                + ", ".join(f"{k}={v}" for k, v in summary["byRule"].items())
            )
        else:
            print(f"clean: {summary['files']} XAML file(s), no findings")

    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
