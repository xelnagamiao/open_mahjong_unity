import asyncio
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest

from ...game_calculation.filipino.rules import PHYSICAL, meld_tiles, TILES
from ...game_calculation.filipino.test_rules import NORMAL
from .FilipinoGameState import FilipinoGameState
from .state_machine import Phase as P
from .bot import choose_action
from .get_action import handle_action


def state(seed=1, **config):
    data = FilipinoGameState._default_room_data()
    data.update(random_seed=seed, detailed_config=config, bot_speed="instant")
    return FilipinoGameState(room_data=data)


def act(s, replies=None):
    window = s.live_pending_window
    responses = replies if replies is not None else {i: choose_action(s, i) for i, a in window["actions"].items() if a}
    return s.apply_action_results(window, responses)


def turn(hand, **config):
    s = state(**config)
    s.tiles_list = [18] * 30
    s.opening_queue = None
    p = s.player_list[0]
    p.hand_tiles = list(hand)
    p.has_draw_slot = True
    p.last_drawn_tile = hand[-1]
    s.open_action_window(s.begin_turn(0))
    return s


def physical(s):
    return Counter(s.tiles_list + [t for p in s.player_list for t in
        p.hand_tiles + p.huapai_list + p.discard_tiles + p.won_tiles +
        [t for c in p.combination_tiles for t in meld_tiles(c)]])


@pytest.mark.parametrize("seed", range(10))
def test_complete_round_conserves_tiles_scores_replay_and_privacy(seed):
    s = state(seed)
    s.initialize_round()
    s.start_game_recording()
    s.start_round_recording()
    s.open_action_window(s.opening_window())
    steps = 0
    while s.machine.phase != P.END:
        assert physical(s) == Counter(PHYSICAL)
        for viewer in range(4):
            payload = s.build_game_start_payload(viewer)
            assert all(p["hand_tiles"] is None for i, p in enumerate(payload["game_info"]["players_info"]) if i != viewer)
        act(s)
        steps += 1
        assert steps < 600
    assert physical(s) == Counter(PHYSICAL)
    s.finalize_round_recording()
    s.finalize_round_recording()
    assert sum(p.score for p in s.player_list) == 0
    assert all(len(p.score_history) == 1 for p in s.player_list)
    assert all(not p.record_counter.recorded_fans for p in s.player_list)
    assert s.game_record["game_title"]["rule"] == "filipino"
    assert s.game_record["game_title"]["detailed_config"]["rule_version"] == s.rule_version


def test_flower_replacement_goes_to_next_player_before_replacing_new_flowers():
    s = state()
    s.player_list[0].hand_tiles = [41] + [11] * 16
    s.player_list[1].hand_tiles = [42] + [12] * 15
    s.tiles_list = [19, 18, 43]
    s.opening_queue = [0, 1, 2, 3]
    s.open_action_window(s.opening_window())
    act(s)
    assert s.current_player_index == 1
    act(s)
    assert s.current_player_index == 0
    assert s.player_list[0].huapai_list == [41]


def test_no_flowers_and_threshold_awards_are_zero_sum_and_once():
    s = turn(NORMAL)
    p = s.player_list[0]
    p.huapai_list = [41] * 12
    p.hand_tiles[-1] = 42
    s.open_action_window(s.begin_turn(0))
    act(s)
    assert s.round_changes == [3, -1, -1, -1]
    s.apply_deferred_score_changes()
    before = [p.score for p in s.player_list]
    s.apply_deferred_score_changes()
    assert before == [p.score for p in s.player_list]


def test_sagasa_requires_the_current_draw_and_kong_preserves_hand_size():
    s = turn(NORMAL[3:-1] + [11], ambitions=False)
    p = s.player_list[0]
    p.combination_tiles = ["k11"]
    p.combination_mask = [[1, 11, 0, 11, 0, 11]]
    s.open_action_window(s.begin_turn(0))
    assert s.kong_tiles(0, "jiagang") == {11}
    act(s, {0: dict(action_type="jiagang", target_tile=11)})
    assert p.combination_tiles == ["g11"] and len(p.hand_tiles) == 14
    assert s.round_changes == [6, -2, -2, -2]
    p.hand_tiles[-1], p.last_drawn_tile = 12, 12
    assert not s.kong_tiles(0, "jiagang")


