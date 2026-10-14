import asyncio
import copy
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest

from .test_state import WIN, state_with, open_turn, act
from .FuzhouGameState import FuzhouGameState
from .actions import empty_actions, declaration_cuts, kong_tiles
from .state_machine import Phase as P
from .runtime import ActionClock
from .get_action import handle_action
from ...room.fuzhou_room import FuzhouRoomValidator
from ...game_calculation.fuzhou import rules as book
from ...database.shared_record_metrics import analyze_shared_record_for_player


def pass_all(s):
    return act(s,{i:dict(action_type="pass") for i,a in s.live_pending_window["actions"].items() if a})


def record(s):
    s.start_game_recording()
    s.start_round_recording()
    return s


def test_twentieth_flower_during_gold_flip_wins_before_indicator_is_removed():
    from collections import Counter
    s=record(state_with());s.tiles_list.append(s.jin);s.jin=None
    s.machine.phase=P.REPLACEMENT
    flowers=[41]*4+[42]*4+[43]*4+[44]*4+[45]*3
    for tile in flowers:s.tiles_list.remove(tile)
    s.player_list[0].huapai_list=flowers
    s.tiles_list.remove(45);s.tiles_list.append(45);s.update_reserve()
    assert s.physical_inventory()==Counter(book.PHYSICAL)
    s.open_action_window(s.open_gold())
    assert s.machine.phase==P.END and s.jin is None
    assert s.dead_wall_count==28 and len(s.player_list[0].huapai_list)==20
    assert s.round_changes==[300,-100,-100,-100]
    assert s.physical_inventory()==Counter(book.PHYSICAL)


def test_last_four_can_declare_three_gold_without_discard_or_extra_replacement():
    s=state_with(WIN[:12]+[39]*2+[29,29]);s.tiles_list=[39,11,12,13]+[21]*18
    s.machine.phase=P.RESPONSE;s.open_action_window(s.draw_for(0))
    assert s.machine.phase==P.SEA and 'sanjin' in s.action_dict[0]
    act(s,{0:dict(action_type='sanjin')})
    assert s.machine.phase==P.END and s.round_settlement['score']['special']=='three_gold'
    assert len(s.tiles_list)==21 and not s.discard_log


def test_heavenly_win_when_no_three_gold_has_priority():
    s=state_with();s.machine.phase=P.OPENING
    s.open_action_window(s.opening_claim_window())
    responses={i:dict(action_type='hu_self' if i==0 else 'pass') for i,a in s.action_dict.items() if a}
    act(s,responses)
    assert s.round_changes==[240,-80,-80,-80]
    assert s.deferred_hu_settlements[0]['heavenly']


@pytest.mark.parametrize('dealer', range(4))
def test_opening_heavenly_win_uses_hand_prompt_without_drawing_twice(dealer):
    s = state_with()
    s.player_list[0].hand_tiles, s.player_list[dealer].hand_tiles = s.player_list[dealer].hand_tiles, s.player_list[0].hand_tiles
    for i, player in enumerate(s.player_list):
        player.has_draw_slot = i == dealer
    s.dealer_index = s.current_player_index = dealer
    s.machine.phase = P.OPENING
    s.open_action_window(s.opening_claim_window())
    for i in range(4):
        payload = s.build_pending_action_payload(i)
        if i == dealer:
            # The client's discard-claim filter intentionally excludes hu_self.
            # A hand prompt preserves both heavenly win and dealer qiangjin.
            assert payload['type'] == 'gamestate/fuzhou/broadcast_hand_action'
            info = payload['ask_hand_action_info']
            assert {'hu_self', 'qiangjin', 'pass'} <= set(info['action_list'])
            assert info['player_index'] == dealer and info['deal_tile_type'] is None
            assert len(payload['game_info']['self_hand_tiles']) == 17
            assert 29 in info['fuzhou_declaration_candidates']['qiangjin']
        else:
            assert payload['ask_hand_action_info'] is None
            assert payload['ask_other_action_info']['cut_tile'] == s.jin
            assert 'hu_self' not in payload['ask_other_action_info']['action_list']
    responses = {i: dict(action_type='hu_self' if i == dealer else 'pass') for i, a in s.action_dict.items() if a}
    act(s, responses)
    assert s.round_settlement['winner'] == dealer
    assert s.deferred_hu_settlements[0]['heavenly']


