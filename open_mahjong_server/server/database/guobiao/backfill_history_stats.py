"""
分析工具：从历史牌谱 JSON 重建 guobiao_history_stats，按 (user_id, match_type) 分开
排位(_rank)与自定义房间数据，并正确赋值 total_round_score（局均点分子）。

- 数据源：game_records.record + game_player_records（match_type / original_player_index / rank）
- 复用 round_score_utils 解析每小局净得分；和牌/放铳/错和/副露从 action_ticks 重建
- 番种统计(guobiao_fan_stats)不动
- 可重跑：先 DELETE guobiao_history_stats 再 INSERT
- total_win_turn（和巡）牌谱未直接存巡目，由 action_ticks 推理 seat 流转重建（seat 0 切牌计巡）
"""
import json
import logging
from collections import defaultdict
from typing import Any, Dict, List, Optional, Tuple

from .round_score_utils import sum_player_round_score, _parse_score_changes
from .store_guobiao import FAN_NAME_TO_FIELD, STACKABLE_FANS, FAN_FIELDS

logger = logging.getLogger(__name__)

REGISTERED_USER_ID_MIN = 10000000
_HU_ACTIONS = frozenset({"hu_self", "hu_first", "hu_second", "hu_third"})
_RON_ACTIONS = frozenset({"hu_first", "hu_second", "hu_third"})
# 明副露 tick 码（不含 ag 暗杠）；cl/cm/cr=吃 p=碰 g=明杠 jg=加杠
_VISIBLE_FULU_CODES = frozenset({"cl", "cm", "cr", "p", "g", "jg"})
_SKIP_SUB_RULES = ("guobiao/xiaolin", "guobiao/kshen")
# 摸牌 tick 码（不改变当前行动 seat）
_DRAW_CODES = frozenset({"d", "bd", "gd", "mo"})
# 副露/鸣牌 tick 码：tick[2] 为行动者 seat（碰/吃/明杠），覆盖当前 seat
_CLAIM_CODES = frozenset({"cl", "cm", "cr", "p", "g"})


def _reconstruct_round_win_turns(rd: Dict[str, Any]) -> Dict[int, int]:
    """从一局 action_ticks 推理每位 seat 的和巡总和。

    国标 self.xunmu 初值 1，每当 seat 0 切牌后 +1；和牌时 record_counter.win_turn += xunmu
    （错和不计入）。牌谱未直接存巡目，这里按 tick 序列模拟 seat 流转重建：
    - d/bd/gd：当前 seat 摸牌，seat 不变
    - c：当前 seat 切牌；若 seat==0 则 xunmu+1，随后 seat 前进 (seat+1)%4
      （若下一拍是鸣牌，会被 claim 覆盖，故该前进为试探性）
    - cl/cm/cr/p/g：tick[2] 为鸣牌者 seat，覆盖当前 seat
    - ca：tick[1] 为鸣牌申请者 seat，覆盖当前 seat
    - ag/jg：暗杠/加杠由当前 seat 发起，seat 不变
    - hu_*：tick[1] 为和牌 seat；非错和则按当前 xunmu 累加（多家荣和同巡）
    """
    ticks = rd.get("action_ticks") or []
    if not isinstance(ticks, list):
        return {}
    start = rd.get("start_player_index")
    if not isinstance(start, int):
        start = rd.get("dealer_index")
    if not isinstance(start, int):
        start = 0
    current_seat = start % 4
    xunmu = 1
    win_turn_by_seat: Dict[int, int] = {}
    for tick in ticks:
        if not isinstance(tick, list) or not tick:
            continue
        code = tick[0]
        if code in _DRAW_CODES:
            continue
        if code == "c":
            if current_seat == 0:
                xunmu += 1
            current_seat = (current_seat + 1) % 4
        elif code in _CLAIM_CODES:
            if len(tick) >= 3 and isinstance(tick[2], int):
                current_seat = tick[2] % 4
        elif code == "ca":
            if len(tick) >= 2 and isinstance(tick[1], int):
                current_seat = tick[1] % 4
        elif code in _HU_ACTIONS:
            if len(tick) >= 5 and isinstance(tick[1], int):
                hu_fan = tick[3] if len(tick) > 3 else []
                is_cuohe = isinstance(hu_fan, list) and any("错和" in str(f) for f in hu_fan)
                if not is_cuohe:
                    win_seat = tick[1] % 4
                    win_turn_by_seat[win_seat] = win_turn_by_seat.get(win_seat, 0) + xunmu
        elif code == "end":
            break
    return win_turn_by_seat


