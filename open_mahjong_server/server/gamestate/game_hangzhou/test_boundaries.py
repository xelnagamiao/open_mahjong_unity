"""Rejected transitions preserve state; fallback input and clock recovery work."""

import asyncio
from copy import deepcopy

import pytest

from .actions import empty_actions, kong_tiles, turn_actions
from .bot import choose_action
from .hints import private_hints
from .test_flow import PLAIN, FLOAT, turn, act, river
from .state_machine import Phase as P
from .HangzhouGameState import HangzhouGameState


def test_scoring_and_waiting_wrappers_keep_source_and_tile_identity():
    s = turn()
    assert s.score_win(0, "discard", 42) is None
    s.player_list[0].is_hu = True
    assert not s.can_win(0, "self_draw", 42)
    s.player_list[0].is_hu = False
    s.player_list[0].has_draw_slot = False
    assert not s.can_win(0, "self_draw", 42)
    s.player_list[0].has_draw_slot = True
    assert s.legal_discard_tiles(0) == set(PLAIN)
    assert 42 in s.action_policy.refresh_waiting_tiles(s, 0, exclude_last_tile=True)
    assert s.action_policy.refresh_waiting_tiles(s, 0) == set()
    s.player_list[0].hand_tiles = PLAIN[:-1]
    s.player_list[0].has_draw_slot = False
    assert 42 in s.action_policy.refresh_waiting_tiles(s, 0)


@pytest.mark.parametrize("bad", [None, True, 46, 99, "11"])
def test_invalid_kong_target_does_not_remove_any_physical_tile(bad):
    s = turn([11] * 4 + PLAIN[4:])
    before = deepcopy(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):
        act(s, 0, "angang", target_tile=bad)
    assert s.player_list[0].hand_tiles == before and not s.burned_tiles
    assert choose_action(s, 0, ["angang", "cut"]) == {"action_type": "angang", "target_tile": 11}


@pytest.mark.parametrize("data", [
    {"TileId": 11, "cutClass": True}, {"TileId": 42, "cutClass": True, "cutIndex": 0},
    {"TileId": 43, "cutClass": False}, {"TileId": 11, "cutIndex": 3},
    {"TileId": 42, "cutIndex": 14},
])
def test_invalid_draw_identity_or_position_rejected(data):
    s = turn()
    with pytest.raises(ValueError):
        act(s, 0, "cut", **data)
    assert not s.discard_log


def test_omitted_draw_class_resolves_true_draw_or_held_identity_and_drag_index():
    for data, tile, drawn in (({"TileId":42},42,True), ({"TileId":11},11,False),
                              ({"TileId":11,"cutIndex":0},11,False),
                              ({"TileId":11,"cutClass":False,"cutIndex":2},11,False)):
        s = turn()
        act(s,0,"cut",**data)
        assert s.domain_events[-1]["tile"] == tile and s.domain_events[-1]["cutClass"] == drawn
    s = turn(PLAIN[:-1]+[43])
    with pytest.raises(ValueError, match="手切"):
        act(s,0,"cut",TileId=43,cutClass=False)


def test_locked_player_with_no_draw_slot_cannot_offer_a_fake_discard():
    s = turn()
    s.cai_discard_locks = {1}
    s.player_list[0].has_draw_slot = False
    s.tiles_list = s.tiles_list[:21]
    assert turn_actions(s,0,True) == empty_actions()
    assert not kong_tiles(s,0,"jiagang")


def test_settlement_rejects_wrong_phase_player_source_and_unqualified_hand():
    s = turn()
    with pytest.raises(ValueError, match="行动者"):
        s.settle_win(1,"self_draw",42)
    with pytest.raises(ValueError, match="自摸牌"):
        s.settle_win(0,"self_draw",11)
    with pytest.raises(ValueError, match="只能"):
        s.settle_win(0,"discard",42)
    with pytest.raises(ValueError, match="十风"):
        s.settle_win(0,"ten_winds",42)
    s.player_list[0].hand_tiles[-1] = 43
    s.player_list[0].pre_draw_tiles = s.player_list[0].hand_tiles[:-1]
    with pytest.raises(ValueError, match="资格"):
        s.settle_win(0,"self_draw",43)
    s.end_draw()
    with pytest.raises(ValueError, match="已经"):
        s.end_draw()
    s.open_action_window(s._window(P.END))
    with pytest.raises(ValueError, match="当前阶段"):
        act(s)


