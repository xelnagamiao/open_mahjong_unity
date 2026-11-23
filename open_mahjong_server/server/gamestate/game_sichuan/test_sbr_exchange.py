import asyncio
import time
from collections import Counter
from unittest.mock import patch

import pytest

from .SbrExchangeGameState import SbrExchangeGameState
from .action_check import check_hepai, check_action_after_cut, check_action_after_peng, check_action_hand_action
from .boardcast import _base_game_info, _build_players_info, reconnected_send_pending_ask
from .init_tiles import init_sichuan_tiles
from .test_action_window import submit
from .test_xueliu_doubles_full_match import play_match
from .wait_action import wait_action
from ..public.game_record_manager import init_game_record, init_game_round
from ..public.lifecycle import cancel_auxiliary_tasks
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ...database.shared_record_metrics import analyze_shared_record_for_player
from ...database.statistics_fans import fan_names
from ...database.statistics_values import sichuan_win_fan
from ...game_calculation.sichuan.xueliu_rules import SBR_EXCHANGE_SUB_RULE
from ...room.room_validators import SichuanRoomValidator

PLAIN = [11,12,13,14,15,16,21,22,23,26,27,28,25,25]
TILES = Counter({10*s+r:4 for s in (1,2,3) for r in range(1,10)})


def make_state():
    messages = []
    db = build_db_manager()
    with patch(__package__.rsplit(".", 1)[0] + ".verifier.host.LAB_USER_IDS", (11000001, 11000002, 11000003, 11000004)):
        server = build_game_server(db, messages=messages)
        room = build_room_data(seed=31415, room_id="912026", hepai_limit=0)
    room.update(room_rule="sichuan", sub_rule=SBR_EXCHANGE_SUB_RULE, blood_battle=False,
                step_timer=1, round_timer=3, claim_protection=False)
    state = SbrExchangeGameState(server, room, server.calculation_service, db, "sbr-exchange")
    state._reset_round_state()
    for i, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = i
        player.hand_tiles = list(PLAIN[:-1])
        player.dingque_suit = 3
    state.tiles_list = [19,29,39]
    init_game_record(state)
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


def score(state, seat, hand, melds=(), method="dianhe", kong=False):
    player = state.player_list[seat]
    player.hand_tiles = list(hand if method == "handgot" else hand[:-1])
    player.combination_tiles = list(melds)
    actions = {i: [] for i in range(4)}
    state.sichuan_hu_results = {}
    check_hepai(state, actions, hand[-1], seat, method, is_get_gang_tile=kong)
    return state.sichuan_hu_results.get(seat)


def test_reconnect_after_confirm_and_partway_through_exchange():
    async def scenario():
        state, messages = make_state()
        state.game_status = "waiting_xueliu_throw"
        state.action_dict = {seat: [state.xueliu_opening_action] for seat in range(4)}
        state.action_dict[0] = []
        state.xueliu_throw_deadline = time.time() + 5
        state._consume_xueliu_throw_tiles(0, [11, 12, 13])
        await reconnected_send_pending_ask(state, state.player_list[0].user_id)
        info = messages[-1]["message"]["ask_hand_action_info"]
        assert info["action_list"] == []
        assert info["xueliu_exchange"] == {"selection_done": True, "outgoing_tiles": [11, 12, 13]}
        await reconnected_send_pending_ask(state, state.player_list[1].user_id)
        info = messages[-1]["message"]["ask_hand_action_info"]
        assert info["action_list"] == [state.xueliu_opening_action]
        assert "xueliu_exchange" not in info
        state.game_status = "sbr_exchange_dice"
        state.exchange_dice = [4, 6]
        state.exchange_direction = 2
        state.exchange_deadline = time.monotonic() + .25
        await reconnected_send_pending_ask(state, state.player_list[0].user_id)
        info = messages[-1]["message"]["ask_hand_action_info"]
        roll = info["xueliu_exchange"]
        assert info["action_list"] == [] and roll["animating"]
        assert roll["dice"] == [4, 6] and roll["direction"] == 0 and roll["active_players"] == []
        assert 0 < roll["animation_seconds"] <= .25 + 1e-6
        state.game_status = "xueliu_exchange_animation"
        state.exchange_deadline = time.monotonic() + .6
        await reconnected_send_pending_ask(state, state.player_list[0].user_id)
        phase = messages[-1]["message"]["ask_hand_action_info"]["xueliu_exchange"]
        assert "dice" not in phase
        assert phase["cards_face_down"]
        assert phase["direction"] == 2 and phase["outgoing_tiles"] == [11, 12, 13]
        assert 0 < phase["animation_seconds"] <= .6 + 1e-6
        assert phase["outgoing_suit"] == phase["teammate_suit"] == 0
    asyncio.run(scenario())


