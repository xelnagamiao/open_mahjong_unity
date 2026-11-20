import asyncio
import random
from importlib import import_module
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from ...database.subrule_fans import collect_subrule_fans
from ...game_calculation.guangdong.config import wall_tiles
from ..verifier.record_sim.goto_action import RecordSim
from .test_doubles import CHICKEN, PROFILE, presentation, state
from .test_state import HAND, hand


def board_targets(number):
    return ((2, 0), (1, 3), (0, 2))[(number - 1) % 3]


def deal_board(gs):
    first, second = board_targets(gs.current_round)
    high = [43 if t == 41 else t for t in HAND]
    gs.round_random_seed = 20231101 + gs.current_round
    if first == 2:
        fixed, opening, next_draw = {0: CHICKEN, 2: high}, 45, 41
    elif first == 1:
        fixed, opening, next_draw = {1: CHICKEN, 3: high}, 41, 45
    else:
        fixed, opening, next_draw = {0: high, 2: CHICKEN}, 45, 41
    deal_from_fixture(gs, fixed, opening, [next_draw])


def deal_from_fixture(gs, fixed, opening, draws):
    supply = Counter(wall_tiles())
    for tiles in fixed.values():
        supply.subtract(tiles)
    supply.subtract([opening, *draws])
    assert min(supply.values()) >= 0
    remaining = list(supply.elements())
    random.Random(gs.round_random_seed).shuffle(remaining)
    for i in range(4):
        cards = list(fixed[i]) if i in fixed else [remaining.pop() for _ in range(13)]
        hand(gs, i, cards + ([opening] if i == 0 else []))
    gs.tiles_list = [*draws, *remaining]
    assert Counter(gs.tiles_list + [t for p in gs.player_list for t in p.hand_tiles]) == Counter(wall_tiles())


def deal_co_win_board(gs):
    if gs.current_round not in (2, 5):
        return deal_board(gs)
    gs.round_random_seed = 20231101 + gs.current_round
    pungs = [21]*3 + [25]*3 + [31]*3 + [35]*3
    high = [19]*3 + [29]*3 + [39]*3 + [42]*3 + [45]
    if gs.current_round == 2:
        deal_from_fixture(gs, {1: CHICKEN, 2: pungs + [41], 3: high}, 41, [45])
    else:
        deal_from_fixture(gs, {0: pungs + [46], 1: CHICKEN, 2: high, 3: CHICKEN}, 41, [45, 55, 46])


def choose_action(gs, player, allowed, co_wins=False):
    if "ready" in allowed:
        return {"action_type": "ready"}
    winners = board_targets(gs.current_round)
    accept = len(gs.win_events) < 2 and player.player_index == winners[len(gs.win_events)]
    if co_wins and gs.current_round in (2, 5):
        accept = player.player_index != 0 or bool(gs.win_events)
    if accept:
        win = next((a for a in allowed if a.startswith("hu_")), None)
        if win:
            return {"action_type": win}
    if "cut" in allowed:
        return {"action_type": "cut", "cutIndex": len(player.hand_tiles) - 1,
                "cutClass": bool(player.has_draw_slot), "TileId": player.hand_tiles[-1]}
    return {"action_type": "pass"}


def play_match(monkeypatch, db_manager, game_round=2, forfeit_at=None, co_wins=False):
    gs = state(player_list=list(range(10000101, 10000105)), game_round=game_round)
    ruled = set()
    starts = []
    snapshots = []
    class Socket:
        def __init__(self, uid):
            self.uid = uid
        async def send_json(self, payload):
            if payload.get("type", "").endswith("/game_start"):
                snapshots.append((self.uid, payload["game_info"]["players_info"]))
            info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info") or {}
            if not info.get("action_list"):
                return
            if gs.current_round == forfeit_at and not ruled:
                ruled.add(gs.current_round)
                await gs.adjudicate(gs.round_index, 0, "forfeit")
            p = next(p for p in gs.player_list if p.user_id == self.uid)
            move = choose_action(gs, p, info["action_list"], co_wins)
            await gs.action_queues[p.player_index].put(move)
            gs.action_events[p.player_index].set()
    def deal():
        starts.append([p.original_player_index for p in gs.player_list])
        (deal_co_win_board if co_wins else deal_board)(gs)
    for p in gs.player_list:
        gs.game_server.user_id_to_connection[p.user_id].websocket = Socket(p.user_id)
    gs.init_tiles = deal
    gs.run_hu_result_ready_phase = AsyncMock()
    gs.db_manager = db_manager
    async def run_cpu(_gs, function, *args):
        return function(*args)
    monkeypatch.setattr(import_module("server.gamestate.game_guangdong.GuangdongGameState"), "run_room_bot_cpu", run_cpu)
    asyncio.run(asyncio.wait_for(gs.game_loop_chinese(), 50))
    assert len(snapshots) == len(starts) * 4
    for uid, players in snapshots:
        own = next(p for p in players if p["user_id"] == uid)
        assert own["has_draw_slot"] == (own["player_index"] == 0)
        assert all("has_draw_slot" not in p and "hand_tiles" not in p for p in players if p["user_id"] != uid)
    return gs, starts


