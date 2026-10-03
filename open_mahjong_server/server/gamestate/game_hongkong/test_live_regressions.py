"""Regressions found through the ordinary Unity/WebSocket room path."""
import asyncio
from unittest.mock import AsyncMock

import pytest

from .test_branches import table, act
from .test_flow import make_state, open_round, assert_conservation
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.models import PROFILES


@pytest.mark.parametrize('profile',PROFILES)
def test_reordered_client_cut_index_does_not_change_physical_tile(profile):
    state=make_state(profile)
    window=open_round(state)
    while state.machine.phase not in (P.TURN,P.DISCARD_ONLY):
        from .bot import choose_action
        window=state.apply_action_results(window,{i:choose_action(state,i,a) for i,a in window['actions'].items() if a})
    owner=window['player']
    p=state.player_list[owner]
    tile=next(t for t in p.hand_tiles[:-1] if t in state.legal_discard_tiles(owner))
    display_index=next(i for i,t in enumerate(p.hand_tiles[:-1]) if t!=tile)
    before=p.hand_tiles.count(tile)
    window=act(state,window,**{str(owner):dict(action_type='cut',TileId=tile,cutIndex=display_index,cutClass=False)})
    assert p.hand_tiles.count(tile)==before-1
    event=next(e for e in reversed(state.domain_events) if e['action']=='cut')
    assert event['tile']==tile and event['cutIndex']==display_index and not event['cutClass']
    assert_conservation(state)


def test_ready_cannot_discard_a_held_duplicate_using_reordered_index():
    state,window=table(PROFILES[1],hands={0:[11,12,13,21,22,23,31,32,33,41,41,41,45,45]})
    p=state.player_list[0]
    p.declared_ready=True
    with pytest.raises(ValueError):
        state._validated_cut(0,dict(TileId=45,cutIndex=13,cutClass=False))
    assert state._validated_cut(0,dict(TileId=45,cutIndex=13,cutClass=True))==(45,13,True)
    for values in (dict(TileId=11,cutIndex=13,cutClass=True),dict(TileId=45,cutIndex=13,cutClass='false')):
        with pytest.raises(ValueError):
            state._validated_cut(0,values)


def test_last_hand_does_not_wait_for_a_ready_action_the_result_ui_never_sends():
    async def run():
        state=make_state()
        open_round(state)
        state.current_round=state.max_round*4
        state.end_draw()
        assert state.match_finishing
        assert any(not p.is_bot for p in state.player_list)
        state.wait_action=AsyncMock(side_effect=AssertionError('Final hand must not wait for ready'))
        await state.run_round_ready_phase(timeout=99)
        assert state.machine.phase==P.READY and not state.waiting_players_list
        state.wait_action.assert_not_awaited()
    asyncio.run(run())
