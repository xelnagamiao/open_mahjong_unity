import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch
import pytest

from .ShanxiGameState import ShanxiGameState
from ...game_calculation.shanxi import rules as sx
from ..public.game_record_manager import init_game_record, init_game_round
from ..game_taiwan.boardcast import _build_do_action_payload

BASE=[11,12,13,21,22,23,31,32,33,17,18,19]


def make_state():
    server=SimpleNamespace(user_id_to_connection={},gamestate_manager=SimpleNamespace(),room_manager=SimpleNamespace())
    state=ShanxiGameState(server,dict(room_id="100012",player_list=[101,102,103,104],
        round_timer=0,step_timer=0,game_round=1,tips=True,room_type="custom",allow_spectator=False),None,None,"sx-test")
    for i,p in enumerate(state.player_list):
        p.player_index=p.original_player_index=i
    state.tiles_list=[11]*30
    init_game_record(state)
    init_game_round(state)
    return state


def ready(player,hand):
    player.hand_tiles=list(hand)
    player.declared_ready=player.ready_locked=True


def test_136_tiles_13_tile_hand_deal_is_deterministic():
    state=make_state()
    state.master_seed=4727
    state.init_tiles()
    wall=list(state.tiles_list)+[t for p in state.player_list for t in p.hand_tiles]
    assert Counter(wall)==Counter({t:4 for t in sx.TILES})
    assert [len(p.hand_tiles) for p in state.player_list]==[14,13,13,13]
    assert state.playable_wall_count()==69
    first=list(state.tiles_list)
    state.init_tiles()
    assert first==state.tiles_list


def test_ready_cut_is_concealed_and_not_claimable():
    state=make_state()
    p=state.player_list[0]
    p.hand_tiles=BASE+[47,16]
    p.has_draw_slot=True
    p.last_drawn_tile=16
    assert 16 in state.ready_candidate_cuts(0)
    asyncio.run(state.execute_cut(0,dict(TileId=16,cutClass=True,cutIndex=13),declare_ready=True))
    assert p.declared_ready and p.ready_locked
    assert p.discard_tiles==[0] and p.concealed_discards=={0:16}
    assert p.discard_origin_tiles==[]  # 该列表只保存被鸣走的弃牌，暗扣牌不可被鸣走。
    assert not any(state.action_dict.values())
    assert 16 not in state.public_tiles()
    ticks=state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert next(t for t in ticks if t[0]=="c")==["c",16,"T","C"]


def test_hidden_cut_payload_does_not_leak_to_opponent():
    state=make_state()
    state.concealed_discard_actor=0
    other=_build_do_action_payload(state,["cut"],0,1,cut_tile=47,cut_tile_index=5)
    own=_build_do_action_payload(state,["cut"],0,0,cut_tile=47,cut_tile_index=5)
    assert other["cut_tile"]==0 and other["concealed_discard"]
    assert own["cut_tile"]==47 and own["concealed_discard"]


def test_ready_player_forced_win_blocks_cut_and_kong():
    state=make_state()
    p=state.player_list[0]
    ready(p,BASE+[47,47])
    p.last_drawn_tile=47
    p.has_draw_slot=True
    assert state.check_hand_actions(0)[0]==["hu_self"]
    asyncio.run(state.execute_cut(0,dict(TileId=47)))
    assert state.game_status=="END" and state.pending_winners[0]["index"]==0


def test_head_bump_resolves_forced_win_even_if_response_says_pass():
    state=make_state()
    state.player_list[0].discard_tiles=[47]
    for i in (1,2):
        ready(state.player_list[i],BASE+[47])
    actions=state.check_discard_actions(47)
    assert actions[1]==["hu_first"] and actions[2]==["hu_second"]
    asyncio.run(state.resolve_discard_responses({1:{"action_type":"pass"},2:{"action_type":"hu_second"}},actions))
    assert [w["index"] for w in state.pending_winners]==[1]


@pytest.mark.parametrize("single,length,normal,kong",[(False,14,False,False),(False,15,True,False),
    (False,16,True,True),(True,15,False,False),(True,16,True,True)])
def test_seven_complete_tail_stacks(single,length,normal,kong):
    state=make_state()
    state.tail_single=single
    state.tiles_list=list(range(length))
    assert state.can_take_normal_tile()==normal
    assert state.can_establish_kong()==kong


def test_replacement_draws_upper_then_lower_from_tail():
    state=make_state()
    state.tiles_list=list(range(30))
    assert state._take_supplement_tile()==28
    assert state.tail_single and state.dead_wall_count==15
    assert state._take_supplement_tile()==29
    assert not state.tail_single and state.dead_wall_count==14


def test_no_chow_and_no_kong_immediately_after_pung():
    state=make_state()
    state.player_list[0].discard_tiles=[11]
    state.player_list[1].hand_tiles=[11,11,12,13,21,21,21,21,31,32,33,47,47]
    assert not any(a.startswith("chi") for a in state.check_discard_actions(11)[1])
    asyncio.run(state.execute_claim(1,"peng"))
    assert state.player_list[1].combination_tiles==["k11"]
    assert not state.kong_allowed(1,21,"G")
    before=list(state.player_list[1].hand_tiles)
    asyncio.run(state.execute_angang(1,21))
    assert state.player_list[1].hand_tiles==before


