"""SBR duplicate active flower-pig transfers reach players, records and both tables."""
import asyncio
from copy import deepcopy
from unittest.mock import AsyncMock, patch

import pytest

from .DuplicateGameState import SbrDuplicateGameState
from .duplicate_wall import init_duplicate_board
from .test_duplicate import pair, run_async
from .test_doubles import THREE, WIN
from ...game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, team_by_seat
from ...game_calculation.sichuan.doubles_rules import team_scores
from ..public.game_record_manager import init_game_record, init_game_round
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ..verifier.record_sim.goto_action import RecordSim


NO_TING = [11, 11, 11, 12, 14, 16, 18, 21, 23, 25, 27, 28, 29]


def make_state(table):
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    match, _ = pair()
    server.gamestate_manager.sichuan_duplicate_matches = {"id": match}
    room = build_room_data(seed=0, room_id=match.data["room_ids"][table])
    room.update(room_rule="sichuan", sub_rule=SBR_DUPLICATE, room_type="events",
                team_by_seat=team_by_seat(table), duplicate_match_id="id",
                duplicate_table=table, duplicate_key="test-key", duplicate_game_id="test-game")
    state = SbrDuplicateGameState(server, room, server.calculation_service, db, table)
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
    state._reset_round_state()
    init_duplicate_board(state, match.board(1))
    for player in state.player_list:
        player.hand_tiles = list(NO_TING)
    assert state._evaluate_liuju_ting(NO_TING, [], 0, []) == (False, set(), 0, None)
    init_game_record(state)
    state._record_rule_profile()
    init_game_round(state)
    return state, messages, match


def mark_pig(state, seat, *, active=True):
    player = state.player_list[seat]
    player.hand_tiles = THREE[:-1]
    player.discard_origin_tiles = [11]
    player.discard_tiles = [21 if active else 12]


def settle(state):
    with patch(__package__ + ".SichuanGameState.asyncio.sleep", new_callable=AsyncMock):
        asyncio.run(state._settle_liuju())


def results(messages):
    return [entry["message"]["show_result_info"] for entry in messages
            if "show_result_info" in entry["message"]]


@pytest.mark.parametrize("table", ["open", "closed"])
@pytest.mark.parametrize("offender", range(4))
@pytest.mark.parametrize("opponent_won", [False, True])
def test_penalty_reaches_both_opponents_and_all_score_outputs(table, offender, opponent_won):
    state, messages, _ = make_state(table)
    mark_pig(state, offender)
    if opponent_won:
        state.player_list[(offender + 1) % 4].is_hu = True
        state.player_list[(offender + 1) % 4].hu_order = 1
    settle(state)
    expected = [0, 0, 0, 0]
    expected[offender] = -12
    expected[(offender + 1) % 4] = expected[(offender + 3) % 4] = 6
    assert [player.score for player in state.player_list] == expected
    assert sum(expected) == 0
    assert state.duplicate_adjustments == [0, 0]
    penalty_panels = [info for info in results(messages)
                      if info.get("liuju_step") == "chajiao"
                      and "hua_zhu_active" in info.get("liuju_status", {}).values()]
    assert len(penalty_panels) == 4  # Every viewer receives the same transfer.
    for panel in penalty_panels:
        assert {int(k): v for k, v in panel["score_changes"].items()} == dict(enumerate(expected))
        assert {int(k): v for k, v in panel["player_to_score"].items()} == dict(enumerate(expected))
    assert [int(player.score_history[-1]) for player in state.player_list] == expected
    replay = RecordSim(state.game_record)
    replay.apply_all("round_index_1")
    assert [replay.players[i].score for i in range(4)] == expected
    state._record_team_round()
    offender_team = state.player_list[offender].team_id
    assert state.team_round_history[-1]["score_changes"] == {offender_team: -12, 1 - offender_team: 12}


@pytest.mark.parametrize("table", ["open", "closed"])
def test_passive_flower_pig_has_no_extra_transfer(table):
    state, _, _ = make_state(table)
    mark_pig(state, 0, active=False)
    settle(state)
    assert [player.score for player in state.player_list] == [0, 0, 0, 0]
    assert state.duplicate_adjustments == [0, 0]


@pytest.mark.parametrize("table", ["open", "closed"])
def test_penalty_credit_is_retained_when_ready_opponents_also_collect_noten_payments(table):
    state, _, _ = make_state(table)
    mark_pig(state, 0)
    for seat in (1, 3):
        state.player_list[seat].hand_tiles = WIN[:-1]
    settle(state)
    assert [player.score for player in state.player_list] == [-14, 8, -2, 8]
    assert [int(player.score_history[-1]) for player in state.player_list] == [-14, 8, -2, 8]
    assert state.duplicate_adjustments == [0, 0]


@pytest.mark.parametrize("table", ["open", "closed"])
@pytest.mark.parametrize("offenders,expected", [
    ((0, 1), [-6, -6, 6, 6]),
    ((0, 2), [-12, 12, -12, 12]),
    ((0, 1, 2, 3), [0, 0, 0, 0]),
])
def test_multiple_penalties_accumulate_without_dropping_opponent_credits(table, offenders, expected):
    state, _, _ = make_state(table)
    for offender in offenders:
        mark_pig(state, offender)
    settle(state)
    assert [player.score for player in state.player_list] == expected
    assert state.duplicate_adjustments == [0, 0]


@pytest.mark.parametrize("table", ["open", "closed"])
def test_team_early_finish_still_skips_flower_pig_settlement(table):
    state, _, _ = make_state(table)
    mark_pig(state, 0)
    for seat in (1, 3):
        state.player_list[seat].is_hu = True
        state.player_list[seat].hu_order = seat
    settle(state)
    assert [player.score for player in state.player_list] == [0, 0, 0, 0]


@pytest.mark.parametrize("table", ["open", "closed"])
@run_async
async def test_pair_submission_checkpoint_and_imp_credit_the_penalty_exactly_once(table):
    state, _, match = make_state(table)
    mark_pig(state, 0)
    with patch(__package__ + ".SichuanGameState.asyncio.sleep", new_callable=AsyncMock):
        await state._settle_liuju()
    state._record_team_round()
    other = "closed" if table == "open" else "open"
    await asyncio.gather(state._after_round_settled(), match.submit(other, 1, [0, 0], [0, 0], {}))
    totals = list(team_scores(state.player_list).values())
    entry = match.data["results"][0]["tables"][table]
    assert entry["scores"] == entry["delta"] == totals
    assert entry["adjustments"] == [0, 0]
    assert match.data["results"][0]["raw_difference"] == totals
    assert match.data["scores"] == ([0, 5] if table == "open" else [5, 0])
    checkpoint = match.data["checkpoints"][table]
    assert checkpoint["scores"] == [-12, 6, 0, 6]
    assert [int(history[-1]) for history in checkpoint["score_history"]] == checkpoint["scores"]
    before = deepcopy(match.data)
    await match.submit(table, 1, totals, [0, 0], state.game_record["game_round"]["round_index_1"])
    assert match.data == before
