import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .LajiheGameState import LajiheGameState
from ...game_calculation.shanghai import lajihe as rule
from ...game_calculation.shanghai.test_lajihe import PLAIN, HONORS, PAIRS
from ...room.room_validators import ShanghaiRoomValidator
from ..public.game_record_manager import init_game_record, init_game_round
from ..game_taiwan.boardcast import send_reconnect_game_state, broadcast_do_action

MODULE = "server.gamestate.game_shanghai.LajiheGameState"


def make_state(**room_overrides):
    server=SimpleNamespace(user_id_to_connection={},gamestate_manager=SimpleNamespace(),room_manager=SimpleNamespace())
    state=LajiheGameState(server,{
        "room_id":"123456","player_list":[101,102,103,104],"sub_rule":rule.SUB_RULE,
        "round_timer":0,"step_timer":0,"game_round":1,"tips":True,"room_type":"custom","allow_spectator":False,
        **room_overrides,
    },None,None,"lajihe-test")
    for i,p in enumerate(state.player_list):
        p.player_index=p.original_player_index=i
    state.tiles_list=[21,22,23,24,25,26]
    init_game_record(state)
    init_game_round(state)
    return state


def ticks(state):
    return state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]


def test_registration_rules_wall_and_public_concealed_kong():
    state=make_state()
    state.master_seed=144
    state.init_tiles()
    tiles=state.tiles_list+[t for p in state.player_list for t in p.hand_tiles]
    assert Counter(tiles)==Counter([t for t in rule.TILES for _ in range(4)]+list(rule.FLOWERS))
    assert state.sub_rule==rule.SUB_RULE and state.concealed_kongs_public
    assert state.build_concealed_kong_mask([45]*4)==[2,45,0,45,0,45,2,45]
    assert not state.draw_debt_enabled and not state.rules.public_ready_enabled
    assert ShanghaiRoomValidator(room_name="垃圾和",game_round=1,round_timer=30,step_timer=5,sub_rule=rule.SUB_RULE).hepai_limit==0
    with pytest.raises(ValueError):
        ShanghaiRoomValidator(room_name="垃圾和",game_round=1,round_timer=30,step_timer=5,sub_rule=rule.SUB_RULE,hepai_limit=1)


@pytest.mark.parametrize("config", [None, {}, {rule.SELF_DRAW_EXTRA_BASE:False}, {rule.SELF_DRAW_EXTRA_BASE:True}])
def test_extra_base_config_defaults_and_state(config):
    expected = bool(config and config.get(rule.SELF_DRAW_EXTRA_BASE))
    validated = ShanghaiRoomValidator(room_name="垃圾和",game_round=1,round_timer=30,step_timer=5,
                                     sub_rule=rule.SUB_RULE,detailed_config=config)
    assert validated.detailed_config == {rule.SELF_DRAW_EXTRA_BASE:expected}
    state = make_state(detailed_config=validated.detailed_config)
    assert state.self_draw_extra_base is expected
    assert state.build_game_info_fields()["detailed_config"][rule.SELF_DRAW_EXTRA_BASE] is expected
    assert state.build_record_title_fields()["detailed_config"][rule.SELF_DRAW_EXTRA_BASE] is expected


@pytest.mark.parametrize("invalid", [None,0,1,"true","false",[],{}])
def test_extra_base_config_requires_boolean(invalid):
    with pytest.raises(ValueError,match="必须为布尔值"):
        ShanghaiRoomValidator(room_name="垃圾和",game_round=1,round_timer=30,step_timer=5,
                             sub_rule=rule.SUB_RULE,detailed_config={rule.SELF_DRAW_EXTRA_BASE:invalid})


@pytest.mark.parametrize("sub_rule", ["shanghai/qiaoma","shanghai/qinghunpeng"])
def test_extra_base_config_is_lajihe_only(sub_rule):
    with pytest.raises(ValueError,match="仅适用于上海垃圾和"):
        ShanghaiRoomValidator(room_name="上海",game_round=1,round_timer=30,step_timer=5,
                             sub_rule=sub_rule,detailed_config={rule.SELF_DRAW_EXTRA_BASE:True})


