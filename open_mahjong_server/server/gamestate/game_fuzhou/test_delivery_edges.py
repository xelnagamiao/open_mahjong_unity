"""Boundary regressions for queued actions, terminal reconnect and bot delivery."""
import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock
import pytest

from .actions import declaration_cuts, kong_tiles
from .bot import play_bot
from .get_action import handle_action
from .state_machine import Phase as P
from .test_state import WIN, state_with, open_turn, act


@pytest.mark.parametrize('index,data',[(-1,{}),(4,{}),(True,{}),(0,None)])
def test_invalid_response_envelopes_do_not_change_state(index,data):
    s=state_with();open_turn(s);before=s.physical_inventory()
    with pytest.raises(ValueError):s.validate_response(index,data,['cut'])
    assert s.physical_inventory()==before


def test_legacy_discard_identity_respects_lock_even_with_duplicate_draw_face():
    s=state_with();open_turn(s)
    assert s._cut_identity(0,dict(TileId=11,cutIndex=0))==(11,0,False)
    s.player_list[0].youjin=1
    with pytest.raises(ValueError,match='宣言后'):s._cut_identity(0,dict(TileId=29,cutIndex=15))
    assert s._cut_identity(0,dict(TileId=29,cutIndex=16))==(29,16,True)
    assert not declaration_cuts(s,0,'tingjin')
    s.player_list[0].youjin=2
    assert not declaration_cuts(s,0,'youjin')
    t=state_with()
    with pytest.raises(ValueError):kong_tiles(t,0,'unknown')


def test_reconnect_after_draw_preserves_ready_window_and_zero_sum():
    async def run():
        s=state_with();open_turn(s);s.end_draw()
        assert s.build_pending_action_payload(0) is None
        s._send_claim_protection_payload=AsyncMock()
        await s.present_final_settlements()
        assert s._send_claim_protection_payload.await_count==4
        for call in s._send_claim_protection_payload.await_args_list:
            payload=call.args[1]
            assert payload['show_result_info']['fuzhou_info']['round_changes']==[0,0,0,0]
        s.flush_outbound_payloads=AsyncMock()
        await s.run_round_ready_phase(0)
        messages=s.restore_payloads(0)
        assert messages[-1]['type'].endswith('/ready_status')
        assert messages[-1]['ready_status_info']['fuzhou_info']['phase']==P.READY.value
        assert [p.score for p in s.player_list]==[0,0,0,0]
    asyncio.run(run())


def test_stale_request_after_round_end_does_not_reopen_action_window():
    async def run():
        s=state_with();open_turn(s);s.end_draw();ws=NS(send_json=AsyncMock())
        server=NS(players={'peer':NS(user_id=s.player_list[0].user_id)},gamestate_manager=NS(get_game_state_by_gamestate_id=lambda _:s))
        await handle_action(server,'peer',dict(gamestate_id=s.gamestate_id,action_tick=-1,action='hu_self'),ws)
        ws.send_json.assert_not_awaited()
        assert s.machine.phase==P.END
    asyncio.run(run())


def test_bot_pass_is_immediate_and_bot_clock_allows_configured_pacing(monkeypatch):
    import server.gamestate.game_fuzhou.bot as bot
    async def run():
        s=state_with(WIN[:-1]);s.tiles_list.remove(29)
        s.player_list[1].hand_tiles.append(29);s.player_list[1].has_draw_slot=True
        open_turn(s,1);act(s,{1:dict(action_type='cut',TileId=29,cutClass=True)})
        index=next(i for i,a in s.action_dict.items() if a)
        s.player_list[index].user_id=1
        monkeypatch.setattr(bot,'choose_action',lambda *a:dict(action_type='pass'))
        delay=AsyncMock();monkeypatch.setattr(bot,'wait_bot_delay',delay)
        await play_bot(s,index,s.server_action_tick)
        delay.assert_not_awaited()
        assert s.action_queues[index].get_nowait()['action_type']=='pass'
        t=state_with();open_turn(t);t.player_list[0].user_id=1
        # The queued answer arrives before expiry, but the bot clock must still
        # reserve time for the configured animation/pacing delay.
        t.action_queues[0].put_nowait(dict(action_type='hu_self'))
        response=await t.wait_action()
        assert response[0]['action_type']=='hu_self' and t.action_clocks[0].minimum_duration>=.1
        await asyncio.gather(*list(t.bot_tasks),return_exceptions=True)
        assert await t.wait_action()=={}
    asyncio.run(run())


def test_reconnect_before_first_action_never_reveals_other_hands():
    s=state_with()
    assert s.live_pending_window is None
    assert len(s.restore_payloads(0))==1
    public=s._adapt({'game_info':{}},-1)['game_info']
    assert public['self_hand_tiles'] is None
    assert 'fuzhou_hints' not in public
    assert all(p['hand_tiles'] is None for p in public['players_info'])
    assert s._adapt({'type':'notice'},0)=={'type':'notice','player_index':0}


def test_missing_record_and_duplicate_win_callbacks_are_idempotent():
    s=state_with();s.finalize_round_recording()
    s.start_game_recording();s.start_round_recording();open_turn(s)
    act(s,{0:dict(action_type='hu_self')})
    before=repr(s.game_record)
    s.record_visible_action(dict(action='hu_self',player=0,tile=29))
    assert repr(s.game_record)==before


def test_terminal_phase_rejects_an_empty_action_batch():
    s=state_with();s.end_draw()
    s.open_action_window(dict(status=P.END.value,player=None,actions={}))
    with pytest.raises(ValueError,match='当前阶段'):act(s,{})
    assert s.machine.phase==P.END and s.round_changes==[0,0,0,0]


def test_last_four_draw_uses_sea_order_when_normal_draw_path_is_called():
    s=state_with();s.tiles_list=s.tiles_list[-22:]
    s.draw_for(0)
    assert s.in_last_four and s.current_player_index==0
    before=len(s.tiles_list)
    s.draw_for(0)
    assert s.current_player_index==1 and len(s.tiles_list)==before-1
    assert s.sea_queue==[2,3]


def test_discard_win_reconnect_preserves_ron_payer_instead_of_self_draw():
    s=state_with(WIN[:-1]);s.tiles_list.remove(29)
    s.player_list[1].hand_tiles.append(29);s.player_list[1].has_draw_slot=True
    open_turn(s,1);act(s,{1:dict(action_type='cut',TileId=29,cutClass=True)})
    act(s,{i:dict(action_type='hu' if i==0 else 'pass') for i,a in s.action_dict.items() if a})
    for viewer in range(4):
        info=s.restore_payloads(viewer)[1]['show_result_info']
        assert info['hu_class']=='hu_third'  # Winner 0 claims from seat 1.
        assert info['fuzhou_info']['round_changes']==[40,-40,0,0]
    assert [p.score for p in s.player_list]==[40,-40,0,0]
