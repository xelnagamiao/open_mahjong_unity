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


SELECTIONS = {rule: StatsSelection(rule) for rule in (
    "guobiao", "riichi", "qingque", "classical", "changsha", "taiwan",
    "hongque", "hongkong", "shanxi", "changchun", "free",
    "guizhou", "yixing", "wenzhou", "hangzhou", "hongzhong",
)}
SELECTIONS.update({
    "guobiao_sanma": StatsSelection("guobiao", "guobiao/sanma", 3),
    "riichi_sanma": StatsSelection("riichi", "riichi/sanma", 3),
    "sichuan": StatsSelection("sichuan", "sichuan/standard"),
    "sichuan_xueliu_exchange": StatsSelection("sichuan", "sichuan/xueliu_exchange"),
    "zhongyong": StatsSelection("zhongyong", "zhongyong/standard"),
    "nanque": StatsSelection("zhongyong", "zhongyong/nanque"),
    "guangdong": StatsSelection("guangdong", "guangdong/mil2023"),
    "guangdong_tuidao": StatsSelection("guangdong", "guangdong/tuidao_mil2024"),
    "shanghai": StatsSelection("shanghai", "shanghai/qiaoma"),
    "shanghai_qinghunpeng": StatsSelection("shanghai", "shanghai/qinghunpeng"),
})


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
    return f"{match[1]}/4" + ("_sanma" if count == 3 else "") if match else (mode or "")


def get_custom_rule_history(db, user_id, rule):
    selection = SELECTIONS[rule]
    # 古早房间缺子规则时，沿用各房间入口当时的实际默认玩法。
    default_sub_rule = {
        "guangdong": "guangdong/tuidao_mil2024", "zhongyong": "zhongyong/standard",
        "sichuan": "sichuan/standard",
        "shanghai": "shanghai/qiaoma",
    }.get(selection.source_rule, "")
    sub_expr = "COALESCE(NULLIF(p.sub_rule,''),NULLIF(g.record->'game_title'->>'sub_rule',''),%s)"
    params = [user_id, selection.source_rule, default_sub_rule]
    if selection.source_rule in ("guobiao", "riichi"):
        # 兼容旧 /3 局制，但不会将三人、四人的样本合在一起。
        predicate = f"({sub_expr}=%s OR COALESCE(p.match_type,'') ~ '^[1-4]/3(_rank)?$' OR COALESCE(p.match_type,'') ~ '^[1-4]/4_sanma(_rank)?$')"
        params.append(selection.source_rule + "/sanma")
        if selection.player_count == 4:
            predicate = "NOT " + predicate
    elif selection.sub_rule:
        predicate = sub_expr + "=%s"
        params.append(selection.sub_rule)
    else:
        predicate = sub_expr + " IS NOT NULL"

    riichi = selection.source_rule == "riichi"
    summary_join = "LEFT JOIN riichi_player_game_stats s ON s.game_id=p.game_id AND s.user_id=p.user_id AND s.version=%s" if riichi else ""
    # 版本参数位于 WHERE 参数之前。
    if riichi:
        params.insert(0, VERSION)
    metrics = [key for key in BASE_KEYS if key != "total_games"]
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cur:
            cur.execute(f"""SELECT p.match_type AS mode,p.rank,
                {','.join('m.' + key for key in metrics)},
                {'s.stats' if riichi else 'NULL::jsonb'} AS summary
                FROM game_player_records p JOIN game_records g USING(game_id)
                LEFT JOIN LATERAL (SELECT * FROM game_player_metrics
                    WHERE game_id=p.game_id AND user_id=p.user_id ORDER BY id DESC LIMIT 1) m ON TRUE
                {summary_join}
                WHERE p.user_id=%s AND p.rule=%s AND p.room_type='custom' AND {predicate}
                AND NOT EXISTS (SELECT 1 FROM game_player_records bot
                    WHERE bot.game_id=p.game_id AND bot.user_id<=10000000)
                ORDER BY p.match_type,p.game_id""", params)
            totals = defaultdict(empty_stats)
            for row in cur.fetchall():
                total = totals[canonical_mode(row["mode"], selection.player_count)]
                summary = row["summary"]
                if summary:
                    for key in BASE_KEYS:
                        total[key] += int(summary.get(key) or 0)
                    for group in ("riichi_details", "fan_stats"):
                        for key, value in (summary.get(group) or {}).items():
                            total[group][key] = total[group].get(key, 0) + int(value or 0)
                else:
                    total["total_games"] += 1
                    for key in metrics:
                        if not key.endswith("_place_count"):
                            total[key] += int(row[key] or 0)
                    if row["rank"] in range(1, selection.player_count + 1):
                        total[("first", "second", "third", "fourth")[row["rank"] - 1] + "_place_count"] += 1
            return [dict(rule=rule, mode=mode,
                         **(stats if riichi else {key: stats[key] for key in BASE_KEYS}))
                    for mode, stats in sorted(totals.items())]
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