def test_opening_priority_three_gold_before_heaven_and_non_dealer_before_dealer():
    s = state_with()
    s.machine.phase=P.OPENING
    s.player_list[1].hand_tiles=[39]*3+[11,12,13,14,15,16,21,22,23,31,32,33,19]
    w=s.open_action_window(s.opening_claim_window())
    responses={i:dict(action_type="pass") for i,a in w["actions"].items() if a}
    responses[0]=dict(action_type="hu_self")
    responses[1]=dict(action_type="sanjin")
    act(s,responses)
    assert s.round_settlement["winner"]==1 and s.round_settlement["score"]["special"]=="three_gold"
    t=state_with()
    t.machine.phase=P.OPENING
    t.player_list[1].hand_tiles=WIN[:-1]
    t.player_list[2].hand_tiles=WIN[:-1]
    w=t.open_action_window(t.opening_claim_window())
    responses={i:dict(action_type="pass") for i,a in w["actions"].items() if a}
    responses.update({0:dict(action_type="qiangjin",target_tile=29),1:dict(action_type="qiangjin"),2:dict(action_type="qiangjin")})
    act(t,responses)
    assert t.round_settlement["winner"]==1 and t.indicator_claimed
    assert t.player_list[1].hand_tiles[-1]==39
    assert "qiangjin" in [r["id"] for r in t.round_settlement["score"]["items"]]


def test_dealer_qiangjin_discards_then_takes_indicator():
    s=record(state_with())
    s.machine.phase=P.OPENING
    w=s.open_action_window(s.opening_claim_window())
    responses={i:dict(action_type="pass") for i,a in w["actions"].items() if a}
    responses[0]=dict(action_type="qiangjin",target_tile=29)
    act(s,responses)
    assert s.player_list[0].discard_tiles==[29]
    assert len(s.player_list[0].hand_tiles)==17 and s.player_list[0].hand_tiles[-1]==39
    s.finalize_round_recording()
    ticks=s.game_record['game_round']['round_index_1']['action_ticks']
    assert any(t[:2]==['fuzhou','qiangjin_take'] for t in ticks)


def test_two_gold_cannot_qiangjin_and_bad_dealer_cut_is_atomic():
    s=state_with(WIN[:-2]+[39,39])
    assert not s.qiangjin_cuts(0)
    s=state_with()
    s.machine.phase=P.OPENING
    w=s.open_action_window(s.opening_claim_window())
    responses={i:dict(action_type="pass") for i,a in w["actions"].items() if a}
    responses[0]=dict(action_type="qiangjin",target_tile=True)
    before=copy.deepcopy(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):act(s,responses)
    assert s.player_list[0].hand_tiles==before and not s.indicator_claimed


def test_gold_flip_collects_honor_and_four_equal_flowers_extend_reserve():
    s=state_with()
    s.jin=None
    s.machine.phase=P.REPLACEMENT
    s.player_list[0].huapai_list=[41]*3
    s.tiles_list=[11]*40+[28,41]
    s.open_action_window(s.open_gold())
    assert s.jin==28 and s.dead_wall_count==20
    assert s.player_list[0].huapai_list==[41]*4
    assert [e['action'] for e in s.domain_events]==['kaijin_flower','kaijin']


def test_twentieth_flower_wins_before_replacement_and_without_shape():
    s=record(state_with(WIN[:-1]+[51]))
    s.player_list[0].huapai_list=[41]*4+[42]*4+[43]*4+[44]*4+[45]*3
    open_turn(s)
    before=len(s.tiles_list)
    act(s,{0:dict(action_type="buhua")})
    assert s.machine.phase==P.END and len(s.tiles_list)==before
    assert s.round_settlement["score"]["special"]=="flower"
    assert s.build_final_settlement_payload(0)["show_result_info"]["skip_hand_reveal"]


@pytest.mark.parametrize('action,owned,meld',[("chi_left",[21,22],"s22"),("chi_mid",[22,24],"s23"),("chi_right",[24,25],"s24"),("peng",[23,23],"k23"),("gang",[23,23,23],"g23")])
def test_each_chow_pung_and_direct_kong_resolves_from_river(action,owned,meld):
    s=state_with(WIN[:-1]+[23])
    s.player_list[1].hand_tiles=owned+[11,12,13,14,15,16,31,32,33,35,36,37,28,28][:16-len(owned)]
    for p in s.player_list[2:]:p.hand_tiles=[19]*16
    s.tiles_list=[11]*45+[27]
    open_turn(s)
    act(s,{0:dict(action_type="cut",TileId=23,cutClass=True)})
    assert action in s.live_pending_window["actions"][1]
    responses={i:dict(action_type="pass") for i,a in s.live_pending_window["actions"].items() if a}
    responses[1]=dict(action_type=action)
    act(s,responses)
    assert s.player_list[1].combination_tiles==[meld]
    assert not s.player_list[0].discard_tiles
    assert s.current_player_index==1
    assert s.machine.phase==(P.TURN if action=="gang" else P.DISCARD_ONLY)


