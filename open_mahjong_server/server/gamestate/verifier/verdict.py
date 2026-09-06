"""把一次 lab 运行收成通过/失败 + 问题列表。"""
from __future__ import annotations

from typing import Any, Dict, Iterable, List, Optional

from .protocol import load_protocol


def _allowlist() -> set:
    proto = load_protocol()
    return set(proto["hand_actions"]) | set(proto["other_actions"]) | set(proto.get("extra_actions") or [])


def _pytest_problems(pytest_out: Optional[Dict[str, Any]]) -> List[Dict[str, Any]]:
    if not pytest_out or pytest_out.get("skipped"):
        return []
    if pytest_out.get("ok") is False:
        failed = (pytest_out.get("failed") or [{}])[0]
        text = str(failed.get("longrepr") or pytest_out.get("stdout") or "pytest 失败")
        return [{"where": "pytest", "message": text.strip()[:4000], "frame": None}]
    return []


def _unity_problems(
    *,
    kind: Optional[str],
    trace_meta: List[Dict[str, Any]],
    frames: Optional[Iterable[Dict[str, Any]]],
) -> List[Dict[str, Any]]:
    needs_trace = kind in ("debug", "script", "tactical")
    meta = list(trace_meta or [])
    if not needs_trace and not meta:
        return []
    problems: List[Dict[str, Any]] = []
    if not meta:
        problems.append({"where": "unity", "message": "轨迹为空，没有 Unity 组件快照", "frame": None})
        return problems
    if not any((item.get("type") or "").endswith("game_start") for item in meta):
        problems.append({"where": "unity", "message": "轨迹里没有 game_start", "frame": 0})
    allow = _allowlist()
    for frame in frames or []:
        comps = frame.get("components") or {}
        gsm = comps.get("GsmSim") or {}
        asked = bool(gsm.get("IsSelfActionRequired") or gsm.get("allowActionList"))
        if not asked:
            continue
        for button in (comps.get("ActionButtonSim") or {}).get("buttons") or []:
            for action in button.get("actions") or []:
                if action not in allow:
                    problems.append(
                        {
                            "where": "unity",
                            "message": f"ActionButtonSim 含非白名单动作 {action}",
                            "frame": frame.get("index"),
                        }
                    )
    return problems


def build_verdict(
    *,
    kind: Optional[str] = None,
    trace_meta: Optional[List[Dict[str, Any]]] = None,
    frames: Optional[Iterable[Dict[str, Any]]] = None,
    loop_error: Optional[str] = None,
    pytest: Optional[Dict[str, Any]] = None,
    http_error: Optional[str] = None,
) -> Dict[str, Any]:
    problems: List[Dict[str, Any]] = []
    if http_error:
        problems.append({"where": "http", "message": str(http_error), "frame": None})
    if loop_error:
        problems.append({"where": "loop", "message": str(loop_error), "frame": None})
    problems.extend(_pytest_problems(pytest))
    problems.extend(
        _unity_problems(kind=kind, trace_meta=list(trace_meta or []), frames=frames)
    )
    ok = not problems
    bits = []
    if pytest and pytest.get("skipped"):
        bits.append("未跑 pytest")
    elif pytest and pytest.get("ok"):
        bits.append("pytest 通过")
    n = len(trace_meta or [])
    if n:
        bits.append(f"轨迹 {n} 帧")
    if loop_error:
        bits.append("循环异常")
    summary = "通过" if ok else "失败"
    detail = " · ".join(bits) if bits else ("无问题" if ok else "有问题")
    return {
        "ok": ok,
        "verdict": {
            "summary": summary,
            "detail": f"{summary} · {detail}" if bits or not ok else summary,
            "problems": problems,
        },
    }
