import asyncio
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .ShangraoGameState import ShangraoGameState
from .actions import claim_actions, kong_tiles
from .state_machine import Phase as P
from .get_action import handle_action
from ...game_calculation.shangrao import rules as r

NORMAL = [11,12,13,14,15,16,22,23,24,31,32,33,41,41]


def state():
    s = ShangraoGameState(calculation_service=object())
    s.initialize_round(dice=[2,3])
    s.machine.transition(P.TURN)
    return s


def turn(hand=NORMAL, index=0):
    s = state()
    s.indicator = 45
    p = s.player_list[index]
    p.hand_tiles, p.has_draw_slot, p.draw_kind = list(hand), True, "normal"
    s.current_player_index = index
    s.open_action_window(s.begin_turn(index))
    return s


def act(s, responses=None, **data):
    window = s.live_pending_window
    if data:
        responses = {window["player"]: data}
    else:
        responses = {i: dict(action_type=(responses or {}).get(i, "pass"))
                     for i, actions in window["actions"].items() if actions}
    return s.apply_action_results(window, responses)


def wall(s, entries):
    s.wall_positions = [p for p, _ in entries]
    s.tiles_list = [t for _, t in entries]
    s.indicator_position = next(p for p, t in entries if t == s.indicator)
    s.indicator_index = s.wall_positions.index(s.indicator_position)


def test_deal_indicator_uses_opening_dice_and_preserves_supply():
    s = state()
    assert [len(p.hand_tiles) for p in s.player_list] == [14,13,13,13]
    assert s.indicator_position == 126 and s.indicator_index == 73
    assert len(s.tiles_list) == 83
    assert Counter(s.tiles_list + [t for p in s.player_list for t in p.hand_tiles]) == Counter({t:4 for t in r.TILES})
    assert s.player_list[0].has_draw_slot and s.player_list[0].draw_kind == "opening"
    assert s.build_game_start_payload(0)["game_info"]["tile_count"] == 82


def test_normal_draw_skips_indicator_and_exhausts_exactly_82_tiles():
    s = state()
    draws = []
    while (tile := s.take_normal_tile()) is not None:
        draws.append(tile)
    assert len(draws) == 82 and s.tiles_list == [s.indicator]
    s.draw_for(1)
    assert s.machine.phase == P.END and s.ended_by == "wall"


def test_kong_draw_lower_burns_upper_then_preserves_indicator_upper():
    s = turn()
    wall(s, [(125,12),(126,45),(127,46),(128,11),(129,47)])
    assert s.take_kong_tile() == (47,11)
    assert s.burnt_tiles == [11]
    assert s.take_kong_tile() == (46,None)
    assert s.tiles_list == [12,45] and not s.can_draw_kong()
    with pytest.raises(ValueError):
        s.take_kong_tile()


def test_last_orphan_lower_cannot_replace_in_v02():
    s = turn()
    wall(s, [(125,12),(126,45)])
    assert not s.can_draw_kong()
    with pytest.raises(ValueError):
        s.take_kong_tile()
    assert s.tiles_list == [12,45]


def test_only_self_draw_and_first_bao_pair_restriction():
    s = turn(NORMAL[:-1] + [47])
    assert "hu_self" not in s.action_dict[0]
    assert not s.can_win(0,"discard",41,payer=1)
    assert not s.can_win(0,"rob_kong",41,payer=1)
    s.player_list[0].hand_tiles = NORMAL[:12] + [47,41]
    assert s.can_win(0,"self_draw",41)
    assert not any("hu" in values for values in claim_actions(s,1,41).values())


def test_physical_claims_cannot_substitute_bao_or_chow_honors():
    s = turn()
    s.player_list[1].hand_tiles = [11,47,41,42,44,21,22,23,31,32,33,45,45]
    assert "chi_mid" not in claim_actions(s,0,12)[1]
    assert not any(a.startswith("chi") for a in claim_actions(s,0,43)[1])
    assert "peng" not in claim_actions(s,0,11)[1]
    s.player_list[1].hand_tiles[:2] = [47,47]
    assert "peng" in claim_actions(s,0,47)[1]


