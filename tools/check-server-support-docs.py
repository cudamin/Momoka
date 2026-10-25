"""Offline guardrails for the dated real-server support documentation.

This checks reviewed copy, local links and support boundaries, not live server behavior.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

REPORT = "docs/实服验证-飞牛与Jellyfin-2026-10-26.md"
FILES = (
    "README.md", "docs/Jellyfin支持.md", "docs/飞牛影视.md", "docs/开发与验证.md", REPORT,
    ".claude/skills/momoka-jellyfin-api/SKILL.md",
    ".claude/skills/momoka-fnnas-media-api/SKILL.md",
)


def check(root: Path) -> None:
    texts: dict[str, str] = {}
    for relative in FILES:
        path = root / relative
        data = path.read_bytes()
        if b"\r" in data:
            raise ValueError(f"non-LF support document: {relative}")
        texts[relative] = data.decode("utf-8")
    readme, jellyfin, fnnas, report = (texts[p] for p in FILES[:3] + (REPORT,))
    for version, document in (("10.10.7", jellyfin), ("0.9.8", fnnas), ("0.8.43", fnnas)):
        if version not in document or version not in report:
            raise ValueError(f"missing measured server version: {version}")
    for relative in ("README.md", "docs/Jellyfin支持.md", "docs/飞牛影视.md", REPORT):
        text = texts[relative]
        for claim in ("管理写入未验证", "续播"):
            if claim not in text:
                raise ValueError(f"missing support boundary {claim}: {relative}")
    if "2026-10-26" not in report or "独占管线" not in report or "内置 libmpv" not in report:
        raise ValueError("missing dated backend/pipeline coverage")
    if "集成管线实播" not in report or "外部 mpv" not in report or "Jellyfin 12" not in report:
        raise ValueError("missing unverified playback/version scope")
    for stale in ("Jellyfin（实服播放与写入未验证）", "飞牛影视（实播未验证）", "当前原画适配的实播未验证"):
        if stale in readme:
            raise ValueError("stale public playback verification claim")
    if not re.search(r"Plex[^\n]*未(?:对|实服|连接|核对)", readme):
        raise ValueError("Plex must remain unverified")
    if "/socket?api_key=" in jellyfin:
        raise ValueError("Jellyfin websocket documentation puts credentials in URL")
    if "真实界面截图出现空白" not in report:
        raise ValueError("missing inconclusive real UI evidence")
    if re.search(r"(?:https?://)?(?:192\.168\.|10\.(?:\d+\.){2}|172\.(?:1[6-9]|2\d|3[01])\.)", report):
        raise ValueError("private server address in public evidence")
    if any(name in report for name in ("ProtectedAccessToken", "ProtectedPassword", "authx=", "api_key=")):
        raise ValueError("credential field in public evidence")
    for relative, text in texts.items():
        for target in re.findall(r"\]\(([^)]+)\)", text):
            if re.match(r"[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("#"):
                continue
            local = target.split("#", 1)[0]
            if local and not ((root / relative).parent / local).exists():
                raise ValueError(f"missing local documentation link: {relative}")
    print("通过：服务端版本、续播覆盖、未验证范围、本地链接、LF 与公开证据边界；未联网。")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        check(args.root.resolve())
    except (OSError, ValueError) as error:
        print(f"失败：{error}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