def test_wall_boundary_draw_helper_never_consumes_protected_tail():
    for replacement,count in ((False,20),(True,21)):
        s = turn()
        s.tiles_list = s.tiles_list[:count]
        before = list(s.tiles_list)
        s.draw_for(0,replacement=replacement)
        assert s.tiles_list == before and s.machine.phase == P.END


def test_duplicate_win_event_and_round_finalization_do_not_duplicate_replay():
    s = turn()
    act(s,0,"hu_self")
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    count = len(ticks)
    s.record_visible_action({"action":"hu_self","player":0,"tile":42})
    assert len(ticks) == count
    s.finalize_round_recording()
    s.record_state()
    assert ticks[-1] == ["end"]
    s = turn([21,22,23,31,32,33,41,41,41,42,42],("s12",))
    act(s,0,"hu_self")
    s.finalize_round_recording()
    assert s.player_list[0].record_counter.fulu_times == 1


def test_empty_unstarted_restore_and_short_invalid_hint_are_safe():
    s = HangzhouGameState()
    s.tips = True
    assert len(s.restore_payloads(0)) == 1
    assert private_hints(s,0)["waiting_tiles"] == []
    assert s._adapt({"type":"gamestate/hangzhou/ready_status"},0)["player_index"] == 0
    asyncio.run(s.send_realtime_spectator_snapshot(999,0))


def test_reconnect_clock_does_not_restart_turn_or_claim_budget():
    async def run():
        s = turn(step_timer=1, round_timer=4)
        waiter = asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        clock = s._action_clocks[0]
        clock.started_at = s._timing_now() - 2
        prompt = s.build_pending_action_payload(0)["ask_hand_action_info"]
        assert prompt["step_remaining"] == 0 and prompt["remaining_time"] == 3
        await s.submit_action(0,"cut",TileId=42,cutClass=True)
        await waiter
        s = turn(step_timer=5, round_timer=20)
        s.player_list[1].hand_tiles = [41,41]+PLAIN[:11]
        river(s,0,41)
        s._action_clocks[1].started_at = s._timing_now() - 8
        prompt = s.build_pending_action_payload(1)["ask_other_action_info"]
        assert prompt["step_remaining"] == 0 and prompt["remaining_time"] == 17
    asyncio.run(run())


def test_cancel_live_match_cancels_owned_bot_tasks():
    async def run():
        s = HangzhouGameState(room_data=dict(HangzhouGameState._default_room_data(),player_list=[1,2,3,4],bot_speed="slow"))
        s._send_claim_protection_payload = __import__('unittest.mock',fromlist=['AsyncMock']).AsyncMock()
        task=asyncio.create_task(s.run_game_loop())
        for _ in range(20):
            if s.bot_tasks:break
            await asyncio.sleep(.005)
        assert s.bot_tasks
        task.cancel()
        with pytest.raises(asyncio.CancelledError):await task
        assert not s.bot_tasks
    asyncio.run(run())


def test_draw_replay_is_closed_once_and_online_reconnect_keeps_presence():
    s=turn()
    s.end_draw();s.finalize_round_recording()
    ticks=s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[-3][0]=="liuju" and ticks[-1]==["end"]
    before=len(ticks)
    s.finalize_round_recording()
    assert len(ticks)==before
    async def run():
        s=turn()
        s.send_payload_to_player=__import__('unittest.mock',fromlist=['AsyncMock']).AsyncMock()
        await s.player_reconnect(101)
        assert "offline" not in s.player_list[0].tag_list
        assert s.send_payload_to_player.await_count==2
    asyncio.run(run())


def test_corrupt_internal_timeout_window_fails_instead_of_spinning():
    async def run():
        s=turn()
        s.action_dict={0:["unknown_internal_action"]}
        s.waiting_players_list=[0]
        with pytest.raises(RuntimeError,match="没有合法超时动作"):
            await asyncio.wait_for(s.wait_action(0),1)
    asyncio.run(run())