@pytest.mark.parametrize("red", range(1,7))
def test_sealed_exchange_direction_private_hands_and_record(red):
    state,_ = make_state()
    state.master_seed = 41
    for player in state.player_list:
        player.hand_tiles = []
        player.dingque_suit = 0
    init_sichuan_tiles(state)
    before = [list(player.hand_tiles) for player in state.player_list]
    chosen = [state._default_xueliu_throw_tiles(i) for i in range(4)]
    directions = []
    phases = []
    async def select(seats):
        state.game_status = "waiting_xueliu_throw"
        for i in seats:
            assert state._validate_xueliu_throw_tiles(i, [11,21,31]) is None
            assert state._validate_xueliu_throw_tiles(i, [True,True,True]) is None
            state._consume_xueliu_throw_tiles(i,chosen[i])
            assert state.player_list[i].hand_tiles == before[i]
            for viewer in range(4):
                snapshot = _build_players_info(state, state.player_list[viewer].user_id)
                assert all(not p["xueliu_throw_tiles"] for p in snapshot if p["player_index"]!=viewer)
                assert all("sbr_received_tiles" not in p for p in snapshot)
            assert state.exchange_view(i) == dict(selection_done=True, outgoing_tiles=chosen[i])
    async def animate(st):
        phases.append((st.game_status, st.server_action_tick))
        for i in range(4):
            frame = st.exchange_view(i)
            assert frame["outgoing_tiles"] == chosen[i]
            assert st.player_list[i].hand_tiles == before[i]
            assert all("sbr_received_tiles" not in p for p in _build_players_info(st, st.player_list[i].user_id))
            if st.game_status == "sbr_exchange_dice":
                assert frame["animating"] and frame["dice"] == [red, 6]
                assert frame["direction"] == 0 and not frame["active_players"]
                assert 0 < frame["animation_seconds"] <= 2.35 + 1e-6
            else:
                assert "dice" not in frame and frame["cards_face_down"]
                assert 0 < frame["animation_seconds"] <= 1.90 + 1e-6
                directions.append(frame["direction"])
                assert frame["outgoing_suit"] == frame["teammate_suit"] == 0
    async def received():
        assert state.game_status == "sbr_exchange_received"
        assert _base_game_info(state)["sbr_exchange_direction"] == state.exchange_direction
        for player in state.player_list:
            i = player.player_index
            snapshot = _build_players_info(state, player.user_id)
            assert [p.get("sbr_received_tiles") for p in snapshot] == [
                chosen[(i - state.exchange_direction) % 4] if seat == i else None for seat in range(4)]
        assert all("sbr_received_tiles" not in p for p in _build_players_info(state, None))
    async def dingque():
        for p in state.player_list: p.dingque_suit = 3
    async def finish(*args): pass
    state._collect_xueliu_selection = select
    state._dingque_phase = dingque
    state.broadcast_game_start = received
    with patch(__package__+".SbrExchangeGameState.Random") as dice, \
         patch(__package__+".SbrExchangeGameState.broadcast_xueliu_exchange_animation",animate), \
         patch(__package__+".SbrExchangeGameState.broadcast_dingque_done",finish), patch("asyncio.sleep",finish):
        dice.return_value.randint.side_effect = [red,6]
        asyncio.run(state._opening_phase())
    direction = (red+1)//2
    assert [phase for phase, _ in phases] == ["sbr_exchange_dice", "xueliu_exchange_animation"]
    assert phases[1][1] == phases[0][1] + 1
    assert directions == [direction]*4
    for i,p in enumerate(state.player_list):
        assert Counter(p.hand_tiles) == Counter(before[i])-Counter(chosen[i])+Counter(chosen[(i-direction)%4])
    rd = state.game_record["game_round"]["round_index_1"]
    assert rd["exchange_start_hands"] == before and rd["exchange_dice"] == [red,6]
    assert rd["dingque_suits"] == dict.fromkeys(range(4),3)
    assert Counter(state.tiles_list)+sum((Counter(p.hand_tiles) for p in state.player_list), Counter()) == TILES


