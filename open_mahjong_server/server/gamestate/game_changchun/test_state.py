import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock,patch

import pytest

from .ChangchunGameState import ChangchunGameState
from ...game_calculation.changchun import rules as book
from ...game_calculation.changchun.test_rules import READY
from ..public.game_record_manager import init_game_record,init_game_round


def make_state(**overrides):
    server=SimpleNamespace(user_id_to_connection={},gamestate_manager=SimpleNamespace(),room_manager=SimpleNamespace())
    room=dict(room_id="123456",player_list=[101,102,103,104],round_timer=20,step_timer=5,
              game_round=1,tips=True,room_type="custom",allow_spectator=False)
    state=ChangchunGameState(server,room|overrides,None,None,"test-changchun")
    for i,p in enumerate(state.player_list): p.player_index=p.original_player_index=i
    state.tiles_list=list(book.TILES)
    init_game_record(state);init_game_round(state)
    return state


def hand(state,index,tiles,melds=(),ready=False,drawn=None):
    player=state.player_list[index]
    player.hand_tiles=list(tiles)
    player.combination_tiles=list(melds)
    player.combination_mask=[[x for t in book.parse_meld(code).physical for x in (2 if code.startswith("G") else 0,t)] for code in melds]
    player.declared_ready=player.ready_locked=ready
    player.last_drawn_tile=drawn
    player.has_draw_slot=drawn is not None
    if ready:
        before=list(tiles)
        if drawn is not None: before.remove(drawn)
        player.locked_waits=book.waits(before,melds,declaration=True)
        player.tag_list.append("declared_ready")
    return player


def ticks(state): return state.game_record["game_round"]["round_index_1"]["action_ticks"]


def test_reconnect_draw_slot_is_explicit_after_special_meld():
    state=make_state()
    remaining=[11,12,13,22,23,24,35,36,37,45,45]
    hand(state,0,remaining,['Cwind:41,42,31:41,42,43'])
    own=state.build_private_game_info_fields(0)['changchun']
    assert not own['self_has_draw_slot'] and own['self_last_drawn_tile']==0
    hand(state,0,remaining,['Cwind:41,42,31:41,42,43'],drawn=45)
    own=state.build_private_game_info_fields(0)['changchun']
    assert own['self_has_draw_slot'] and own['self_last_drawn_tile']==45
    other=state.build_private_game_info_fields(1)['changchun']
    assert not other['self_has_draw_slot'] and other['self_last_drawn_tile']==0


def test_deal_conserves136_and_source_hand_sizes():
    state=make_state();state.master_seed=2024;state.init_tiles()
    assert [len(p.hand_tiles) for p in state.player_list]==[14,13,13,13]
    assert state.view_state(0)['self_has_draw_slot']
    assert state.view_state(0)['self_last_drawn_tile']==state.player_list[0].hand_tiles[-1]
    assert all(not state.view_state(i)['self_has_draw_slot'] for i in (1,2,3))
    all_tiles=state.tiles_list+[t for p in state.player_list for t in p.hand_tiles]
    assert len(all_tiles)==136 and set(Counter(all_tiles).values())=={4}
    assert len(state.cc_wall_ids)==83
    assert state.rules_dict["ordinary_jokers"] is False


def test_special_initial_no_replacement_and_first_turn_only():
    state=make_state()
    player=hand(state,0,[11,21,31,12,13,14,22,23,24,35,36,37,45,45],drawn=45)
    before=list(state.tiles_list)
    candidate=next(c for c in state.special_candidates(0) if c["code"]=="Cyao:11,21,31:11,21,31")
    assert asyncio.run(state.execute_special(0,candidate["token"]))
    assert len(player.hand_tiles)==11 and state.tiles_list==before
    assert player.combination_mask==[[0,11,0,21,0,31]]
    assert [p.score for p in state.player_list]==[3,-1,-1,-1]
    assert state.game_status=="waiting_hand_action"
    player.discard_count=1
    assert not state.special_candidates(0)


def special_ready_for_add():
    state=make_state()
    code="Cwind:41,42,31:41,42,43"
    player=hand(state,0,[11,12,13,22,23,24,35,36,37,45,31],[code],drawn=31)
    candidate=next(c for c in state.special_added_candidates(0) if c["tile"]==31 and c["represented"]==44)
    return state,player,candidate


