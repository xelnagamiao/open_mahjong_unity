"""边界、强制动作和继承执行器的契约分支。"""
import asyncio
from unittest.mock import AsyncMock, patch
import pytest

from .test_state import make_state, ready, BASE
from .ShanxiGameState import ShanxiGameState
from . import bot
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ...game_calculation.shanxi import rules as sx
from ...room.shanxi_room import ShanxiRoomValidator
from ...room.test_shanxi_room import BASE as ROOM_BASE


@pytest.mark.parametrize("tile", [0, 51, True, "11", None])
def test_reject_invalid_tiles(tile):
    with pytest.raises(ValueError):
        sx.tile_points(tile)


@pytest.mark.parametrize("meld", [None, 11, "s12", "k99", "kxx", "g1"])
def test_reject_chow_and_invalid_melds(meld):
    with pytest.raises((ValueError, TypeError)):
        sx.meld_tiles(meld)
    assert not sx.shapes(BASE[:9]+[47,47], [meld])


@pytest.mark.parametrize("kwargs", [dict(winner=4), dict(winner=0,dealer=-1),
    dict(winner=0,base_score=-1),dict(winner=0,discarder=0),dict(winner=0,discarder=4),
    dict(winner=0,kongs=[(4,11,True)]),dict(winner=0,kongs=[(1,11,1)])])
def test_invalid_payment_rejected(kwargs):
    with pytest.raises(ValueError):
        sx.payments(**kwargs)


@pytest.mark.parametrize("extra", [dict(sub_rule="shanxi/future"),dict(detailed_config=[]),
    dict(detailed_config={"joker":True}),dict(detailed_config={"rule_version":"future"})])
def test_reject_unknown_state_profile(extra):
    s=make_state()
    with pytest.raises(ValueError):
        ShanxiGameState(s.game_server,dict(room_id="sx-bad",player_list=[1,2,3,4],**extra),None,None,"bad")


def test_fixed_false_options_and_claim_delivery_fallback():
    v=ShanxiRoomValidator(**ROOM_BASE,use_flowers=False,open_cuohe=False,
        tactical_call=False,claim_protection=False,tian_di_ren_he=False)
    assert not v.use_flowers
    s=make_state()
    p=s.player_list[0]
    p.remaining_time=10
    s._ask_broadcast_time=100
    with patch("server.gamestate.game_shanxi.ShanxiGameState.time.time",return_value=105):
        assert s.claim_clock(p,True)==(0,0)
    tick=["c",11,"T"]
    s.decorate_record_cut_tick(tick)
    assert len(tick)==3
    assert s.build_private_hand_action_info(0)=={"riichi_candidate_cuts":{}}
    assert s._liability_payer_for_win(0) is None
    assert s._declared_ready_auto_jiagang_tile(0) is None
    assert not s.enter_water(0)


def test_ineligible_players_and_post_claim_choices():
    s=make_state()
    p=s.player_list[0]
    ready(p,BASE+[47,47])
    assert s.score_candidate(0,"self_draw") is None
    assert s.score_candidate(0,"rob_kong",47) is None
    p.tag_list.append("peida")
    assert s.score_candidate(0,"discard",47) is None
    s.player_list[1].tag_list.append("peida")
    p.discard_tiles=[47]
    assert s.check_discard_actions(47)[1]==[]
    p.tag_list=[]
    p.ready_locked=False
    p.hand_tiles=BASE+[47,16]
    assert s.after_claim_actions()[0]==["riichi_cut","cut"]
    p.hand_tiles=[11,12]
    assert s.after_claim_actions()[0]==["cut"]
    s.tiles_list=[11]*14
    assert not any(s.check_discard_actions(47).values())


def test_out_of_turn_and_invalid_claims_do_not_mutate():
    s=make_state()
    before=list(s.tiles_list)
    asyncio.run(s.execute_cut(1,dict(TileId=11)))
    asyncio.run(s.execute_angang(1,11))
    asyncio.run(s.execute_jiagang(1,11))
    asyncio.run(s.execute_claim(1,"chi_left"))
    s.player_list[0].discard_tiles=[11]
    asyncio.run(s.execute_claim(1,"gang"))
    assert s.tiles_list==before and not s.kong_ledger