@pytest.mark.parametrize("method,seat,discards,expected", [
    ("handgot",0,[], {"天和"}), ("handgot",1,[],{"自摸","地和"}),
    ("handgot",1,[11],{"自摸"}), ("dianhe",1,[],{"平和"}),
])
def test_actual_opening_and_five_fan_cap(method,seat,discards,expected):
    state,_ = make_state()
    state.player_list[seat].discard_tiles=list(discards)
    result=score(state,seat,PLAIN,method=method)
    assert set(result["fan_list"])==expected
    assert state._base_from_fan(7,[]) == 32
    assert state._base_from_fan(0,["平和"]) == 1


def test_dealer_first_discard_and_no_earth_after_call_or_win():
    state,_=make_state()
    state.player_list[0].discard_tiles=[25]
    assert score(state,2,PLAIN)["fan_list"]==["地和"]
    state.player_list[3].is_hu=True
    assert score(state,2,PLAIN)["fan_list"]==["平和"]
    state.player_list[3].is_hu=False
    state.player_list[3].combination_tiles=["k12"]
    assert score(state,2,PLAIN)["fan"]==0


def test_situational_stacking_and_payments():
    state,_=make_state()
    state.player_list[0].discard_tiles=[11,13]
    state.player_list[1].is_hu=True
    state.player_list[1].hu_order=1
    state.player_list[2].hu_order=2
    state.tiles_list=[]
    state.last_action_was_gang=True
    result=score(state,2,[25,25],["G11","g13","k15","k17"],"handgot",True)
    assert Counter(result["fan_list"])==Counter(["杠","杠","大对子","金钩钓","海底","杠上花","自摸"])
    assert state._apply_hu_score_changes(2,32,True,fan=result["fan"])=={0:-32,1:0,2:64,3:-32}
    assert score(state,2,PLAIN)["fan_list"]==["杠上炮","海底"]
    state.jiagang_after_gang=True
    assert set(score(state,2,PLAIN,method="qianggang")["fan_list"])=={"杠上炮","抢杠"}


def test_dajiao_uses_concealed_copies_and_counts_roots_only_once():
    state,_=make_state()
    assert state._chajiao_fan([23,24,25,26,27,38,38,38,39,39],["k25"],1)==(1,["根"])
    assert state._chajiao_fan([23,24,25,26,27,28,28,28,39,39],["k25"],1)==(2,["根","根"])
    assert state._chajiao_fan(PLAIN[:-1],[],3)==(0,["平和"])
    assert state._classify_hua_zhu([31],[],3,[])=="passive"
    assert state._classify_hua_zhu([31],[],3,[11])=="active"
    assert state._active_flower_pig_score_changes(1)=={1:-96}


def test_threshold_shunhe_and_strict_dingque():
    state,_=make_state()
    state.player_list[1].discard_tiles=[11]
    state.hepai_limit=1
    assert not score(state,1,PLAIN) and score(state,1,PLAIN,method="handgot")
    state.hepai_limit=0
    state.player_list[1].shunhe_passed_max_fan=0
    assert not score(state,1,PLAIN) and score(state,1,PLAIN,method="handgot")
    state.player_list[1].dingque_suit=2
    assert not score(state,1,PLAIN,method="handgot")
    state.player_list[1].hand_tiles=[21]*3+[11]*4+[12]*3+[13]*2+[14]
    assert "gang" not in check_action_after_cut(state,21)[1]
    assert "peng" not in check_action_after_cut(state,21)[1]
    assert check_action_after_peng(state,1)[1]==["cut"]
    state.tiles_list=[]
    assert "angang" not in check_action_hand_action(state,1)[1]