def test_pung_beats_chow_and_nearest_pung_wins():
    s = turn(NORMAL[:-1] + [15])
    s.player_list[1].hand_tiles = [14,16,21,22,23,31,32,33,41,41,42,42,43]
    for index in (2,3):
        s.player_list[index].hand_tiles = [15,15,21,22,23,31,32,33,41,41,42,42,43]
    act(s, action_type="cut", TileId=15, cutClass=True)
    act(s, {1:"chi_mid",2:"peng",3:"peng"})
    assert s.current_player_index == 2 and s.player_list[2].combination_tiles == ["k15"]
    assert s.machine.phase == P.AFTER_MELD and "hu_self" not in s.action_dict[2]


@pytest.mark.parametrize("kind,points", [("angang",2),("jiagang",1)])
def test_immediate_kong_after_chow_and_pung(kind,points):
    s = turn()
    p = s.player_list[1]
    p.hand_tiles = [21]*4 + [11,12,13,31,32,33,41]
    p.combination_tiles, p.combination_mask = ["s25"], [[1,24,0,25,0,26]]
    if kind == "jiagang":
        p.hand_tiles = [21,11,12,13,31,32,33,41]
        p.combination_tiles.append("k21")
        p.combination_mask.append([0,21,1,21,0,21])
    p.has_draw_slot = False
    s.machine.phase = P.RESPONSE
    s.open_action_window(s.begin_turn(1,after_meld=True))
    assert 21 in kong_tiles(s,1,kind)
    act(s,action_type=kind,target_tile=21)
    assert p.after_kong and p.has_draw_slot
    assert [q.score for q in s.player_list] == r.payments(1,points)
    assert len(s.burnt_tiles) == 1
    assert s.machine.phase == P.TURN


def test_self_kong_all_four_visible_to_every_viewer():
    s = turn([21]*4 + NORMAL[4:])
    s.commit_kong(0,21,"angang")
    for viewer in range(4):
        view = s.build_game_start_payload(viewer)["game_info"]["players_info"][0]
        assert view["combination_tiles"] == ["G21"]
        assert view["combination_mask"] == [[0,21]*4]
        event = next(x for x in s.outbound_payloads if x.get("player_index")==viewer and x.get("action")=="angang")
        assert event["tile"] == 21 and event["do_action_info"]["combination_mask"] == [0,21]*4


def test_indicator_pung_pays_once_without_replacement():
    s = turn(NORMAL[:-1]+[45])
    p=s.player_list[1];p.hand_tiles = [45,45,21,22,23,31,32,33,41,41,42,42,43]
    before=len(s.tiles_list)
    act(s,action_type="cut",TileId=45,cutClass=True)
    act(s,{1:"peng"})
    assert p.combination_tiles == ["k45"] and len(s.tiles_list)==before
    assert [p.score for p in s.player_list] == r.payments(1,1)
    assert s.payment_ledger[0]["kind"] == "indicator_pung"


@pytest.mark.parametrize("winner", [None,0,2])
def test_indicator_triplet_paid_at_end_for_any_holder(winner):
    s = turn(NORMAL,index=winner or 0)
    s.player_list[1].hand_tiles = [45]*3 + [11,12,13,21,22,23,31,32,33,41]
    if winner is None:
        s.end_draw()
        expected=r.payments(1,2)
    else:
        s.settle_win(winner,"self_draw",41)
        expected=[a+b for a,b in zip(r.payments(winner,2),r.payments(1,2))]
    assert [p.score for p in s.player_list] == expected
    s.apply_deferred_score_changes();s.apply_deferred_score_changes()
    assert [p.score for p in s.player_list] == expected
    assert all(len(p.score_history)==1 for p in s.player_list)
    with pytest.raises(ValueError):s.end_draw()


