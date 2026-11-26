"""Check the repository's development policy and the routes to its authoritative sections.

Run: py tools/check-main-tree-policy.py --self-test
Reads selected repository documents and runs in-memory counterexamples. The checks
pin decisions and links to their owners and catch specific retired or conflicting
phrases. They cannot validate arbitrary wording or product behavior.
No application or Git mutations.
"""
from __future__ import annotations

import argparse
from pathlib import Path
from posixpath import dirname, relpath
import re

POLICY = "CLAUDE.md"
DOC = "docs/开发与验证.md"
SKILL = ".claude/skills/momoka-verification/SKILL.md"
LEDGER = "docs/待交付批次.md"
MATRIX = "docs/后端能力矩阵.md"

# A tuple belongs to one decision (a paragraph, bullet or table row) inside a specific
# section. Keep short condition/action/assertion fragments here, not mirrors of paragraphs.
DECISIONS = {
    "优先级与完成条件": (
        ("P0 红线", "授权", "真实数据与凭据", "证据真实性"),
        ("P1 必做", "阻断", "检查并集"),
        ("P2 默认", "等效实现", "证据"),
        ("上下文压缩后先重读", "当前工作树"),
        ("可独立回滚", "一个 Git 提交", "修复缺陷必须有相应测试"),
        ("待提交差异", "调用链", "错误出口"),
        ("失败", "未执行", "不适用", "缺少", "未验证"),
    ),
    "动手前核对项目配置": (("global.json", ".csproj", "nuget.config", "前滚策略"),),
    "分层与代码风格": (
        ("禁止 Core 引用 Shell", "框架外运行时", "Windowing 禁止回依赖 Views", "架构专项"),
        ("后端能力矩阵", "显隐、契约测试", "分管线检查"),
        ("AgreementsTests", "逐项更新并运行"),
    ),
    "异常、并发与数据兼容": (
        ("丢弃迟到结果", "复查该身份及权限", "切换默认账号", "原账号退出"),
        ("结果未知", "禁止对回执丢失的写入盲目重发", "实际调用次数"),
        ("损坏文件", "无效输入须报错且不写入", "旧快照不能覆盖新值"),
    ),
    "参考与复刻先读源码": (("版本源码", "固定提交或版本", "未引用外部实现", "直接查本仓"),),
    "WinUI 官方优先与偏离确认": (
        ("新控件类型", "须查", "Gallery 源码", "既有控件", "直接改"),
        ("官方建议冲突", "只读核对", "请用户选择"),
    ),
    "Fluent 界面验收": (
        ("Microsoft Fluent 2", "Windows 应用设计指南", "为标准"),
        ("需要运行证据时", "用截图核对画面", "实际交互或 UIA", "标为未验证"),
    ),
    "功能可用性与显隐": (("MoviePilot 字幕", "始终显示", "保留选中项", "阻止请求"),),
    "弹窗": (
        ("点外关闭", "只返回取消", "禁止触发保存或确认"),
        ("EgDialogSurfaceStyle", "DialogChromeTests", "IsLightDismissEnabled"),
    ),
    "凭据": (
        ("客户端持久化须走", "ISecretProtector", "DPAPI"),
        ("凭据禁止进入 URL", "子进程参数", "禁止鉴权跨源", "按未支持处理"),
        ("占位令牌", "禁止出口", "check-report-credentials.py", "--file", "疑似值必须为零"),
    ),
    "播放相关改动的验证（三档）": (
        ("默认展示", "不起播", "隔离目录"),
        ("隔离本地播放", "不登录、不上报", "拒绝 URL", "两段哈希"),
        ("真实服务器播放或写入", "明确授权", "账号和操作", "未获授权改用匿名夹具"),
    ),
    "依赖与分析器升级": (
        ("稳定发行版", "传递依赖", "未核", "先还原", "四道闸门"),
        ("独立可信来源", "禁止从待验发布目录反向生成白名单"),
        ("分发权未核实", "适用的对应源码义务", "禁止装入发行包"),
    ),
    "四道闸门与日常交付": (
        ("提交级", "编译受影响项目及测试", "覆盖改动的用例", "纯文档", "不发布应用"),
        ("批次级", "日常 UI 默认不跑探针", "自动发布本树", "刷新快捷方式", "体验刷新不销账", "仅合并不触发"),
        ("统一交付级", "用户明确要求", "目标提交", "已纳入", "最终整合代码", "闸门 2", "闸门 3"),
        ("四闸全验", "发新版本", "升级依赖或分析器", "失败、跳过或未执行", "阻断"),
        ("高风险 XAML", "首次引用控件基样式", "跨资源字典取键", "字典加载顺序",
         "匿名隔离", "应用模板", "无法安全隔离或加载失败时，暂缓体验发布"),
        ("tools/quality.ps1", "0 警告、0 错误", "-SkipBuild", "-SkipDuplication", "仅供定位"),
        ("编译成功才可", "--no-build", "失败 0、跳过 0", "deferred", "-All -IncludeExternal",
         "summary.json", "无延期、跳过、失败及遗漏"),
        ("verify-publish.ps1", "TargetPath", "WorkingDirectory", "Momoka-<中文简述>.lnk", "改了什么或新增了什么功能"),
        ("本次发布件", "selfcheck-diff.ps1", "缺项", "通过降级", "只比较既有报告", "禁止靠接纳失败"),
    ),
    "资源占用与等待": (
        ("只读探针", "可中断等待", "释放后由正式操作重新取得锁", "竞争失败继续等待"),
        ("禁止结束用户或其他会话的进程、绕过锁", "最多等待 30 秒", "跳过本次发布", "快捷方式仍指向旧版"),
        ("超时或确需用户处理", "具体占用", "未完成检查"),
    ),
    "发布状态与销账": (
        ("Directory.Build.props", "UpdateTests", "实际程序集", "清单身份"),
        ("预发布与正式发布均须明确上传授权", "四闸", "验收全过", "目标提交", "SHA-256", "上传状态"),
        ("只关闭已合入 master", "目标提交", "必需检查全过", "交付产物已核对", "继续保留"),
        ("合并后的代码必须重新验证", "分支报告只覆盖该分支", "主树实际目录并核对快捷方式"),
    ),
    "Git 与协作": (
        ("唯一长期主树", "一个写入会话"),
        ("只在用户明确要求合并时合入 master", "未合并保留分支"),
        ("按内容确认全部进入 master", "工作树干净", "没有未保留改动或独有提交", "没有其他会话占用",
         "立即删除分支快捷方式、临时分支及其工作树", "任一项不明则登记待清理", "未合并分支禁止删除"),
        ("只暂存本任务", "不写死协作者模型版本", "获明确上传授权后", "git push origin master"),
    ),
    "文档查阅与维护": (("文档验收", "check-docs.py", "check-main-tree-policy.py --self-test", "纯文档不构建应用"),),
}

