"""血流单桌 2V2：两阶段开局、私密消息、有效分、控猪及强制点和。"""
import asyncio
import random
from collections import Counter
from unittest.mock import AsyncMock, patch

import pytest

from .XueliuDoublesGameState import XueliuDoublesGameState
from .action_check import check_action_after_cut, refresh_waiting_tiles
from .boardcast import _base_game_info, _build_players_info, reconnected_send_pending_ask
from .shunhe import apply_passed_win_shunhe
from .wait_action import _prepare_dingque_cut_action, wait_action
from .test_action_window import submit
from ..public.game_record_manager import init_game_record, init_game_round
from ..public.lifecycle import cancel_auxiliary_tasks
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ...room.room_teams import ensure_teams, validate_team_start, assign_team
from ...room.room_validators import SichuanRoomValidator

WIN = [11,12,13,14,15,16,21,22,23,26,27,28,25,25]


def make_state():
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    room = build_room_data(seed=20261009, room_id="900019", hepai_limit=64)
    room.update(room_rule="sichuan", sub_rule="sichuan/xueliu_doubles", team_by_seat=[0,1,0,1], blood_battle=True)
    state = XueliuDoublesGameState(server, room, server.calculation_service, db, "xueliu-doubles")
    for i, p in enumerate(state.player_list):
        p.player_index = p.original_player_index = i
    state._reset_round_state()
    for p in state.player_list:
        p.hand_tiles = WIN[:-1].copy()
        p.dingque_suit = 3
    state.tiles_list = [19,29,39] * 3
    init_game_record(state)
    state._record_rule_profile()
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


def test_room_profile_roster_and_snapshot():
    v = SichuanRoomValidator(sub_rule="sichuan/xueliu_doubles", room_name="血流双打", game_round=1, round_timer=20, step_timer=5, blood_battle=True, hepai_limit=64)
    assert not v.blood_battle and v.hepai_limit == 0
    room = dict(sub_rule=v.sub_rule, seat_list=[101,102,0,0], ready_list=[102], host_user_id=101)
    assert ensure_teams(room) == [0,1,0,1]
    validate_team_start(room, 0)
    assign_team(room, 101, 2, -1, 0)
    assert room['ready_list'] == [] and room['roster_revision'] == 1
    with pytest.raises(ValueError):
        validate_team_start(room, 0)
    state, _ = make_state()
    assert state.is_team_game and state.is_xueliu and not state.is_doubles and not state.blood_battle
    assert _base_game_info(state)['sichuan_teams']['team_by_player_index'] == [0,1,0,1]
    assert state.game_record['game_title']['doubles_rule_profile']['direct_kong_payment'] == 2
    for seed in range(12):
        state._shuffle_players(random.Random(seed))
        assert state.player_list[0].team_id == state.player_list[2].team_id != state.player_list[1].team_id == state.player_list[3].team_id


