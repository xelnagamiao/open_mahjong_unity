"""可选天敲：首次弃牌前无人鸣牌，声明和计分使用同一个权威状态。"""
import asyncio
from types import MethodType, SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from ...game_calculation.shanghai import qiaoma
from ...room.room_validators import ShanghaiRoomValidator
from ...room.room_manager import RoomManager
from ...room.room_router import handle_room_message
from ..game_taiwan.boardcast import broadcast_game_start, send_reconnect_game_state, send_realtime_spectator_snapshot
from .test_shanghai_state import make_state

WAITING = [11,12,13,21,22,23,31,32,33,17,18,19,44]


def state_for(seat=0, enabled=True, **config):
    state = make_state(detailed_config={qiaoma.ENABLE_TIAN_QIAO: enabled, **config})
    state.current_player_index = seat
    state.opening_dealer_action = seat == 0
    for player in state.player_list:
        player.opening_flowers_done = True
    player = state.player_list[seat]
    player.hand_tiles = WAITING + [29]
    player.has_draw_slot = seat != 0
    player.last_drawn_tile = 29 if seat != 0 else None
    player.huapai_list = [51]
    return state


async def discard(state, seat=0, *, declared=True):
    with patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action', new=AsyncMock()), \
         patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_refresh_player_tag_list', new=AsyncMock()):
        await state.execute_cut(seat, {'TileId':29}, declare_ready=declared)


@pytest.mark.parametrize('seat', range(4))
@pytest.mark.parametrize('enabled', [False, True])
def test_first_discard_for_each_seat_and_default_scores(seat, enabled):
    state = state_for(seat, enabled)
    asyncio.run(discard(state, seat))
    player = state.player_list[seat]
    assert player.discard_count == 1 and player.declared_ready and player.ready_locked
    assert player.tian_qiao is enabled
    assert ('tian_qiao' in player.tag_list) is enabled
    detail = state.score_candidate(seat, 'discard', 44)
    assert detail['fan'] == (3 if enabled else 1)
    assert detail['base_score'] == (16 if enabled else 4)
    assert ('天敲' in detail['fan_names']) is enabled
    ready_ticks = [tick for tick in ticks(state) if tick[:2] == ['state','ready']]
    assert ready_ticks[-1] == ['state','ready',seat,'tian_qiao' if enabled else 'public','T']


def test_first_draw_can_create_waiting_hand_like_double_riichi():
    initial = WAITING.copy(); initial.remove(19); initial.append(29)
    assert not qiaoma.waits(initial)
    state = state_for(1)
    state.player_list[1].hand_tiles = initial + [19]
    state.player_list[1].last_drawn_tile = 19
    state.player_list[1].normal_draw_count = 1
    asyncio.run(discard(state, 1))
    assert state.player_list[1].tian_qiao


@pytest.mark.parametrize('meld', ['s12','k11','g11','G11'])
def test_any_chow_pung_kong_before_declaration_interrupts(meld):
    state = state_for(3)
    state.player_list[0].combination_tiles = [meld]
    state.table_claim_or_kong = True
    asyncio.run(discard(state, 3))
    assert state.player_list[3].declared_ready
    assert not state.player_list[3].tian_qiao


def test_table_interrupt_marker_is_monotonic_even_if_visible_meld_disappears():
    state = state_for(1)
    state.table_claim_or_kong = True
    asyncio.run(discard(state, 1))
    assert not state.player_list[1].tian_qiao


@pytest.mark.parametrize('claimed_river', [False, True])
def test_later_discard_cannot_reopen_first_discard_window(claimed_river):
    state = state_for(1)
    state.player_list[1].discard_count = 1
    state.player_list[1].discard_tiles = [] if claimed_river else [15]
    asyncio.run(discard(state, 1))
    assert state.player_list[1].discard_count == 2
    assert not state.player_list[1].tian_qiao