def test_discarded_pung_pass_persists_to_next_draw():
    async def run():
        state,_=make_state()
        p=state.player_list[1]
        p.hand_tiles=[11,11,13,15,17,19,21,23,25,27,29,31,33]
        state.player_list[0].discard_tiles=[11]
        state.game_status="waiting_action_after_cut"
        state.action_dict=check_action_after_cut(state,11)
        await state.broadcast_ask_other_action()
        for i,actions in state.action_dict.items():
            if actions: await submit(state,i,"pass")
        await wait_action(state)
        assert "peng" not in check_action_after_cut(state,11)[1]
        await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_room_flags_private_dingque_and_tied_competition_points():
    state,_=make_state()
    assert state.blood_battle and not state.is_xueliu
    config = SichuanRoomValidator(room_name="血战", sub_rule=SBR_EXCHANGE_SUB_RULE, game_round=1,
                                  step_timer=5, round_timer=20, blood_battle=False)
    assert config.blood_battle and config.hepai_limit == 0
    state.game_status="waiting_dingque"
    state.waiting_players_list=[1,2]
    assert [p["dingque_suit"] for p in _build_players_info(state,state.player_list[0].user_id)]==[3,0,0,0]
    for p,n in zip(state.player_list,[6,6,1,-13]):p.score=n
    state._assign_final_ranks()
    assert [p.competition_points for p in state.player_list]==[3.5,3.5,2,1]
    assert [p.record_counter.rank_result for p in state.player_list]==[1,1,3,4]
    assert len(fan_names(SBR_EXCHANGE_SUB_RULE))==14


@pytest.mark.parametrize("seed",[51,316,2026])
def test_full_four_seat_routed_match_and_statistics(seed):
    state,messages=asyncio.run(play_match(seed,make_state))
    rounds=state.game_record["game_round"]
    assert len(rounds)==4
    assert all("exchange_dice" in rd and len(rd["dingque_suits"])==4 for rd in rounds.values())
    assert sum(p.score for p in state.player_list)==0
    assert sum(p.competition_points for p in state.player_list)==10
    assert len([m for m in messages if m["message"]["type"].endswith("/game_end")])==4
    title=state.game_record["game_title"]
    total_wins=0
    for p in state.player_list:
        metrics=analyze_shared_record_for_player(state.game_record,p.original_player_index)
        assert metrics["total_rounds"]==4 and metrics["total_round_score"]==p.score
        assert metrics["win_count"]==p.record_counter.zimo_times+p.record_counter.dianhe_times
        assert metrics["total_fan_score"]==p.record_counter.win_score
        total_wins+=metrics["win_count"]
    assert total_wins
    for rd in rounds.values():
        for idx,tick in enumerate(rd["action_ticks"]):
            if tick[0].startswith("hu_"):
                assert sichuan_win_fan(title,rd,tick,idx)==sichuan_win_fan(title,{},tick,idx)



@pytest.mark.parametrize("hand,status,allowed,want", [
    ([11,12,14,15,16,21,22,23,24,25,26,27,28,29], "waiting_hand_action", ["hu_self","angang","cut"], ("cut",29,True)),
    ([11,31,14,15,16,21,22,23,24,25,26,27,28,29], "waiting_hand_action", ["cut"], ("cut",31,False)),
    ([11,31,14,15,16,21,22,23,24,25,26,27,28,33], "waiting_hand_action", ["cut"], ("cut",33,True)),
    (PLAIN, "waiting_action_after_cut", ["hu","peng","gang","pass"], ("pass",None,None)),
    (PLAIN, "waiting_action_qianggang", ["hu","pass"], ("pass",None,None)),
])
def test_auto_cut_bot_uses_only_legal_draw_discard_or_pass(hand,status,allowed,want):
    from ..public.ai.auto_cut_ai import auto_cut_action
    async def run():
        state,_ = make_state()
        player=state.player_list[0]
        player.user_id=1
        player.hand_tiles=hand.copy()
        player.has_draw_slot=True
        state.bot_speed="instant"
        state.game_status=status
        state.waiting_players_list=[0]
        state.action_dict={0:allowed.copy(),1:[],2:[],3:[]}
        state.current_player_index=0 if status=="waiting_hand_action" else 1
        await auto_cut_action(state,0,allowed,status)
        action=await asyncio.wait_for(state.action_queues[0].get(),2)
        assert action["action_type"] == want[0]
        if want[0]=="cut":
            assert (action["TileId"],action["cutClass"]) == want[1:]
    asyncio.run(run())


