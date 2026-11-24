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


@pytest.mark.parametrize("tile", [45, 27, 28])
def test_single_special_tile_cut_is_converted_to_kong(tile):
    s = hand_state()
    p = s.player_list[0]
    p.hand_tiles = [tile]
    s.tiles_list = [22]
    window = s.take_turn(0, {"action_type": "cut", "TileId": tile, "cutClass": False})
    assert p.discard_tiles == []
    assert p.specials == [tile]
    assert p.hand_tiles == [22]
    assert window["status"] == Phase.TURN


@pytest.mark.parametrize("action", ["cut", "angang"])
def test_four_red_dragons_still_offer_one_rob_and_only_remove_one(action):
    s = hand_state()
    p = s.player_list[0]
    p.hand_tiles = [45] * 4
    p.has_draw_slot = True
    s.tiles_list = [22]
    s.marker = -1
    s.player_list[1].hand_tiles = list(ORPHANS)
    data = {"action_type": action, "TileId": 45, "cutClass": True, "target_tile": 45}
    w = invoke(s, s.turn(0), {0: data})
    assert w["status"] == Phase.ROB_KONG and "hu" in w["actions"][1]
    assert p.hand_tiles == [45] * 4 and not p.discard_tiles and not p.specials
    invoke(s, w, {1: "pass"})
    assert p.hand_tiles == [45, 45, 45, 22] and p.specials == [45]
    assert not p.discard_tiles and not p.combination_tiles


def test_last_drawn_special_without_replacement_cannot_be_konged_or_discarded():
    s = hand_state()
    p = s.player_list[0]
    p.hand_tiles = [11, 45]
    p.has_draw_slot = True
    s.tiles_list = []
    w = s.open_action_window(s.turn(0))
    assert w["actions"][0] == ["cut"]
    with pytest.raises(ValueError):
        s.validate_response(0, dict(action_type="cut", TileId=45), ["cut"])
    for user_id in (0, 1):
        p.user_id = user_id
        assert choose(s, 0, ["cut"])["TileId"] == 11
    timed = s._build_timeout_action(0)
    assert timed["TileId"] == 11 and not timed["cutClass"]
    invoke(s, w, {0: timed})
    assert p.discard_tiles == [11] and not p.specials

    s = hand_state()
    s.tiles_list = []
    s.player_list[0].hand_tiles = [45, 27]
    assert s.turn(0)["status"] == Phase.END
    assert s.ended_by == "wall" and not s.player_list[0].specials


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


def second_suited_chow(s):
    p = s.player_list[0]
    p.calls = [1]
    p.combination_tiles = ["k16"]
    p.hand_tiles = [11, 12, 14, 14, 14, 15, 17, 17, 17, 29]
    s.player_list[3].discard_tiles = [13]
    s.player_list[3].riichi_flags = [False]
    w = s.window(Phase.RESPONSE, 3, 13, s.claims(3, 13))
    return invoke(s, w, {0: "chi_left"})


def test_clear_declaration_before_third_call_locks_supplier():
    s=hand_state()
    p=s.player_list[0]
    w=invoke(s,second_suited_chow(s),{0:"baoqing"})
    assert p.baoqing and "baoqing" not in w["actions"][0]
    s.player_list[2].discard_tiles=[17]
    s.player_list[2].riichi_flags=[False]
    w=s.window(Phase.RESPONSE,2,17,s.claims(2,17))
    invoke(s,w,{0:"peng"})
    assert p.liable==2 and p.calls==[1,1,1]


def test_second_chow_is_the_only_declaration_window():
    s = hand_state()
    p = s.player_list[0]
    p.specials = [45, 45, 45]
    w = second_suited_chow(s)
    assert "baoqing" in w["actions"][0]
    assert "baoqing" in s.build_pending_action_payload(0)["ask_hand_action_info"]["action_list"]
    s.tiles_list[:4] = [31, 32, 33, 15]
    w = invoke(s, w, {0: dict(action_type="cut", TileId=29)})
    for seat, tile in [(1, 31), (2, 32), (3, 33)]:
        w = invoke(s, w, {})
        w = invoke(s, w, {seat: dict(action_type="cut", TileId=tile, cutClass=True)})
    w = invoke(s, w, {})
    assert "baoqing" not in w["actions"][0]
    assert "baoqing" not in s.build_pending_action_payload(0)["ask_hand_action_info"]["action_list"]
    with pytest.raises(ValueError):
        invoke(s, w, {0: "baoqing"})
    assert "hu_self" in w["actions"][0]
    invoke(s, w, {0: "hu_self"})
    assert "清一色" not in s.deferred_hu_settlements[-1]["fan_names"]