def test_added_special_commits_after_window_and_pays_once():
    state,player,candidate=special_ready_for_add()
    assert asyncio.run(state.execute_special_added(0,candidate["token"]))
    assert state.game_status=="deal_card_after_gang"
    assert book.parse_meld(player.combination_tiles[0]).physical[-1]==31
    assert [p.score for p in state.player_list]==[3,-1,-1,-1]
    asyncio.run(state.finalize_jiagang())
    assert [p.score for p in state.player_list]==[3,-1,-1,-1]
    size=len(state.tiles_list)
    asyncio.run(state._deal_supplement())
    assert len(state.tiles_list)==size-1 and len(player.hand_tiles)==11


@pytest.mark.parametrize("kind,tile",[
    (kind,tile) for kind,domain in book.SPECIAL_DOMAINS.items()
    for tile in sorted(set(domain) | {book.ONE_BAMBOO})
])
def test_declared_ready_special_addition_is_offered_and_preserves_lock_through_supplement(kind,tile):
    state=make_state()
    domain=book.SPECIAL_DOMAINS[kind]
    code=book.special_code(kind,domain[:3],domain[:3])
    standing=[11,12,13,22,23,24,35,36,37,45]
    player=hand(state,0,standing+[tile],[code],ready=True,drawn=tile)
    player.discard_count=1
    player.cc_looked_turn=state.cc_turn_serial
    state.cc_bao_exhausted=True
    state.tiles_list[-2]=28
    size=len(state.tiles_list)
    assert player.locked_waits=={45}
    assert "cc_added" in state.check_hand_actions(0)[0]
    wire=state.build_private_hand_action_info(0)
    candidate=next(c for c in wire["changchun"]["added_candidates"] if c["tile"]==tile)
    assert wire["changchun"]["self_has_draw_slot"]
    assert wire["changchun"]["self_last_drawn_tile"]==tile

    assert asyncio.run(state.execute_special_added(0,candidate["token"]))
    assert state.game_status=="deal_card_after_gang"
    assert Counter(player.hand_tiles)==Counter(standing) and player.last_drawn_tile is None
    assert book.parse_meld(player.combination_tiles[0]).physical[-1]==tile
    assert [p.score for p in state.player_list]==[3,-1,-1,-1]
    asyncio.run(state._deal_supplement())
    assert len(state.tiles_list)==size-1 and player.last_drawn_tile==28
    assert player.ready_locked and player.declared_ready and player.locked_waits=={45}
    assert book.waits(player.hand_tiles[:-1],player.combination_tiles,declaration=True)=={45}
    before=list(player.hand_tiles)
    asyncio.run(state.execute_cut(0,{"TileId":11}))
    assert player.hand_tiles==before
    asyncio.run(state.execute_cut(0,{"TileId":28,"cutIndex":10,"cutClass":True}))
    assert Counter(player.hand_tiles)==Counter(standing) and player.discard_tiles[-1]==28
    assert player.ready_locked and player.locked_waits=={45}


def test_declared_ready_special_addition_can_be_robbed_without_payment_or_supplement():
    state,player,candidate=special_ready_for_add()
    player=hand(state,0,player.hand_tiles,player.combination_tiles,ready=True,drawn=31)
    player.discard_count=1
    other=hand(state,1,[31,31,11,12,13,22,23,24,33,34,35,45,45])
    original=player.combination_tiles[0]
    size=len(state.tiles_list)
    assert asyncio.run(state.execute_special_added(0,candidate["token"]))
    assert "peng" in state.action_dict[1]
    asyncio.run(state.resolve_rob_kong_responses({1:{"action_type":"peng"}},state.action_dict))
    assert player.combination_tiles==[original] and "k31" in other.combination_tiles
    assert player.ready_locked and player.declared_ready and player.locked_waits=={45}
    assert book.waits(player.hand_tiles,player.combination_tiles,declaration=True)=={45}
    assert len(state.tiles_list)==size and not state.kong_ledger
    assert state.current_player_index==1 and state.cc_pending_special is None


def test_physical_one_bamboo_can_be_robbed_as_pung():
    state,player,candidate=special_ready_for_add()
    other=hand(state,1,[31,31,11,12,13,22,23,24,33,34,35,45,45])
    code=player.combination_tiles[0]
    asyncio.run(state.execute_special_added(0,candidate["token"]))
    assert state.game_status=="waiting_action_qianggang"
    assert "peng" in state.action_dict[1]
    assert not any(a.startswith("chi_") for a in state.action_dict[1])
    asyncio.run(state.resolve_rob_kong_responses({1:{"action_type":"peng"}},state.action_dict))
    assert player.combination_tiles==[code] and "k31" in other.combination_tiles
    assert not state.kong_ledger and state.current_player_index==1
    assert not player.discard_tiles and player.discard_origin_tiles==[31]