def test_smart_bot_prefers_a_win_and_discards_an_isolated_tile():
    from . import xueliu_bot
    async def run():
        state,_=make_state()
        p=state.player_list[0]
        p.user_id=2
        p.hand_tiles=[19,11,12,13,14,15,16,22,23,24,25,26,27,28]
        p.has_draw_slot=True
        state.game_status="waiting_hand_action"
        state.bot_speed="instant"
        state.waiting_players_list=[0]
        async def inline_cpu(_state,fn,*args): return fn(*args)
        for actions,expected in [(["cut"],"cut"),(["hu_self","cut"],"hu_self")]:
            state.action_dict={0:actions.copy(),1:[],2:[],3:[]}
            with patch.object(xueliu_bot,"run_room_bot_cpu",inline_cpu):
                await xueliu_bot.xueliu_smart_bot_action(state,0,actions,state.game_status)
            actual=await asyncio.wait_for(state.action_queues[0].get(),2)
            assert actual["action_type"]==expected
            if expected=="cut": assert actual["TileId"]==19 and not actual["cutClass"]
    asyncio.run(run())



def test_replay_pass_threshold_starts_on_next_action_and_clears_on_draw():
    state,_=make_state()
    rd=state.game_record["game_round"]["round_index_1"]
    player=state.player_list[1]
    state.record_passed_fans()
    assert "sbr_passed_fans" not in rd
    player.shunhe_passed_max_fan=0
    state.record_passed_fans()
    rd["action_ticks"].extend([["c",25,"F"],["c",23,"F"]])
    state.record_passed_fans()
    player.shunhe_passed_max_fan=None
    state.record_passed_fans()
    assert rd["sbr_passed_fans"] == {1:{1:0,3:-1}}



@pytest.mark.parametrize("winners", [(1,), (1,2), (1,2,3)])
def test_chained_kong_refunds_and_first_multi_ron_dealer(winners):
    from unittest.mock import AsyncMock
    from .action_check import refresh_waiting_tiles
    from ..public.game_record_manager import player_action_record_deal
    async def run():
        state,_=make_state()
        actor=state.player_list[0]
        actor.hand_tiles=[12,13,14,16,17,18,24,11]
        actor.combination_tiles=["k11","k21"]
        actor.combination_mask=[[1,t,0,t,0,t] for t in (11,21)]
        actor.has_draw_slot=True
        for seat,p in enumerate(state.player_list[1:],1):
            p.hand_tiles=([22,23,14,15,16,17,18,19,26,27,28,25,25] if seat in winners
                          else [12,14,16,18,22,24,26,28,29,32,34,36,38])
            refresh_waiting_tiles(state,seat)
        state.tiles_list=[21,25,29,19,19,19]
        init_game_round(state)
        try:
            for target in (11,21):
                state.game_status="waiting_hand_action"
                state.action_dict=check_action_hand_action(state,0,is_get_gang_tile=state.last_action_was_gang)
                assert "jiagang" in state.action_dict[0]
                await state.broadcast_ask_hand_action()
                await submit(state,0,"jiagang",target_tile=target)
                await wait_action(state)
                if target==11:
                    assert state.game_status=="deal_card_after_gang"
                    actor.get_gang_tile(state.tiles_list,state)
                    player_action_record_deal(state,deal_tile=21,deal_type="gd")
            assert state.game_status=="waiting_action_qianggang" and len(actor.gang_score_records)==2
            await state.broadcast_ask_other_action()
            for i,choices in state.action_dict.items():
                if choices: await submit(state,i,"hu" if i in winners else "pass")
            await wait_action(state)
            assert actor.combination_tiles==["g11","k21"] and not actor.gang_score_records
            assert all(set(state.sichuan_hu_results[i]["fan_list"])=={"抢杠","杠上炮"} for i in winners)
            with patch("asyncio.sleep",new_callable=AsyncMock): await state._settle_win()
            if len(winners)<3:
                assert [p.score for p in state.player_list] == [0]*4
                assert [row["base"] for row in state.deferred_hu_settlements] == [4]*len(winners)
            else:
                assert [p.score for p in state.player_list] == [-12,4,4,4]
            assert state._decide_next_dealer()==(1 if len(winners)==1 else 0)
            assert state.game_record["game_round"]["round_index_1"]["action_ticks"].count(["gr","gs",-6,2,2,2])==1
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