def test_no_rob_added_kong_and_kong_ledger_is_deferred():
    state=make_state()
    p=state.player_list[0]
    p.hand_tiles=[47,11,12,13,21,22,23,31,32,33,19]
    p.combination_tiles=["k47"]
    p.combination_mask=[[0,47,1,47,0,47]]
    p.has_draw_slot=True
    p.last_drawn_tile=19
    asyncio.run(state.execute_jiagang(0,47))
    assert p.combination_tiles==["g47"]
    assert state.kong_ledger==[(0,47,False)]
    assert state.game_status=="deal_card_after_gang"
    assert not any(state.check_added_kong_actions(47).values())
    assert all(p.score==0 for p in state.player_list)


def test_draw_rotation_depends_on_kong_not_pung():
    state=make_state()
    state.player_list[1].combination_tiles=["k11"]
    assert state._dealer_continues()
    state.kong_ledger=[(1,11,False)]
    assert not state._dealer_continues()
    assert not any(state.settlement()["total"].values())
    state.pending_winners=[{"index":0}]
    assert state._dealer_continues()


def test_passing_pung_blocks_same_tile_until_next_draw():
    state=make_state()
    state.player_list[0].discard_tiles=[11]
    state.player_list[1].hand_tiles=[11,11]+BASE[:11]
    actions=state.check_discard_actions(11)
    asyncio.run(state.resolve_discard_responses({1:{"action_type":"pass"}},actions))
    assert 11 in state.player_list[1].passed_pungs
    assert "peng" not in state.check_discard_actions(11)[1]


def test_ready_kong_can_change_waits_but_must_still_allow_high_tile_ready():
    state=make_state()
    p=state.player_list[0]
    ready(p,[11]*4+[12,13,14,15,16,17,18]+[19]*3)
    p.has_draw_slot=True
    p.last_drawn_tile=11
    before=sx.waits(p.hand_tiles[1:])
    assert state.kong_allowed(0,11,"G")
    asyncio.run(state.execute_angang(0,11))
    assert p.combination_tiles==["G11"] and p.declared_ready and p.ready_locked
    assert state.kong_ledger==[(0,11,True)]
    assert sx.waits(p.hand_tiles,p.combination_tiles)!=before
    assert sx.ready_waits(p.hand_tiles,p.combination_tiles,state.public_tiles())


def test_ready_kong_rejected_if_it_breaks_the_shape():
    state=make_state()
    p=state.player_list[0]
    ready(p,[19]*4+[21,21,23,23,25,25,27,27,29,29])
    p.has_draw_slot=True
    p.last_drawn_tile=19
    assert not state.kong_allowed(0,19,"G")


@pytest.mark.parametrize("kind,hand,meld",[("g",[11]*3+BASE[:10],[]),("added",[11]+BASE[:10],["k11"])])
def test_terminal_wall_cannot_establish_any_kong(kind,hand,meld):
    state=make_state()
    state.tiles_list=[11]*14
    p=state.player_list[1]
    p.hand_tiles=hand
    p.combination_tiles=meld
    p.has_draw_slot=True
    assert not state.kong_allowed(1,11,kind)
    with pytest.raises(RuntimeError): state._take_supplement_tile()


def test_round_reset_clears_passed_pung_hidden_discards_and_kong_ledger():
    state=make_state()
    p=state.player_list[0]
    p.passed_pungs={11}
    p.concealed_discards={0:47}
    p.discard_origin_tiles=[11]
    state.kong_ledger=[(0,47,True)]
    state.tail_single=True
    state._reset_hand_runtime()
    assert not p.passed_pungs and not p.concealed_discards and not p.discard_origin_tiles
    assert not state.kong_ledger and not state.tail_single and state.dead_wall_count==14


def test_claim_clock_uses_actual_delivery_and_three_second_cap():
    state=make_state()
    p=state.player_list[0]
    p.remaining_time=20
    state.step_time=5
    assert state.claim_clock(p)==(0,3)
    state._ask_delivered_at={0:100.0}
    with patch("server.gamestate.game_shanxi.ShanxiGameState.time.time",return_value=101.2):
        assert state.claim_clock(p,True)==(0,2)


def test_registered_state_retains_shanxi_index_and_reconnect_identity():
    from ..gamestate_manager import GameStateManager
    state=make_state()
    state.game_server.room_manager=SimpleNamespace(active_game_room_ids={},rooms={})
    manager=GameStateManager(state.game_server)
    manager.register_game(state,{})
    assert manager.room_id_to_ShanxiGameState[state.room_id] is state
    assert manager.get_game_state_by_room_id(state.room_id) is state
    for p in state.player_list: assert manager.user_id_to_game_state[p.user_id] is state