def _seat_to_original_map(seats) -> Dict[int, int]:
    """seats[original] = seat → {seat: original}。缺省视为 seat==original。"""
    if not isinstance(seats, list) or len(seats) != 4:
        return {0: 0, 1: 1, 2: 2, 3: 3}
    m: Dict[int, int] = {}
    for orig, seat in enumerate(seats):
        try:
            m[int(seat)] = int(orig)
        except (TypeError, ValueError):
            pass
    if len(m) == 4:
        return m
    return {0: 0, 1: 1, 2: 2, 3: 3}


def _analyze_record_for_player(record: Dict[str, Any], original_player_index: int) -> Optional[dict]:
    """从牌谱重建该玩家本场计数（zimo/dianhe/fangchong/fangchong_score/cuohe/fulu_rounds/win_score）。"""
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return None
    zimo = dianhe = fangchong = cuohe = fulu_rounds = 0
    win_score = fangchong_score = win_turn = 0
    for rd in game_round.values():
        if not isinstance(rd, dict):
            continue
        seat2orig = _seat_to_original_map(rd.get("seats"))
        my_seat = None
        for s, o in seat2orig.items():
            if o == original_player_index:
                my_seat = s
                break
        if my_seat is None:
            continue
        ticks = rd.get("action_ticks") or []
        had_fulu = False
        for tick in ticks:
            if not isinstance(tick, list) or not tick:
                continue
            code = tick[0]
            if code in _VISIBLE_FULU_CODES and len(tick) >= 3 and tick[2] == my_seat:
                had_fulu = True
            if code not in _HU_ACTIONS or len(tick) < 5:
                continue
            sc = _parse_score_changes(tick[4])
            if sc is None or my_seat < 0 or my_seat >= len(sc):
                continue
            hu_fan = tick[3] if len(tick) > 3 else []
            is_cuohe = isinstance(hu_fan, list) and any("错和" in str(f) for f in hu_fan)
            hu_score = tick[2] if isinstance(tick[2], (int, float)) else 0
            my_delta = sc[my_seat]
            if is_cuohe:
                if my_delta < 0:
                    cuohe += 1
                continue
            if my_delta > 0:
                if code == "hu_self":
                    zimo += 1
                else:
                    dianhe += 1
                win_score += int(hu_score)
            elif code in _RON_ACTIONS and my_delta < 0:
                neg = [x for x in sc if isinstance(x, (int, float)) and x < 0]
                if neg and my_delta == min(neg):
                    fangchong += 1
                    fangchong_score += int(hu_score)
        if had_fulu:
            fulu_rounds += 1
        # 和巡推理：本局该 seat 的和巡总和
        win_turn += _reconstruct_round_win_turns(rd).get(my_seat, 0)
    return {
        "zimo": zimo, "dianhe": dianhe, "fangchong": fangchong,
        "fangchong_score": fangchong_score, "cuohe": cuohe,
        "fulu_rounds": fulu_rounds, "win_score": win_score, "win_turn": win_turn,
    }


def _parse_fan_increment(hu_fan: Any) -> Dict[str, int]:
    """从一次和牌的 hu_fan 列表解析番种字段增量（与 store_guobiao_fan_stats 一致）。"""
    inc: Dict[str, int] = {}
    if not isinstance(hu_fan, list):
        return inc
    for fan_name in hu_fan:
        if not isinstance(fan_name, str):
            continue
        if "*" in fan_name:
            base_name, _, count_str = fan_name.partition("*")
            base_name = base_name.strip()
            if base_name in STACKABLE_FANS and base_name in FAN_NAME_TO_FIELD:
                try:
                    cnt = int(count_str.strip())
                except ValueError:
                    continue
                field = FAN_NAME_TO_FIELD[base_name]
                inc[field] = inc.get(field, 0) + cnt
        else:
            field = FAN_NAME_TO_FIELD.get(fan_name)
            if field:
                inc[field] = inc.get(field, 0) + 1
    return inc