def test_selecting_ready_or_cancelling_never_commits_tianqiao():
    state = state_for()
    assert 29 in state.ready_candidate_cuts(0)
    assert not state.player_list[0].tian_qiao
    asyncio.run(discard(state, declared=False))
    assert not state.player_list[0].declared_ready
    assert not state.player_list[0].tian_qiao


def test_other_players_discards_and_ready_do_not_interrupt():
    async def run():
        state = state_for()
        await discard(state)
        state.current_player_index = 1
        player = state.player_list[1]
        player.hand_tiles = WAITING + [29]; player.huapai_list = [51]
        player.last_drawn_tile = 29; player.has_draw_slot = True
        await discard(state, 1)
        assert state.player_list[0].tian_qiao and player.tian_qiao
    asyncio.run(run())


def test_declaration_discard_called_later_preserves_committed_flag_and_reset_clears():
    state = state_for()
    asyncio.run(discard(state))
    state.table_claim_or_kong = True
    state.player_list[1].combination_tiles = ['k29']
    state.player_list[0].discard_tiles.clear()
    state._declare_ready(0)  # 重复补发/状态刷新不降级，也不重复加标签。
    assert state.player_list[0].tian_qiao
    assert state.player_list[0].tag_list.count('tian_qiao') == 1
    assert state.score_candidate(0,'discard',44)['base_score'] == 16
    state._reset_hand_runtime()
    assert state.enable_tian_qiao
    assert not state.player_list[0].tian_qiao
    assert 'tian_qiao' not in state.player_list[0].tag_list


def test_flower_replacement_does_not_interrupt_first_discard():
    async def run():
        state = state_for()
        state.player_list[0].hand_tiles[-1] = 45
        state.tiles_list = [11,29,52]
        with patch('server.gamestate.game_shanghai.ShanghaiGameState.broadcast_do_action',new=AsyncMock()):
            await state._complete_opening_flowers(0)
        assert not state.table_claim_or_kong
        assert state.player_list[0].discard_count == 0
        assert state.player_list[0].huapai_list == [51,45,52]
        await discard(state)
        assert state.player_list[0].tian_qiao
    asyncio.run(run())


def test_legal_concealed_kong_after_tianqiao_keeps_bonus():
    async def run():
        state=state_for()
        player=state.player_list[0]
        player.hand_tiles=[11,11,11,14,14,14,17,17,17,21,21,21,44,29]
        await discard(state)
        assert player.tian_qiao
        player.hand_tiles.append(11); player.last_drawn_tile=11; player.has_draw_slot=True
        with patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action',new=AsyncMock()):
            await state.execute_angang(0,11)
        assert player.combination_tiles == ['G11']
        assert state.table_claim_or_kong and player.tian_qiao
        assert '天敲' in state.score_candidate(0,'discard',44)['fan_names']
    asyncio.run(run())


def test_actual_settlement_record_and_fan_statistic_count_once():
    from ...database.subrule_fans import collect_subrule_fans
    async def run():
        state=state_for()
        await discard(state)
        detail=state.score_candidate(0,'discard',44)
        state.current_round=state.max_round*4
        state.pending_winners=[{'index':0,'payer':1,'detail':detail,'source':'discard','tile':44,'hu_class':'hu_first'}]
        before={p.original_player_index:p.score for p in state.player_list}
        with patch('server.gamestate.game_shanghai.ShanghaiGameState.broadcast_result',new=AsyncMock()) as output:
            assert await state._settle_hand(before)
        assert '天敲（2番）' in output.await_args.kwargs['hu_fan']
        assert '天敲' in state.player_list[0].record_counter.recorded_fans[-1]
        counts=collect_subrule_fans(state.game_record,0,'shanghai/qiaoma')
        assert counts['天敲']==1 and '天敲（2番）' not in counts
        assert sum(p.score for p in state.player_list)==sum(before.values())
    asyncio.run(run())