@pytest.mark.parametrize("enabled", [False,True])
@pytest.mark.parametrize("source", ["discard","self_draw","robbing_kong"])
def test_extra_base_settlement_and_record(enabled,source):
    async def run():
        state = make_state(detailed_config={rule.SELF_DRAW_EXTRA_BASE:enabled})
        state.run_hu_result_ready_phase=AsyncMock()
        player=state.player_list[1]
        player.hand_tiles=HONORS.copy() if source=="self_draw" else HONORS[:-1]
        detail=state.score_candidate(1,source,47)
        points=40 if source=="discard" else 30
        assert detail["base_score"]==points
        state.pending_winners=[{"index":1,"payer":None if source=="self_draw" else 0,"source":source,
                                "hu_class":"hu_self" if source=="self_draw" else "hu_first","tile":47,"detail":detail}]
        await state._settle_hand({p.original_player_index:p.score for p in state.player_list})
        expected={"discard":[-40,40,0,0],"self_draw":[-points,3*points,-points,-points],
                  "robbing_kong":[-3*points,3*points,0,0]}[source]
        assert [p.score for p in state.player_list]==expected
        hu=next(t for t in ticks(state) if t[0] in ("hu_self","hu_first"))
        assert hu[2]==points and hu[4]==expected
        if source!="discard":
            label="自摸" if source=="self_draw" else "抢杠"
            assert f"{label}（{15 if enabled else 10}分）" in hu[3]
    asyncio.run(run())


@pytest.mark.parametrize("flowers,can_ron",[(0,False),(1,False),(2,True)])
def test_ron_gate_self_draw_and_rob_actions(flowers,can_ron):
    state=make_state()
    p=state.player_list[1]
    p.hand_tiles=PLAIN[:-1]
    p.huapai_list=list(range(51,51+flowers))
    assert ("hu_first" in state.check_discard_actions(41)[1])==can_ron
    assert "hu_first" in state.check_added_kong_actions(41)[1]
    p.hand_tiles=PLAIN.copy()
    assert "hu_self" in state.check_hand_actions(1)[1]
    assert state.ready_candidate_cuts(1)=={}


@pytest.mark.parametrize("action,used,tile,forbidden",[("chi_right",[12,13],11,{11,14}),
    ("chi_mid",[11,13],12,{12}),("chi_left",[11,12],13,{13}),("peng",[18,18],18,{18})])
def test_claim_priority_source_and_strict_kuikae(action,used,tile,forbidden):
    async def run():
        state=make_state();p=state.player_list[1]
        p.hand_tiles=used+[11,14,18,21,22,23,24,25,26,27,28]
        state.player_list[0].discard_tiles=[tile]
        assert action in state.check_discard_actions(tile)[1]
        if action.startswith("chi"):
            state.player_list[2].hand_tiles=p.hand_tiles.copy()
            assert action not in state.check_discard_actions(tile)[2]
        await state.execute_claim(1,action)
        assert p.kuikae_forbidden_tiles==forbidden
        assert state.after_claim_actions()[1]==["cut"]
        for t in forbidden & set(p.hand_tiles):
            before=p.hand_tiles.copy()
            await state.execute_cut(1,{"TileId":t,"TileIndex":p.hand_tiles.index(t),"CutClass":False})
            assert p.hand_tiles==before
        assert "angang" not in state.check_hand_actions(1)[1]
        assert "jiagang" not in state.check_hand_actions(1)[1]
    asyncio.run(run())


@pytest.mark.parametrize("third_meld",["k31","G31"])
def test_only_fourth_chow_is_kuikae_exception(third_meld):
    async def run():
        state=make_state();p=state.player_list[1]
        p.combination_tiles=["s22","s25",third_meld]
        p.hand_tiles=[11,11,12,13]
        state.player_list[0].discard_tiles=[11]
        await state.execute_claim(1,"chi_right")
        assert len(p.combination_tiles)==4 and not p.kuikae_forbidden_tiles
        await state.execute_cut(1,{"TileId":11,"TileIndex":0,"CutClass":False})
        assert p.discard_tiles==[11] and p.lajihe_passed_win
        state=make_state();p=state.player_list[1]
        p.combination_tiles=["s22","s25",third_meld]
        p.hand_tiles=[11]*3+[12];state.player_list[0].discard_tiles=[11]
        await state.execute_claim(1,"peng")
        assert p.kuikae_forbidden_tiles=={11}
    asyncio.run(run())