def test_rob_hu_beats_pung_and_nearest_winner_only():
    state,player,candidate=special_ready_for_add()
    # Legal wait on physical 1s, including three suits and a dragon triplet.
    ready=[11,12,13,21,22,23,34,35,36,45,45,45,31]
    hand(state,1,ready);hand(state,2,ready);hand(state,3,[31,31]+[22]*11)
    original=player.combination_tiles[0]
    asyncio.run(state.execute_special_added(0,candidate["token"]))
    asyncio.run(state.resolve_rob_kong_responses({1:{"action_type":"hu_first"},2:{"action_type":"hu_second"},3:{"action_type":"peng"}},state.action_dict))
    assert state.game_status=="END" and [x["index"] for x in state.pending_winners]==[1]
    assert state.pending_winners[0]["tile"]==31 and state.pending_winners[0]["source"]=="robbing_kong"
    assert player.combination_tiles==[original] and not state.kong_ledger


@pytest.mark.parametrize("action",["peng","chi_right","gang"])
def test_claim_enters_first_turn_forbids_special_add_but_allows_initial(action):
    state=make_state();source=hand(state,0,[]);source.discard_tiles=[15]
    required=[15,15] if action=="peng" else [16,17] if action=="chi_right" else [15,15,15]
    other=hand(state,1,required+[11,21,31,41,42,43,45,45]+([22,23,24] if len(required)==2 else [22,23]))
    asyncio.run(state.execute_claim(1,action))
    assert other.cc_first_from_claim
    if action=="gang": asyncio.run(state._deal_supplement())
    assert state.special_candidates(1)
    assert not state.special_added_candidates(1)


def test_chow_cannot_reduce_standing_hand_to_one_but_concealed_kong_counts():
    state=make_state();hand(state,0,[]).discard_tiles=[35]
    hand(state,1,[33,34,45,45],["s12","k22","k29"])
    assert "chi_left" not in state.check_discard_actions(35)[1]
    hand(state,1,[33,34,45,45],["s12","k22","G29"])
    assert "chi_left" in state.check_discard_actions(35)[1]


def test_ready_cuts_and_locked_discard_no_chow_or_pung():
    state=make_state();player=hand(state,0,READY+[28],drawn=28)
    assert state.ready_candidate_cuts(0)[28]==[19]
    asyncio.run(state.execute_cut(0,{"TileId":28,"cutIndex":13,"cutClass":True},declare_ready=True))
    assert player.ready_locked and player.locked_waits=={19}
    player.hand_tiles.append(22);player.last_drawn_tile=22;player.has_draw_slot=True
    before=list(player.hand_tiles)
    asyncio.run(state.execute_cut(0,{"TileId":11}))
    assert player.hand_tiles==before
    state.current_player_index=1
    assert "peng" not in state.check_discard_actions(45)[0]


def test_ready_kong_locks_wait_and_no_kong_after_pung():
    state=make_state()
    ready=[11]*3+[22,23,24,31,32,33,45,45,45,19]
    player=hand(state,0,ready+[11],ready=True,drawn=11)
    assert state.kong_allowed(0,11,"concealed")
    player.cc_no_kong_until_draw=True
    assert not state.kong_allowed(0,11,"concealed")
    player.cc_no_kong_until_draw=False;player.locked_waits={29}
    assert not state.kong_allowed(0,11,"concealed")


def test_pass_threshold_allows_higher_fan_and_self_draw():
    state=make_state();player=hand(state,1,READY)
    detail=state.score_candidate(1,"discard",19);state.result_dict["hu_first"]=detail
    state.enter_water(1)
    assert state.score_candidate(1,"discard",19) is None
    player.hand_tiles.append(19);player.last_drawn_tile=19
    assert state.score_candidate(1,"self_draw")
    asyncio.run(state._process_drawn_flowers(1,"normal"))
    assert player.passed_fan==-1


