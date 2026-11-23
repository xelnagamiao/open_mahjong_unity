import pytest

from ...game_calculation.jokers import ORPHANS
from ...game_calculation.sanhua import rules
from .test_sanhua import state
from .bot import choose
from ..game_new_chinese.state_machine import Phase


def invoke(s, window, choices):
    w = s.open_action_window(window) if window is not s.live_pending_window else window
    responses = {i:{"action_type":"pass"} for i, a in w["actions"].items() if a}
    responses.update({i:{"action_type":d} if isinstance(d,str) else d for i,d in choices.items()})
    return s.apply_action_results(w,responses)


def hand_state():
    s = state()
    s.skin,s.joker,s.indicator=27,28,27
    for p in s.player_list:
        p.hand_tiles=[]
        p.has_draw_slot=False
    s.machine.transition(Phase.TURN)
    return s


def test_call_precedence_and_automatic_first_win():
    s=hand_state()
    for i in (1,3):
        s.player_list[i].hand_tiles=list(ORPHANS)
    s.player_list[0].discard_tiles=[11]
    s.player_list[0].riichi_flags=[False]
    w=s.window(Phase.RESPONSE,0,11,s.claims(0,11))
    invoke(s,w,{1:"pass",3:"hu"})
    assert s.next_dealer==1 and s.round_changes==[-200,600,-200,-200]


@pytest.mark.parametrize("kind,tile",[("angang",45),("angang",27),("angang",28),("jiagang",11)])
def test_only_orphans_can_rob_single_and_added_kongs(kind,tile):
    s=hand_state()
    s.player_list[0].hand_tiles=[tile]
    if kind=="jiagang":
        s.player_list[0].combination_tiles=["k11"]
        s.player_list[0].combination_mask=[[1,11,0,11,0,11]]
    # Pizi face needs to be an orphan for a thirteen-orphans win.
    if tile==27:
        tile=s.indicator=s.skin=41
        s.joker=42
        s.player_list[0].hand_tiles=[41]
    s.player_list[1].hand_tiles=list(ORPHANS)
    w=s.offer_kong(0,tile,kind)
    assert w["status"]==Phase.ROB_KONG
    invoke(s,w,{1:"hu"})
    assert not s.player_list[0].specials and not s.player_list[0].hand_tiles
    assert s.next_dealer==1 and s.round_changes[1]==600
    if kind=="jiagang": assert s.player_list[0].combination_tiles==["k11"]


@pytest.mark.parametrize("indicator,skin,joker",[(27,27,28),(45,46,47),(47,47,46)])
def test_only_physical_revealed_tile_chains(indicator,skin,joker):
    s=hand_state()
    s.skin,s.joker,s.indicator=skin,joker,indicator
    s.tiles_list=[11,indicator,indicator]
    s.marker=1
    s.draw(0,kong=True)
    assert s.player_list[0].hand_tiles==[indicator] and not s.player_list[0].specials
    s.commit_kong(0,indicator,"angang")
    assert s.player_list[0].specials==[indicator,indicator]
    assert s.player_list[0].hand_tiles==[11] and s.player_list[0].after_kong


def test_ordinary_concealed_kong_opens_without_rob_window():
    s=hand_state()
    p=s.player_list[0]
    p.hand_tiles=[11]*4
    s.tiles_list=[22]
    s.player_list[1].hand_tiles=list(ORPHANS)
    w=s.take_turn(0,dict(action_type="angang",target_tile=11))
    assert w["status"]==Phase.TURN and p.combination_tiles==["G11"] and p.hand_tiles==[22]
    assert s.table()[0]==["暗杠","开口番"]


def test_gold_overlapping_liability_three_shares():
    s=hand_state()
    p=s.player_list[0]
    p.combination_tiles=["s12","k16","k17","g18"]
    p.hand_tiles=[15]
    p.calls=[1,1,1,1]
    p.baoqing,p.liable=True,1
    quote=s.quote(0,15,1)
    assert quote["changes"]==[300,-180,-60,-60] and quote["gold"]
    assert {"全求人","清一色","硬和","明杠","开口番"}<=set(quote["fan_names"])
    assert s.quote(0,25,1) is None


def test_clear_declaration_before_third_call_locks_supplier():
    s=hand_state()
    p=s.player_list[0]
    p.combination_tiles=["s12","k16"]
    p.calls=[1,1]
    p.hand_tiles=[17,17,11,12,13,15,15,18]
    s.machine.transition(Phase.RESPONSE)
    w=invoke(s,s.turn(0),{0:"baoqing"})
    assert p.baoqing and "baoqing" not in w["actions"][0]
    s.player_list[2].discard_tiles=[17]
    s.player_list[2].riichi_flags=[False]
    w=s.window(Phase.RESPONSE,2,17,s.claims(2,17))
    invoke(s,w,{0:"peng"})
    assert p.liable==2 and p.calls==[1,1,1]


def test_closed_pure_pairs_and_luxury_tiers():
    for n in range(4):
        hand=sum(([t]*4 for t in range(11,11+n)),[])+sum(([t]*2 for t in range(11+n,18-n)),[])
        quote=rules.score(hand,[],27,0,None,[[] for _ in range(4)])
        assert quote["fan_names"]==[rules.PAIRS[n],"清一色","硬和"]
        assert quote["changes"]==[180,-60,-60,-60]


def test_wild_orphan_and_no_ordinary_special_substitution():
    hand=[t for t in ORPHANS if t!=45]+[28,11]
    assert rules.score(hand,[],27,0,3,[[]]*4,rob=True)["points"]==600
    assert not rules.variants([45,45]+[11,12,13]*4,[],27)
    assert not rules.variants([27,27]+[11,12,13]*4,[],27)


def test_two_jokers_and_no_shape_do_not_win():
    hand=[11,12,28,21,22,23,34,35,28,12,12]
    assert rules.score(hand,["s16"],27,0,None,[["开口番","暗杠","暗杠"],[],[],[]]) is None
    hand=[12,12,12,15,18,22,25,28,28,32,35,38,38,38]
    assert not rules.variants(hand,[],37)


def test_efficiency_bot_preserves_draw_when_other_cut_improves_hand():
    s=hand_state()
    p=s.player_list[0]
    p.user_id=1
    p.hand_tiles=[11,12,13,21,22,23,31,32,33,35,35,36,43,37]
    p.has_draw_slot=True
    d=choose(s,0,["cut"])
    assert d["TileId"]==43 and not d["cutClass"]
    p.user_id=0
    assert choose(s,0,["cut"])["TileId"]==37


def test_four_bot_match_through_real_action_queues(monkeypatch):
    import asyncio
    from unittest.mock import AsyncMock
    from . import bot
    from .SanhuaGameState import SanhuaGameState

    monkeypatch.setattr(bot, "wait_bot_delay", AsyncMock())
    table = SanhuaGameState(room_data=dict(room_id=751, player_list=[0, 1, 2, 3],
                                           random_seed=79, game_round=4))
    asyncio.run(asyncio.wait_for(table.run_game_loop(), timeout=90))
    assert table.lifecycle_completed
    assert len(table.game_record["game_round"]) == 4
    assert sum(p.score for p in table.player_list) == 0
