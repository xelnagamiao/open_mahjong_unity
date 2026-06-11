"""
对局严重异常日志：独立文件 + JSON 快照。
供切牌拒绝、状态不一致等 CRITICAL 事件使用；日常 INFO 仍走 app.log。
"""
from __future__ import annotations

import json
import logging
import os
from datetime import datetime, timezone
from logging.handlers import RotatingFileHandler
from typing import Any, Dict, List, Optional

CRITICAL_LOGGER_NAME = "mahjong.critical"

_CRITICAL_LOGGER: Optional[logging.Logger] = None
_SNAPSHOT_ROOT: Optional[str] = None

# 与 app.log 相同的轮转策略
CRITICAL_LOG_MAX_BYTES = 512 * 1024
CRITICAL_LOG_BACKUP_COUNT = 99


def setup_critical_logging(log_dir: str) -> logging.Logger:
    """初始化 mahjong.critical logger（不 propagate，仅写入 critical/gamestate.log）。"""
    global _CRITICAL_LOGGER, _SNAPSHOT_ROOT

    critical_dir = os.path.join(log_dir, "critical")
    snapshot_dir = os.path.join(critical_dir, "snapshots")
    os.makedirs(snapshot_dir, exist_ok=True)
    _SNAPSHOT_ROOT = snapshot_dir

    critical_log_path = os.path.join(critical_dir, "gamestate.log")
    logger = logging.getLogger(CRITICAL_LOGGER_NAME)
    logger.setLevel(logging.CRITICAL)
    logger.propagate = False

    if not logger.handlers:
        handler = RotatingFileHandler(
            critical_log_path,
            maxBytes=CRITICAL_LOG_MAX_BYTES,
            backupCount=CRITICAL_LOG_BACKUP_COUNT,
            encoding="utf-8",
        )
        handler.setFormatter(
            logging.Formatter("%(asctime)s - %(levelname)s - %(message)s")
        )
        logger.addHandler(handler)

    _CRITICAL_LOGGER = logger
    return logger


def get_critical_logger() -> logging.Logger:
    if _CRITICAL_LOGGER is None:
        return logging.getLogger(CRITICAL_LOGGER_NAME)
    return _CRITICAL_LOGGER


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def _safe_list(value: Any) -> List[Any]:
    if value is None:
        return []
    if isinstance(value, list):
        return list(value)
    return [value]


def build_gamestate_snapshot(
    gamestate,
    *,
    event: str,
    player_index: Optional[int] = None,
    extra: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """收集当局可序列化快照（四规则通用字段 + getattr 兜底）。"""
    players: List[Dict[str, Any]] = []
    for p in getattr(gamestate, "player_list", []) or []:
        players.append(
            {
                "index": getattr(p, "player_index", None),
                "user_id": getattr(p, "user_id", None),
                "username": getattr(p, "username", None),
                "hand_tiles": _safe_list(getattr(p, "hand_tiles", None)),
                "has_draw_slot": bool(getattr(p, "has_draw_slot", False)),
                "discard_tiles": _safe_list(getattr(p, "discard_tiles", None)),
                "combination_tiles": _safe_list(getattr(p, "combination_tiles", None)),
                "score": getattr(p, "score", None),
            }
        )

    tiles_list = getattr(gamestate, "tiles_list", None)
    snapshot: Dict[str, Any] = {
        "event": event,
        "ts": _utc_now_iso(),
        "gamestate_id": getattr(gamestate, "gamestate_id", None),
        "rule": _infer_rule(gamestate),
        "game_status": getattr(gamestate, "game_status", None),
        "current_player_index": getattr(gamestate, "current_player_index", None),
        "round_index": _infer_round_index(gamestate),
        "xunmu": getattr(gamestate, "xunmu", None),
        "action_player_index": player_index,
        "tiles_remaining": len(tiles_list) if tiles_list is not None else None,
        "players": players,
    }
    if extra:
        snapshot["extra"] = extra
    return snapshot


def _infer_rule(gamestate) -> Optional[str]:
    record = getattr(gamestate, "game_record", None) or {}
    title = record.get("game_title") if isinstance(record, dict) else None
    if isinstance(title, dict):
        return title.get("rule")
    cls = type(gamestate).__name__
    if "Riichi" in cls:
        return "riichi"
    if "Guobiao" in cls:
        return "guobiao"
    if "Classical" in cls:
        return "classical"
    if "Qingque" in cls:
        return "qingque"
    return cls


def _infer_round_index(gamestate) -> Optional[int]:
    if hasattr(gamestate, "round_index"):
        return getattr(gamestate, "round_index")
    if hasattr(gamestate, "current_round"):
        return getattr(gamestate, "current_round")
    return None


def _write_snapshot_file(snapshot: Dict[str, Any]) -> Optional[str]:
    if _SNAPSHOT_ROOT is None:
        return None
    game_id = snapshot.get("gamestate_id") or "unknown"
    safe_id = "".join(c if c.isalnum() or c in "-_" else "_" for c in str(game_id))
    event = snapshot.get("event") or "event"
    ts_slug = (snapshot.get("ts") or "").replace(":", "").replace(".", "")
    dir_path = os.path.join(_SNAPSHOT_ROOT, safe_id)
    os.makedirs(dir_path, exist_ok=True)
    filename = f"{ts_slug}_{event}.json"
    file_path = os.path.join(dir_path, filename)
    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(snapshot, f, ensure_ascii=False, indent=2)
    return file_path


def log_critical_gamestate(
    gamestate,
    event: str,
    message: str,
    *,
    player_index: Optional[int] = None,
    extra: Optional[Dict[str, Any]] = None,
) -> None:
    """
    写入 CRITICAL 日志并保存 JSON 快照。
    message 为单行摘要；完整状态见 snapshot 文件路径（日志中附带）。
    """
    snapshot = build_gamestate_snapshot(
        gamestate, event=event, player_index=player_index, extra=extra
    )
    snapshot_path: Optional[str] = None
    try:
        snapshot_path = _write_snapshot_file(snapshot)
    except OSError as e:
        snapshot["snapshot_write_error"] = str(e)

    payload = {
        "event": event,
        "message": message,
        "gamestate_id": snapshot.get("gamestate_id"),
        "player_index": player_index,
        "snapshot_path": snapshot_path,
    }
    line = json.dumps(payload, ensure_ascii=False)
    get_critical_logger().critical(line)