# Consumer documents route to the owner instead of restating all of the mandatory decisions.
ROUTES = {
    DOC: ("git-与协作", "四道闸门与日常交付", "资源占用与等待", "发布状态与销账", "播放相关改动的验证三档",
          "fluent-界面验收"),
    SKILL: ("git-与协作", "四道闸门与日常交付", "资源占用与等待", "发布状态与销账", "fluent-界面验收"),
    LEDGER: ("git-与协作", "四道闸门与日常交付", "发布状态与销账"),
    MATRIX: ("播放相关改动的验证三档",),
}
REQUIRED = {
    DOC: ("tools/check-doc-commands.ps1", "py tools/check-main-tree-policy.py --self-test",
          "](待交付批次.md)", "Momoka-<中文简述>", "TargetPath", "WorkingDirectory"),
    SKILL: ("../../../docs/待交付批次.md", "instantiate the affected production view",
            "apply its template", "deferred external suites", "working directory"),
    LEDGER: ("写清树与合并状态", "未纳入的批次保持待合并", "保留剩余未销账条目", "## 交付时怎么做", "## 已交付"),
    MATRIX: ("| 共享能力 | Emby | Jellyfin | 飞牛影视 | Plex |", "真实验证按后端分开记账",
             "只证明各自覆盖", "代码中的能力声明、适配器和契约测试是实现真源", "同一批次更新本表"),
}
FORBIDDEN = {
    POLICY: (
        "文件锁须请用户关闭",
        "分支做完只合并",
        "临时分支完成后主动合入主树",
        "分支做完自动合并",
        "合进 master 才算这一轮做完",
    ),
    DOC: (
        "合并与日常收尾不跑这一闸",
        "真实服务器播放无需用户授权",
    ),
    SKILL: (
        "At batch close, run gate 2, the affected runtime checks/probes and gate 3 once",
        "Merging closes a batch; publishing happens only on an explicit delivery request",
        "Daily UI edits run no probes, native run checks or acceptance screenshots",
        "A user request to deliver closes the pending ledger entries at the requested commit",
    ),
    LEDGER: (
        "分支已发布即算交付",
        "只记已合入 master 的东西",
        "只在合并完成时追加、只在交付完成时销账",
        "从空表重新开始",
    ),
}


HEADING = re.compile(r"^#{1,6} (.+)$")
LINK = re.compile(r"\]\(([^)]+)\)")


def prose_lines(text: str) -> list[str]:
    """Omit fenced examples and comments so a retired rule cannot satisfy a decision."""
    lines: list[str] = []
    fence = ""
    for line in re.sub(r"<!--.*?-->", "", text, flags=re.S).splitlines():
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if fence:
            if marker and marker[1].startswith(fence) and not line[marker.end():].strip():
                fence = ""
        elif marker:
            fence = marker[1]
        else:
            lines.append(line)
    return lines


def sections(lines: list[str]) -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    body: list[str] = []
    for line in lines:
        heading = HEADING.fullmatch(line)
        if heading:
            title = heading[1]
            if title in result:
                raise ValueError(f"duplicate policy heading: {title}")
            body = []
            result[title] = body
        else:
            body.append(line)
    return result


