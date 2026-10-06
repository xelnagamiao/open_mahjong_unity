"""Pure Guobiao record replay, shared by validation and recovery; never writes a database."""
import json
from collections import defaultdict

from .record_analyzer import HU_ACTIONS, _parse_hu_tick, reconstruct_round_win_turns
from .round_score_utils import resolve_round_seats

STANDARD_SUB_RULES = ("", "guobiao", "guobiao/standard")


def replay_win_turns(raw):
    """一次重放整场，返回原始玩家顺序的和牌次数及巡目，不修改原始牌谱。"""
    record = json.loads(raw) if isinstance(raw, str) else raw
    if not isinstance(record, dict) or not isinstance(record.get("game_round"), dict) or not record["game_round"]:
        raise ValueError("missing game_round")
    title = record.get("game_title") or {}
    if not isinstance(title, dict) or title.get("rule", "guobiao") != "guobiao":
        raise ValueError("invalid game_title")
    if title.get("sub_rule") not in (None, *STANDARD_SUB_RULES):
        raise ValueError("not standard four-player guobiao")
    turns, wins = [0] * 4, [0] * 4
    for key, rd in record["game_round"].items():
        if not isinstance(rd, dict):
            raise ValueError("invalid round")
        ticks = rd.get("action_ticks")
        if not isinstance(ticks, list) or not ticks or any(
            not isinstance(t, list) or not t or not isinstance(t[0], str) for t in ticks
        ):
            raise ValueError("invalid action_ticks")
        if not any(t[0] == "end" or t[0] in HU_ACTIONS for t in ticks):
            raise ValueError("unfinished round")
        rd = dict(rd)
        if not any(k in rd for k in ("seats", "current_round", "round_index")):
            if not isinstance(key, str) or not key.startswith("round_index_") or not key[12:].isdigit():
                raise ValueError("missing round seats")
            rd["current_round"] = int(key[12:])
        # 早期牌谱没有开局补花后的 reset；仅在重放副本中补回庄家的首打位置。
        if not any(t[0] == "reset" for t in ticks):
            first_cut = next((i for i, t in enumerate(ticks) if t[0] == "c"), len(ticks))
            rd["action_ticks"] = ticks[:first_cut] + [["reset", 0]] + ticks[first_cut:]
        round_turns = reconstruct_round_win_turns(rd)
        round_wins = defaultdict(int)
        for tick in ticks:
            if tick[0] == "end":
                break
            if tick[0] not in HU_ACTIONS:
                continue
            hu = _parse_hu_tick(tick)
            if hu is None:
                raise ValueError("invalid hu tick")
            if not isinstance(hu["yaku"], list) or not any("错和" in str(f) for f in hu["yaku"]):
                round_wins[hu["winner_seat"]] += 1
        for original, seat in enumerate(resolve_round_seats(rd)):
            turns[original] += round_turns.get(seat, 0)
            wins[original] += round_wins[seat]
    return title, len(record["game_round"]), turns, wins