def _collect_fans_for_player(record: Dict[str, Any], original_player_index: int) -> Dict[str, int]:
    """从牌谱 hu tick 重建该玩家本场番种增量（跳过错和）。"""
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return {}
    total: Dict[str, int] = {}
    for rd in game_round.values():
        if not isinstance(rd, dict):
            continue
        seat2orig = _seat_to_original_map(rd.get("seats"))
        my_seat = None
        for s, o in seat2orig.items():
            if o == original_player_index:
                my_seat = s
                break
        if my_seat is None:
            continue
        for tick in rd.get("action_ticks") or []:
            if not isinstance(tick, list) or len(tick) < 4:
                continue
            code = tick[0]
            if code not in _HU_ACTIONS:
                continue
            if tick[1] != my_seat:
                continue
            hu_fan = tick[3] if len(tick) > 3 else []
            if isinstance(hu_fan, list) and any("错和" in str(f) for f in hu_fan):
                continue
            for field, cnt in _parse_fan_increment(hu_fan).items():
                total[field] = total.get(field, 0) + cnt
    return total


def backfill_guobiao_history_stats(db_manager) -> None:
    """重建 guobiao_history_stats：按 (user_id, match_type) 分开聚合，局均点分子正确赋值。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        # 1) 排除含 AI 的对局
        cursor.execute("""
            SELECT DISTINCT game_id FROM game_player_records
            WHERE rule='guobiao' AND user_id <= %s
        """, (REGISTERED_USER_ID_MIN,))
        exclude_games = {r[0] for r in cursor.fetchall()}

        # 3) 统计 eligible game_id（注册玩家、有 match_type、非小林/K神）
        cursor.execute("""
            SELECT DISTINCT game_id FROM game_player_records
            WHERE rule='guobiao'
              AND user_id > %s
              AND match_type IS NOT NULL
              AND (sub_rule IS NULL OR sub_rule NOT IN %s)
        """, (REGISTERED_USER_ID_MIN, _SKIP_SUB_RULES))
        eligible_games = [r[0] for r in cursor.fetchall() if r[0] not in exclude_games]
        logger.info("backfill history_stats: eligible games=%d, excluded(AI)=%d", len(eligible_games), len(exclude_games))

        # 4) 拉取这些对局的 gpr 元数据
        cursor.execute("""
            SELECT game_id, user_id, match_type, original_player_index, rank
            FROM game_player_records
            WHERE rule='guobiao' AND user_id > %s AND match_type IS NOT NULL
              AND game_id = ANY(%s::varchar[])
        """, (REGISTERED_USER_ID_MIN, eligible_games))
        gpr_rows = cursor.fetchall()

        # 5) 拉取牌谱 JSON
        cursor.execute("""
            SELECT game_id, record FROM game_records
            WHERE game_id = ANY(%s::varchar[])
        """, (eligible_games,))
        record_map: Dict[str, Any] = {}
        for gid, raw in cursor.fetchall():
            if raw is None:
                continue
            record_map[gid] = json.loads(raw) if isinstance(raw, str) else raw

        # 6) 聚合 per (user_id, match_type)
        agg: Dict[Tuple[int, str], dict] = defaultdict(lambda: {
            "total_games": 0, "total_rounds": 0, "win_count": 0, "self_draw_count": 0,
            "deal_in_count": 0, "total_fan_score": 0, "total_fangchong_score": 0,
            "first_place_count": 0, "second_place_count": 0, "third_place_count": 0,
            "fourth_place_count": 0, "fulu_round_count": 0, "cuohe_count": 0,
            "total_round_score": 0, "total_win_turn": 0,
        })
        fan_agg: Dict[Tuple[int, str], Dict[str, int]] = defaultdict(lambda: {f: 0 for f in FAN_FIELDS})
        processed = 0
        for game_id, user_id, match_type, opi, rank in gpr_rows:
            rec = record_map.get(game_id)
            if rec is None:
                continue
            try:
                orig_idx = int(opi)
            except (TypeError, ValueError):
                continue
            key = (int(user_id), str(match_type))
            a = agg[key]
            a["total_games"] += 1
            game_round = rec.get("game_round") or {}
            a["total_rounds"] += sum(1 for k in game_round if isinstance(k, str) and k.startswith("round_index_"))
            cnt = _analyze_record_for_player(rec, orig_idx)
            if cnt:
                a["win_count"] += cnt["zimo"] + cnt["dianhe"]
                a["self_draw_count"] += cnt["zimo"]
                a["deal_in_count"] += cnt["fangchong"]
                a["total_fan_score"] += cnt["win_score"]
                a["total_fangchong_score"] += cnt["fangchong_score"]
                a["fulu_round_count"] += cnt["fulu_rounds"]
                a["cuohe_count"] += cnt["cuohe"]
                a["total_win_turn"] += cnt["win_turn"]
            a["total_round_score"] += sum_player_round_score(rec, orig_idx)
            r = int(rank) if rank is not None else 0
            if r == 1: a["first_place_count"] += 1
            elif r == 2: a["second_place_count"] += 1
            elif r == 3: a["third_place_count"] += 1
            elif r == 4: a["fourth_place_count"] += 1
            # 番种聚合（按 match_type 分开）
            for field, c in _collect_fans_for_player(rec, orig_idx).items():
                fan_agg[key][field] = fan_agg[key].get(field, 0) + c
            processed += 1

        # 7) total_win_turn 已由 _reconstruct_round_win_turns 从 action_ticks 推理累加（见步骤 6）

        # 8) 重建表
        cursor.execute("DELETE FROM guobiao_history_stats")
        cols = [
            "user_id", "rule", "mode", "total_games", "total_rounds", "win_count",
            "self_draw_count", "deal_in_count", "total_fan_score", "total_win_turn",
            "total_fangchong_score", "first_place_count", "second_place_count",
            "third_place_count", "fourth_place_count", "fulu_round_count",
            "cuohe_count", "total_round_score",
        ]
        placeholders = ", ".join(["%s"] * len(cols))
        col_list = ", ".join(cols)
        inserted = 0
        for (uid, mt), a in agg.items():
            values = [
                uid, "guobiao", mt, a["total_games"], a["total_rounds"], a["win_count"],
                a["self_draw_count"], a["deal_in_count"], a["total_fan_score"],
                a["total_win_turn"], a["total_fangchong_score"], a["first_place_count"],
                a["second_place_count"], a["third_place_count"], a["fourth_place_count"],
                a["fulu_round_count"], a["cuohe_count"], a["total_round_score"],
            ]
            cursor.execute(
                f"INSERT INTO guobiao_history_stats ({col_list}) VALUES ({placeholders})",
                values,
            )
            inserted += 1

        # 9) 重建 guobiao_fan_stats（按 match_type 分开，与 history_stats mode 对齐）
        cursor.execute("DELETE FROM guobiao_fan_stats")
        fan_cols = ["user_id", "rule", "mode"] + FAN_FIELDS
        fan_placeholders = ", ".join(["%s"] * len(fan_cols))
        fan_col_list = ", ".join(fan_cols)
        fan_inserted = 0
        for (uid, mt), fans in fan_agg.items():
            if not any(v for v in fans.values()):
                continue
            fan_values = [uid, "guobiao", mt] + [fans.get(f, 0) for f in FAN_FIELDS]
            cursor.execute(
                f"INSERT INTO guobiao_fan_stats ({fan_col_list}) VALUES ({fan_placeholders})",
                fan_values,
            )
            fan_inserted += 1

        conn.commit()
        logger.info(
            "guobiao_history_stats 重建完成：处理 %d 条玩家对局，写入 %d 个 history 行 / %d 个 fan 行",
            processed, inserted, fan_inserted,
        )
    except Exception as e:
        logger.error("重建 guobiao_history_stats 失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
