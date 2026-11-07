"""Offline checks for the full-review documentation fixes; never reads user data."""
from __future__ import annotations

import argparse
import ipaddress
from pathlib import Path
import re

FILES = (
    "PRIVACY.md", "README.md", "website/index.html", "website/dist/index.html",
    "docs/开发与验证.md", "docs/MoviePilot订阅.md", "docs/Plex支持.md",
    ".claude/skills/momoka-dandanplay/SKILL.md",
    ".claude/skills/momoka-dandanplay/references/credential-endpoint.md",
    ".claude/skills/momoka-dandanplay/references/protocol.md",
    ".claude/skills/momoka-playback/SKILL.md",
    ".claude/skills/momoka-verification/SKILL.md",
    ".claude/skills/momoka-fnnas-media-api/references/http-and-auth.md",
    "tests/Momoka.ImagePerformanceTests/README.md",
    "tests/Momoka.PageContractTests/README.md",
    "PROGRESS.md",
    ".qoder/team-runs/20261025-032101-8172a0/results/T4.md",
    ".qoder/team-runs/20261025-032101-8172a0/run.json",
    ".qoder/team-runs/20261025-032101-8172a0/trajectory.jsonl",
)


def private_urls(text: str) -> list[str]:
    hosts = re.findall(r"https?://(\d{1,3}(?:\.\d{1,3}){3})(?=[:/\s\"<>]|$)", text)
    ranges = tuple(ipaddress.ip_network(value) for value in ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"))
    return [host for host in hosts if any(ipaddress.ip_address(host) in network for network in ranges)]


def check(root: Path) -> dict[str, bool]:
    texts = {name: (root / name).read_text(encoding="utf-8") for name in FILES}
    privacy = texts["PRIVACY.md"]
    cn, en = privacy.split("# Momoka Privacy Policy", 1)
    dandan = texts[FILES[7]]
    endpoint = texts[FILES[8]]
    protocol = texts[FILES[9]]
    dev = texts["docs/开发与验证.md"]
    moviepilot = texts["docs/MoviePilot订阅.md"]
    playback = texts[FILES[10]]
    verification = texts[FILES[11]]
    reference = root / FILES[12]
    reference_text = texts[FILES[12]]
    reference_links = re.findall(r"\]\(([^)]+)\)", reference_text)
    return {
        "R18-001": all(word in cn for word in ("danmu_api", "字幕翻译", "路径", "取消"))
        and all(word in en for word in ("danmu_api", "Translation", "path", "cancel", "charges"))
        and "you may instead enter your own AppId" not in en
        and all("不上传、不分享你的" not in texts[name] for name in FILES[1:4])
        and texts["website/index.html"] == texts["website/dist/index.html"],
        "R18-002": "共享目录" in cn and "只删除" in cn and "视频" in cn and "Momoka 子目录" in cn
        and "shared folder" in en and "only files" in en
        and "以及你自己设置的截图目录与下载目录" not in cn
        and "(and any screenshot or download folders you configured)" not in en,
        "R18-003": not any(private_urls(texts[name]) for name in FILES[15:]),
        "R18-004": "`api_key` 与 `deviceId` 进查询串" not in dev
        and "deviceId" in dev and "Handshake" in dev,
        "R18-005": "用户可改用自定义凭据" not in endpoint
        and "等功能恢复或改用自定义凭据" not in endpoint
        and "自定义凭据只映射" not in protocol
        and "内置来源或自定义凭据齐全" not in dandan,
        "R18-006": "四个来源始终显示" in moviepilot and "不自动改选" in moviepilot
        and "搜索页回到媒体库" not in moviepilot
        and "搜索页的 AniBT 来源、版本来源" not in moviepilot,
        "R18-007": "与飞牛影视同样待遇" not in texts["docs/Plex支持.md"],
        "R18-008": "narrows the result back to one season" not in playback
        and "whole-series" in playback and "全剧列表" in dev,
        "R18-009": "共 38 个可重绑动作" not in texts["README.md"]
        and "专项48/48" not in texts[FILES[13]] and "六份产品源码" not in texts[FILES[13]]
        and "默认 116 项" not in texts[FILES[14]] and "三张卡**" not in dandan
        and "四张卡" in dandan and "danmu_api" in dandan
        and "all nine library" not in verification and "seven Emby" in verification,
        "R18-010": "../../momoka-playback/SKILL.md" in reference_text
        and all((reference.parent / target.split("#", 1)[0]).is_file()
                for target in reference_links if not re.match(r"[a-z][a-z0-9+.-]*:", target, re.I)
                and not target.startswith("#")),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        results = check(args.root.resolve())
    except (OSError, ValueError) as error:
        print(f"Documentation input rejected: {type(error).__name__}")
        return 1
    for name, passed in results.items():
        print(f"{'PASS' if passed else 'FAIL'} {name}")
    print(f"Documentation checks: {sum(results.values())}/{len(results)}; offline")
    return 0 if all(results.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
