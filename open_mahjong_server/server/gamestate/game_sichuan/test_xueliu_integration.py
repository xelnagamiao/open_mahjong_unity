"""血流集成回归：真实算番、操作等待、广播与牌谱；仅替换持久化和网络出口。"""
import asyncio
from unittest.mock import patch

import pytest

from .XueliuGameState import XueliuGameState
from .action_check import check_action_hand_action, check_action_after_cut, refresh_waiting_tiles
from .wait_action import wait_action
from ..public.game_record_manager import init_game_record, init_game_round
from ..verifier.host import build_db_manager, build_game_server, build_room_data


WIN_HAND = [11, 12, 13, 14, 15, 16, 21, 22, 23, 25, 25]
WAIT_HAND = WIN_HAND[:-1]


def full_state():
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    room = build_room_data(seed=20260922, room_id=900001, hepai_limit=0)
    room.update(room_rule="sichuan", sub_rule="sichuan/xueliu", step_timer=1, round_timer=2)
    state = XueliuGameState(server, room, server.calculation_service, db, "xueliu-test")
    for i, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = i
        player.hand_tiles = list(WAIT_HAND)
    state.tiles_list = [19, 29, 39] * 3
    init_game_record(state)
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


async def fast_sleep(delay=0, result=None):
    return result


def settle_win(state):
    with patch(__package__ + ".XueliuGameState.asyncio.sleep", fast_sleep):
        asyncio.run(state._settle_win())


def test_closed_kong_keeps_concealed_self_draw_bonus():
    state, _ = full_state()
    fan, names = state.calculation_service.Sichuan_xueliu_hepai_check(
        [12, 13, 14, 21, 22, 23, 25, 25], ["G11"], ["自摸"], 25)
    assert "门清自摸" in names
    assert fan == 4


@pytest.mark.parametrize("hand,melds", [
    ([11] * 5 + [12, 13, 14, 21, 22, 23], []),
    ([11, 11, 21, 22, 23, 24, 25, 26], ["G11"]),
    ([10, 11, 12, 21, 22, 23, 24, 25, 26, 27, 27], []),
    ([11, 12, 13, 21, 22, 23, 25, 25], ["s15"]),
])
def test_invalid_physical_tiles_and_chow_melds_cannot_win(hand, melds):
    state, _ = full_state()
    assert state.calculation_service.Sichuan_xueliu_hepai_check(hand, melds, ["自摸"], hand[-1]) == (0, [])


def test_initial_dealer_cannot_claim_heavenly_win():
    state, _ = full_state()
    state.player_list[0].hand_tiles = list(WIN_HAND)
    refresh_waiting_tiles(state, 0, is_first_action=True)
    assert "hu_self" not in check_action_hand_action(state, 0, is_first_action=True)[0]


def test_repeated_self_draw_preserves_ten_tile_base_and_continuation():
    state, messages = full_state()
    player = state.player_list[0]
    for count in (1, 2):
        player.hand_tiles = list(WIN_HAND)
        player.has_draw_slot = True
        player.waiting_tiles = {25}
        state.current_player_index = 0
        state.sichuan_hu_results = {0: {"fan": 2, "fan_list": ["基本胡", "门清自摸"], "hepai_tile": 25}}
        state.pending_win = {"type": "zimo"}
        settle_win(state)
        assert player.hand_tiles == WAIT_HAND
        assert player.has_draw_slot is False
        assert player.win_count == count
        assert player.is_hu is False
        assert player.locked_waiting_tiles == {25}
        assert state.game_status == "deal_card"
        result = next(m['message']['show_result_info'] for m in reversed(messages) if m['type'].endswith('/show_result'))
        assert result['round_continues'] is True
        assert result['next_status'] == 'round_continue'
    assert [p.score for p in state.player_list] == [12, -4, -4, -4]


