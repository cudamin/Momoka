"""Reject credential-shaped values in reports and archived evidence.

Run: py tools/check-report-credentials.py
Test: py tools/check-report-credentials.py --self-test
This is read-only and never prints the suspected value. A zero result proves only
that the checked patterns are absent; it does not prove an unknown shape is safe.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import re
import tempfile
from urllib.parse import unquote

PLACEHOLDER = re.compile(
    r"^(?:<[^<>]*>|\[(?:redacted|placeholder)\]|\$\{?[A-Za-z_][A-Za-z0-9_]*\}?|"
    r"\{[^{}]*\}|…+|\.\.\.+|redacted|placeholder(?:[-_][^\s]*)?|xxx+|null|none)?$",
    re.IGNORECASE,
)
QUERY = re.compile(
    r"[?&](?P<key>api[_-]?key|apikey|token|access[_-]?token|x-plex-token|authx|password|secret)=([^&\s#\"'<>]*)",
    re.IGNORECASE,
)
USERINFO = re.compile(r"[a-z][a-z0-9+.-]*://[^/\s:@]+:([^/\s@]+)@", re.IGNORECASE)
HEADER = re.compile(
    r"^\s*(X-Emby-Token|X-Plex-Token|Authorization|Proxy-Authorization|Cookie|Set-Cookie)\s*:\s*(.+?)\s*$",
    re.IGNORECASE,
)
AUTH_TOKEN = re.compile(
    r"(?:Token\s*=\s*|Bearer\s+|Basic\s+)(\"[^\"]*\"|'[^']*'|[^\s,;]+)",
    re.IGNORECASE,
)
JSON_SECRET = re.compile(
    r"\"(?:access[_-]?token|auth[_-]?token|token|password|api[_-]?key|app[_-]?secret|secret|cookie)\"\s*:\s*\"([^\"]*)\"",
    re.IGNORECASE,
)
DEFAULT_FILES = ("PROGRESS.md", "docs/待交付批次.md")
DEFAULT_DIRS = ("docs/archive",)


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    kind: str
    subject: str = ""

    def render(self) -> str:
        suffix = f" ({self.subject})" if self.subject else ""
        return f"{self.path}:{self.line}: {self.kind}{suffix}"


def literal(value: str) -> bool:
    value = value.strip().strip("\"'")
    return bool(PLACEHOLDER.fullmatch(unquote(value)))


def inspect_text(path: str, text: str) -> list[Finding]:
    findings: list[Finding] = []
    for number, line in enumerate(text.splitlines(), 1):
        for match in QUERY.finditer(line):
            if not literal(match.group(2)):
                findings.append(Finding(path, number, "query-credential", match.group("key").lower()))
                break
        for match in USERINFO.finditer(line):
            if not literal(match.group(1)):
                findings.append(Finding(path, number, "url-userinfo"))
                break
        header = HEADER.match(line)
        if header:
            name = header.group(1).lower()
            value = header.group(2).strip()
            if name in {"x-emby-token", "x-plex-token"}:
                if value and not literal(value):
                    findings.append(Finding(path, number, "header-credential"))
            elif name in {"cookie", "set-cookie"}:
                if value and not literal(value):
                    findings.append(Finding(path, number, "header-credential"))
            elif name in {"authorization", "proxy-authorization"}:
                tokens = list(AUTH_TOKEN.finditer(value))
                if tokens:
                    if any(not literal(item.group(1)) for item in tokens):
                        findings.append(Finding(path, number, "header-credential"))
                elif not literal(value) and not value.lower().startswith("mediabrowser "):
                    findings.append(Finding(path, number, "header-credential"))
        for match in JSON_SECRET.finditer(line):
            if not literal(match.group(1)):
                findings.append(Finding(path, number, "json-credential"))
                break
    return findings


def redact_text(text: str) -> tuple[str, int]:
    count = 0

    def replace(match: re.Match[str], group: int) -> str:
        nonlocal count
        if literal(match.group(group)):
            return match.group(0)
        count += 1
        start = match.start(group) - match.start(0)
        end = match.end(group) - match.start(0)
        original = match.group(0)
        return original[:start] + "[redacted]" + original[end:]

    text = QUERY.sub(lambda match: replace(match, 2), text)
    text = USERINFO.sub(lambda match: replace(match, 1), text)
    return text, count


def scan_paths(root: Path, paths: list[Path]) -> list[Finding]:
    findings: list[Finding] = []
    seen: set[Path] = set()
    for path in paths:
        resolved = path.resolve()
        if resolved in seen or not resolved.is_file():
            continue
        seen.add(resolved)
        try:
            text = resolved.read_bytes().decode("utf-8-sig")
            relative = resolved.relative_to(root.resolve()).as_posix()
        except UnicodeDecodeError:
            findings.append(Finding(path.name, 1, "unreadable-report"))
            continue
        except ValueError:
            relative = resolved.as_posix()
        findings.extend(inspect_text(relative, text))
    return findings


def scan_defaults(root: Path) -> list[Finding]:
    paths = [root / name for name in DEFAULT_FILES]
    for relative in DEFAULT_DIRS:
        directory = root / relative
        if directory.is_dir():
            paths.extend(sorted(directory.rglob("*.md")))
    return scan_paths(root, paths)


def self_test() -> int:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "PROGRESS.md").write_text(
            "Authorization: MediaBrowser Token=\"<access token>\"\n"
            "https://example.invalid/item?api_key=fixture-secret\n",
            encoding="utf-8",
        )
        archive = root / "docs" / "archive"
        archive.mkdir(parents=True)
        (archive / "old.md").write_text("{\"password\": \"fixture-secret\"}\n", encoding="utf-8")
        findings = scan_defaults(root)
        kinds = {item.kind for item in findings}
        if kinds != {"query-credential", "json-credential"}:
            raise AssertionError(f"self-test unexpected kinds: {sorted(kinds)}")
        if any("fixture-secret" in item.render() for item in findings):
            raise AssertionError("self-test leaked a suspected value")
        (root / "PROGRESS.md").write_text("https://example.invalid?token=${TOKEN}\n", encoding="utf-8")
        (archive / "old.md").unlink()
        if scan_defaults(root):
            raise AssertionError("self-test rejected placeholders")
    print("Report credential self-test passed: query, header, JSON, userinfo and archive paths; values stay hidden.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--file", type=Path, action="append", default=[])
    parser.add_argument("--redact", action="store_true", help="Replace URL query/userinfo values in place; explicit and irreversible.")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    root = args.root.resolve()
    if args.redact:
        paths = args.file if args.file else [
            *[root / name for name in DEFAULT_FILES],
            *[path for relative in DEFAULT_DIRS if (root / relative).is_dir() for path in sorted((root / relative).rglob("*.md"))],
        ]
        total = 0
        for path in paths:
            if not path.is_file():
                continue
            text = path.read_bytes().decode("utf-8-sig")
            updated, count = redact_text(text)
            if count:
                path.write_bytes(updated.encode("utf-8"))
                total += count
        print(f"Report credential redaction: {total} values replaced with [redacted]; no values printed.")
        return 0
    findings = scan_paths(root, args.file) if args.file else scan_defaults(root)
    for finding in findings:
        print(finding.render())
    print(f"Report credential check: {len(findings)} suspicious values. Values are not echoed.")
    return 1 if findings else 0


if __name__ == "__main__":
    raise SystemExit(main())