def test_bao_not_visible_until_next_own_turn_and_chong_does_not_draw():
    state=make_state();player=hand(state,1,READY,ready=True)
    hand(state,2,READY,ready=True)
    state.tiles_list=[22]*20+[19,22]*6
    before=len(state.tiles_list)
    asyncio.run(state._deal_normal())
    assert state.current_player_index==1 and state.cc_window=="before_draw"
    assert state.bao_visible(1) and not state.bao_visible(2) and not state.bao_visible(0)
    assert state.view_state(0)["bao_tile"]==0 and state.view_state(1)["bao_tile"]==19
    assert len(player.hand_tiles)==13 and len(state.tiles_list)==before-1
    assert "hu_self" in state.action_dict[1]
    state.accept_self_draw(1)
    assert state.pending_winners[0]["source"]=="bao_indicator" and len(player.hand_tiles)==13


def test_bao_change_restores_position_and_resets_viewers():
    state=make_state();hand(state,0,READY,ready=True);hand(state,1,READY,ready=True)
    state.cc_window="before_draw"
    asyncio.run(state.change_bao(initial=True))
    old,slot=state.cc_bao_tile,state.cc_bao_slot
    state.player_list[1].cc_bao_seen=state.cc_bao_revision
    state.player_list[2].discard_tiles=[old]*3
    assert state.can_change_bao()
    asyncio.run(state.change_bao())
    assert slot in state.cc_wall_ids and state.tiles_list[state.cc_wall_ids.index(slot)]==old
    assert state.cc_bao_slot!=slot and not state.bao_visible(1)
    assert state.bao_visible(0)


def test_no_bao_when_six_candidate_slots_exhausted():
    state=make_state();hand(state,0,READY,ready=True)
    state._sync_wall_ids();even=len(state.tiles_list)//2*2
    state.cc_bao_used_slots={state.cc_wall_ids[even-2*d] for d in range(1,7)}
    state.cc_window="before_draw"
    asyncio.run(state.change_bao(initial=True))
    assert state.cc_bao_exhausted and state.cc_bao_tile is None


@pytest.mark.parametrize("size",[18,19])
def test_final_four_no_discards_claims_or_kongs(size):
    state=make_state();state.tiles_list=[18]*size
    for i in range(4): hand(state,i,READY)
    async def run():
        for count in range(4):
            await state._deal_normal()
            assert state.cc_window=="final_four"
            assert state.action_dict[state.current_player_index]==["cc_pass"]
            await state.execute_cut(state.current_player_index,{"TileId":18})
            assert not state.player_list[state.current_player_index].discard_tiles
            await state.pass_final_tile()
        assert state.game_status=="END" and state.draw_reason=="final_four_exhausted"
    asyncio.run(run())
    assert len(state.cc_tail_tiles)==4 and all(len(p.hand_tiles)==13 for p in state.player_list)
    assert len(state.tiles_list)==size-4


def test_bao_private_metadata_survives_models_and_delayed_record_is_redacted():
    from ...response import Do_action_info
    state=make_state();hand(state,0,READY,ready=True);state.cc_window="before_draw"
    asyncio.run(state.change_bao(initial=True))
    view=Do_action_info(action_list=[],action_player=0,action_tick=1,**state.build_private_do_action_info(0,1)).model_dump()
    assert view["changchun"]["bao_tile"]==0
    tick=next(t for t in ticks(state) if t[0]=="cc" and t[1]["kind"]=="bao_reveal")
    redacted=state.spectator_record_tick(tick)[1]
    assert tick[1]['tile']>0 and redacted['tile']==0 and 'dice' not in redacted


def test_settlement_baozhuang_and_draw_preserves_kong_score():
    state=make_state();state.current_round=4
    player=hand(state,1,READY)
    detail=state.score_candidate(1,"discard",19)
    state.pending_winners=[state._build_pending_winner(1,"discard","hu_first",detail,19,2)]
    state.game_status="END"
    asyncio.run(state._settle_hand({i:0 for i in range(4)}))
    assert [p.score for p in state.player_list]==[0,20,-20,0]
    assert state._terminal_result["show_result_info"]["changchun"]["payments"]
    assert state._terminal_result["show_result_info"]["changchun"]["round_score_changes"]=={0:0,1:20,2:-20,3:0}
    assert state.next_dealer()==1
    state=make_state();state.current_round=4
    asyncio.run(state._pay_kong(0,"concealed",31))
    with patch('server.gamestate.game_changchun.lifecycle.asyncio.sleep',new=AsyncMock()):
        asyncio.run(state._settle_hand({i:0 for i in range(4)}))
    assert [p.score for p in state.player_list]==[12,-4,-4,-4]
    assert state._terminal_result["show_result_info"]["changchun"]["round_score_changes"]=={0:12,1:-4,2:-4,3:-4}
    assert state.next_dealer()==0
