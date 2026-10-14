"""Adversarial requests must leave physical state and score authority untouched."""
import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock
import pytest

from .FuzhouGameState import FuzhouGameState
from .state_machine import Phase as P
from .test_state import WIN,state_with,open_turn,act
from .get_action import handle_action
from ...game_calculation.fuzhou import rules as book


def test_locked_hand_without_a_draw_cannot_offer_a_discard():
    from .actions import turn_actions
    s = state_with()
    s.player_list[0].golden_middle = True
    s.player_list[0].has_draw_slot = False
    before = list(s.player_list[0].hand_tiles)
    assert not any(turn_actions(s, 0, discard_only=True).values())
    assert s.player_list[0].hand_tiles == before


def test_missing_shared_wire_prompt_does_not_invent_actions_or_expose_hand(monkeypatch):
    from . import protocol
    s = state_with(); open_turn(s)
    monkeypatch.setattr(protocol.wire, 'ask_action_payload', lambda *a, **kw: {'type': 'empty'})
    assert s._ask_payload(0, s.live_pending_window) == {'type': 'empty', 'player_index': 0}


@pytest.mark.parametrize('bad',[{'sub_rule':'fuzhou/x'},{'use_flowers':False},{'open_cuohe':True},{'player_list':[101,102,103]}])
def test_domain_constructor_rejects_incompatible_room(bad):
    data={**FuzhouGameState._default_room_data(),**bad}
    with pytest.raises(ValueError):FuzhouGameState(room_data=data,calculation_service=object())


@pytest.mark.parametrize('command',[
    {'TileId':11.0},{'TileId':11,'cutIndex':True},{'TileId':11,'cutIndex':-2},
    {'TileId':11,'cutIndex':17},{'TileId':11,'cutClass':'true'},
    {'TileId':11,'cutClass':True},{'TileId':29,'cutClass':True,'cutIndex':17},
    {'TileId':38,'cutClass':False,'cutIndex':16},
    {'TileId':11,'cutIndex':2},{'TileId':39,'cutClass':False},
])
def test_bad_physical_discard_identity_is_atomic(command):
    async def run():
        s=state_with();open_turn(s);before=s.physical_inventory();tick=s.server_action_tick
        hand=list(s.player_list[0].hand_tiles)
        with pytest.raises(ValueError):await s.submit_action(0,'cut',**command)
        assert s.player_list[0].hand_tiles==hand and s.physical_inventory()==before
        assert s.server_action_tick==tick and all(q.empty() for q in s.action_queues.values())
    asyncio.run(run())


@pytest.mark.parametrize('action', ['jiagang','youjin','tingjin','qiangjin'])
def test_invalid_candidate_never_mutates_hand_even_when_action_is_offered(action):
    s=state_with();open_turn(s);before=list(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):s.validate_response(0,dict(action_type=action,target_tile=True),[action])
    assert s.player_list[0].hand_tiles==before


@pytest.mark.parametrize('visual_index',[9,16])
def test_dragged_or_sorted_hand_uses_tile_identity_and_preserves_visual_cut_coordinate(visual_index):
    s=state_with();open_turn(s);before=s.physical_inventory()
    act(s,{0:dict(action_type='cut',TileId=11,cutClass=False,cutIndex=visual_index)})
    assert 11 not in s.player_list[0].hand_tiles
    cut=next(e for e in reversed(s.domain_events) if e['action']=='cut')
    assert cut['tile']==11 and cut['cutIndex']==visual_index and not cut['cutClass']
    assert s.physical_inventory()==before
    t=state_with();open_turn(t);t.player_list[0].youjin=1
    with pytest.raises(ValueError):t._cut_identity(0,dict(TileId=29,cutClass=False,cutIndex=0))
    assert t._cut_identity(0,dict(TileId=29,cutClass=True,cutIndex=16))==(29,16,True)


