"""Late requests, interrupted calculations and side-payment terminal boundaries."""
import asyncio
from unittest.mock import AsyncMock, Mock

import pytest

from . import bot
from .test_state import make_state, drawn, ticks
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ...game_calculation.singapore.test_rules import PLAIN


@pytest.mark.parametrize('action_case',['no_flower','no_kong','no_discard','wrong_turn','ready','invalid_cut'])
def test_rejected_actions_preserve_tiles_and_scores(action_case):
    async def run():
        state=make_state();state.player_list[0].hand_tiles=list(PLAIN)
        state.current_player_index=0
        def snapshot():
            return [(p.hand_tiles[:],p.discard_tiles[:],p.combination_tiles[:],p.score) for p in state.player_list],state.tiles_list[:],state.sg_payments[:]
        before=snapshot()
        if action_case=='no_flower':await state.execute_buhua(0)
        elif action_case=='no_kong':await state.execute_angang(0,47)
        elif action_case=='no_discard':await state.execute_claim(1,'peng')
        elif action_case=='wrong_turn':await state.execute_cut(1,{'TileId':11,'cutIndex':0,'cutClass':False})
        elif action_case=='ready':await state.execute_cut(0,{'TileId':11,'cutIndex':0,'cutClass':False},declare_ready=True)
        else:await state.execute_cut(0,{'TileId':47,'cutIndex':0,'cutClass':False})
        assert snapshot()==before
        assert not state.enter_water(0) and state.ready_candidate_cuts(0)=={}
    asyncio.run(run())


@pytest.mark.parametrize('phase',['window_closed','calculation_superseded','empty_kong'])
def test_bot_does_not_submit_after_action_window_changes(monkeypatch,phase):
    async def run():
        state=make_state();state.player_list[0].hand_tiles=list(PLAIN);state.waiting_players_list=[0]
        monkeypatch.setattr(bot,'_wait_until_actionable',AsyncMock(return_value=phase!='window_closed'))
        async def worker(*args):
            state.server_action_tick+=1
            return 11,0
        monkeypatch.setattr(bot,'run_room_bot_cpu',worker)
        submit=AsyncMock();monkeypatch.setattr(bot,'submit_bot_action',submit)
        await bot.singapore_bot_action(state,0,['angang' if phase=='empty_kong' else 'cut'],'waiting_hand_action')
        submit.assert_not_awaited()
    asyncio.run(run())


def test_marriage_exhausts_capital_before_replacement_draw():
    async def run():
        state=make_state();state.player_list[0].huapai_list=[61];state.player_list[0].hand_tiles=[62]
        state.player_list[1].score=1;wall=state.tiles_list[:]
        assert not await state._replace_one_flower(0,0,is_drawn=True,opening=False)
        assert state.game_status=='END' and state.draw_reason=='capital_exhausted'
        assert state.tiles_list==wall and not state.pending_winners
        assert state.player_list[0].huapai_list==[61,62]
    asyncio.run(run())


def test_missing_connection_reconnect_is_harmless():
    state=make_state()
    asyncio.run(state.player_reconnect(state.player_list[0].user_id))
    assert state._terminal_result is None and not state.sg_payments


@pytest.mark.parametrize('action',['concealed_kong','claimed_kong'])
def test_base_action_rejection_cannot_charge_kong_or_assign_liability(monkeypatch,action):
    """A common-engine rejection must not turn the rule's prior offer into a payment."""
    async def run():
        state=make_state()
        if action=='concealed_kong':
            drawn(state.player_list[0],[11]*4+list(PLAIN[4:]))
            assert 11 in state._kong_candidates(0)
            base=AsyncMock();monkeypatch.setattr(TaiwanGameState,'execute_angang',base)
        else:
            state.player_list[0].discard_tiles=[11]
            state.player_list[1].hand_tiles=[11]*3+list(PLAIN[4:])
            state.sg_fresh_discard=True;state.sg_last_draw_count=3
            assert 'gang' in state.check_discard_actions(11)[1]
            base=AsyncMock();monkeypatch.setattr(TaiwanGameState,'execute_claim',base)
        before=ticks(state)[:]
        if action=='concealed_kong':await state.execute_angang(0,11)
        else:await state.execute_claim(1,'gang')
        base.assert_awaited_once()
        assert [p.score for p in state.player_list]==[400]*4
        assert not state.sg_payments and not state.sg_liability and not state.sg_kong_chain
        assert not state.pending_winners and ticks(state)==before
    asyncio.run(run())


def test_finished_match_entry_does_not_deal_another_hand(monkeypatch):
    async def run():
        state=make_state();state.current_round=state.max_round*4+1
        deal=Mock();monkeypatch.setattr(state,'init_tiles',deal)
        persist=AsyncMock();monkeypatch.setattr(state,'_persist_match',persist)
        await state.game_loop_chinese()
        deal.assert_not_called();persist.assert_awaited_once()
        assert state.game_record['game_round']=={}
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(gamestate_id=state.gamestate_id)
    asyncio.run(run())
