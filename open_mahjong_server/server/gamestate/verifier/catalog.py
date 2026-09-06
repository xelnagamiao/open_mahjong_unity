"""场景收集：按规则/文件树化，pytest docstring 作中文注释。"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

from .scenarios import list_scenarios
from .tactical_bridge import run_bridge

_OM_SERVER = Path(__file__).resolve().parents[3]
_SERVER_ROOT = _OM_SERVER / "server"
_GAMESTATE = _SERVER_ROOT / "gamestate"
_PYTEST_INI = _SERVER_ROOT / "pytest.ini"
_CATALOG_CACHE: Optional[Dict[str, Any]] = None

RULE_ORDER = [
    "guobiao_debug",
    "guobiao_script",
    "guobiao_tactical",
    "guobiao",
    "hongque",
    "changsha",
    "taiwan",
    "riichi",
    "sichuan",
    "public",
    "verifier",
    "other",
]

RULE_TITLES = {
    "guobiao_debug": "国标 · 牌例",
    "guobiao_script": "国标 · 脚本",
    "guobiao_tactical": "国标 · 战鸣",
    "guobiao": "国标",
    "hongque": "虹雀",
    "changsha": "长沙",
    "taiwan": "台湾",
    "riichi": "立直",
    "sichuan": "四川",
    "public": "公共",
    "verifier": "测试台",
    "other": "其它",
}

GUOBIAO_PUBLIC_FILES = (
    "test_player_index_xunmu",
    "test_opening_flower_draw_slot",
    "test_hand_draw_source",
)

BRIDGE_CASES = [
    {
        "id": "tactical:presubmit:peng",
        "kind": "tactical",
        "title": "战鸣：预提交碰抢吃",
        "comment": "A 已广播吃后，B 预提交碰时最终动作不能继承吃的跳过标记。",
        "rule": "guobiao_tactical",
        "file": "gamestate/public/test_tactical_claim.py",
        "file_doc": "战术鸣牌窗口：预提交与抢断。",
        "case_id": "presubmit",
        "params": {"replacement_action": "peng"},
        "nodeid": "gamestate/public/test_tactical_claim.py::test_pre_submitted_higher_action_gets_its_own_claim_broadcast[peng]",
    },
    {
        "id": "tactical:presubmit:gang",
        "kind": "tactical",
        "title": "战鸣：预提交杠抢吃",
        "comment": "A 已广播吃后，B 预提交杠时最终动作不能继承吃的跳过标记。",
        "rule": "guobiao_tactical",
        "file": "gamestate/public/test_tactical_claim.py",
        "file_doc": "战术鸣牌窗口：预提交与抢断。",
        "case_id": "presubmit",
        "params": {"replacement_action": "gang"},
        "nodeid": "gamestate/public/test_tactical_claim.py::test_pre_submitted_higher_action_gets_its_own_claim_broadcast[gang]",
    },
    {
        "id": "tactical:presubmit:hu",
        "kind": "tactical",
        "title": "战鸣：预提交和抢吃",
        "comment": "A 已广播吃后，B 预提交和时最终动作不能继承吃的跳过标记。",
        "rule": "guobiao_tactical",
        "file": "gamestate/public/test_tactical_claim.py",
        "file_doc": "战术鸣牌窗口：预提交与抢断。",
        "case_id": "presubmit",
        "params": {"replacement_action": "hu"},
        "nodeid": "gamestate/public/test_tactical_claim.py::test_pre_submitted_higher_action_gets_its_own_claim_broadcast[hu]",
    },
    {
        "id": "tactical:switch:on",
        "kind": "tactical",
        "title": "战鸣：开关开时广播和申请",
        "comment": "鸣牌保护开关不应改变战术申请是否广播；两者正交。",
        "rule": "guobiao_tactical",
        "file": "gamestate/public/test_tactical_claim.py",
        "file_doc": "战术鸣牌窗口：预提交与抢断。",
        "case_id": "tactical_switch",
        "params": {"tactical_call": True, "claim_protection": False},
        "nodeid": "gamestate/public/test_tactical_claim.py::test_tactical_and_claim_protection_switches[True-False]",
    },
]

SCRIPTS = [
    {
        "id": "script:qi_dui_cut",
        "kind": "script",
        "title": "脚本：七对听牌切 9 万后 bot 打完",
        "comment": "东亲七对听 1 万，先切手牌 9 万，其余座位 turbo bot 打完。",
        "rule": "guobiao_script",
        "file": "script:qi_dui_cut",
        "file_doc": "带动作脚本的国标对局。",
        "scenario_id": "qi_dui_tenpai_1m",
        "script": [{"player_index": 0, "action_type": "cut", "tile_id": 19}],
        "autoplay": True,
    },
    {
        "id": "script:random_turbo",
        "kind": "script",
        "title": "脚本：随机种子 turbo bot 打完一局",
        "comment": "四席启发式立刻入队，直到终局或步数上限。",
        "rule": "guobiao_script",
        "file": "script:random_turbo",
        "file_doc": "带动作脚本的国标对局。",
        "scenario_id": "random",
        "script": [],
        "autoplay": True,
    },
]


def infer_rule(nodeid: str) -> str:
    path = (nodeid or "").replace("\\", "/")
    if "/game_hongque/" in f"/{path}" or path.startswith("gamestate/game_hongque/"):
        return "hongque"
    if "/game_changsha/" in f"/{path}" or path.startswith("gamestate/game_changsha/"):
        return "changsha"
    if "/game_taiwan/" in f"/{path}" or path.startswith("gamestate/game_taiwan/"):
        return "taiwan"
    if "/game_riichi/" in f"/{path}" or path.startswith("gamestate/game_riichi/"):
        return "riichi"
    if "/game_sichuan/" in f"/{path}" or path.startswith("gamestate/game_sichuan/"):
        return "sichuan"
    if path.startswith("gamestate/verifier/") or "/verifier/" in f"/{path}":
        return "verifier"
    name = path.split("/")[-1].split("::")[0]
    if "test_tactical_claim" in name:
        return "guobiao_tactical"
    if "test_guobiao" in name or any(token in name for token in GUOBIAO_PUBLIC_FILES):
        return "guobiao"
    if path.startswith("gamestate/public/"):
        return "public"
    return "other"


def _pytest_python() -> Optional[str]:
    try:
        import pytest  # noqa: F401
        return sys.executable
    except ImportError:
        pass
    candidate = shutil.which("python")
    if not candidate:
        return None
    probe = subprocess.run(
        [candidate, "-c", "import pytest"],
        capture_output=True,
        text=True,
    )
    if probe.returncode != 0:
        return None
    return candidate


def _pytest_common_args() -> List[str]:
    return [
        "-m",
        "pytest",
        "-c",
        str(_PYTEST_INI),
        "-p",
        "no:cacheprovider",
        "--rootdir",
        str(_SERVER_ROOT),
    ]


def collect_pytest() -> List[Dict[str, Any]]:
    python = _pytest_python()
    if python is None:
        return []
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as handle:
        out_path = handle.name
    env = os.environ.copy()
    env["LAB_COLLECT_JSON"] = out_path
    # 插件按模块名加载，避免 import gamestate.verifier 时拉起 session/fastapi。
    env["PYTHONPATH"] = os.pathsep.join(
        [str(Path(__file__).resolve().parent), env.get("PYTHONPATH") or ""]
    )
    try:
        subprocess.run(
            [
                python,
                *_pytest_common_args(),
                "-p",
                "collect_plugin",
                "--collect-only",
                "-q",
                str(_GAMESTATE),
            ],
            capture_output=True,
            text=True,
            cwd=str(_SERVER_ROOT),
            env=env,
        )
        raw = Path(out_path).read_text(encoding="utf-8") if Path(out_path).is_file() else "[]"
        rows = json.loads(raw or "[]")
    except (OSError, json.JSONDecodeError):
        rows = []
    finally:
        try:
            os.unlink(out_path)
        except OSError:
            pass
    if not rows:
        proc = subprocess.run(
            [python, *_pytest_common_args(), "--collect-only", "-q", str(_GAMESTATE)],
            capture_output=True,
            text=True,
            cwd=str(_SERVER_ROOT),
            env=env,
        )
        for line in (proc.stdout or "").splitlines():
            line = line.strip()
            if "::" not in line or line.startswith("="):
                continue
            nodeid = line.split(" ", 1)[0]
            rows.append({"nodeid": nodeid, "doc": "", "file_doc": "", "func": nodeid.split("::")[-1]})
    items = []
    for row in rows:
        nodeid = row.get("nodeid") or ""
        if "::" not in nodeid:
            continue
        doc = (row.get("doc") or "").strip()
        file_doc = (row.get("file_doc") or "").strip()
        rel = nodeid.split("::")[0]
        items.append(
            {
                "id": f"pytest:{nodeid}",
                "kind": "pytest",
                "title": doc or nodeid.split("::")[-1],
                "comment": doc or file_doc or nodeid,
                "rule": infer_rule(nodeid),
                "file": rel,
                "file_doc": file_doc,
                "nodeid": nodeid,
                "func": row.get("func") or nodeid.split("::")[-1],
            }
        )
    return items


def _file_key(item: Dict[str, Any]) -> str:
    return item.get("file") or item.get("nodeid") or item.get("id") or ""


def _build_tree(items: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
    buckets: Dict[str, Dict[str, Dict[str, Any]]] = {}
    for item in items:
        rule = item.get("rule") or "other"
        file_id = _file_key(item)
        rule_files = buckets.setdefault(rule, {})
        folder = rule_files.setdefault(
            file_id,
            {
                "file": file_id,
                "file_doc": item.get("file_doc") or "",
                "items": [],
            },
        )
        if item.get("file_doc") and not folder.get("file_doc"):
            folder["file_doc"] = item["file_doc"]
        folder["items"].append(item)
    groups = []
    for rule in RULE_ORDER:
        files_map = buckets.pop(rule, None)
        if not files_map:
            continue
        files = list(files_map.values())
        count = sum(len(entry["items"]) for entry in files)
        groups.append(
            {
                "id": rule,
                "rule": rule,
                "title": RULE_TITLES.get(rule, rule),
                "count": count,
                "files": files,
            }
        )
    for rule, files_map in buckets.items():
        files = list(files_map.values())
        groups.append(
            {
                "id": rule,
                "rule": rule,
                "title": RULE_TITLES.get(rule, rule),
                "count": sum(len(entry["items"]) for entry in files),
                "files": files,
            }
        )
    return groups


def iter_items(catalog: Optional[Dict[str, Any]] = None) -> Iterable[Dict[str, Any]]:
    data = catalog if catalog is not None else list_catalog()
    for group in data.get("groups") or []:
        for folder in group.get("files") or []:
            yield from folder.get("items") or []


def list_catalog(*, refresh: bool = False) -> Dict[str, Any]:
    global _CATALOG_CACHE
    if _CATALOG_CACHE is not None and not refresh:
        return _CATALOG_CACHE
    debug = []
    for scenario in list_scenarios():
        debug.append(
            {
                "id": f"debug:{scenario.get('id')}",
                "kind": "debug",
                "title": scenario.get("title") or scenario.get("id"),
                "comment": scenario.get("title") or scenario.get("id"),
                "rule": "guobiao_debug",
                "file": f"scenarios/{scenario.get('id')}.json",
                "file_doc": "guobiao_debug 牌例，turbo 自动打完。",
                "scenario_id": scenario.get("id"),
                "autoplay": True,
            }
        )
    items: List[Dict[str, Any]] = []
    items.extend(debug)
    items.extend(SCRIPTS)
    items.extend(BRIDGE_CASES)
    items.extend(collect_pytest())
    groups = _build_tree(items)
    result = {
        "groups": groups,
        "counts": {group["id"]: group["count"] for group in groups},
    }
    _CATALOG_CACHE = result
    return result


def find_item(item_id: str) -> Dict[str, Any]:
    for item in BRIDGE_CASES + SCRIPTS:
        if item.get("id") == item_id:
            return item
    for scenario in list_scenarios():
        sid = f"debug:{scenario.get('id')}"
        if sid == item_id:
            return {
                "id": sid,
                "kind": "debug",
                "title": scenario.get("title") or scenario.get("id"),
                "comment": scenario.get("title") or scenario.get("id"),
                "rule": "guobiao_debug",
                "scenario_id": scenario.get("id"),
                "autoplay": True,
            }
    for item in iter_items():
        if item.get("id") == item_id:
            return item
    raise KeyError(f"未知场景: {item_id}")


def run_pytest_node(nodeid: str) -> Dict[str, Any]:
    python = _pytest_python()
    if python is None:
        return {
            "ok": None,
            "skipped": True,
            "exitcode": None,
            "reports": [],
            "failed": [],
            "stdout": "当前 Python 没有 pytest，已跳过 nodeid 执行（UnitySim 轨迹仍可用）",
        }
    proc = subprocess.run(
        [python, *_pytest_common_args(), "-q", "--tb=short", nodeid],
        capture_output=True,
        text=True,
        cwd=str(_SERVER_ROOT),
    )
    failed = []
    if proc.returncode != 0:
        failed.append(
            {
                "nodeid": nodeid,
                "outcome": "failed",
                "longrepr": (proc.stdout or "") + (proc.stderr or ""),
            }
        )
    return {
        "ok": proc.returncode == 0,
        "skipped": False,
        "exitcode": int(proc.returncode),
        "reports": [],
        "failed": failed,
        "stdout": proc.stdout,
    }


def match_tactical_item(nodeid: str) -> Optional[Dict[str, Any]]:
    for item in BRIDGE_CASES:
        if item.get("nodeid") == nodeid:
            return item
    return None


def run_tactical_item(item: Dict[str, Any]) -> Dict[str, Any]:
    frames = run_bridge(item["case_id"], item.get("params") or {})
    pytest_out = None
    if item.get("nodeid"):
        pytest_out = run_pytest_node(item["nodeid"])
    pytest_ok = pytest_out is None or pytest_out.get("skipped") or pytest_out.get("ok")
    return {
        "ok": bool(frames.get("ok")) and bool(pytest_ok),
        "kind": "tactical",
        "item": item,
        "pytest": pytest_out,
        "trace": frames,
    }
