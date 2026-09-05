"""读取 verifier/scenarios/*.json。"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List

SCENARIO_DIR = Path(__file__).resolve().parent / "scenarios"


def list_scenarios() -> List[Dict[str, Any]]:
    items = []
    for path in sorted(SCENARIO_DIR.glob("*.json")):
        data = json.loads(path.read_text(encoding="utf-8"))
        data["file"] = path.name
        items.append(data)
    return items


def get_scenario(scenario_id: str) -> Dict[str, Any]:
    for item in list_scenarios():
        if item.get("id") == scenario_id:
            return item
    raise KeyError(f"未知场景: {scenario_id}")
