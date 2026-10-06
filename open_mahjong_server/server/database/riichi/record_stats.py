"""日麻逐对局统计。牌谱是事实来源；摘要可重建，旧累计表保持兼容。"""
import json
import logging
from collections import defaultdict
from psycopg2.extras import Json, RealDictCursor
from .store_riichi import FAN_FIELDS, _accumulate_yaku_stats
from ...game_calculation.riichi.sanma import player_count

logger = logging.getLogger(__name__)
VERSION = 3
DETAIL_KEYS = (
    "riichi_round_count", "total_riichi_turn", "riichi_win_count", "riichi_deal_in_count",
    "fulu_win_count", "damaten_win_count", "draw_round_count", "exhaustive_draw_count",
    "tenpai_draw_count", "tsumo_loss_count", "total_win_points", "total_deal_in_points",
    "final_score_total", "final_score_count", "busted_count", "net_score_count",
)
BASE_KEYS = (
    "total_games", "total_rounds", "win_count", "self_draw_count", "deal_in_count",
    "total_fan_score", "total_win_turn", "total_fangchong_score", "fulu_round_count",
    "cuohe_count", "total_round_score", "first_place_count", "second_place_count",
    "third_place_count", "fourth_place_count",
)
CALLS = {"cl", "cm", "cr", "p", "g"}
HU = {"hu_self", "hu_first", "hu_second", "hu_third"}


def empty_stats():
    return {**dict.fromkeys(BASE_KEYS, 0), "riichi_details": dict.fromkeys(DETAIL_KEYS, 0), "fan_stats": {}}


def analyze_riichi_record(record, original_index, final_score=None, rank=None):
    """按小局去重；和牌取本家待和巡目（历史弃牌数+1），立直取宣言弃牌序号。"""
    out = empty_stats()
    details = out["riichi_details"]
    title = record.get("game_title") or {}
    count = player_count(title.get("sub_rule"))
    rounds = record.get("game_round")
    if title.get("rule") != "riichi" or not isinstance(rounds, dict) or not 0 <= original_index < count:
        return None
    out["total_games"] = 1
    fans = dict.fromkeys(FAN_FIELDS, 0)
    for key, rd in rounds.items():
        if not str(key).startswith("round_index_") or not isinstance(rd, dict):
            continue
        ticks = rd.get("action_ticks")
        # 尚未结束的小局（例如投票中断）不能混入各项率的分母。
        if not isinstance(ticks, list) or not any(isinstance(t, list) and t and t[0] == "end" for t in ticks):
            continue
        seats = rd.get("seats") or list(range(count))
        if sorted(seats) != list(range(count)):
            continue
        seat = seats[original_index]
        current = rd.get("start_player_index", rd.get("dealer_index", 0))
        opening = True
        cuts = [0] * count
        declarations = set()
        called = set()
        won = dealt = drawn = tsumo_lost = False
        round_net = 0
        out["total_rounds"] += 1
        for tick in ticks:
            if not isinstance(tick, list) or not tick:
                continue
            code = tick[0]
            if code == "end":
                break
            if code == "reset" and len(tick) > 1:
                current = int(tick[1]) % count
            elif code in ("d", "mo"):
                if len(tick) > 2 and isinstance(tick[2], int):
                    current = tick[2] % count
                elif not opening:
                    current = (current + 1) % count
                opening = False
            elif code == "c":
                cuts[current] += 1
                opening = False
            elif code in CALLS and len(tick) > 2:
                current = int(tick[2]) % count
                called.add(current)
                opening = False
            elif code == "jg":
                called.add(current)
            elif code == "nuki" and len(tick) > 1:
                # Extracting north preserves the closed hand; nd keeps this actor.
                current = int(tick[1]) % count
                opening = False
            elif code == "riichi" and len(tick) > 1:
                declarer = int(tick[1]) % count
                if declarer not in declarations:
                    declarations.add(declarer)
                    if declarer == seat:
                        details["riichi_round_count"] += 1
                        details["total_riichi_turn"] += max(1, cuts[seat])
                        round_net -= 1000
            elif code == "ryuukyoku" and len(tick) >= 3:
                changes = tick[2]
                if isinstance(changes, list) and len(changes) == count:
                    round_net += int(changes[seat])
                if not drawn:
                    details["draw_round_count"] += 1
                    if len(tick) < 4 or tick[3] == "exhaustive":
                        details["exhaustive_draw_count"] += 1
                        flags = tick[1]
                        if isinstance(flags, list) and len(flags) == count and flags[seat]:
                            details["tenpai_draw_count"] += 1
                    drawn = True
            elif code == "hu_riichi" and len(tick) >= 7 and tick[2] in HU:
                winner, hu_class, han, yaku, changes = tick[1], tick[2], tick[3], tick[5], tick[6]
                if not isinstance(changes, list) or len(changes) != count:
                    continue
                round_net += int(changes[seat])
                if any("错和" in str(y) for y in yaku):
                    if winner == seat:
                        out["cuohe_count"] += 1
                    # 错和返还本局立直棒；实际结算 tick 不含这部分返还。
                    if seat in declarations:
                        round_net += 1000
                    declarations.clear()
                    continue
                if winner == seat and not won:
                    won = True
                    out["win_count"] += 1
                    out["self_draw_count"] += int(hu_class == "hu_self")
                    out["total_fan_score"] += int(han)
                    out["total_win_turn"] += cuts[seat] + 1
                    details["total_win_points"] += max(0, int(changes[seat]))
                    if seat in declarations:
                        details["riichi_win_count"] += 1
                    elif seat not in called:
                        details["damaten_win_count"] += 1
                    if seat in called:
                        details["fulu_win_count"] += 1
                    _accumulate_yaku_stats(fans, yaku)
                elif hu_class != "hu_self" and current == seat:
                    # 一炮多响算一个放铳局，点数累加各家实际支付。
                    dealt = True
                    out["total_fangchong_score"] += int(han)
                    details["total_deal_in_points"] += max(0, -int(changes[seat]))
                elif hu_class == "hu_self" and int(changes[seat]) < 0:
                    tsumo_lost = True
        out["fulu_round_count"] += int(seat in called)
        out["deal_in_count"] += int(dealt)
        details["riichi_deal_in_count"] += int(dealt and seat in declarations)
        details["tsumo_loss_count"] += int(tsumo_lost)
        out["total_round_score"] += round_net
    starts = title.get("starting_scores")
    start = starts[original_index] if isinstance(starts, list) and len(starts) == count else title.get("starting_score")
    if final_score is not None:
        details["final_score_total"] = int(final_score)
        details["final_score_count"] = 1
        details["busted_count"] = int(final_score < 0)
        # 结算分涵盖终局供托分配，自定义起始分不能默认为 25000。
        if start is not None:
            out["total_round_score"] = int(final_score) - int(start)
            details["net_score_count"] = 1
    if rank in range(1, count + 1):
        out[("first", "second", "third", "fourth")[rank - 1] + "_place_count"] = 1
    out["fan_stats"] = {k: v for k, v in fans.items() if v}
    return out


