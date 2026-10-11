"""
每玩家每局原始指标写入 game_player_metrics，供每天 04:00 聚合 scene_daily_stats。
实时写入使用统一牌谱解析器，保持所有子规则的统计口径一致。
"""
import logging
from typing import Any, Dict, Optional
import re

logger = logging.getLogger(__name__)

# 主 title：room_type = match / custom / events
# 次级 sign：match_tier；天梯为 beginner/intermediate/advanced/mcrpl，比赛场填 event_id
LADDER_TIERS = ("beginner", "intermediate", "advanced", "mcrpl", "elo")

# match_type → 局制 game_type
_GAME_TYPE_MAP = {
    "1/4": "dongfeng", "1/4_rank": "dongfeng",
    "2/4": "banzhuang", "2/4_rank": "banzhuang",
    "1/4_sanma_rank": "dongfeng", "2/4_sanma_rank": "banzhuang",
    "3/4": "xifeng",
    "4/4": "quanzhuang", "4/4_rank": "quanzhuang",
}


def _blank_to_none(value: Optional[str]) -> Optional[str]:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def normalize_scene_fields(
    room_type: Optional[str],
    match_tier: Optional[str],
    event_id: Optional[str],
) -> tuple:
    """统一场次三元组：比赛场用 event_id 填充次级 match_tier。"""
    room_type = _blank_to_none(room_type)
    match_tier = _blank_to_none(match_tier)
    event_id = _blank_to_none(event_id)
    if room_type == "events" and event_id:
        match_tier = event_id
    return room_type, match_tier, event_id


def should_record_scene_metrics(
    room_type: Optional[str],
    match_tier: Optional[str],
    event_id: Optional[str] = None,
) -> bool:
    room_type, match_tier, event_id = normalize_scene_fields(room_type, match_tier, event_id)
    if room_type == "custom":
        return True
    if room_type == "match" and match_tier in LADDER_TIERS:
        return True
    if room_type == "events" and event_id:
        return True
    return False


def derive_game_type(match_type: Optional[str], rule: Optional[str] = None) -> Optional[str]:
    if not match_type:
        return None
    if rule == "hongque":
        match = re.fullmatch(r"(4|8|12|16)/4(?:_rank)?", match_type)
        return f"hands{match[1]}" if match else None
    match = re.fullmatch(r"([1-4])/(?:3|4)(?:_sanma)?(?:_rank)?", match_type)
    return {"1": "dongfeng", "2": "banzhuang", "3": "xifeng", "4": "quanzhuang"}.get(match[1]) if match else _GAME_TYPE_MAP.get(match_type)


def _count_rounds(game_record: Dict[str, Any]) -> int:
    game_round = game_record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return 0
    return sum(1 for k in game_round.keys() if isinstance(k, str) and k.startswith("round_index_"))


def record_game_metrics(db_manager, game_id, game_record, player_list, scene, *, cursor=None):
    """解析当前结算牌谱；传入 cursor 时由牌谱事务统一提交或回滚。"""
    from .record_metrics import _build_metrics_row, _METRIC_INSERT_SQL
    from .statistics_catalog import resolve_statistics_identity, BY_SUB_RULE
    from .riichi.record_stats import resolve_original_index
    title = game_record.get("game_title") or {}
    rule, sub_rule = resolve_statistics_identity(scene.get("rule"), scene.get("sub_rule"), title, scene.get("match_type"))
    if not BY_SUB_RULE.get(sub_rule, {}).get("basic_stats"):
        return
    record = {**game_record, "game_title": {**title, "rule": rule, "sub_rule": sub_rule}}
    room_type, tier, event_id = normalize_scene_fields(scene.get("room_type"), scene.get("match_tier"), scene.get("event_id"))
    if not should_record_scene_metrics(room_type, tier, event_id) or any(getattr(p, "user_id", 0) <= 10 for p in player_list):
        return
    def write_rows(target):
        for player in player_list:
            uid = getattr(player, "user_id", 0)
            if uid <= 10000000:
                continue
            index = resolve_original_index(record, uid, getattr(player, "original_player_index", None))
            if index < 0:
                raise ValueError(f"无法识别统计玩家席位: {game_id}/{uid}")
            row = _build_metrics_row(game_id, uid, player.username, rule, sub_rule,
                room_type, tier, event_id, scene.get("match_type"), getattr(player, "score", 0) or 0,
                getattr(getattr(player, "record_counter", None), "rank_result", 0) or 0,
                record, index, None)
            target.execute(_METRIC_INSERT_SQL + " ON CONFLICT (game_id, user_id) DO NOTHING", row)

    if cursor is not None:
        write_rows(cursor)
        return
    conn = db_manager._get_connection()
    try:
        with conn.cursor() as target:
            write_rows(target)
        conn.commit()
    except Exception:
        conn.rollback()
        logger.exception("写入子规则统计失败: %s", game_id)
        raise
    finally:
        db_manager._put_connection(conn)