def test_kong_payments_survive_draw_and_do_not_double_on_restore():
    s = turn([21]*4 + NORMAL[4:])
    s.commit_kong(0,21,"angang")
    expected=[p.score for p in s.player_list]
    s.end_draw()
    for _ in range(3):s.restore_payloads(0)
    assert [p.score for p in s.player_list] == expected
    assert s.round_changes == expected


def test_four_bao_draw_history_includes_flown_but_not_claimed_or_burnt():
    s = turn();s.detailed_config["four_bao_reward"] = True
    p=s.player_list[0];p.drawn_bao_count=3;p.discard_origin_tiles=[47,47,47]
    wall(s,[(124,11),(125,47),(126,45)])
    s.draw_for(0,"kong")
    assert p.drawn_bao_count==4 and not p.four_bao_paid
    assert [p.score for p in s.player_list] == [0]*4
    s.check_four_bao(0)
    assert not s.payment_ledger
    s.end_draw()
    assert p.four_bao_paid and [p.score for p in s.player_list] == r.payments(0,2)
    assert len(s.payment_ledger)==1
    s=turn();s.detailed_config["four_bao_reward"] = True
    wall(s,[(124,47),(125,11),(126,45)])
    s.draw_for(0,"kong")
    assert s.burnt_tiles==[47] and s.player_list[0].drawn_bao_count==0
    assert s.player_list[0].discard_origin_tiles==[] and not s.payment_ledger
    assert s.player_list[0].discard_tiles==[] and s.burnt_river_indices==[[],[],[],[]]


@pytest.mark.parametrize('actor',range(4))
def test_kong_burn_is_blue_in_actor_river_without_becoming_a_discard(actor):
    s=turn(index=actor);p=s.player_list[actor]
    p.discard_tiles=[19];p.discard_origin_tiles=[19]
    wall(s,[(124,31),(125,22),(126,45)])
    s.start_game_recording();s.start_round_recording()
    result=s.draw_for(actor,'kong')
    assert result['status']==P.TURN.value and result['player']==actor
    assert p.discard_tiles==[19,31] and p.discard_origin_tiles==[19]
    assert s.burnt_river_indices[actor]==[1] and s.discard_log==[]
    for viewer in range(4):
        info=s.build_game_start_payload(viewer)['game_info']
        assert info['players_info'][actor]['discard_tiles']==[19,31]
        assert info['shangrao_info']['burnt_river_indices'][actor]==[1]
    ticks=next(iter(s.game_record['game_round'].values()))['action_ticks']
    assert ['shangrao','burnt',31,actor] in ticks
    assert not any(t[0]=='c' for t in ticks)


def test_live_payment_and_restored_history_have_exactly_one_delta():
    s=turn();s.record_payment(2,1,'gang',21)
    payload=s.outbound_payloads[-1]
    assert payload['do_action_info']['gang_score_changes']=={0:-1,1:-1,2:3,3:-1}
    assert payload['game_info']['shangrao_info']['start_scores']==[0,0,0,0]
    assert [p.score for p in s.player_list]==[-1,-1,3,-1]
    s.settle_win(0,'self_draw',41)
    for viewer in range(4):
        for _ in range(2):s.restore_payloads(viewer)
    assert [p.score for p in s.player_list]==[5,-3,1,-3]
    assert [p.score_history for p in s.player_list]==[['+5'],['-3'],['+1'],['-3']]


def test_initial_four_bao_reward_once_and_flying_star_history():
    s=turn([47]*4+NORMAL[:10]);s.detailed_config["four_bao_reward"]=True
    s.opening_window();assert s.player_list[0].drawn_bao_count==4
    assert s.player_list[0].score==0
    s.end_draw();assert s.player_list[0].score==6
    s=turn([11,14,17,21,24,27,31,41,42,43,44,45,46,47])
    act(s,action_type="cut",TileId=47,cutClass=True)
    assert s.player_list[0].flying_seven_stars
    assert s.player_list[0].discard_origin_tiles==[47]


