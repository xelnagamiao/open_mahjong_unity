"""核对 Unity 协议面文件哈希 + 动作白名单，防止模拟客户端漏验。"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List

from .protocol import PROTOCOL_PATH, load_protocol

LOCK_PATH = Path(__file__).resolve().parent / "unity_contract.lock.json"
HAND_RE = re.compile(
    r"AllowHandActionCheck\s*=\s*new string\[\]\s*\{([^}]+)\}",
    re.S,
)
OTHER_RE = re.compile(
    r"allowOtherActionCheck\s*=\s*new string\[\]\s*\{([^}]+)\}",
    re.S,
)
STRING_RE = re.compile(r'"([^"]+)"')


def repo_root() -> Path:
    return Path(__file__).resolve().parents[4]


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    digest.update(path.read_bytes())
    return digest.hexdigest()


def git_head(root: Path) -> str:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=str(root),
            check=True,
            capture_output=True,
            text=True,
        )
        return (result.stdout or "").strip()
    except (OSError, subprocess.CalledProcessError):
        return ""


def parse_csharp_string_array(blob: str) -> List[str]:
    return STRING_RE.findall(blob)


def extract_unity_allowlists(actions_cs: Path) -> Dict[str, List[str]]:
    text = actions_cs.read_text(encoding="utf-8")
    hand = HAND_RE.search(text)
    other = OTHER_RE.search(text)
    if not hand or not other:
        raise SystemExit(f"无法从 {actions_cs} 抽出 AllowHandActionCheck / allowOtherActionCheck")
    return {
        "hand_actions": parse_csharp_string_array(hand.group(1)),
        "other_actions": parse_csharp_string_array(other.group(1)),
    }


def build_lock() -> Dict[str, Any]:
    root = repo_root()
    proto = load_protocol()
    files = {}
    for rel in proto["unity_contract_files"]:
        path = root / rel
        if not path.is_file():
            raise SystemExit(f"缺少 Unity 契约文件: {rel}")
        files[rel] = {
            "sha256": sha256_file(path),
            "size": path.stat().st_size,
        }
    actions_rel = "open_mahjong_unity/Assets/Scripts/GameScene/GameStateManager/NormalGameStateManager.Actions.cs"
    allow = extract_unity_allowlists(root / actions_rel)
    return {
        "git_head": git_head(root),
        "sim_client_version": proto.get("sim_client_version"),
        "files": files,
        "unity_hand_actions": allow["hand_actions"],
        "unity_other_actions": allow["other_actions"],
        "protocol_hand_actions": list(proto["hand_actions"]),
        "protocol_other_actions": list(proto["other_actions"]),
    }


def _same_list(left: List[str], right: List[str]) -> bool:
    return list(left) == list(right)


def check_lock(*, update: bool) -> int:
    proto = load_protocol()
    current = build_lock()
    if update:
        LOCK_PATH.write_text(json.dumps(current, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"已写入 {LOCK_PATH}")
        return 0
    if not LOCK_PATH.is_file():
        print(f"缺少 {LOCK_PATH}，请先运行: python -m server.gamestate.verifier.check_contract --update-lock")
        return 2
    locked = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
    errors: List[str] = []
    if current["sim_client_version"] != locked.get("sim_client_version"):
        errors.append(
            f"sim_client_version {current['sim_client_version']} != lock {locked.get('sim_client_version')}"
        )
    for rel, meta in current["files"].items():
        old = (locked.get("files") or {}).get(rel) or {}
        if meta["sha256"] != old.get("sha256"):
            errors.append(f"Unity 文件已变: {rel}")
    if not _same_list(current["unity_hand_actions"], proto["hand_actions"]):
        errors.append(
            "AllowHandActionCheck 与 sim_client/protocol.json hand_actions 不一致: "
            f"unity={current['unity_hand_actions']} protocol={proto['hand_actions']}"
        )
    if not _same_list(current["unity_other_actions"], proto["other_actions"]):
        errors.append(
            "allowOtherActionCheck 与 sim_client/protocol.json other_actions 不一致: "
            f"unity={current['unity_other_actions']} protocol={proto['other_actions']}"
        )
    if not _same_list(current["unity_hand_actions"], locked.get("unity_hand_actions") or []):
        errors.append("手牌白名单与 lock 不一致")
    if not _same_list(current["unity_other_actions"], locked.get("unity_other_actions") or []):
        errors.append("鸣牌白名单与 lock 不一致")
    if errors:
        print("验证器契约检查失败：")
        for item in errors:
            print(f"  - {item}")
        print("先改模拟器 protocol.json 对齐 Unity，再运行:")
        print("  python -m server.gamestate.verifier.check_contract --update-lock")
        print(f"当前 git HEAD（备注）: {current['git_head']}")
        print(f"lock git HEAD（备注）: {locked.get('git_head')}")
        return 1
    print(
        f"契约一致 sim_client_version={current['sim_client_version']} "
        f"git_head={current['git_head'] or '(unknown)'}"
    )
    return 0


def main(argv: List[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="检查 Unity 客户端与验证器模拟客户端是否同版本")
    parser.add_argument("--update-lock", action="store_true", help="按当前 Unity 文件重写 lock")
    args = parser.parse_args(argv)
    return check_lock(update=args.update_lock)


if __name__ == "__main__":
    sys.exit(main())
