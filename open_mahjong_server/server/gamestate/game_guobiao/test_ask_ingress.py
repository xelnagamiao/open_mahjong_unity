"""A local WebSocket can answer while the ask broadcast still sends other seats."""
import asyncio
from types import SimpleNamespace
from unittest.mock import patch

import pytest

from .test_sanma import sanma
from . import boardcast
from .wait_action import wait_action
from ..public.ai.get_action import get_action


@pytest.mark.parametrize('sub_rule',['guobiao/standard','guobiao/sixteen','guobiao/four_jokers'])
@pytest.mark.parametrize('phase',['hand','other'])
def test_reply_arriving_during_broadcast_is_accepted_and_consumed(sub_rule,phase):
    async def run():
        g=sanma(sub_rule=sub_rule)
        g.claim_protection=False;g.tactical_call=False;g.current_player_index=0
        g.waiting_players_list=[];g.server_action_tick=12
        seat=0 if phase=='hand' else 1
        g.game_status='waiting_buhua_round' if phase=='hand' else 'waiting_action_after_cut'
        g.action_dict={i:['pass'] if i==seat else [] for i in range(4)}
        g.player_list[0].discard_tiles=[22]
        g.game_server.players={'immediate-client':SimpleNamespace(user_id=g.player_list[seat].user_id)}
        ingress=[]
        async def deliver(state,index,response,**kwargs):
            if index!=seat:return
            ingress.append((list(state.waiting_players_list),getattr(state,'_waiting_action_tick',None)))
            await get_action(state,'immediate-client','pass',False,None,None,None,action_tick=state.server_action_tick-1)
            assert state.action_queues[seat].empty()
            await get_action(state,'immediate-client','pass',False,None,None,None,action_tick=state.server_action_tick)
        with patch.object(boardcast,'_send_ask_response_to_viewer',deliver):
            if phase=='hand':await boardcast.broadcast_ask_hand_action(g)
            else:await boardcast.broadcast_ask_other_action(g)
        assert ingress==[([seat],13)]
        assert g.action_queues[seat].qsize()==1
        await asyncio.wait_for(wait_action(g),1)
        assert g.action_queues[seat].empty()
    asyncio.run(run())