def test_wall_end_uses_maximum_self_draw_fan_and_flower_pig_multiplier():
    state, messages = full_state()
    for player in state.player_list[1:]:
        player.hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 35]
    asyncio.run(state._settle_xueliu_wall_end())
    assert [p.score for p in state.player_list] == [12, -4, -4, -4]
    results = [m['message']['show_result_info'] for m in messages if m['user_id'] == 101]
    assert results[-1]['liuju_status_final'] is True


def test_wall_end_all_tenpai_still_emits_final_panel():
    state, messages = full_state()
    asyncio.run(state._settle_xueliu_wall_end())
    results = [m['message']['show_result_info'] for m in messages if m['user_id'] == 101]
    assert any(m.get('liuju_status_final') for m in results)
    assert results[-1]['liuju_step'] == 'chajiao'
    assert results[-1]['liuju_status'] == {3: 'ting'} or results[-1]['liuju_status'] == {'3': 'ting'}
    assert [p.score for p in state.player_list] == [0, 0, 0, 0]


def test_multi_ron_waits_for_every_eligible_response_and_excludes_pass():
    async def scenario():
        state, _ = full_state()
        state.current_player_index = 0
        state.player_list[0].discard_tiles = [25]
        for player in state.player_list[1:]:
            refresh_waiting_tiles(state, player.player_index)
        state.action_dict = check_action_after_cut(state, 25)
        state.game_status = 'waiting_action_after_cut'
        task = asyncio.create_task(wait_action(state))
        await asyncio.sleep(0)
        await state.action_queues[1].put({'action_type': 'hu'})
        state.action_events[1].set()
        for _ in range(5):
            await asyncio.sleep(0)
        assert not task.done(), 'must still wait for other possible winners'
        for i in (2, 3):
            await state.action_queues[i].put({'action_type': 'pass'})
            state.action_events[i].set()
        await asyncio.wait_for(task, 2)
        assert list(state.sichuan_hu_results) == [1]
        assert state.player_list[2].shunhe_passed_max_fan == 1
        assert state.player_list[3].shunhe_passed_max_fan == 1
        with patch(__package__ + '.XueliuGameState.asyncio.sleep', fast_sleep):
            await state._settle_win()
        assert [p.win_count for p in state.player_list] == [0, 1, 0, 0]
        assert state.player_list[1].hand_tiles == WAIT_HAND
    asyncio.run(scenario())


@pytest.mark.parametrize('chosen', [None, [], [11, 11], [11, 12, 21], [True, 12, 13], ['11', 12, 13], [11.0, 12, 13], [11, 11, 11], [10, 11, 12]])
def test_throw_rejects_bad_payloads_without_mutating_hand(chosen):
    state, _ = full_state()
    assert state._validate_xueliu_throw_tiles(0, chosen) is None
    assert state.player_list[0].hand_tiles == WAIT_HAND


async def enqueue(state, index, action, **payload):
    await state.action_queues[index].put({'action_type': action, **payload})
    state.action_events[index].set()


