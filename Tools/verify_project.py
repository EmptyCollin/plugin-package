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

    declared = {name for name, _ in packages}
    for name in deps:
        if name.startswith("file:") or name in declared:
            continue
        local = os.path.join(PKG_DIR, name)
        if os.path.isdir(local):
            add("FAIL", "manifest-sync",
                "%s 目录存在但未在 manifest 声明（两份真源漂移）" % name)
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


def main():
    as_json = "--json" in sys.argv[1:]
    check_unity_version()
    packages = unity_packages()
    check_manifest(packages)
    check_package_layout(packages)
    check_assets_no_package()
    check_locks()
    check_hygiene()

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