def test_pung_has_priority_over_chow_and_nearer_ron_over_farther():
    s=state_with(WIN[:-1]+[23])
    s.player_list[1].hand_tiles=[21,22]+[19]*14
    s.player_list[2].hand_tiles=[23,23]+[18]*14
    s.player_list[3].hand_tiles=[17]*16
    open_turn(s)
    act(s,{0:dict(action_type='cut',TileId=23,cutClass=True)})
    act(s,{1:dict(action_type='chi_left'),2:dict(action_type='peng')})
    assert s.current_player_index==2 and s.player_list[2].combination_tiles==['k23']
    t=state_with()
    t.player_list[1].hand_tiles=WIN[:-1]
    t.player_list[2].hand_tiles=WIN[:-1]
    t.player_list[3].hand_tiles=[17]*16
    open_turn(t)
    act(t,{0:dict(action_type='cut',TileId=29,cutClass=True)})
    act(t,{1:dict(action_type='hu'),2:dict(action_type='hu')})
    assert t.round_settlement['winner']==1 and t.round_changes[2]==0


def kong_state():
    s=state_with()
    s.player_list[0].hand_tiles=[11,12,13,14,15,16,31,32,33,35,36,37,28,22]
    s.player_list[0].combination_tiles=['k22']
    s.player_list[0].combination_mask=[[1,22,0,22,0,22]]
    s.player_list[1].hand_tiles=[11,12,13,14,15,16,31,32,33,35,36,37,21,23,28,28]
    s.player_list[2].hand_tiles=[18]*16
    s.player_list[3].hand_tiles=[19]*16
    s.tiles_list=[11]*45+[28]
    return s


def test_rob_added_kong_keeps_pung_and_pays_as_self_draw_with_consistent_statistics():
    s=record(kong_state())
    open_turn(s)
    act(s,{0:dict(action_type='jiagang',target_tile=22)})
    assert s.player_list[0].combination_tiles==['k22']
    act(s,{1:dict(action_type='hu')})
    assert s.player_list[0].combination_tiles==['k22'] and 22 not in s.player_list[0].hand_tiles
    assert s.round_changes==[-55,165,-55,-55]
    assert s.player_list[1].won_tiles==[22]
    s.finalize_round_recording()
    s.finalize_game_recording()
    rows=[analyze_shared_record_for_player(s.game_record,i) for i in range(4)]
    assert [r['self_draw_count'] for r in rows]==[0,1,0,0]
    assert not any(r['deal_in_count'] for r in rows)
    assert [r['total_round_score'] for r in rows]==s.round_changes
    assert s.player_list[1].record_counter.zimo_times==1


def test_added_kong_commit_only_after_all_pass_and_ready_kong_draw():
    s=kong_state()
    s.player_list[0].hand_tiles[-2]=28
    open_turn(s)
    act(s,{0:dict(action_type='jiagang',target_tile=22)})
    pass_all(s)
    assert s.player_list[0].combination_tiles==['g22'] and s.dead_wall_count==19
    assert len(s.player_list[0].combination_mask[0])==8
    assert s.player_list[0].hand_tiles[-1]==28
    t=state_with([11]*4+[21,22,23,31,32,33,35,36,37,24,25,26,28])
    t.tiles_list=[19]*45+[28]
    open_turn(t)
    act(t,{0:dict(action_type='angang',target_tile=11)})
    assert not any(t.action_dict.values())
    act(t,{})
    assert t.player_list[0].combination_tiles==['G11'] and t.dead_wall_count==20


def test_youjin_declared_cut_locks_self_wait_and_double_only_gold():
    s=state_with(WIN[:-2]+[39,29])
    assert 29 in declaration_cuts(s,0,'youjin')
    open_turn(s)
    act(s,{0:dict(action_type='youjin',target_tile=29)})
    p=s.player_list[0]
    assert p.youjin==1 and p.locked_waits=={29,39} and s.youjin_owner==0
    p.hand_tiles.append(39);p.has_draw_slot=True
    s.open_action_window(s.begin_turn(0))
    assert 'youjin' in s.action_dict[0]
    act(s,{0:dict(action_type='youjin',target_tile=39)})
    assert p.youjin==2 and p.locked_waits=={39}
    assert s.double_youjin_remaining==[1,2,3]
    p.hand_tiles.append(29);p.has_draw_slot=True
    assert not s.can_win(0,'self_draw',29)


