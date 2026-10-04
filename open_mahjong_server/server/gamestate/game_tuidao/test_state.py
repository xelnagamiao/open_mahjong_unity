import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .TuidaoGameState import TuidaoGameState
from ...game_calculation.tuidao import rules as book
from ...room.tuidao_room import normalize_tuidao_config, TuidaoRoomValidator
from ..public.game_record_manager import init_game_record, init_game_round


HAND = [11,12,13,14,15,16,21,22,23,34,35,36,28]


def make_state(**overrides):
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(), room_manager=SimpleNamespace())
    room = dict(room_id="123456", player_list=[101,102,103,104],
        round_timer=20, step_timer=5, game_round=1, tips=True, room_type="custom", allow_spectator=False)
    state = TuidaoGameState(server, room | overrides, None, None, "unit-tuidao-state")
    for i,p in enumerate(state.player_list):
        p.player_index=p.original_player_index=i
    state.tiles_list=[19]*30
    init_game_record(state)
    init_game_round(state)
    return state


def hand(state,index,tiles,melds=(),ready=False):
    player=state.player_list[index]
    player.hand_tiles=list(tiles)
    player.combination_tiles=list(melds)
    player.combination_mask=[[0,int(c[1:])]*3 for c in melds]
    player.declared_ready=player.ready_locked=ready
    if ready:
        player.locked_waits=book.waits(tiles,melds)
    return player


def draw(player,tile):
    player.hand_tiles.append(tile)
    player.has_draw_slot=True
    player.last_drawn_tile=tile


def test_deal_has_four_copies_no_flowers_or_wildcards():
    state=make_state()
    state.master_seed=2024
    state.init_tiles()
    all_tiles=state.tiles_list+[t for p in state.player_list for t in p.hand_tiles]
    assert len(all_tiles)==136 and set(Counter(all_tiles).values())=={4}
    assert [len(p.hand_tiles) for p in state.player_list]==[14,13,13,13]
    assert state.playable_wall_count()==70
    assert state.rules_dict["wildcards"] is False


@pytest.mark.parametrize("remaining,can_draw,count",[(12,False,0),(13,False,0),(14,True,1),(15,True,2)])
def test_six_stack_boundary_counts_unpaired_tile(remaining,can_draw,count):
    state=make_state(); state.tiles_list=[19]*remaining
    assert state.can_take_normal_tile() is can_draw
    assert state.can_take_supplement_tile() is can_draw
    assert state.playable_wall_count()==count


def test_optional_ready_and_first_draw_bonus():
    state=make_state(); player=hand(state,1,HAND); draw(player,28)
    player.normal_draw_count=2
    detail=state.score_candidate(1,"self_draw")
    assert detail and "报听" not in detail["fan_names"]
    player.normal_draw_count=1
    assert "天和" in state.score_candidate(1,"self_draw")["fan_names"]
    state.table_claim_or_kong=True
    assert "天和" not in state.score_candidate(1,"self_draw")["fan_names"]


def test_ready_available_only_after_draw_and_locks_discard():
    state=make_state(); player=hand(state,0,HAND); draw(player,19)
    assert state.ready_candidate_cuts(0)[19]==[28]
    asyncio.run(state.execute_cut(0,{"TileId":19,"cutIndex":13,"cutClass":True},declare_ready=True))
    assert player.ready_locked and player.tuidao_heavenly_ready
    assert player.locked_waits=={28}
    draw(player,22); before=list(player.hand_tiles)
    asyncio.run(state.execute_cut(0,{"TileId":11}))
    assert player.hand_tiles==before
    assert not state.ready_candidate_cuts(0)


def test_no_ready_or_kongs_immediately_after_chow_or_pung():
    state=make_state(); hand(state,0,[21,22,23,31,32,33,17,18,19,44,11],["s12"])
    assert not state.ready_candidate_cuts(0)
    assert state.after_claim_actions()[0]==["cut"]
    assert not state.kong_allowed(0,21,"added")


def test_pass_blocks_only_equal_or_lower_fan_until_draw():
    state=make_state(); player=hand(state,1,HAND)
    detail=state.score_candidate(1,"discard",28)
    state.result_dict["hu_first"]=detail
    state.enter_water(1)
    assert not player.water
    assert state.score_candidate(1,"discard",28) is None
    assert state.score_candidate(1,"robbing_kong",28)
    draw(player,28)
    assert state.score_candidate(1,"self_draw")
    asyncio.run(state._process_drawn_flowers(1,"normal"))
    assert player.passed_fan==-1


def test_self_discard_of_winning_tile_sets_pass_threshold():
    state=make_state(); player=hand(state,0,HAND); draw(player,28)
    player.normal_draw_count=2
    asyncio.run(state.execute_cut(0,{"TileId":28,"cutIndex":13,"cutClass":True}))
    assert player.passed_fan>=0
    assert state.score_candidate(0,"discard",28) is None


def test_chow_pung_priority_and_head_bump():
    state=make_state(); state.current_player_index=0
    source=hand(state,0,[]); source.discard_tiles=[11]
    hand(state,1,[12,13]+[29]*10+[28]); hand(state,2,[11,11]+[29]*10+[28])
    allowed=state.check_discard_actions(11)
    assert "chi_right" in allowed[1] and "peng" in allowed[2]
    asyncio.run(state.resolve_discard_responses({1:{"action_type":"chi_right"},2:{"action_type":"peng"}},allowed))
    assert state.current_player_index==2
    assert state.player_list[2].combination_tiles==["k11"]
    state.current_player_index=0
    assert state._selected_winners([(3,"hu_third"),(1,"hu_first"),(2,"hu_second")])==[(1,"hu_first")]


