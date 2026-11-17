"""Offline guardrails for the dated real-server support documentation.

This checks reviewed copy, local links and support boundaries, not live server behavior.
The dated 2026-10-26 evidence document was deleted on 2026-11-17; the same boundary now
lives in the two support documents below, so they are the files this guard reads.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re

FILES = (
    "README.md", "docs/Jellyfin支持.md", "docs/飞牛影视.md", "docs/开发与验证.md",
    ".claude/skills/momoka-jellyfin-api/SKILL.md",
    ".claude/skills/momoka-fnnas-media-api/SKILL.md",
)
# 对外承诺实测范围的三份文档：私网地址与凭据字段只在这三份里守。
SUPPORT = ("README.md", "docs/Jellyfin支持.md", "docs/飞牛影视.md")
PRIVATE = r"(?:https?://)?(?:192\.168\.|10\.(?:\d+\.){2}|172\.(?:1[6-9]|2\d|3[01])\.)"
CREDENTIALS = ("ProtectedAccessToken", "ProtectedPassword", "authx=", "api_key=")


def check(root: Path) -> None:
    texts: dict[str, str] = {}
    for relative in FILES:
        path = root / relative
        data = path.read_bytes()
        if b"\r" in data:
            raise ValueError(f"non-LF support document: {relative}")
        texts[relative] = data.decode("utf-8")
    readme, jellyfin, fnnas = (texts[p] for p in SUPPORT)

    for version, document, name in (("10.10.7", jellyfin, "Jellyfin"), ("0.9.8", fnnas, "飞牛"), ("0.8.43", fnnas, "飞牛")):
        if version not in document:
            raise ValueError(f"missing measured server version {version}: {name}")
    for relative in SUPPORT:
        for claim in ("管理写入未验证", "续播"):
            if claim not in texts[relative]:
                raise ValueError(f"missing support boundary {claim}: {relative}")
    for relative, document in (("docs/Jellyfin支持.md", jellyfin), ("docs/飞牛影视.md", fnnas)):
        for claim in ("内置 libmpv", "独占管线"):
            if claim not in document:
                raise ValueError(f"missing backend/pipeline coverage: {relative}")
    for claim in ("Jellyfin 12", "外部 mpv", "集成管线实播"):
        if claim not in jellyfin:
            raise ValueError(f"missing unverified playback/version scope: {claim}")
    if "外部 mpv" not in fnnas:
        raise ValueError("missing unverified playback scope: 外部 mpv")
    if "真实界面截图出现空白" not in jellyfin:
        raise ValueError("missing inconclusive real UI evidence")
    for stale in ("Jellyfin（实服播放与写入未验证）", "飞牛影视（实播未验证）", "当前原画适配的实播未验证"):
        if stale in readme:
            raise ValueError("stale public playback verification claim")
    if not re.search(r"Plex[^\n]*未(?:对|实服|连接|核对)", readme):
        raise ValueError("Plex must remain unverified")
    if "/socket?api_key=" in jellyfin:
        raise ValueError("Jellyfin websocket documentation puts credentials in URL")
    for relative in SUPPORT:
        text = texts[relative]
        if re.search(PRIVATE, text):
            raise ValueError(f"private server address in public support copy: {relative}")
        if any(name in text for name in CREDENTIALS):
            raise ValueError(f"credential field in public support copy: {relative}")
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