def test_hints_contain_only_own_inputs_and_public_configuration():
    s=turn();s.tips=True
    for viewer in range(4):
        info=s.build_game_start_payload(viewer)["game_info"]
        hints=info["shangrao_hints"]
        assert set(hints)=={"source_hand_tiles","source_melds","local_request"}
        assert hints["local_request"]["hand"] == s.player_list[viewer].hand_tiles
        for i,p in enumerate(info["players_info"]):
            assert (p["hand_tiles"] is not None)==(i==viewer)
    before=deepcopy(s.player_list[0].hand_tiles)
    s.build_game_start_payload(0)["game_info"]["shangrao_hints"]["local_request"]["hand"].clear()
    assert s.player_list[0].hand_tiles==before


def test_win_record_stats_count_own_discards_and_payment_replay_ticks():
    s=turn();p=s.player_list[0];p.discard_origin_tiles=[19,29,39];p.discard_tiles=[29]
    s.start_game_recording();s.start_round_recording()
    s.record_payment(2,1,"gang",21)
    s.settle_win(0,"self_draw",41);s.finalize_round_recording()
    assert p.record_counter.zimo_times==1 and p.record_counter.win_turn==4
    assert p.record_counter.win_score==6 and p.record_counter.dianhe_times==0
    snapshot=next(iter(s.game_record["game_round"].values()))
    assert "shangrao" in snapshot and "shangrao_hints" in snapshot
    assert s.round_record_finalized
    assert s.next_dealer==0


def test_winner_becomes_dealer_and_player_identity_stays_stable():
    s=turn(index=2);ids=[p.user_id for p in s.player_list]
    s.settle_win(2,"self_draw",41);s.machine.transition(P.READY);s.advance_round_after_ready()
    assert [p.user_id for p in s.player_list]==ids[2:]+ids[:2]
    assert [p.original_player_index for p in s.player_list]==[2,3,0,1]
    s.initialize_round();assert s.current_round==2 and s.current_player_index==0
    assert all(not p.flying_seven_stars and not p.discard_origin_tiles for p in s.player_list)


def test_window_rejects_stale_duplicate_or_forged_settlement():
    s=turn();old=s.live_pending_window
    with pytest.raises(ValueError):s.apply_action_results(old,{0:dict(action_type="hu_self")},settlements=[{"points":999}])
    with pytest.raises(ValueError):s.apply_action_results(old,{1:dict(action_type="hu_self")})
    act(s,action_type="cut",TileId=41,cutClass=True)
    with pytest.raises(ValueError):s.apply_action_results(old,{0:dict(action_type="hu_self")})


def test_socket_identity_and_exact_action_tick():
    async def run():
        s=turn();socket=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:s),
            players={"connection":SimpleNamespace(user_id=999)})
        packet=dict(type="gamestate/shangrao/send_action",gamestate_id=s.gamestate_id,action="hu_self",action_tick=s.server_action_tick)
        await handle_action(server,"connection",packet,socket)
        assert s.action_queues[0].empty()
        server.players["connection"].user_id=101
        await handle_action(server,"connection",{**packet,"action_tick":-1},socket)
        assert s.action_queues[0].empty() and socket.send_json.await_count==1
        await handle_action(server,"connection",packet,socket)
        await handle_action(server,"connection",packet,socket)
        assert s.action_queues[0].qsize()==1
    asyncio.run(run())


def test_wait_action_preserves_early_reply_and_charges_fractional_bank():
    async def run():
        s=turn();s.player_list[0].remaining_time=5;s.step_time=1
        s.open_action_window(s.begin_turn(0))
        s._timing_now=lambda:10.0;s._start_action_clock(0)
        s._timing_now=lambda:11.25
        await s.submit_action(0,"cut",TileId=41,cutClass=True)
        responses=await s.wait_action()
        assert responses[0]["TileId"]==41 and s.player_list[0].remaining_time==4.75
    asyncio.run(run())