def test_pass_win_blocks_ron_until_actual_draw_not_claim_but_allows_rob_kong():
    async def run():
        state=make_state();p=state.player_list[1];p.hand_tiles=HONORS[:-1]
        state.player_list[0].discard_tiles=[47];state.game_status="waiting_action_after_cut"
        allowed=state.check_discard_actions(47)
        await state.resolve_discard_responses({1:{"action_type":"pass"}},allowed)
        assert p.lajihe_passed_win
        for tile in rule.HONORS:
            assert state.score_candidate(1,"discard",tile) is None
            assert state.score_candidate(1,"robbing_kong",tile) is not None
        state.player_list[0].discard_tiles=[41]
        await state.execute_claim(1,"peng")
        assert p.lajihe_passed_win
        state.current_player_index=0
        await state._deal_normal()
        assert not p.lajihe_passed_win
    asyncio.run(run())


def test_pass_pung_blocks_same_tile_and_replacement_clears_it():
    async def run():
        state=make_state();p=state.player_list[1];p.hand_tiles=[11,11,21,21,12,14,16,18,22,24,26,28,51]
        state.current_player_index=3  # 对家弃牌仍可先碰，再补起手花。
        state.player_list[3].discard_tiles=[11];state.game_status="waiting_action_after_cut"
        await state.resolve_discard_responses({1:{"action_type":"pass"}},state.check_discard_actions(11))
        assert p.passed_pungs=={11}
        assert "peng" not in state.check_discard_actions(11)[1]
        assert "peng" in state.check_discard_actions(21)[1]
        state.current_player_index=1
        await state.execute_buhua(1)
        assert not p.passed_pungs
    asyncio.run(run())


def test_own_winning_discard_blocks_other_waits_but_nonwinning_cut_does_not():
    async def run():
        for tile,blocked in [(47,True),(11,False)]:
            state=make_state();p=state.player_list[0]
            p.hand_tiles=HONORS.copy() if blocked else HONORS[:-1]+[11]
            await state.execute_cut(0,{"TileId":tile,"TileIndex":p.hand_tiles.index(tile),"CutClass":False})
            assert p.lajihe_passed_win==blocked
    asyncio.run(run())


def test_opening_flowers_precede_head_draw_then_reopen_only_upper_claim():
    async def run():
        state=make_state();p=state.player_list[1]
        p.hand_tiles=PLAIN[:-2]+[51]
        p.huapai_list=[52]
        state.player_list[0].discard_tiles=[41];state.tiles_list=[22,41]
        await state._deal_normal()
        assert state.opening_before_draw==1 and p.normal_draw_count==0
        assert state.check_hand_actions(1)[1]==["buhua"]
        await state.execute_buhua(1)
        assert p.hand_tiles==PLAIN[:-1] and p.normal_draw_count==0
        assert state.current_player_index==0 and state.opening_claim_window==1
        assert "hu_first" in state.action_dict[1]
        assert all(not state.action_dict[i] for i in (0,2,3))
        await state.resolve_discard_responses({1:{"action_type":"pass"}},state.action_dict)
        await state._deal_normal()
        assert p.hand_tiles[-1]==22 and p.normal_draw_count==1 and not p.lajihe_passed_win
    asyncio.run(run())


def test_initial_pung_then_flowers_permits_self_draw_and_kong():
    async def run():
        state=make_state();p=state.player_list[2]
        p.hand_tiles=[11,11,21,22,23,31,32,33,17,18,19,41,51]
        state.player_list[0].discard_tiles=[11];state.tiles_list=[22,41]
        await state.execute_claim(2,"peng")
        assert state.after_claim_actions()[2]==["buhua"]
        await state.execute_buhua(2)
        assert "hu_self" in state.action_dict[2] and state.last_draw_after_kong
        assert "大吊车" not in state.result_dict["hu_self"]["fan_names"]
        assert "杠头开花" in state.result_dict["hu_self"]["fan_names"]
        state=make_state();p=state.player_list[2]
        p.hand_tiles=[11,11,21,21,21,31,32,33,17,18,19,41,51]
        state.player_list[0].discard_tiles=[11];state.tiles_list=[22,21]
        await state.execute_claim(2,"peng");await state.execute_buhua(2)
        assert "angang" in state.action_dict[2]
        await state.execute_angang(2,21)
        assert "G21" in p.combination_tiles
    asyncio.run(run())


