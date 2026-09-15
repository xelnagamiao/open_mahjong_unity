"""Unity 模拟客户端白名单：只暴露 Unity 会画成按钮/可点牌的动作。"""
from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path
from typing import Any, Dict, Iterable, List, Set

PROTOCOL_PATH = Path(__file__).resolve().parent / "sim_client" / "protocol.json"


@lru_cache(maxsize=1)
def load_protocol() -> Dict[str, Any]:
    return json.loads(PROTOCOL_PATH.read_text(encoding="utf-8"))


def hand_action_allowlist() -> Set[str]:
    proto = load_protocol()
    return set(proto["hand_actions"]) | set(proto.get("extra_actions") or [])


def other_action_allowlist() -> Set[str]:
    proto = load_protocol()
    return set(proto["other_actions"]) | set(proto.get("extra_actions") or [])


def filter_client_actions(server_actions: Iterable[str], *, kind: str) -> List[str]:
    allow = hand_action_allowlist() if kind == "hand" else other_action_allowlist()
    seen = set()
    out: List[str] = []
    for action in server_actions:
        if action not in allow or action in seen:
            continue
        seen.add(action)
        out.append(action)
    return out
