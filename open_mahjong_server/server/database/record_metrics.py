"""结算时从当前牌谱生成逐玩家指标；不负责历史扫描或回填。"""
from typing import Any, Dict, Optional

from psycopg2.extras import Json

from .guobiao.record_analyzer import analyze_record_for_player
from .guobiao.round_score_utils import sum_player_round_score
from .scene_stats import derive_game_type

_METRIC_INSERT_SQL = """
    INSERT INTO game_player_metrics (
        game_id, user_id, username, rule, sub_rule, room_type, match_tier, event_id,
        game_type, match_type, score, rank, total_rounds,
        win_count, self_draw_count, deal_in_count, total_fan_score, total_win_turn,
        total_fangchong_score, first_place_count, second_place_count, third_place_count,
        fourth_place_count, fulu_round_count, cuohe_count, total_round_score, created_at, fan_stats
    ) VALUES (
        %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s,
        %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, COALESCE(%s, NOW()), %s
    )
"""


def _count_rounds(record: Dict[str, Any]) -> int:
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return 0
    return sum(1 for k in game_round.keys() if isinstance(k, str) and k.startswith("round_index_"))


def _rank_place_counts(rank: int) -> tuple:
    return (
        1 if rank == 1 else 0,
        1 if rank == 2 else 0,
        1 if rank == 3 else 0,
        1 if rank == 4 else 0,
    )


def _build_metrics_row(
    game_id: str,
    user_id: int,
    username: str,
    rule: str,
    sub_rule: Optional[str],
    room_type: Optional[str],
    match_tier: Optional[str],
    event_id: Optional[str],
    match_type: Optional[str],
    score: int,
    rank: int,
    record: Dict[str, Any],
    original_player_index: Optional[int],
    created_at,
) -> tuple:
    game_type = derive_game_type(match_type, rule)
    total_rounds = _count_rounds(record)
    first, second, third, fourth = _rank_place_counts(rank)

    win_count = self_draw = deal_in = 0
    total_fan_score = total_win_turn = total_fangchong_score = 0
    fulu_round_count = cuohe_count = total_round_score = 0

    if rule == "guobiao" and sub_rule not in ("guobiao/sanma", "guobiao/blood_battle") and original_player_index is not None:
        cnt = analyze_record_for_player(record, original_player_index)
        if cnt:
            self_draw = cnt["zimo"]
            win_count = cnt["zimo"] + cnt["dianhe"]
            deal_in = cnt["fangchong"]
            total_fan_score = cnt["win_score"]
            total_fangchong_score = cnt["fangchong_score"]
            fulu_round_count = cnt["fulu_rounds"]
            cuohe_count = cnt["cuohe"]
            total_win_turn = cnt["win_turn"]
        total_round_score = sum_player_round_score(record, original_player_index)
        if sub_rule in (None, "", "guobiao", "guobiao/standard"):
            from .guobiao.record_replay import replay_win_turns
            _, total_rounds, turns, wins = replay_win_turns(record)
            total_win_turn = turns[original_player_index]
            win_count = wins[original_player_index]
    elif rule == "riichi":
        from .riichi.record_stats import analyze_riichi_record, resolve_original_index
        index = resolve_original_index(record, user_id, original_player_index)
        stats = analyze_riichi_record(record, index, score, rank)
        if stats:
            total_rounds = stats["total_rounds"]
            win_count = stats["win_count"]
            self_draw = stats["self_draw_count"]
            deal_in = stats["deal_in_count"]
            total_fan_score = stats["total_fan_score"]
            total_win_turn = stats["total_win_turn"]
            total_fangchong_score = stats["total_fangchong_score"]
            fulu_round_count = stats["fulu_round_count"]
            cuohe_count = stats["cuohe_count"]
            total_round_score = stats["total_round_score"]
    elif original_player_index is not None:
        from .shared_record_metrics import analyze_shared_record_for_player
        cnt = analyze_shared_record_for_player(record, original_player_index)
        total_rounds = cnt["total_rounds"]
        self_draw = cnt["self_draw_count"]
        win_count = cnt["win_count"]
        deal_in = cnt["deal_in_count"]
        total_fan_score = cnt["total_fan_score"]
        total_fangchong_score = cnt["total_fangchong_score"]
        fulu_round_count = cnt["fulu_round_count"]
        cuohe_count = cnt["cuohe_count"]
        total_win_turn = cnt["total_win_turn"]
        total_round_score = cnt["total_round_score"]

    from .subrule_fans import collect_subrule_fans
    fans = collect_subrule_fans(record, original_player_index, sub_rule) if original_player_index is not None else None
    return (
        game_id, user_id, username, rule, sub_rule, room_type, match_tier, event_id,
        game_type, match_type, score, rank, total_rounds,
        win_count, self_draw, deal_in, total_fan_score, total_win_turn,
        total_fangchong_score, first, second, third, fourth,
        fulu_round_count, cuohe_count, total_round_score, created_at, Json(fans) if fans is not None else None,
    )