@pytest.mark.parametrize("kind",["direct","concealed","added"])
def test_all_kongs_tail_draw_and_replacement_win(kind):
    async def run():
        state=make_state();p=state.player_list[0]
        p.hand_tiles=[11]*4+[21,22,23,31,32,33,17,18,19,41]
        p.has_draw_slot=True;p.last_drawn_tile=11
        state.tiles_list=[22,41]
        if kind=="concealed":
            await state.execute_angang(0,11)
        elif kind=="added":
            p.hand_tiles=p.hand_tiles[3:];p.combination_tiles=["k11"];p.combination_mask=[[0,11,0,11,1,11]]
            await state.execute_jiagang(0,11)
            if state.game_status=="waiting_action_qianggang":
                await state.resolve_rob_kong_responses({},state.action_dict)
        else:
            p.hand_tiles.remove(11);state.current_player_index=3
            state.player_list[3].discard_tiles=[11]
            await state.execute_claim(0,"gang")
        assert state.game_status=="deal_card_after_gang"
        await state._deal_supplement()
        assert p.hand_tiles[-1]==41 and state.tiles_list==[22]
        assert "hu_self" in state.action_dict[0]
        assert "杠头开花" in state.result_dict["hu_self"]["fan_names"]
    asyncio.run(run())


@pytest.mark.parametrize("stop",[False,True])
@pytest.mark.parametrize("wall",[[],[22]])
def test_eight_flowers_paid_once_before_choice_even_without_tail(stop,wall):
    async def run():
        state=make_state();p=state.player_list[0]
        p.hand_tiles=PLAIN[:-1]+[58];p.huapai_list=list(range(51,58));state.tiles_list=wall.copy()
        async def choice(s):
            assert [x.score for x in s.player_list]==[90,-30,-30,-30]
            assert s.game_status=="waiting_flower_choice"
            assert s.action_dict[0]==["lajihe_flower_end","lajihe_flower_continue"]
            return {0:{"action_type":"lajihe_flower_end" if stop else "lajihe_flower_continue"}},s.action_dict
        with patch(MODULE+"._collect_responses",side_effect=choice):
            await state.execute_buhua(0)
            await state._award_eight_flowers(0)
        assert [x.score for x in state.player_list]==[90,-30,-30,-30]
        assert len([t for t in ticks(state) if t[0]=="lajihe_eight_flowers"])==1
        assert not state.pending_winners and p.record_counter.zimo_times==0
        assert (state.game_status=="END")==bool(stop or not wall)
        if not stop and wall:
            assert p.hand_tiles[-1]==22 and len(p.hand_tiles)==14
        assert state.next_dealer()==0
    asyncio.run(run())


def test_terminal_normal_draw_eighth_flower_is_paid_and_times_out_to_continue():
    async def run():
        state=make_state();state.current_player_index=3
        p=state.player_list[0];p.huapai_list=list(range(51,58));p.hand_tiles=PLAIN[:-1];state.tiles_list=[58]
        await state._deal_normal()
        assert state.game_status=="END" and p.score==90 and p.eight_flowers_paid
    asyncio.run(run())


@pytest.mark.parametrize("count",[1,2,3])
@pytest.mark.parametrize("source",["discard","robbing_kong"])
def test_multi_win_settlement_and_dealer(count,source):
    async def run():
        state=make_state();state.run_hu_result_ready_phase=AsyncMock()
        for i in range(1,count+1):
            state.player_list[i].hand_tiles=HONORS[:-1]
        state.pending_winners=[{"index":i,"payer":0,"source":source,"hu_class":["","hu_first","hu_second","hu_third"][i],
            "tile":47,"detail":state.score_candidate(i,source,47)} for i in range(1,count+1)]
        before={p.original_player_index:p.score for p in state.player_list}
        await state._settle_hand(before)
        per=40 if source=="discard" else 90
        assert [p.score for p in state.player_list]==[-per*count]+[per]*count+[0]*(3-count)
        assert state.next_dealer()==(1 if count==1 else 0)
        assert state.huangfan_count==0
    asyncio.run(run())


def test_four_draws_end_schedule_without_huangfan_or_extra_hands():
    async def run():
        state=make_state()
        state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        state._run_hand=AsyncMock()
        with patch("server.gamestate.game_shanghai.QinghunpengGameState.liuju_ready_wait_seconds",return_value=0):
            await state.game_loop_chinese()
        assert len(state.game_record["game_round"])==4 and state.huangfan_count==0
        assert sum(p.score for p in state.player_list)==0
    asyncio.run(run())
