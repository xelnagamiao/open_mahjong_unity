"""Platform doubles: no partner payments, deferred kongs, and persistent open void kongs."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_doubles import make_state, settle
from .boardcast import _build_players_info


def kong(state, kind, payer=None, tile=11):
    p = state.player_list[0]
    p.combination_tiles = [('G' if kind == 'xiayu2' else 'g') + str(tile)]
    p.combination_mask = [[2,tile,0,tile,0,tile,2,tile] if kind == 'xiayu2'
                          else [0,tile,3,tile,1,tile,0,tile] if kind in ('xiayu1','non_timely')
                          else [1,tile,0,tile,0,tile,0,tile]]
    changes = state._record_gang_score(0, tile, kind, payer)
    assert list(changes.values()) == [0]*4
    assert [p.score for p in state.player_list] == [0]*4
    return p.gang_score_records[-1]


def finish_early(state):
    for seat in (0,2):
        state.player_list[seat].is_hu = True
        state.player_list[seat].hu_order = 1 if seat == 0 else 2
    with patch('asyncio.sleep', new_callable=AsyncMock):
        asyncio.run(state._settle_liuju())


@pytest.mark.parametrize('kind,payer,expected', [
    ('xiayu2',None,[4,-2,0,-2]), ('xiayu1',None,[2,-1,0,-1]),
    ('guafeng',1,[2,-2,0,0]), ('guafeng',2,[0,0,0,0]),
    ('non_timely',None,[0,0,0,0]),
])
def test_kong_changes_only_at_board_end_and_only_enemies_pay(kind,payer,expected):
    state, messages = make_state()
    record = kong(state,kind,payer)
    finish_early(state)
    assert [p.score for p in state.player_list] == expected
    panels = [m['message']['show_result_info'] for m in messages
              if m['message'].get('show_result_info',{}).get('liuju_step') == 'settle_gang']
    assert len(panels) == 4
    assert all(list(p['score_changes'].values()) == expected for p in panels)
    assert record['settled']
    from .doubles_kong_settlement import settle_kongs
    assert settle_kongs(state) == ({i:0 for i in range(4)}, [])
    assert [p.score for p in state.player_list] == expected


@pytest.mark.parametrize('winner', range(4))
@pytest.mark.parametrize('zimo', [False,True])
def test_both_hu_paths_exempt_partner(winner,zimo):
    state,_ = make_state()
    mate=(winner+2)%4
    changes=state._apply_hu_score_changes(winner,8,zimo,mate)
    expected={i:0 for i in range(4)}
    if zimo:
        expected={i:(18 if i==winner else 0 if i==mate else -9) for i in range(4)}
    assert changes==expected
    assert [p.score for p in state.player_list]==list(expected.values())


@pytest.mark.parametrize('kind', ['xiayu2','xiayu1','guafeng'])
def test_gang_shang_pao_voids_reserved_payment_and_reveals_four_upright_tiles(kind):
    state,messages=make_state()
    record=kong(state,kind,1 if kind=='guafeng' else None)
    settle(state,[1],discarder=0)
    assert [p.score for p in state.player_list]==[0]*4
    assert record['cancelled'] and record['void_reason']=='gang_shang_pao'
    assert state.player_list[0].combination_mask==[[0,11]*4]
    reveals=[m for m in messages if m['message'].get('do_action_info',{}).get('action_list')==['sichuan_kong_reveal']]
    assert len(reveals)==4
    for p in state.player_list:
        info=_build_players_info(state,p.user_id)
        assert info[0]['combination_mask']==[[0,11]*4]


@pytest.mark.parametrize('kind,payer', [('non_timely',None),('guafeng',2)])
def test_unpaid_kong_opens_without_changing_any_points(kind,payer):
    state,_=make_state()
    kong(state,kind,payer)
    asyncio.run(state._broadcast_kong_reveals())
    assert state.player_list[0].combination_mask==[[0,11]*4]
    assert [p.score for p in state.player_list]==[0]*4


def test_no_ting_cancels_kong_before_final_payment():
    state,messages=make_state()
    record=kong(state,'xiayu2')
    state.player_list[0].hand_tiles=[12,14,16,18,21,23,25,27,29,31]
    with patch('asyncio.sleep', new_callable=AsyncMock):
        asyncio.run(state._settle_liuju())
    assert record['cancelled'] and record['void_reason']=='no_ting'
    assert state.player_list[0].combination_mask==[[0,11]*4]
    panels=[m['message']['show_result_info'] for m in messages
            if m['message'].get('show_result_info',{}).get('liuju_step')=='settle_gang']
    assert panels and all(not any(p['score_changes'].values()) for p in panels)


def test_replay_reveal_and_deferred_score_do_not_change_turn_or_charge_partner():
    from ..verifier.record_sim.goto_action import RecordSim, RecordPlayer, stringify_tick
    sim = RecordSim({'game_title': {'rule': 'sichuan', 'sub_rule': 'sichuan/doubles'}})
    sim.players = {i: RecordPlayer(i, i) for i in range(4)}
    sim.players[0].combination_tiles = ['G11']
    sim.players[0].combination_masks = [[2,11,0,11,0,11,2,11]]
    sim.current_player_index = 3
    sim.apply_tick(stringify_tick(['sichuan_kong_reveal', 0, 11]))
    assert sim.current_player_index == 3
    assert sim.players[0].combination_masks == [[0,11]*4]
    assert [p.score for p in sim.players.values()] == [0]*4
    sim.apply_tick(stringify_tick(['liuju', 'settle_gang', [4,-2,0,-2], ['暗杠 +4分']]))
    assert [p.score for p in sim.players.values()] == [4,-2,0,-2]


def test_robbed_added_kong_never_reveals_a_nonexistent_fourth_tile():
    state,messages=make_state()
    record=kong(state,'xiayu1')
    state._refund_last_gang(0,11)
    state.player_list[0].combination_tiles=['k11']
    state.player_list[0].combination_mask=[[0,11,1,11,0,11]]
    asyncio.run(state._broadcast_kong_reveals())
    assert record['cancelled'] and record['void_reason']=='robbed_kong'
    assert state.player_list[0].combination_mask==[[0,11,1,11,0,11]]
    assert not any(m['message'].get('do_action_info',{}).get('action_list')==['sichuan_kong_reveal'] for m in messages)