def test_tingjin_locks_golden_middle_and_rejects_other_joker_substitution():
    hand=[11,12,13,21,22,23,31,32,33,35,36,37,16,18,29,29,24]
    s=state_with(hand,jin=17)
    assert 24 in declaration_cuts(s,0,'tingjin')
    open_turn(s)
    act(s,{0:dict(action_type='tingjin',target_tile=24)})
    p=s.player_list[0]
    assert p.golden_middle and p.locked_waits=={17}
    p.hand_tiles.append(17);p.has_draw_slot=True
    assert s.can_win(0,'self_draw',17)
    assert 'gold_middle' in [r['id'] for r in s.score_win(0,'self_draw',17)['items']]


@pytest.mark.parametrize('bad',[dict(sub_rule='fuzhou/other'),dict(use_flowers=False),dict(tactical_call=True),dict(game_round=True),dict(step_timer='5'),dict(round_timer=-1),dict(detailed_config={'base_score':11}),dict(detailed_config={'flower':False})])
def test_strict_room_settings_reject_unsupported_values(bad):
    with pytest.raises(ValueError):
        FuzhouRoomValidator(**{'room_name':'福州','round_timer':20,'step_timer':5,**bad})


def test_double_youjin_last_chance_cannot_add_kong_draws_or_replace_owner():
    s=state_with([11]*4+[21,22,23,31,32,33,35,36,37,24,25,26,28])
    assert kong_tiles(s,0,'angang')=={11}
    s.double_youjin_owner=2
    assert not kong_tiles(s,0,'angang')
    assert not declaration_cuts(s,0,'youjin') and not declaration_cuts(s,0,'tingjin')


def test_router_authentication_stale_ticks_and_illegal_actions():
    async def run():
        s=state_with();s.step_time=5;open_turn(s)
        socket=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(players={'self':SimpleNamespace(user_id=101),'other':SimpleNamespace(user_id=999)},
            gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:s))
        message=dict(type='gamestate/fuzhou/send_action',gamestate_id=s.gamestate_id,action_tick=s.server_action_tick,action='hu_self')
        await handle_action(server,'other',message,socket)
        assert s.action_queues[0].empty()
        await handle_action(server,'self',{**message,'action_tick':-1},socket)
        assert s.action_queues[0].empty() and socket.send_json.await_count==1
        await handle_action(server,'self',{**message,'action':'invented'},socket)
        assert socket.send_json.await_count==2
        await handle_action(server,'self',message,socket)
        assert not s.action_queues[0].empty()
    asyncio.run(run())


def test_fixed_reaction_clock_has_no_bank_charge_and_reconnect_preserves_deadline():
    async def run():
        s=state_with();s.player_list[1].hand_tiles=WIN[:-1]
        for p in s.player_list:p.remaining_time=20
        open_turn(s)
        act(s,{0:dict(action_type='cut',TileId=29,cutClass=True)})
        c=s.action_clocks[1];c.start(100);c.start(200)
        s._timing_now=lambda:101.5
        info=s.build_pending_action_payload(1)['ask_other_action_info']
        assert info['remaining_time']==0 and info['step_remaining']==2
        assert c.deadline==103
        s._timing_now=lambda:103
        with pytest.raises(ValueError):await s.submit_action(1,'hu')
        s._timing_now=lambda:102
        for i in s.waiting_players_list:await s.submit_action(i,'hu' if i==1 else 'pass')
        await s.wait_action()
        assert s.player_list[1].remaining_time==20
    asyncio.run(run())


def test_round_ready_discards_stale_queues_and_advances_fixed_seats():
    async def run():
        s=state_with();s.end_draw()
        await s.action_queues[0].put(dict(action_type='cut',TileId=29))
        async def flush():
            for i in list(s.waiting_players_list):
                assert s.action_queues[i].empty()
                await s.submit_action(i,'ready')
        s.flush_outbound_payloads=flush
        await s.run_round_ready_phase(.01)
        s.advance_round_after_ready()
        assert s.machine.phase==P.START and s.dealer_index==0 and s.dealer_streak==1
        assert [p.original_player_index for p in s.player_list]==list(range(4))
    asyncio.run(run())


def test_no_shape_win_and_restored_payloads_never_invent_winning_tile():
    s=state_with(WIN[:12]+[39]*3+WIN[15:])
    open_turn(s)
    act(s,{0:dict(action_type='sanjin')})
    payloads=s.restore_payloads(1)
    assert [p['type'] for p in payloads]==['gamestate/fuzhou/game_start','gamestate/fuzhou/show_result']
    assert payloads[-1]['show_result_info']['skip_hand_reveal']