def test_invalid_cut_and_stale_window_do_not_mutate_table():
    s = turn(NORMAL)
    before = deepcopy(s.player_list[0].hand_tiles)
    window = s.live_pending_window
    with pytest.raises(ValueError):
        act(s, {0: dict(action_type="cut", TileId=41)})
    assert s.player_list[0].hand_tiles == before
    with pytest.raises(ValueError):
        act(s, {0: dict(action_type="cut", TileId=11, cutClass=True)})
    act(s, {0: dict(action_type="cut", TileId=11, cutClass=False)})
    with pytest.raises(ValueError):
        s.apply_action_results(window, {0: dict(action_type="cut", TileId=12)})


def test_simultaneous_win_uses_nearest_seat_and_records_payer():
    s = turn(NORMAL, ambitions=False)
    for i in (1, 2):
        s.player_list[i].hand_tiles = NORMAL[:-1]
    s.start_game_recording()
    s.start_round_recording()
    act(s, {0: dict(action_type="cut", TileId=29, cutClass=True)})
    responses = {i: dict(action_type="hu" if i in (1, 2) else "pass") for i, actions in s.action_dict.items() if actions}
    act(s, responses)
    assert s.deferred_hu_settlements[0]["winner"] == 1
    assert s.deferred_hu_settlements[0]["discarder"] == 0
    assert s.player_list[0].record_counter.fangchong_times == 1
    s.finalize_round_recording()
    assert [p.score for p in s.player_list] == [-4, 12, -4, -4]
    with pytest.raises(ValueError):
        s.win(2, "discard", 29, 0)


def test_last_discard_can_win_but_cannot_claim_a_kong():
    s = turn(NORMAL, ambitions=False)
    s.tiles_list = []
    s.player_list[1].hand_tiles = NORMAL[:-1]
    act(s, {0: dict(action_type="cut", TileId=29, cutClass=True)})
    assert s.action_dict[1] == ["hu", "pass"]
    act(s, {1: dict(action_type="pass")})
    assert s.machine.phase == P.END


def test_reconnect_and_network_reject_observer_stale_tick_and_duplicate():
    s = turn(NORMAL)
    ws = SimpleNamespace(send_json=AsyncMock())
    server = SimpleNamespace(players={"owner": SimpleNamespace(user_id=101), "observer": SimpleNamespace(user_id=999)},
        gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s))
    message = dict(type="gamestate/filipino/cut_tile", gamestate_id=s.gamestate_id,
                   action_tick=s.server_action_tick, TileId=29, cutClass=True)
    asyncio.run(handle_action(server, "observer", message, ws))
    assert s.action_queues[0].empty()
    asyncio.run(handle_action(server, "owner", {**message, "action_tick": -1}, ws))
    assert s.action_queues[0].empty() and ws.send_json.await_count == 1
    asyncio.run(handle_action(server, "owner", message, ws))
    asyncio.run(handle_action(server, "owner", message, ws))
    assert s.action_queues[0].qsize() == 1 and ws.send_json.await_count == 2
    restored = s.restore_payloads(0)
    assert restored[-1]["action_tick"] == s.server_action_tick


def test_ready_required_and_rotation_preserves_identity():
    s = turn(NORMAL, ambitions=False, dealer_mode="rotate")
    s.current_round = 2
    act(s, {0: dict(action_type="hu_self")})
    with pytest.raises(RuntimeError):
        s.initialize_round()
    asyncio.run(s.run_round_ready_phase(timeout=0))
    old = [p.user_id for p in s.player_list]
    s.advance_round_after_ready()
    s.initialize_round()
    assert [p.user_id for p in s.player_list] == old[1:] + old[:1]
    assert s.current_round == 3 and s.round_index == 2


def test_late_network_action_is_rejected_before_queue_mutation():
    import asyncio
    async def run():
        s=turn(NORMAL)
        s._active_deadlines={0:asyncio.get_running_loop().time()-0.01}
        before=list(s.player_list[0].hand_tiles)
        with pytest.raises(ValueError,match='时限'):
            await s.submit_action(0,'cut',TileId=NORMAL[-1],cutClass=True)
        assert s.action_queues[0].empty() and s.player_list[0].hand_tiles==before
    asyncio.run(run())


def test_hand_cut_index_must_match_physical_tile_before_mutation():
    s=turn(NORMAL);before=list(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):act(s,{0:dict(action_type='cut',TileId=before[3],cutIndex=0,cutClass=False)})
    assert s.player_list[0].hand_tiles==before
