import asyncio,time
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock
import pytest
from . import ZhongyongGameState,NanqueGameState
from ..public.claim_protection import end_claim_protection_interval
from ..public.outbound_pipe import drain_viewer,close_outbound_pipes
from ..public.ai import pacing


@pytest.mark.parametrize('cls',[ZhongyongGameState,NanqueGameState])
@pytest.mark.parametrize('enabled',[False,True])
@pytest.mark.parametrize('speed',['instant','fast','medium','slow'])
@pytest.mark.parametrize('respond',['pass','peng'])
@pytest.mark.parametrize('bot',[False,True])
def test_live_outbox_protects_only_human_rooms_and_preserves_following_ask(monkeypatch,cls,enabled,speed,respond,bot):
    async def run():
        s=cls();s.initialize_round();s.claim_protection=enabled;s.bot_speed=speed
        delay={'instant':0,'fast':.03,'medium':.05,'slow':.07}[speed]
        monkeypatch.setitem(pacing.BOT_SPEEDS,speed,delay)
        s.claim_protect_delay=.2;s.claim_meld_followup_gap=.04;s.claim_meld_post_gap=.02
        wire={i:[] for i in range(4)};connections={}
        for i,p in enumerate(s.player_list):
            p.user_id=0 if bot and i==0 else 100+i
            async def send(payload,idx=i):wire[idx].append((time.monotonic(),payload))
            connections[p.user_id]=NS(websocket=NS(send_json=send))
        s.game_server=NS(user_id_to_connection=connections);s.send_to_realtime_spectators=AsyncMock()
        s.start_game_recording();s.start_round_recording();s.current_player_index=0
        try:
            s.open_action_window(dict(status='waiting_hand_action',player=0,actions={0:['cut']}))
            await s.flush_outbound_payloads();await drain_viewer(s,2)
            asks={1:['peng','pass']}
            s.begin_claim_protection(asks,0)
            s.emit_visible_action_payloads(dict(action='cut',player=0,tile=11))
            s.open_action_window(dict(status='waiting_action_after_cut',player=0,tile=11,actions=asks))
            await asyncio.wait_for(s.flush_outbound_payloads(),.15)
            protected=enabled and not bot
            if protected:assert not any(p.get('do_action_info',{}).get('action_list')==['cut'] for _,p in wire[2])
            else:assert any(p.get('do_action_info',{}).get('action_list')==['cut'] for _,p in wire[2])
            await s.finish_claim_protection()
            if respond=='peng':
                s.emit_visible_action_payloads(dict(action='peng',player=1,tile=11,combination_mask=[0,11,0,11,1,11],meld_code='p11'))
                s.current_player_index=1
                s.open_action_window(dict(status='onlycut_after_action',player=1,actions={1:['cut']}))
                await s.flush_outbound_payloads()
            await asyncio.gather(*(drain_viewer(s,i) for i in range(4)))
            cuts=[(t,p) for t,p in wire[2] if p.get('do_action_info',{}).get('action_list')==['cut']]
            assert len(cuts)==1
            if respond=='peng':
                meld=next((t,p) for t,p in wire[2] if p.get('do_action_info',{}).get('action_list')==['peng'])
                assert wire[2][-1][1].get('ask_hand_action_info') is not None
                if protected:assert meld[0]-cuts[0][0]>=.023
        finally:
            close_outbound_pipes(s);end_claim_protection_interval(s)
    asyncio.run(run())