@pytest.mark.parametrize("kind,drawn,expected",[("concealed",True,[6,-2,-2,-2]),("added",True,[3,-1,-1,-1]),("added",False,[0,0,0,0])])
def test_kong_payments_commit_once_with_different_draw_origin(kind,drawn,expected):
    state=make_state()
    if kind=="concealed":
        player=hand(state,0,[11]*3+[22,23,24,31,32,33,41,41,41,47]); draw(player,11)
        asyncio.run(state.execute_angang(0,11))
        asyncio.run(state.execute_angang(0,11))  # stale duplicate is a no-op
    else:
        player=hand(state,0,[11,22,23,24,31,32,33,41,41,47],["k11"])
        if drawn:
            player.hand_tiles.remove(11); player.hand_tiles.append(47); draw(player,11)
        else: draw(player,47)
        asyncio.run(state.execute_jiagang(0,11))
        asyncio.run(state.finalize_jiagang())  # finalization must not pay again
    assert [p.score for p in state.player_list]==expected
    assert state.game_status=="deal_card_after_gang"


def test_robbed_added_kong_does_not_pay_kong_score():
    state=make_state()
    player=hand(state,0,[22,23,24,31,32,33,41,41,47,47],["k28"]); draw(player,28)
    hand(state,1,HAND)
    asyncio.run(state.execute_jiagang(0,28))
    assert state.game_status=="waiting_action_qianggang"
    allowed=state.action_dict
    asyncio.run(state.resolve_rob_kong_responses({1:{"action_type":"hu_first"}},allowed))
    assert state.game_status=="END" and state.pending_winners[0]["source"]=="robbing_kong"
    assert [p.score for p in state.player_list]==[0]*4
    assert player.combination_tiles==["k28"] and 28 not in player.hand_tiles


def test_direct_kong_charges_only_discarder_and_persists_on_draw():
    state=make_state(); source=hand(state,0,[]); source.discard_tiles=[11]
    hand(state,1,[11]*3+[22,23,24,31,32,33,41,41,41,47])
    asyncio.run(state.execute_claim(1,"gang"))
    assert [p.score for p in state.player_list]==[-2,2,0,0]
    before={i:0 for i in range(4)}
    state.current_round=4
    with patch("server.gamestate.game_tuidao.TuidaoGameState.asyncio.sleep",new=AsyncMock()):
        asyncio.run(state._settle_hand(before))
    assert [p.score for p in state.player_list]==[-2,2,0,0]
    ticks=state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ["tuidao","kong_score",[-2,2,0,0],"direct"] in ticks


def test_ready_kong_keeps_shape_and_does_not_revoke_ready():
    state=make_state(); player=hand(state,0,[11]*3+[22,23,24,31,32,33,41,41,41,47],ready=True)
    draw(player,11)
    assert state.kong_allowed(0,11,"concealed")
    asyncio.run(state.execute_angang(0,11))
    assert player.ready_locked and player.declared_ready
    assert book.waits(player.hand_tiles,player.combination_tiles)=={47}


def test_ready_kong_rejects_alternate_sequence_shape_even_with_same_waits():
    state=make_state(); player=hand(state,0,[11]*3+[12]*3+[13]*3+[21,22,23,47],ready=True); draw(player,11)
    assert not state.kong_allowed(0,11,"concealed")


def test_last_discard_has_haidi_but_last_self_draw_does_not():
    state=make_state(); state.tiles_list=[19]*13
    player=hand(state,1,HAND)
    assert "海底" in state.score_candidate(1,"discard",28)["fan_names"]
    draw(player,28)
    assert "海底" not in state.score_candidate(1,"self_draw")["fan_names"]
    assert all(a.startswith("hu_") or a=="pass" for row in state.check_discard_actions(28).values() for a in row)


def test_dealer_continuation_and_round_reset():
    state=make_state(); assert state.next_dealer()==0
    state.pending_winners=[{"index":0}]; assert state.next_dealer()==0
    state.pending_winners=[{"index":3}]; assert state.next_dealer()==1
    player=state.player_list[0]; player.passed_fan=32; player.ready_locked=True
    state._reset_hand_runtime()
    assert player.passed_fan==-1 and not player.ready_locked and not state.kong_ledger


@pytest.mark.parametrize("config",[{"wildcards":True},{"wildcards":0},{"fan_cap":64},{"edition":"wrong"},{"foo":False},[]])
def test_fixed_profile_rejects_nonstandard_variants(config):
    with pytest.raises(ValueError): normalize_tuidao_config(config)


def test_room_defaults_pin_version_and_no_flowers():
    room=TuidaoRoomValidator(room_name="推倒和",game_round=1,round_timer=20,step_timer=5)
    assert room.sub_rule==book.SUB_RULE and room.detailed_config["edition"]==book.EDITION
    assert not room.use_flowers


def test_protocol_metadata_and_mask_hide_private_tiles():
    state=make_state(); hand(state,0,[11]*4+[21,22,23,31,32,33,41,41,41,47]); state.player_list[0].last_drawn_tile=11
    payloads=[]
    class Socket:
        async def send_json(self,data): payloads.append(data)
    for p in state.player_list:
        state.game_server.user_id_to_connection[p.user_id]=SimpleNamespace(websocket=Socket())
    asyncio.run(state.execute_angang(0,11))
    assert len(payloads)==8
    assert all(m["type"]=="gamestate/guangdong/do_action" for m in payloads)
    assert payloads[4]["do_action_info"]["gang_score_changes"]=={0:6,1:-2,2:-2,3:-2}
    assert state.build_record_title_fields()["detailed_config"]["edition"]==book.EDITION