@pytest.mark.parametrize('locked',[False,True])
def test_sorted_drawn_card_keeps_draw_identity_at_any_visual_position(locked):
    s=state_with();open_turn(s);before=s.physical_inventory()
    held=list(s.player_list[0].hand_tiles[:-1])
    if locked:s.player_list[0].youjin=1
    act(s,{0:dict(action_type='cut',TileId=29,cutClass=True,cutIndex=4)})
    assert s.player_list[0].hand_tiles==sorted(held)
    cut=next(e for e in reversed(s.domain_events) if e['action']=='cut')
    assert cut['tile']==29 and cut['cutIndex']==4 and cut['cutClass']
    assert s.physical_inventory()==before


@pytest.mark.parametrize('source,payer', [('discard',None),('discard',True),('discard',0),('discard',4),('rob_kong',1)])
def test_settlement_rejects_missing_or_wrong_physical_source(source,payer):
    s=state_with(WIN[:-1]);before=s.physical_inventory()
    with pytest.raises(ValueError):s.settle_win(0,source,29,payer=payer)
    assert s.round_settlement is None and s.physical_inventory()==before and s.round_changes==[0]*4


def test_settlement_rejects_wrong_river_and_concealed_kong():
    s=state_with(WIN[:-1]);s.player_list[1].discard_tiles=[19]
    with pytest.raises(ValueError,match='牌河'):s.settle_win(0,'discard',29,payer=1)
    s.pending_kong=dict(actor=1,tile=29,concealed=True)
    with pytest.raises(ValueError,match='暗杠'):s.settle_win(0,'rob_kong',29,payer=1)
    assert s.player_list[1].discard_tiles==[19] and s.pending_kong['concealed']
    with pytest.raises(ValueError):s.settle_win(0,'self_draw',29)
    with pytest.raises(RuntimeError):s.advance_round_after_ready()
    s.end_draw()
    with pytest.raises(ValueError):s.end_draw()
    assert not s.is_match_end()


def test_flow_before_gold_or_after_lock_cannot_offer_illegal_win():
    s=state_with();s.jin=None
    assert not s.can_win(0,'self_draw',29) and not s.can_three_gold(0)
    s.jin=39
    assert not s.can_win(0,'self_draw',28)
    s.player_list[0].youjin=1;s.player_list[0].locked_waits={39}
    assert not s.can_win(0,'self_draw',29)
    s.player_list[0].hand_tiles=WIN[:12]+[39]*3+WIN[15:]
    assert not s.can_three_gold(0)


def test_unavailable_transport_and_wrong_rule_requests_are_ignored():
    async def run():
        s=state_with();open_turn(s);ws=NS(send_json=AsyncMock())
        server=NS(players={},gamestate_manager=NS(get_game_state_by_gamestate_id=lambda _:s))
        await handle_action(server,'missing',{},ws)
        server.players['x']=NS(user_id=s.player_list[0].user_id);s.room_rule='guobiao'
        await handle_action(server,'x',{},ws)
        assert all(q.empty() for q in s.action_queues.values())
        ws.send_json.assert_not_awaited()
        s.game_server=None
        await s.send_realtime_spectator_snapshot(999,0)
    asyncio.run(run())


def test_flower_or_gold_exhaustion_never_draws_reserved_tiles():
    s=FuzhouGameState(calculation_service=object());s.initialize_round()
    s.tiles_list=s.tiles_list[:22];before=list(s.tiles_list)
    s.open_action_window(s.opening_window())
    actor=s.live_pending_window['player'];act(s,{actor:dict(action_type='buhua')})
    assert s.machine.phase==P.END and s.tiles_list==before
    t=FuzhouGameState(calculation_service=object());t.initialize_round();t.tiles_list=t.tiles_list[:22]
    assert t.open_gold()['status']==P.END.value and t.jin is None


def test_score_input_guards_are_explicit():
    with pytest.raises(ValueError):book.policy(41)
    assert book.score_hand(WIN,jin=39,context=book.WinContext(source='bad')) is None
    with pytest.raises(ValueError):book.score_hand(WIN,jin=39,base_score=True)
    with pytest.raises(ValueError):book.score_hand(WIN,jin=39,context=book.WinContext(dealer_streak=-1))
    assert book.score_hand(WIN,jin=39,special='flower') is None
    assert not book.golden_middle_wait([39]*3+[11]*13,jin=39)