@pytest.mark.parametrize("self_draw",[False,True])
def test_settlement_records_full_zero_sum_payment_with_seat_remap(self_draw):
    state=make_state()
    state.current_round=4
    winner=state.player_list[1]
    ready(winner,BASE+[17,17] if self_draw else BASE+[17])
    for p in state.player_list: p.original_player_index=(p.player_index+2)%4
    state.pending_winners=[dict(index=1,source="self_draw" if self_draw else "discard",tile=17,
        payer=None if self_draw else 0,hu_class="hu_self" if self_draw else "hu_first",
        detail=sx.score(BASE+[17,17],winning_tile=17,declared_ready=True))]
    state.kong_ledger=[(2,19,False)]
    before={p.original_player_index:p.score for p in state.player_list}
    with patch("server.gamestate.game_shanxi.ShanxiGameState.broadcast_result",new_callable=AsyncMock) as broadcast:
        keeps,ends=asyncio.run(state._settle_hand(before))
    assert not keeps and ends
    assert sum(p.score for p in state.player_list)==0
    changes=broadcast.await_args.kwargs["score_changes"]
    assert all(changes[p.original_player_index]==p.score for p in state.player_list)
    ticks=state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert any(t[:2]==["state","shanxi_settlement"] for t in ticks)


def test_reconnect_snapshot_hides_ready_discard_and_preserves_locks():
    from ..game_taiwan.boardcast import send_reconnect_game_state
    state=make_state()
    p=state.player_list[0]
    p.hand_tiles=BASE+[47,16]
    p.has_draw_slot=True
    p.last_drawn_tile=16
    asyncio.run(state.execute_cut(0,dict(TileId=16,cutClass=True,cutIndex=13),declare_ready=True))
    for index in range(4):
        viewer=state.player_list[index]
        socket=SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[viewer.user_id]=SimpleNamespace(websocket=socket)
        with patch("server.gamestate.game_taiwan.boardcast.reconnected_send_pending_ask",new_callable=AsyncMock):
            asyncio.run(send_reconnect_game_state(state,viewer))
        info=socket.send_json.await_args.args[0]["game_info"]
        assert info["room_rule"]=="shanxi" and info["sub_rule"]==sx.SUB_RULE
        assert info["detailed_config"]=={"rule_version":sx.VERSION}
        actor=info["players_info"][0]
        assert actor["discard_tiles"]==[0] and "declared_ready" in actor["tag_list"]
        assert actor["discard_origin_tiles"]==[]
        if index:
            assert "hand_tiles" not in actor and "known_concealed_discards" not in actor
        else:
            assert actor["known_concealed_discards"]==[16]


def test_supplement_payload_carries_authoritative_dynamic_wall_count():
    from ...response import Do_action_info
    state=make_state()
    for expected in [14,14]:
        state._take_supplement_tile()
        for viewer in range(4):
            data=Do_action_info(**_build_do_action_payload(state,["deal_gang_tile"],0,viewer,deal_tile=11))
            assert data.tile_count==expected


def test_cut_record_extension_preserves_existing_rules_without_hook():
    from ..public.game_record_manager import player_action_record_cut
    state=SimpleNamespace(player_action_tick=0,round_index=1,
        game_record={"game_round":{"round_index_1":{"action_ticks":[]}}})
    player_action_record_cut(state,16,True)
    player_action_record_cut(state,47,False,True)
    assert state.game_record["game_round"]["round_index_1"]["action_ticks"]==[
        ["c",16,"T"],["c",47,"F","H"]]


def test_match_loop_retains_no_kong_draw_then_saves_shanxi_record_on_final_rotation():
    from unittest.mock import Mock
    state=make_state()
    state.current_round=4
    state.room_random_seed=12345
    state.db_manager=SimpleNamespace(store_shanxi_game_record=Mock(return_value="shanxi-saved"))
    state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
    state.spectator_manager.send_final_record_and_close=AsyncMock()
    count=0
    async def hand():
        nonlocal count
        count+=1
        state.draw_reason="exhaustive_draw"
        if count==2:
            state.pending_winners=[dict(index=1,source="discard",tile=17,payer=0,hu_class="hu_first",
                detail=sx.score(BASE+[17,17],winning_tile=17,declared_ready=True))]
    state._run_hand=hand
    with patch("server.gamestate.game_shanxi.ShanxiGameState.broadcast_game_start",new_callable=AsyncMock), \
         patch("server.gamestate.game_shanxi.ShanxiGameState.broadcast_result",new_callable=AsyncMock), \
         patch("server.gamestate.game_shanxi.ShanxiGameState.broadcast_game_end",new_callable=AsyncMock), \
         patch("server.gamestate.game_shanxi.ShanxiGameState.asyncio.sleep",new_callable=AsyncMock):
        asyncio.run(state.game_loop_chinese())
    assert count==2 and state.round_index==2
    title=state.db_manager.store_shanxi_game_record.call_args.args[0]["game_title"]
    assert title["rule"]=="shanxi" and title["sub_rule"]==sx.SUB_RULE
    assert title["detailed_config"]=={"rule_version":sx.VERSION}
    state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(gamestate_id=state.gamestate_id)