def validate(texts: dict[str, str]) -> None:
    prose: dict[str, str] = {}
    for name, text in texts.items():
        if "\r" in text or not text.endswith("\n"):
            raise ValueError(f"non-LF or missing final newline: {name}")
        prose[name] = "\n".join(prose_lines(text))
        for phrase in FORBIDDEN.get(name, ()):
            if phrase in prose[name]:
                raise ValueError(f"superseded or conflicting rule: {name}: {phrase}")

    policy = sections(prose[POLICY].splitlines())
    for title, rules in DECISIONS.items():
        for fragments in rules:
            if not any(all(part in line for part in fragments) for line in policy.get(title, ())):
                raise ValueError(f"missing decision: {POLICY} / {title}: {', '.join(fragments)}")

    anchors = {re.sub(r"[^\w -]", "", heading.lower()).replace(" ", "-") for heading in policy}
    for name, required_anchors in ROUTES.items():
        links = LINK.findall(re.sub(r"`[^`\n]*`", "", prose[name]))
        for anchor in required_anchors:
            target = f"{relpath(POLICY, dirname(name))}#{anchor}"
            if target not in links or anchor not in anchors:
                raise ValueError(f"missing policy link: {name}: {target}")
    for name, phrases in REQUIRED.items():
        for phrase in phrases:
            if phrase not in prose[name]:
                raise ValueError(f"missing document contract: {name}: {phrase}")


def self_test(texts: dict[str, str]) -> int:
    rejected = 0

    def must_reject(name: str, old: str, new: str, *, append: bool = False) -> None:
        nonlocal rejected
        original = texts[name]
        mutated = original + new + "\n" if append else original.replace(old, new)
        if original == mutated:
            raise AssertionError(f"fixture did not change {name}: {old}")
        try:
            validate({**texts, name: mutated})
        except ValueError:
            rejected += 1
        else:
            raise AssertionError(f"invalid policy passed: {name}: {old or new}")

    for rules in DECISIONS.values():
        for fragments in rules:
            for fragment in fragments:
                must_reject(POLICY, fragment, "")
    for name, phrases in REQUIRED.items():
        for phrase in phrases:
            must_reject(name, phrase, "")
    for name, phrases in FORBIDDEN.items():
        for phrase in phrases:
            must_reject(name, "", phrase, append=True)
    for name, link_anchors in ROUTES.items():
        for anchor in link_anchors:
            old = f"{relpath(POLICY, dirname(name))}#{anchor}"
            must_reject(name, old, old.replace("CLAUDE.md", "README.md"))

    # Coverage in an individual matrix cell can change as new evidence arrives.
    matrix = texts[MATRIX]
    row = next(line for line in matrix.splitlines() if line.startswith("| ") and len(line.split("|")) == 7
               and "共享能力" not in line and "---" not in line)
    cells = row.split("|")
    cells[-2] = " 新增实服证据见本批记录 "
    validate({**texts, MATRIX: matrix.replace(row, "|".join(cells))})

    # Exercise meaning-changing edits, including a good rule hidden outside its owner.
    for old, wrong in (
        ("仅供定位", "结果也算完整闸门"),
        ("无延期、跳过、失败及遗漏", "允许延期、跳过、失败及遗漏"),
        ("只比较既有报告", "可用旧报告验收当前构建"),
        ("只在用户明确要求合并时合入 master", "完成就合入 master"),
        ("凭据禁止进入 URL", "凭据允许进入 URL"),
        ("预发布与正式发布均须明确上传授权", "预发布可以免验并上传"),
        ("无法安全隔离或加载失败时，暂缓体验发布", "无法安全隔离或加载失败时，照常体验发布"),
        ("禁止触发保存或确认", "可以触发保存或确认"),
        ("为标准。按本次受影响界面", "作为可选参考。按本次受影响界面"),
        ("需要运行证据时，用截图核对画面，用实际交互或 UIA 核对控件状态与行为。", "截图和交互证据均可省略。"),
    ):
        must_reject(POLICY, old, wrong)
    must_reject(POLICY, "\n### 弹窗\n", "")
    must_reject(POLICY, "\n### Fluent 界面验收\n", "")
    release = next(line for line in sections(prose_lines(texts[POLICY]))["发布状态与销账"]
                   if "预发布与正式发布均须明确上传授权" in line)
    must_reject(LEDGER, "写清树与合并状态", "<!--写清树与合并状态-->")
    must_reject(POLICY, release, f"<!-- {release} -->")
    must_reject(POLICY, release, f"```text\n{release}\n```")
    broken = texts[POLICY].replace(release, "")
    must_reject(POLICY, texts[POLICY], broken + "\n" + release + "\n")
    return rejected


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    try:
        texts = {name: (args.root / name).read_bytes().decode("utf-8") for name in (POLICY, *ROUTES)}
        validate(texts)
        count = self_test(texts) if args.self_test else 0
    except (OSError, ValueError) as error:
        print(f"Policy check failed: {error}")
        return 1
    print(f"Policy check passed: {len(texts)} documents, {count} rejected negative fixtures; no app or publish.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