@pytest.mark.parametrize("payer", [1, 2, 3])
def test_direct_discard_after_declaration_doubles_only_the_discarder(payer):
    s = hand_state()
    w = invoke(s, second_suited_chow(s), {0: "baoqing"})
    invoke(s, w, {0: dict(action_type="cut", TileId=29)})
    p = s.player_list[0]
    assert p.liable is None
    p.hand_tiles.append(15)
    assert s.quote(0, 15)["changes"] == [30, -10, -10, -10]
    p.hand_tiles.pop()
    s.player_list[payer].discard_tiles = [15]
    s.player_list[payer].riichi_flags = [False]
    w = s.window(Phase.RESPONSE, payer, 15, s.claims(payer, 15))
    invoke(s, w, {0: "hu"})
    changes = [40, -10, -10, -10]
    changes[payer] = -20
    assert s.round_changes == changes
    assert s.deferred_hu_settlements[-1]["score_changes"] == changes


def test_existing_third_call_supplier_stays_liable_when_someone_else_discards():
    s = hand_state()
    p = s.player_list[0]
    p.baoqing, p.liable = True, 1
    p.combination_tiles = ["s12", "k16", "k17"]
    p.hand_tiles = [11, 12, 13, 15]
    assert s.quote(0, 15, 2)["changes"] == [40, -20, -10, -10]


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


def test_two_jokers_cannot_win_small_hand():
    hand=[11,12,28,21,22,23,34,35,28,12,12]
    assert rules.score(hand,["s16"],27,0,None,[["开口番","暗杠","暗杠"],[],[],[]]) is None


@pytest.mark.parametrize("self_draw", [True, False])
def test_all_258_wins_without_groups_or_seven_pairs(self_draw):
    s = hand_state()
    s.indicator, s.skin, s.joker = 41, 41, 42
    p = s.player_list[0]
    p.hand_tiles = [12,12,12,15,18,22,25,28,28,32,35,38,38]
    if self_draw:
        p.hand_tiles.append(38)
        p.has_draw_slot = True
        invoke(s, s.turn(0), {0: "hu_self"})
    else:
        s.player_list[1].discard_tiles = [38]
        s.player_list[1].riichi_flags = [False]
        invoke(s, s.window(Phase.RESPONSE, 1, 38, s.claims(1, 38)), {0: "hu"})
    assert s.deferred_hu_settlements[-1]["fan_names"] == ["将一色", "硬和"]
    assert s.round_changes == [30, -10, -10, -10]


def test_independent_jiang_checks_melds_wildcards_and_minimum_fan():
    hand = [12,15,18,22,25,28,32,32,35,35,38]
    table = [["开口番"], [], [], []]
    score = lambda tiles, meld, payer: rules.score(tiles, [meld], 41, 0, payer, table)
    assert score(hand, "G22", 1) is None  # five physical copies of 2筒
    assert score(hand, "s15", 1) is None
    assert score(hand, "k32", 1) is None
    wild = [42] + hand[1:]
    assert score(wild, "k12", 1) is None  # 3.5番 against this payer
    table[1] = ["开口番"]
    assert score(wild, "k12", 1)["fan_names"] == ["将一色", "开口番"]
    assert score(hand, "k12", 1)["fan_names"] == ["将一色", "硬和", "开口番"]
    assert score([41] + hand[1:], "k12", 1) is None
    assert score([45] + hand[1:], "k12", 1) is None
    assert score([19] + hand[1:], "k12", 1) is None


def test_jiang_and_seven_pairs_stack_once_each():
    hand = [12,15,18,22,25,28,32] * 2
    result = rules.score(hand, [], 41, 0, 1, [[], [], [], []])
    assert result["fan_names"] == ["七对", "将一色", "硬和"]


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