def test_opening_offline_fallback_and_record_header():
    async def scenario():
        state, messages = full_state()
        for i, player in enumerate(state.player_list):
            player.hand_tiles = WAIT_HAND + [31, 32, 33] + ([25] if i == 0 else [])
            player.tag_list.append('offline')
        await asyncio.wait_for(state._opening_phase(), 2)
        assert [len(p.hand_tiles) for p in state.player_list] == [11, 10, 10, 10]
        assert all(len(p.xueliu_throw_tiles) == 3 and len({t//10 for t in p.xueliu_throw_tiles}) == 1 for p in state.player_list)
        header = state.game_record['game_round'][f'round_index_{state.round_index}']
        assert [len(header[f'p{i}_tiles']) for i in range(4)] == [11, 10, 10, 10]
        assert len(header['xueliu_throw_tiles']) == 4
    asyncio.run(scenario())


def test_opening_reconnect_and_timeout_real_clock():
    async def scenario():
        state, messages = full_state()
        for i, player in enumerate(state.player_list):
            player.hand_tiles = WAIT_HAND + [31, 32, 33] + ([25] if i == 0 else [])
        task = asyncio.create_task(state._opening_phase())
        await asyncio.sleep(0.05)
        await state.player_reconnect(state.player_list[0].user_id)
        asks = [m['message']['ask_hand_action_info'] for m in messages if m['user_id'] == 101 and 'ask_hand_action_info' in m['message']]
        assert asks[-1]['action_list'] == ['xueliu_throw_three']
        assert 1 <= asks[-1]['remaining_time'] <= 10
        # 三家正常提交；一家保持在线但沉默，实际等待 10 秒走服务端超时。
        for i in (1, 2, 3):
            await enqueue(state, i, 'xueliu_throw_three', selected_tiles=[31, 32, 33], _action_tick=state.server_action_tick)
        await asyncio.wait_for(task, 12)
        assert [len(p.hand_tiles) for p in state.player_list] == [11, 10, 10, 10]
        assert len(state.player_list[0].xueliu_throw_tiles) == 3
    asyncio.run(scenario())


def test_queued_invalid_and_stale_actions_do_not_hide_valid_action():
    async def scenario():
        state, _ = full_state()
        state.current_player_index = 0
        state.player_list[0].hand_tiles = list(WIN_HAND)
        state.player_list[0].has_draw_slot = True
        state.game_status = 'waiting_hand_action'
        state.action_dict = {0: ['cut'], 1: [], 2: [], 3: []}
        await enqueue(state, 0, 'hu_self')
        await enqueue(state, 0, 'cut', TileId=25, cutClass=True, _action_tick=state.server_action_tick-1)
        await enqueue(state, 0, 'cut', TileId=25, cutClass=True, _action_tick=state.server_action_tick)
        await asyncio.wait_for(wait_action(state), 2)
        assert state.player_list[0].hand_tiles == WAIT_HAND
        assert state.player_list[0].discard_tiles == [25]
    asyncio.run(scenario())


def test_router_rejects_wrong_seat_stale_tick_and_unknown_tile():
    from ..gamestate_router import handle_cut_tile
    async def scenario():
        state, _ = full_state()
        state.game_status = 'waiting_hand_action'
        state.waiting_players_list = [0]
        state.action_dict = {0: ['cut'], 1: [], 2: [], 3: []}
        for i, tile, tick in ((1, 11, state.server_action_tick), (0, 11, state.server_action_tick-1), (0, 39, state.server_action_tick)):
            player = state.player_list[i]
            conn = state.game_server.user_id_to_connection[player.user_id]
            await handle_cut_tile(state.game_server, f'conn-{player.user_id}', {'gamestate_id':state.gamestate_id, 'action_tick':tick, 'TileId':tile, 'cutClass':False}, conn.websocket)
        assert all(q.empty() for q in state.action_queues.values())
    asyncio.run(scenario())


def test_post_win_forces_drawn_tile_discard():
    async def scenario():
        state, _ = full_state()
        player = state.player_list[0]
        player.hand_tiles = WAIT_HAND + [29]
        player.has_draw_slot = player.post_hu_lock = player.has_won = True
        player.locked_waiting_tiles = {25}
        state.current_player_index = 0
        state.game_status = 'waiting_hand_action'
        state.action_dict = {0: ['cut'], 1: [], 2: [], 3: []}
        await enqueue(state, 0, 'cut', TileId=11, cutClass=False)
        await wait_action(state)
        assert player.hand_tiles == WAIT_HAND
        assert player.discard_tiles == [29]
    asyncio.run(scenario())


def test_robbed_kong_reconnect_reverts_to_pung_and_removes_kong_fan():
    from .action_check import check_action_jiagang
    async def scenario():
        state, messages = full_state()
        source = state.player_list[0]
        source.hand_tiles = [11, 12, 13, 14, 15, 16, 29]
        source.combination_tiles = ['g25']
        source.combination_mask = [[0,25,0,25,3,25,1,25]]
        state._record_gang_score(0, 25, 'xiayu1')
        for p in state.player_list[1:]:
            p.hand_tiles = [11,12,13,14,15,16,23,24,29,29]
            refresh_waiting_tiles(state, p.player_index)
        state.current_player_index = 0
        state.jiagang_tile = 25
        state.action_dict = check_action_jiagang(state,25)
        state.game_status = 'waiting_action_qianggang'
        await state.player_reconnect(state.player_list[1].user_id)
        asks = [m['message']['ask_other_action_info'] for m in messages if 'ask_other_action_info' in m['message']]
        assert asks[-1]['cut_tile'] == 25
        for i, action in ((1,'hu'),(2,'hu'),(3,'pass')):
            await enqueue(state,i,action)
        await wait_action(state)
        assert source.combination_tiles == ['k25']
        assert source.gang_score_records == []
        with patch(__package__ + '.XueliuGameState.asyncio.sleep', fast_sleep):
            await state._settle_win()
        assert [p.win_count for p in state.player_list] == [0,1,1,0]
        assert [p.score for p in state.player_list] == [-2,1,1,0]
        assert source.discard_tiles == []
    asyncio.run(scenario())


def test_last_tile_win_waits_for_wall_settlement_before_match_end():
    state, messages = full_state()
    state.tiles_list = []
    state.current_round = state.max_round * 4
    state.player_list[0].hand_tiles = list(WIN_HAND)
    state.player_list[0].waiting_tiles = {25}
    state.pending_win = {'type':'zimo'}
    state.sichuan_hu_results = {0:{'fan':2,'fan_list':['基本胡','门清自摸'],'hepai_tile':25}}
    settle_win(state)
    assert state.game_status == 'END'
    wins = [m['message']['show_result_info'] for m in messages if m['type'].endswith('/show_result')]
    assert all(x['next_status'] == 'round_continue' for x in wins)
    asyncio.run(state._settle_xueliu_wall_end())
    assert messages[-1]['message']['show_result_info']['next_status'] == 'match_end'


def test_meld_fan_is_paid_per_opponent_and_zero_sum():
    state, _ = full_state()
    state._record_gang_score(1, 11, 'xiayu2')
    state._record_gang_score(2, 22, 'guafeng')
    assert [p.score for p in state.player_list] == [0,0,0,0]
    assert state._apply_hu_score_changes(0, 6, True, fan=6) == {0:21,1:-8,2:-7,3:-6}
    assert sum(p.score for p in state.player_list) == 0


@pytest.mark.parametrize('hand,melds,tile,waits,allowed', [
    ([15,12,11,16,12,12,13], ['G36'], 12, {14,17}, False),
    ([25,26,27,17,22,22,15,28,22,25], [], 22, {16}, True),
])
def test_post_win_concealed_kong_must_preserve_every_wait(hand, melds, tile, waits, allowed):
    state, _ = full_state()
    player = state.player_list[0]
    player.hand_tiles = hand + [tile]
    player.combination_tiles = melds
    player.has_draw_slot = player.post_hu_lock = player.has_won = True
    player.waiting_tiles = player.locked_waiting_tiles = waits
    assert ('angang' in check_action_hand_action(state, 0)[0]) is allowed


def test_last_wall_tile_prohibits_kong_and_post_win_claims():
    state, _ = full_state()
    state.tiles_list = []
    player = state.player_list[0]
    player.hand_tiles = [11]*4 + [12,13,14,21,22,23,25]
    assert 'angang' not in check_action_hand_action(state, 0)[0]
    player = state.player_list[1]
    player.hand_tiles = [25]*3 + [11,12,13,14,15,16,28]
    player.has_won = player.post_hu_lock = True
    actions = check_action_after_cut(state,25)[1]
    assert 'peng' not in actions and 'gang' not in actions


def test_wall_end_no_one_tenpai_is_zero_sum_and_finalized():
    state, messages = full_state()
    for p in state.player_list:
        p.hand_tiles = [11,13,15,17,19,21,23,25,27,29]
    asyncio.run(state._settle_xueliu_wall_end())
    assert [p.score for p in state.player_list] == [0,0,0,0]
    assert messages[-1]['message']['show_result_info']['liuju_status_final'] is True