def ensure_schema(cursor):
    cursor.execute("""CREATE TABLE IF NOT EXISTS riichi_player_game_stats (
        game_id VARCHAR(16) NOT NULL REFERENCES game_records(game_id) ON DELETE CASCADE,
        user_id BIGINT NOT NULL, version INT NOT NULL, stats JSONB NOT NULL,
        PRIMARY KEY(game_id, user_id));
        CREATE INDEX IF NOT EXISTS riichi_player_game_stats_user ON riichi_player_game_stats(user_id);""")


def write_stats(cursor, game_id, user_id, record, original_index, score, rank):
    stats = analyze_riichi_record(record, original_index, score, rank)
    cursor.execute("""INSERT INTO riichi_player_game_stats(game_id,user_id,version,stats)
        VALUES(%s,%s,%s,%s) ON CONFLICT(game_id,user_id) DO UPDATE
        SET version=EXCLUDED.version,stats=EXCLUDED.stats""", (game_id, user_id, VERSION, Json(stats)))
    return stats


def resolve_original_index(record, user_id, original_index):
    """兼容旧谱缺席位字段，以及三麻的玩家数；不能把无效席位当作零统计。"""
    title = record.get("game_title") or {}
    count = player_count(title.get("sub_rule"))
    if original_index is None:
        original_index = next((i for i in range(count)
            if str(title.get(f"p{i}_uid")) == str(user_id)), -1)
    return original_index if isinstance(original_index, int) and 0 <= original_index < count else -1


def backfill_stats(db, batch_size=200):
    """启动维护中增量重建摘要；每批提交，可重试且不会累加两次。"""
    conn = db._get_connection()
    count = 0
    try:
        while True:
            with conn.cursor(cursor_factory=RealDictCursor) as cur:
                cur.execute("""SELECT gpr.game_id,gpr.user_id,gpr.original_player_index,gpr.score,gpr.rank,gr.record
                    FROM game_player_records gpr JOIN game_records gr USING(game_id)
                    LEFT JOIN riichi_player_game_stats s ON s.game_id=gpr.game_id AND s.user_id=gpr.user_id
                    WHERE gpr.rule='riichi' AND (s.version IS NULL OR s.version<>%s)
                    ORDER BY gpr.game_id,gpr.user_id LIMIT %s""", (VERSION, batch_size))
                rows = cur.fetchall()
                if not rows:
                    break
                for row in rows:
                    record = row["record"]
                    if isinstance(record, str):
                        record = json.loads(record)
                    index = resolve_original_index(record, row["user_id"], row["original_player_index"])
                    if index < 0:
                        logger.warning("无法重建日麻摘要 game_id=%s user_id=%s", row["game_id"], row["user_id"])
                    write_stats(cur, row["game_id"], row["user_id"], record, index, row["score"], row["rank"])
                    count += 1
            conn.commit()
        logger.info("日麻统计摘要增量重建 %s 条", count)
        return count
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def get_history_stats(db, user_id, ranked=None, sanma=None):
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cur:
            cur.execute("""SELECT gpr.match_type AS mode,s.stats FROM game_player_records gpr
                JOIN riichi_player_game_stats s USING(game_id,user_id)
                WHERE gpr.rule='riichi' AND gpr.user_id=%s
                AND s.version=%s
                AND (gpr.room_type='match' OR gpr.room_type='custom')
                AND (%s IS NULL OR (gpr.room_type='match')=%s)
                AND (%s IS NULL OR (COALESCE(gpr.sub_rule,'')='riichi/sanma')=%s)
                ORDER BY gpr.match_type""", (user_id, VERSION, ranked, ranked, sanma, sanma))
            totals = defaultdict(empty_stats)
            for row in cur.fetchall():
                if row["stats"] is None:
                    continue
                total = totals[row["mode"]]
                for key in BASE_KEYS:
                    total[key] += row["stats"].get(key, 0)
                for group in ("riichi_details", "fan_stats"):
                    for key, value in row["stats"].get(group, {}).items():
                        total[group][key] = total[group].get(key, 0) + value
            return [dict(rule="riichi_sanma" if sanma else "riichi", mode=mode, **stats) for mode, stats in totals.items()]
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