@pytest.mark.parametrize("kind",["concealed","added"])
def test_forced_selfdraw_precedes_otherwise_legal_kong(kind):
    s=make_state()
    p=s.player_list[0]
    if kind=="concealed":
        hand=[11]*4+[12,13,14,15,16,17,18]+[19]*3
        tile, drawn=11,19
        action=s.execute_angang
    else:
        hand=[26,27,28,11,12,13,31,32,33,47,47]
        p.combination_tiles=["k26"]
        tile, drawn=26,28
        action=s.execute_jiagang
    ready(p,hand)
    p.has_draw_slot=True
    p.last_drawn_tile=drawn
    assert s.kong_allowed(0,tile,"G" if kind=="concealed" else "added")
    asyncio.run(action(0,tile))
    assert s.pending_winners[0]["index"]==0 and s.game_status=="END"
    assert not s.kong_ledger


@pytest.mark.parametrize("kind",["G","added"])
def test_no_ledger_when_base_executor_rejects_stale_kong(kind):
    s=make_state()
    p=s.player_list[0]
    p.hand_tiles=[11]*4+BASE[:10] if kind=="G" else [11]+BASE[:10]
    if kind=="added": p.combination_tiles=["k11"]
    p.has_draw_slot=True
    method="execute_angang" if kind=="G" else "execute_jiagang"
    with patch.object(TaiwanGameState,method,new_callable=AsyncMock):
        asyncio.run(getattr(s,method)(0,11))
    assert not s.kong_ledger


def test_no_ledger_when_claim_executor_rejects_stale_action():
    s=make_state()
    s.player_list[0].discard_tiles=[11]
    s.player_list[1].hand_tiles=[11]*3+BASE[:10]
    with patch.object(TaiwanGameState,"execute_claim",new_callable=AsyncMock):
        asyncio.run(s.execute_claim(1,"gang"))
    assert not s.kong_ledger
    s._register_initial_heavenly_ready()
    assert not any(p.declared_ready for p in s.player_list)


def test_successful_direct_kong_records_one_deferred_payment():
    s=make_state()
    s.player_list[0].discard_tiles=[11]
    p=s.player_list[1]
    p.hand_tiles=[11]*3+[21,22,23,24,25,26,31,32,33,47]
    asyncio.run(s.execute_claim(1,"gang"))
    assert p.combination_tiles==["g11"]
    assert p.hand_tiles==[21,22,23,24,25,26,31,32,33,47]
    assert s.kong_ledger==[(1,11,False)]
    assert s.current_player_index==1 and s.game_status=="deal_card_after_gang"


def test_draw_resets_passed_pung_only_for_receiving_player():
    s=make_state()
    for p in s.player_list: p.passed_pungs={11}
    with patch.object(TaiwanGameState,"_deal_normal",new_callable=AsyncMock):
        asyncio.run(s._deal_normal())
    assert not s.player_list[1].passed_pungs and s.player_list[0].passed_pungs
    with patch.object(TaiwanGameState,"_deal_supplement",new_callable=AsyncMock):
        asyncio.run(s._deal_supplement())
    assert not s.player_list[0].passed_pungs and s.player_list[2].passed_pungs


def test_nonfinal_win_waits_for_result_ready_phase():
    s=make_state()
    ready(s.player_list[0],BASE+[47,47])
    s.player_list[0].has_draw_slot=True
    s.player_list[0].last_drawn_tile=47
    s.accept_self_draw(0)
    s.run_hu_result_ready_phase=AsyncMock()
    with patch("server.gamestate.game_shanxi.ShanxiGameState.broadcast_result",new_callable=AsyncMock):
        keep,end=asyncio.run(s._settle_hand({i:0 for i in range(4)}))
    assert keep and not end
    s.run_hu_result_ready_phase.assert_awaited_once()


@pytest.mark.parametrize("mode",["locked","cpu_stale","no_legal_kong"])
def test_bot_lock_and_stale_cpu_result(mode):
    s=make_state()
    p=s.player_list[0]
    p.hand_tiles=BASE+[47,19]
    p.has_draw_slot=True
    p.last_drawn_tile=19
    p.ready_locked=mode=="locked"
    actions=["cut"]+(["angang","jiagang"] if mode=="no_legal_kong" else [])
    with patch.object(bot,"_wait_until_actionable",AsyncMock(return_value=True)), \
         patch.object(bot,"bot_action_is_current",side_effect=[True,False] if mode=="cpu_stale" else lambda *a:True), \
         patch.object(bot,"run_room_bot_cpu",AsyncMock(return_value=(19,13))), \
         patch.object(bot,"submit_bot_action",new_callable=AsyncMock) as submit:
        asyncio.run(bot.shanxi_smart_bot_action.__wrapped__(s,0,actions,"waiting_hand_action"))
    if mode=="cpu_stale": submit.assert_not_awaited()
    else:
        assert submit.await_args.args[3]=="cut" and submit.await_args.args[5:7]==(19,13)
