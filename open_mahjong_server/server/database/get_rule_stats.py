"""按实际玩法读取自定义对局，三人场与子规则分别汇总。"""

from collections import defaultdict
from dataclasses import dataclass
import re

from psycopg2.extras import RealDictCursor

from .riichi.record_stats import BASE_KEYS, VERSION, empty_stats


@dataclass(frozen=True)
class StatsSelection:
    source_rule: str
    sub_rule: str | None = None
    player_count: int = 4


from .statistics_catalog import RULES, selection

SELECTIONS = {row["key"]: StatsSelection(row["rule"], row["sub_rule"], row["player_count"])
              for row in RULES if row["basic_stats"]}


def validate_selection(message):
    """客户端附带的族、子规则和人数只能与选项匹配，不能扩大查询范围。"""
    rule = message.get("rule")
    if not isinstance(rule, str) or rule not in SELECTIONS:
        raise ValueError("不支持的统计规则")
    selection = SELECTIONS[rule]
    if message.get("source_rule") not in (None, selection.source_rule):
        raise ValueError("统计规则与房间规则不匹配")
    if message.get("sub_rule") not in (None, selection.sub_rule):
        raise ValueError("统计子规则不匹配")
    if message.get("player_count") is not None:
        try:
            count = int(message["player_count"])
        except (ValueError, TypeError):
            raise ValueError("无效的统计人数") from None
        if count != selection.player_count:
            raise ValueError("统计人数与规则不匹配")
    if message.get("room_type", "custom") != "custom":
        raise ValueError("此接口仅查询自定义对局")
    return rule


def canonical_mode(mode, count):
    match = re.fullmatch(r"([1-4])/(?:3|4)(?:_sanma)?(?:_rank)?", mode or "")
    return f"{match[1]}/{count}" if match else (mode or "")


def get_rule_history(db, user_id, rule, room_type="custom"):
    definition = selection(rule)
    fields = [key for key in BASE_KEYS if key != "total_games"]
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cur:
            cur.execute(f"""SELECT m.match_type AS mode,m.room_type,{','.join('m.' + key for key in fields)},
                m.fan_stats,s.stats AS summary
                FROM game_player_metrics m
                LEFT JOIN riichi_player_game_stats s ON m.rule='riichi'
                    AND s.game_id=m.game_id AND s.user_id=m.user_id AND s.version=%s
                WHERE m.user_id=%s AND m.rule=%s AND m.sub_rule=%s
                    AND (%s IS NULL OR m.room_type=%s)
                ORDER BY m.match_type,m.game_id""",
                (VERSION, user_id, definition["rule"], definition["sub_rule"], room_type, room_type))
            totals = defaultdict(empty_stats)
            for row in cur.fetchall():
                mode = canonical_mode(row["mode"], definition["player_count"])
                if row["room_type"] == "match":
                    mode += "_rank" if not mode.endswith("_rank") else ""
                total = totals[mode]
                summary = row["summary"] if definition["rule"] == "riichi" else None
                total["total_games"] += 1
                for key in fields:
                    total[key] += (summary.get(key, 0) if summary else row[key]) or 0
                for key, value in ((summary or {}).get("riichi_details") or {}).items():
                    total["riichi_details"][key] = total["riichi_details"].get(key, 0) + value
                if definition["fan_stats"]:
                    for key, value in (row["fan_stats"] or {}).items():
                        total["fan_stats"][key] = total["fan_stats"].get(key, 0) + value
            return [dict(rule=definition["key"], sub_rule=definition["sub_rule"], mode=mode,
                         **{key: stats[key] for key in BASE_KEYS},
                         **({"fan_stats": stats["fan_stats"]} if definition["fan_stats"] else {}),
                         **({"riichi_details": stats["riichi_details"]} if definition["rule"] == "riichi" else {}))
                    for mode, stats in sorted(totals.items())]
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def get_custom_rule_history(db, user_id, rule):
    return get_rule_history(db, user_id, rule, "custom")


def get_rule_fans(db, user_id, rule, room_type="custom"):
    total = {}
    for row in get_rule_history(db, user_id, rule, room_type):
        for key, value in (row.get("fan_stats") or {}).items():
            total[key] = total.get(key, 0) + value
    return total