@pytest.mark.parametrize('round_number', range(1,9))
def test_exchange_two_stages_rotate_preserve_tiles_and_hide_teammate_ranks(round_number):
    state, messages = make_state()
    state.current_round = round_number
    deck = [s*10+r for s in (1,2,3) for r in range(1,10) for _ in range(4)]
    random.Random(round_number).shuffle(deck)
    before = [deck[:14]] + [deck[14+13*i:27+13*i] for i in range(3)]
    for p, hand in zip(state.player_list, before):
        p.hand_tiles = hand.copy()
    asks = []
    async def respond(current):
        active = list(current.waiting_players_list)
        asks.append(active)
        assert len(active) == 2
        for index in range(4):
            view = current.exchange_view(index)
            assert set(view) == {'stage','active_players','selection_done','teammate_suit','outgoing_suit','animating','animation_seconds'}
            assert not view['animating']
            expected = current.player_list[(index+2)%4].xueliu_throw_tiles
            assert view['teammate_suit'] == (expected[0]//10 if len(asks)==2 and index in active else 0)
            await reconnected_send_pending_ask(current, current.player_list[index].user_id)
            for other in _build_players_info(current, current.player_list[index].user_id):
                if other['player_index'] != index:
                    assert other['hand_tiles'] is None and other['xueliu_throw_tiles'] == []
        for index in active:
            chosen = current._default_xueliu_throw_tiles(index)
            # Old stage packets and invalid choices must not consume a hand.
            for tick, tiles in [(current.server_action_tick-1, chosen), (current.server_action_tick, [99,99,99]), (current.server_action_tick, chosen)]:
                await current.action_queues[index].put(dict(action_type='xueliu_exchange_three', selected_tiles=tiles, _action_tick=tick))
            current.action_events[index].set()
    with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', respond):
        asyncio.run(state._xueliu_throw_three_phase())
    start = (round_number-1)%4
    assert asks == [[start,(start+1)%4],[(start+2)%4,(start+3)%4]]
    assert state.xueliu_exchange_direction == 2 and not state.waiting_players_list
    assert Counter(t for p in state.player_list for t in p.hand_tiles) == Counter(t for h in before for t in h)
    for index, p in enumerate(state.player_list):
        assert Counter(p.hand_tiles) == Counter(before[index])-Counter(p.xueliu_throw_tiles)+Counter(state.player_list[(index+2)%4].xueliu_throw_tiles)
    assert [len(p.hand_tiles) for p in state.player_list] == [14,13,13,13]


def test_phase_timeout_only_selects_active_group_and_bot_avoids_incoming_suit():
    state, _ = make_state()
    state.exchange_stage, state.exchange_groups = 2, [[0,1],[2,3]]
    state.player_list[0].xueliu_throw_tiles = [11,12,13]
    state.player_list[1].xueliu_throw_tiles = [21,22,23]
    async def expire(current):
        current.xueliu_throw_deadline = 0
    with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', expire):
        asyncio.run(state._collect_xueliu_selection([2,3]))
    assert state.player_list[0].xueliu_throw_tiles == [11,12,13]
    assert all(t//10 != 1 for t in state.player_list[2].xueliu_throw_tiles)
    assert all(t//10 != 2 for t in state.player_list[3].xueliu_throw_tiles)


@pytest.mark.parametrize('kind,payer,expected', [
    ('xiayu2',None,[4,-2,0,-2]), ('xiayu1',None,[2,-1,0,-1]),
    ('guafeng',1,[2,-2,0,0]), ('guafeng',2,[0,0,0,0]),
])
def test_kong_scores_and_refund_only_transfer_opponents(kind,payer,expected):
    state, _ = make_state()
    changes = state._record_gang_score(0,11,kind,payer)
    assert list(changes.values()) == expected
    record = state.player_list[0].gang_score_records[-1]
    refund = state._refund_gang_record(record)
    assert [refund.get(i,0) for i in range(4)] == [-x for x in expected]
    assert [p.score for p in state.player_list] == [0]*4
    assert not any(state._refund_gang_record(record).values())


@pytest.mark.parametrize('zimo,payer,expected', [(True,None,[14,-7,0,-7]),(False,1,[7,-7,0,0]),(False,2,[0]*4)])
def test_hu_scores_never_transfer_teammate(zimo,payer,expected):
    state, _ = make_state()
    assert list(state._apply_hu_score_changes(0,7,zimo,payer,fan=7).values()) == expected


def test_control_pig_keeps_dingque_but_cannot_win_with_it():
    state, _ = make_state()
    p = state.player_list[0]
    p.hand_tiles = WIN[:-1]+[31]
    cut, changed = _prepare_dingque_cut_action(p, {'TileId':11,'cutClass':False,'cutIndex':0})
    assert cut['TileId']==11 and not changed
    refresh_waiting_tiles(state, 0)
    assert not p.waiting_tiles
    assert state._evaluate_xueliu_wall_end(p)[0]=='hua_zhu'
    p.can_hold_dingque = False
    assert _prepare_dingque_cut_action(p, {'TileId':11})[0]['TileId'] == 31


@pytest.mark.parametrize('source,won,expected', [(1,True,['hu']),(2,True,['hu','pass']),(1,False,['hu','pass'])])
def test_post_hu_enemy_required_teammate_optional(source,won,expected):
    state, _ = make_state()
    state.current_player_index = source
    p=state.player_list[0]
    p.has_won=p.post_hu_lock=won
    p.waiting_tiles={25}
    if won:
        p.shunhe_passed_max_fan=99
    assert check_action_after_cut(state,25)[0] == expected
    if source==2:
        apply_passed_win_shunhe(state,[0])
        assert p.shunhe_passed_max_fan==99


def test_required_ron_on_timeout_and_teammate_pass_cannot_cancel_it():
    state, _ = make_state()
    state.current_player_index = 1
    state.game_status='waiting_action_after_cut'
    state.player_list[1].discard_tiles=[25]
    state.step_time=0
    for p in state.player_list:
        p.remaining_time=0
    state.action_dict={0:['hu'],1:[],2:['hu','pass'],3:[]}
    state.sichuan_hu_results={0:dict(fan=3,fan_list=['基本胡'],hepai_tile=25),2:dict(fan=3,fan_list=['基本胡'],hepai_tile=25)}
    async def run():
        try:
            await wait_action(state)
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
    assert state.game_status=='settle_win' and list(state.sichuan_hu_results)==[0]


@pytest.mark.parametrize('fan,expected', [(3,32),(16,32),(24,48)])
def test_wall_end_pig_minimum_and_no_teammate_payment(fan,expected):
    state, _ = make_state()
    statuses=['hua_zhu','ting','ting','no_ting']
    state._evaluate_xueliu_wall_end=lambda p:(statuses[p.player_index],{25},fan,['基本胡'])
    with patch('asyncio.sleep', new_callable=AsyncMock):
        asyncio.run(state._settle_xueliu_wall_end())
    assert [p.score for p in state.player_list] == [-expected,expected,fan,-fan]


def test_team_tie_plays_extra_board_instead_of_sbr_tiebreak():
    state, _ = make_state()
    state.current_round=state.max_round*4
    assert not state._is_match_end()
    state.current_round += 1
    assert state._should_play_round()
    state.player_list[0].score=2
    state.player_list[1].score=-2
    assert state._is_match_end() and not state._should_play_round()
    state._finish_team_match()
    assert state.team_result['winner_team_id']==0