@pytest.mark.parametrize('profile',[False,True])
@pytest.mark.parametrize('source',['discard','self_draw','robbing_kong'])
def test_fan_stacking_cap_and_all_win_sources(profile, source):
    state = state_for(qing3_hun2_peng2=profile)
    asyncio.run(discard(state))
    player = state.player_list[0]
    if source == 'self_draw':
        player.hand_tiles.append(44); player.last_drawn_tile = 44
    detail = state.score_candidate(0,source,44)
    assert detail['base_score'] == 16
    assert detail['capped_fan'] == 3
    assert detail['fan'] == (4 if source == 'robbing_kong' else 3)
    assert qiaoma.display_fan_names(detail['fan_names'],profile).count('天敲（2番）') == 1


@pytest.mark.parametrize('invalid',[1,0,'true',None,[]])
def test_config_is_strict_boolean(invalid):
    with pytest.raises(ValueError):
        ShanghaiRoomValidator(room_name='上海',game_round=1,round_timer=20,step_timer=5,
            detailed_config={qiaoma.ENABLE_TIAN_QIAO:invalid})


@pytest.mark.parametrize('sub_rule',['shanghai/qinghunpeng','shanghai/lajihe'])
def test_disabled_for_other_shanghai_rules(sub_rule):
    with pytest.raises(ValueError,match='开启天敲仅适用于上海敲麻'):
        ShanghaiRoomValidator(room_name='上海',game_round=1,round_timer=20,step_timer=5,sub_rule=sub_rule,
            detailed_config={qiaoma.ENABLE_TIAN_QIAO:True})


@pytest.mark.parametrize('configured',[None,False,True])
def test_real_route_persistence_start_reconnect_spectator_record(configured):
    from ...server import GameServer
    async def run():
        server=SimpleNamespace(players={'client':SimpleNamespace(user_id=101,username='host',current_room_id=None)},
            db_manager=SimpleNamespace(get_user_settings=lambda _: {'username':'host'}),
            gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _:False),match_manager=None)
        server.room_manager=RoomManager(server); server.room_manager._broadcast_room_info=AsyncMock()
        server.create_Shanghai_room=MethodType(GameServer.create_Shanghai_room,server)
        request=dict(type='room/create_Shanghai_room',roomname='天敲测试',gameround=1,password='',
            roundTimerValue=20,stepTimerValue=5,tips=True,hepai_limit=1)
        if configured is not None: request['detailed_config']={qiaoma.ENABLE_TIAN_QIAO:configured,qiaoma.QING3_HUN2_PENG2:True}
        socket=AsyncMock()
        await handle_room_message(server,'client',request,socket)
        response=socket.send_json.await_args.args[0]; assert response['success'],response
        room=response['room_info']; expected=configured is True
        assert room['detailed_config'][qiaoma.ENABLE_TIAN_QIAO] is expected
        assert server.room_manager.rooms[room['room_id']]['detailed_config']==room['detailed_config']
        assert (room['round_timer'],room['step_timer'],room['hepai_limit'])==(20,5,1)
        state=make_state(**{**room,'player_list':[101,102,103,104]})
        state.game_record['game_title'].update(state.build_record_title_fields())
        assert state.game_record['game_title']['detailed_config'][qiaoma.ENABLE_TIAN_QIAO] is expected
        player=state.player_list[0]; player.hand_tiles=WAITING.copy(); player.discard_count=1
        state._declare_ready(0)
        state.game_server.user_id_to_connection[101]=SimpleNamespace(websocket=socket)
        for send in (lambda:broadcast_game_start(state), lambda:send_reconnect_game_state(state,player)):
            await send(); payload=socket.send_json.await_args.args[0]
            assert payload['game_info']['detailed_config'][qiaoma.ENABLE_TIAN_QIAO] is expected
            assert ('tian_qiao' in payload['game_info']['players_info'][0]['tag_list']) is expected
        state.game_server.user_id_to_connection[999]=SimpleNamespace(websocket=socket)
        await send_realtime_spectator_snapshot(state,999,0)
        assert socket.send_json.await_args.args[0]['game_info']['detailed_config'][qiaoma.ENABLE_TIAN_QIAO] is expected
    asyncio.run(run())


def ticks(state):
    return [tick for round_data in state.game_record['game_round'].values() for tick in round_data['action_ticks']]
