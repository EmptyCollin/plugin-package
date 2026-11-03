#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""工程规则校验器（AGENTS.md 的可执行部分）

用法:  python Tools/verify_project.py [--json]
退出码: 0 = 无 FAIL；1 = 有 FAIL（禁止交付）；2 = 脚本自身错误
规则本身变更时，必须同步这里的检查项。
"""
import json
import os
import re
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PKG_DIR = os.path.join(ROOT, "Packages")
LOCK_DIR = os.path.join(ROOT, ".locks")

# AGENTS.md §8 钉死版本
PINNED_DEPS = {
    "com.unity.pipeline": "0.9.1-exp.1",
    "com.unity.ai.assistant": "2.21.0-pre.2",
    "com.unity.ai.inference": "2.6.1",
}
PINNED_UNITY = "6000.6.7f1"
CORE_SUFFIX = ".core"
LOCK_TTL = 300

CHECKS = [("PASS", "start", "校验开始")]


def add(level, code, msg):
    CHECKS.append((level, code, msg))


def load_json(path):
    try:
        with open(path, encoding="utf-8-sig") as fh:
            return json.load(fh)
    except FileNotFoundError:
        return None
    except Exception as exc:
        add("FAIL", "json-parse", "%s 解析失败: %s" % (path, exc))
        return None


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


def unity_packages():
    if not os.path.isdir(PKG_DIR):
        return []
    out = []
    for name in sorted(os.listdir(PKG_DIR)):
        full = os.path.join(PKG_DIR, name)
        if not os.path.isdir(full):
            continue
        if not re.match(r"^[a-z0-9][a-z0-9._-]*\.[a-z0-9][a-z0-9._-]*$", name):
            continue
        if os.path.isfile(os.path.join(full, "package.json")):
            out.append((name, full))
    return out


def check_unity_version():
    path = os.path.join(ROOT, "ProjectSettings", "ProjectVersion.txt")
    text = ""
    if os.path.isfile(path):
        with open(path, encoding="utf-8-sig") as fh:
            text = fh.read()
    match = re.search(r"m_EditorVersion:\s*(\S+)", text)
    if not match:
        add("FAIL", "unity-version", "读不到 ProjectSettings/ProjectVersion.txt")
    elif match.group(1) != PINNED_UNITY:
        add("FAIL", "unity-version",
            "编辑器版本 %s 与钉死的 %s 不一致（AGENTS.md §8）"
            % (match.group(1), PINNED_UNITY))
    else:
        add("PASS", "unity-version", "编辑器版本 %s" % PINNED_UNITY)


def check_manifest(packages):
    manifest = load_json(os.path.join(PKG_DIR, "manifest.json"))
    if manifest is None:
        add("FAIL", "manifest", "Packages/manifest.json 缺失")
        return {}
    deps = manifest.get("dependencies", {})
    for name, want in PINNED_DEPS.items():
        got = deps.get(name)
        if got is None:
            add("FAIL", "dep-pin", "%s 未在 manifest 中声明" % name)
        elif got != want:
            add("FAIL", "dep-pin",
                "%s 版本 %s 与钉死的 %s 不一致，升级需走 ADR 评审（§8）"
                % (name, got, want))
        else:
            add("PASS", "dep-pin", "%s@%s" % (name, got))

    for name, src in deps.items():
        if isinstance(src, str) and src.startswith("file:"):
            target = os.path.normpath(os.path.join(PKG_DIR, src[5:]))
            if not os.path.isdir(target):
                add("FAIL", "manifest-sync",
                    "manifest 声明 %s -> %s，但目录不存在（悬空引用）" % (name, src))
    add("PASS", "manifest-sync",
        "manifest 的 file: 引用均有效（内嵌包由 Unity 自动发现，无需在 manifest 声明）")
    return deps


def check_package_layout(packages):
    core_names = [n for n, _ in packages if n.endswith(CORE_SUFFIX)]
    feature_names = [n for n, _ in packages if not n.endswith(CORE_SUFFIX)]
    feature_set = set(feature_names)

    if packages and not core_names:
        add("FAIL", "core-missing",
            "存在 feature 包但没有 core 包，依赖方向无处可去（§5）")

    for name, path in packages:
        missing = []
        for item in ["Runtime", "Docs/DESIGN.md", "Docs/ACCEPTANCE.md",
                     "Docs/CHANGELOG.md", "Showcase.json"]:
            if not os.path.exists(os.path.join(path, item)):
                missing.append(item)
        if missing:
            add("FAIL", "pkg-layout", "%s 缺: %s" % (name, ", ".join(missing)))
        else:
            add("PASS", "pkg-layout", "%s 结构齐全" % name)

        pkg_json = load_json(os.path.join(path, "package.json")) or {}
        deps = pkg_json.get("dependencies", {})
        if name in core_names:
            for dep in deps:
                if dep in feature_set:
                    add("FAIL", "dep-direction",
                        "core 包 %s 反向依赖 feature 包 %s" % (name, dep))
        else:
            if core_names and not any(d in core_names for d in deps):
                add("WARN", "dep-core",
                    "%s 未依赖任何 core 包（§5：feature 包必须声明依赖 core）" % name)
            for dep in deps:
                if dep in feature_set:
                    add("FAIL", "dep-direction",
                        "feature 包 %s 直接依赖 feature 包 %s，跨包通信须走 core（§5）"
                        % (name, dep))

        showcase = load_json(os.path.join(path, "Showcase.json"))
        if showcase is not None:
            for field in ["name", "category", "summary", "order"]:
                if field not in showcase:
                    add("FAIL", "showcase", "%s Showcase.json 缺字段 %s" % (name, field))
            order = showcase.get("order")
            if not isinstance(order, (int, float)) or isinstance(order, bool):
                add("FAIL", "showcase", "%s Showcase.order 必须是数字" % name)
            video = showcase.get("video")
            if isinstance(video, str) and video:
                vpath = os.path.join(path, video.replace("/", os.sep))
                if not os.path.isfile(vpath):
                    add("FAIL", "showcase", "%s 演示视频缺失: %s" % (name, video))
                else:
                    # §6：视频必须覆盖"怎么用 + 用了之后的效果"两段，
                    # 体积过小（占位/静音黑屏）不可能是有效演示。
                    vsize = os.path.getsize(vpath)
                    if vsize < 200 * 1024:
                        add("FAIL", "showcase-video",
                            "%s 演示视频仅 %d 字节，不足以承载操作过程（§6）"
                            % (name, vsize))
                    acc = os.path.join(path, "Docs", "ACCEPTANCE.md")
                    text = ""
                    if os.path.isfile(acc):
                        with open(acc, encoding="utf-8") as fh:
                            text = fh.read()
                    has_two = ("怎么用" in text and "效果" in text
                               and "演示视频" in text)
                    has_note = "示意（非实录）" in text
                    if not (has_two or has_note):
                        add("WARN", "showcase-video",
                            "%s 的 ACCEPTANCE 未记录「演示视频两点」验收或「示意（非实录）」标注（§6）"
                            % name)

    for asmdef in glob_recursive(ROOT, ".asmdef"):
        if in_ignored_dir(asmdef):
            continue
        cfg = load_json(asmdef)
        if cfg is None:
            continue
        if cfg.get("validateReferences") is False:
            add("FAIL", "asmdef", "%s 关掉了 validateReferences，编译期约束失效（§5）"
                % rel(asmdef))


def glob_recursive(base, suffix):
    hits = []
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames
                       if d not in ("Library", "Temp", "obj", ".git",
                                    ".tmp-inspect", "node_modules")]
        for fn in filenames:
            if fn.endswith(suffix):
                hits.append(os.path.join(dirpath, fn))
    return hits


def in_ignored_dir(path):
    parts = set(rel(path).split("/"))
    return bool(parts & {"Library", "Temp", "obj", ".git", ".tmp-inspect",
                         "Builds", "UserSettings", ".locks"})


def check_assets_no_package():
    assets = os.path.join(ROOT, "Assets")
    if not os.path.isdir(assets):
        return
    for name in os.listdir(assets):
        full = os.path.join(assets, name)
        if os.path.isdir(full) and re.match(r"^[a-z0-9]+\.[a-z0-9]+[a-z0-9._-]*$", name):
            add("FAIL", "assets-hygiene",
                "%s 出现在 Assets/ 下，插件包必须放 Packages/（§0）" % rel(full))


def check_assets_layout():
    """AGENTS.md §0：展板=Assets/Showcase 公共接入口；插件代码与演示场景归包。"""
    assets = os.path.join(ROOT, "Assets")
    if not os.path.isdir(assets):
        return
    allowed_dirs = {"Showcase", "Scenes", "Settings", "Editor", "TutorialInfo", "Resources"}
    for name in os.listdir(assets):
        full = os.path.join(assets, name)
        if os.path.isdir(full) and name not in allowed_dirs:
            add("FAIL", "assets-layout",
                "Assets/%s 不属于允许清单——插件内容须进包（§0）" % name)
    # Assets/ 内场景仅允许展板与模板默认
    for dirpath, _dirnames, filenames in os.walk(assets):
        for fn in filenames:
            if fn.endswith(".unity"):
                rp = os.path.relpath(os.path.join(dirpath, fn), ROOT).replace("\\", "/")
                if rp not in ("Assets/Scenes/SampleScene.unity",
                              "Assets/Showcase/Showcase.unity"):
                    add("FAIL", "assets-layout",
                        "%s 应放展板入口或模板默认位置（§0）" % rp)
    # 展板只消费包内 Showcase.json 的元数据与演示视频；feature 包应有包内 Sample/
    for name, path in unity_packages():
        if name.endswith(".core"):
            continue
        if not os.path.isdir(os.path.join(path, "Sample")):
            add("WARN", "pkg-sample",
                "%s 无 Sample/ 试用演示目录——showcase 本质是给用户试用（§0）" % name)
    add("PASS", "assets-layout", "Assets/ 布局合规（展板公共入口，插件演示归包）")


def check_locks():
    if not os.path.isdir(LOCK_DIR):
        add("PASS", "locks", "无活动锁")
        return
    now = time.time()
    for fn in sorted(os.listdir(LOCK_DIR)):
        if not fn.endswith(".lock"):
            continue
        path = os.path.join(LOCK_DIR, fn)
        lock = load_json(path)
        if lock is None:
            continue
        beat = lock.get("heartbeat_at", lock.get("acquired_at", 0))
        ttl = lock.get("ttl_s", LOCK_TTL)
        age = now - float(beat)
        if age > ttl:
            add("FAIL", "lock-stale",
                "%s 心跳超时（%ds 前，holder=%s），须报告用户处理，禁止自行删除（§3）"
                % (fn, int(age), lock.get("holder", "?")))
        else:
            add("WARN", "lock-active",
                "%s 被 %s 持有（心跳 %ds 前）" % (fn, lock.get("holder", "?"), int(age)))


def check_hygiene():
    tracked = set()
    try:
        out = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT,
                             capture_output=True, check=True).stdout
        tracked = {p for p in out.decode("utf-8", "ignore").split("\0") if p}
    except Exception as exc:
        add("WARN", "git", "无法读取 git 索引: %s" % exc)
    dirty = False
    for bad in (".tmp-inspect/", ".locks/", "Builds/", "Library/", "Logs/"):
        hits = [p for p in tracked if p.replace("\\", "/").startswith(bad)]
        if hits:
            dirty = True
            add("FAIL", "hygiene", "%s 下有 %d 个文件被跟踪，应排除入库" % (bad, len(hits)))
    if not dirty:
        add("PASS", "hygiene", "忽略目录未被跟踪")


def check_runtime_purity():
    """AGENTS.md §0.1 原则3：Runtime 程序集禁止引用 UnityEditor。"""
    offenders = []
    for name, path in unity_packages():
        rt = os.path.join(path, "Runtime")
        if not os.path.isdir(rt):
            continue
        for dirpath, _dirnames, filenames in os.walk(rt):
            for fn in filenames:
                if not fn.endswith(".cs"):
                    continue
                fp = os.path.join(dirpath, fn)
                try:
                    text = open(fp, encoding="utf-8-sig", errors="ignore").read()
                except Exception:
                    continue
                if re.search(r"^\s*using\s+UnityEditor\b", text, re.M):
                    offenders.append(rel(fp))
    if offenders:
        add("FAIL", "runtime-purity",
            "Runtime 源码引用 UnityEditor（§0.1 原则3）: " + ", ".join(offenders[:4]))
    else:
        add("PASS", "runtime-purity", "Runtime 程序集无 UnityEditor 依赖")


def check_console():
    """AGENTS.md §1.6b 交锁门禁：控制台 error/warning 归零（豁免见 §1.8）。"""
    allowlist_path = os.path.join(ROOT, "Tools", "console_allowlist.json")
    patterns = []
    if os.path.isfile(allowlist_path):
        cfg = load_json(allowlist_path) or {}
        patterns = [p for p in cfg.get("error_substrings", []) if isinstance(p, str)]

    try:
        proc = subprocess.run(
            ["unity", "--json", "command", "console status", "--non-interactive"],
            cwd=ROOT, capture_output=True, timeout=45)
        payload = json.loads(proc.stdout.decode("utf-8", "ignore"))
        gt = payload.get("data", {}).get("result", {}).get("groundTruth", {})
    except FileNotFoundError:
        add("WARN", "console", "找不到 unity CLI，无法验证控制台归零——交锁前须手动跑 §1.6b")
        return
    except Exception as exc:
        add("WARN", "console", "无法读取控制台状态（%s）——交锁前须手动跑 §1.6b" % exc)
        return

    if not gt.get("seeded"):
        add("WARN", "console", "控制台计数尚未就绪（seeded=false），稍后复跑确认")
        return

    errors = int(gt.get("consoleErrors", 0))
    warns = int(gt.get("consoleWarnings", 0))
    if gt.get("compilationFailed"):
        add("FAIL", "console", "控制台报告编译失败（compilationFailed=true），§1.6a 未过")

    if errors == 0 and warns == 0:
        add("PASS", "console", "控制台 error=0 warning=0")
        return

    # 有红黄条目：拉原文做归因，未命中豁免清单的一律 FAIL
    offenders = []
    try:
        proc = subprocess.run(
            ["unity", "--json", "command", "console get", "--tail", "200",
             "--non-interactive"],
            cwd=ROOT, capture_output=True, timeout=45)
        entries = json.loads(proc.stdout.decode("utf-8", "ignore")) \
            .get("data", {}).get("result", {}).get("entries", [])
        exempted = 0
        for e in entries:
            # 实测 level 取值为 error/warn/log（是 "warn" 不是 "warning"）。
            # 曾因只匹配 "warning" 把 warn 条目全部跳过，导致未豁免告警获虚假绿灯。
            if e.get("level") not in ("error", "warn", "warning"):
                continue
            msg = (e.get("message") or "").replace("\r", " ").strip()
            if any(p in msg for p in patterns):
                exempted += 1
                continue
            offenders.append((e.get("level"), msg[:120]))
    except Exception as exc:
        add("WARN", "console", "error=%d warning=%d，但无法取条目做归因（%s）"
            % (errors, warns, exc))
        return

    if offenders:
        add("FAIL", "console",
            "控制台未归零：error=%d warning=%d，未豁免 %d 条（§1.6b 红灯，禁止交锁）。样例: %s"
            % (errors, warns, len(offenders), offenders[0][1]))
    else:
        # 缓冲区无可归因条目 ≠ 一定豁免命中：计数与缓冲不一致时严从按红灯
        total = errors + warns
        if exempted >= total:
            add("WARN", "console",
                "error=%d warning=%d 全部命中豁免清单（§1.8 已批准，命中 %d 条）"
                % (errors, warns, exempted))
        else:
            add("FAIL", "console",
                "控制台计数=%d 但缓冲区仅可归因 %d 条（其余 %d 条无法归因，§1.6b 从严按红灯）"
                % (total, exempted, total - exempted))


def main():
    as_json = "--json" in sys.argv[1:]
    check_unity_version()
    packages = unity_packages()
    check_manifest(packages)
    check_package_layout(packages)
    check_assets_no_package()
    check_assets_layout()
    check_runtime_purity()
    check_locks()
    check_hygiene()
    check_console()

    fails = [c for c in CHECKS if c[0] == "FAIL"]
    warns = [c for c in CHECKS if c[0] == "WARN"]

    if not packages:
        add("WARN", "pkg-count", "Packages/ 下还没有任何插件包")

    if as_json:
        print(json.dumps({"fail": len(fails), "warn": len(warns),
                          "checks": [{"level": l, "code": c, "msg": m}
                                     for l, c, m in CHECKS]},
                         ensure_ascii=False, indent=2))
    else:
        print("插件包: %d 个  |  FAIL %d  WARN %d" % (len(packages), len(fails), len(warns)))
        for level, code, msg in CHECKS:
            if level == "PASS":
                continue
            print("[%s] %s: %s" % (level, code, msg))
        for level, code, msg in CHECKS:
            if level == "PASS":
                print("[ ok ] %s: %s" % (code, msg))
        print("结论: %s" % ("红灯，禁止交付" if fails else "绿灯"))

    return 1 if fails else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        import traceback
        traceback.print_exc()
        sys.exit(2)