def test_eight_boards_production_loop_and_record(monkeypatch):
    saved = []
    db = SimpleNamespace(store_guangdong_game_record=lambda *args: saved.append(deepcopy(args[0])) or 'gd2v2-local')
    gs, starts = play_match(monkeypatch, db)
    assert len(saved) == 1 and len(saved[0]["game_round"]) == len(starts) == 8
    for i in range(7):
        assert starts[i + 1][0] == starts[i][board_targets(i + 1)[0]]
    assert all(r["end_reason"] == "team_finished" for r in gs.team_round_history)
    assert len(gs.team_boards) == 8 and all(len(p.score_history) == 8 for p in gs.player_list)
    assert sum(p.score for p in gs.player_list) == 0 and gs.team_result["scores"] == gs.team_round_history[-1]["scores"]
    assert sum(collect_subrule_fans(saved[0], i, PROFILE).get("鸡和", 0) for i in range(4)) == 8
    replay = RecordSim(saved[0])
    for n, board in enumerate(gs.team_boards, 1):
        key = f"round_index_{n}"
        frames = replay.apply_all(key)
        assert replay.goto_action(key, len(frames) - 1) == frames[-1]
        assert {int(i): seat["score"] for i, seat in frames[-1]["players"].items()} == board["changes"]
        assert sum(seat["is_hu"] for seat in frames[-1]["players"].values()) == 2
    assert gs.game_server.gamestate_manager.cleanup_game_state_complete.await_count == 1


def test_multi_ron_full_table_recovery(monkeypatch):
    gs, _ = play_match(monkeypatch, None, co_wins=True)
    assert [r["end_reason"] for r in gs.team_round_history].count("three_winners") == 2
    for n, first in [(2, [1, 2]), (5, [1])]:
        actions = gs.game_record["game_round"][f"round_index_{n}"]["action_ticks"]
        wins = [tick for tick in actions if tick[0].startswith("hu_")]
        assert len(wins) == 3 and [t[1] for t in wins[:len(first)]] == first
        assert all(bool(t[6]) == (n == 2) for t in wins[:len(first)])
        assert not any(t[:2] == ["guangdong", "penalty"] for t in actions)


@pytest.mark.parametrize("forfeit_at", [None, 3])
def test_two_sets_reset_dealer_and_resolve_each_set(monkeypatch, forfeit_at):
    gs, starts = play_match(monkeypatch, None, game_round=4, forfeit_at=forfeit_at)
    rounds = [row["round"] for row in gs.team_round_history]
    assert rounds == list(range(1, (forfeit_at or 8) + 1)) + list(range(9, 17))
    assert starts[rounds.index(9)][0] == 1
    first, second = gs.game_record["doubles_sets"]
    if forfeit_at:
        assert first["reason"] == "forfeit" and first["winner_team_id"] == 1
        assert not gs.forfeited_teams
    for result, completed in [(first, range(1, (forfeit_at or 8) + 1)), (second, range(9, 17))]:
        assert result["scores"] == {t: sum(r["score_changes"][t] for r in gs.team_round_history
                                           if r["round"] in completed) for t in (0, 1)}
    assert gs.team_result["match_points"] == {t: first["match_points"][t] + second["match_points"][t] for t in (0, 1)}
