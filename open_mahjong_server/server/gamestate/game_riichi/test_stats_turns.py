"""Live counters must use the same personal turn convention as saved replays."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from server.database.riichi.record_stats import analyze_riichi_record
from .test_double_riichi import declare, make_game
from .test_execution_paths import result
from .test_rule_branches import game


@pytest.mark.parametrize("seat", range(4))
def test_first_riichi_discard_is_personal_turn_one(seat):
    g, _ = make_game(seat)
    declare(g)
    assert g.player_list[seat].riichi_turn == 1


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("tsumo", [False, True])
def test_live_and_replay_win_turns_keep_called_away_discards(winner, tsumo):
    g = game()
    # Each player discarded once; the winner's tile was subsequently called away.
    ticks = []
    for seat, player in enumerate(g.player_list):
        tile = 11 + seat
        player.discard_origin_tiles = [tile]
        player.discard_tiles = [tile]
        ticks.extend([["d", tile], ["c", tile, "F"]])
    caller = (winner + 1) % 4
    ticks.extend([["reset", winner], ["p", 11 + winner, caller], ["c", 21, "F"]])
    g.player_list[winner].discard_tiles.clear()
    g.player_list[caller].discard_origin_tiles.append(21)
    if tsumo:
        ticks.extend([["reset", winner], ["d", 31, winner]])
    g.game_record["game_round"]["round_index_1"]["action_ticks"] = ticks
    g.current_player_index = winner if tsumo else caller
    g.ron_player_index = winner
    g.hu_class = "hu_self" if tsumo else "hu_first"
    g.result_dict = {g.hu_class: result(12000 if winner == 0 else 8000, tsumo, winner == 0)}
    # A table-wide counter must have no influence on an individual player's statistic.
    g.xunmu = 8
    with patch("server.gamestate.game_riichi.RiichiGameState.broadcast_result", new=AsyncMock()):
        asyncio.run(g._settle_hu(is_last_settle=True))
    ticks.append(["end"])
    record = {"game_title": {"rule": "riichi"}, "game_round": g.game_record["game_round"]}
    replay = analyze_riichi_record(record, winner)
    assert replay["total_win_turn"] == 2
    assert g.player_list[winner].record_counter.win_turn == 2
    assert all(p.record_counter.win_turn == 0 for p in g.player_list if p.player_index != winner)
