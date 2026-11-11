"""First-kong liability follows uninterrupted chains, including replay payments."""
import pytest

from .test_branches import act, table
from .test_flow import assert_conservation
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.test_hongkong_calculation import tiles


PROFILE = "hongkong/new13_lianhuise"


def arrange_wall(state, *, tail=(), head=()):
    """Move existing physical copies, in the order in which they will be drawn."""
    for tile in (*head, *tail):
        state.tiles_list.remove(tile)
    state.tiles_list[:0] = head
    state.tiles_list.extend(reversed(tail))
    assert_conservation(state)


def own_kong(state, window, kind, tile):
    window = act(state, window, **{"1": dict(action_type=kind, target_tile=tile)})
    assert state.machine.phase == P.KONG_RESPONSE
    return act(state, window)


def first_open_kong(kind="angang", *, count=2, enabled=True, fresh=True, flower_at="", prior_liability=""):
    hands = {
        (2, "angang"): "111m 2222p 345s 667z",
        (2, "jiagang"): "111m 2p 345s 667z",
        (3, "angang"): "111m 2222p 3333s 66z",
        (3, "jiagang"): "111m 2p 3s 66z",
        (4, "angang"): "111m 2222p 333s 666z",
        (4, "jiagang"): "111m 2p",
    }
    kong_tiles = [22, 33, 46][:count-1]
    melds = {1: [f"k{tile}" for tile in kong_tiles]} if kind == "jiagang" else None
    state, window = table(PROFILE, hands={1: list(tiles(hands[count, kind]))}, melds=melds,
        detailed_config={"liability_kong": enabled, "flowers": bool(flower_at),
                         "liability_twelve": bool(prior_liability), "liability_dragons": bool(prior_liability)})
    draws = {2: [47, 46], 3: [47, 46, 47], 4: [33, 46, 47, 47]}[count]
    if flower_at:
        draws.insert(0 if flower_at == "first" else 1, 52)
    arrange_wall(state, tail=draws)
    if not fresh:
        # Historical visibility is separate from current physical river tiles.
        state.discard_log = [(2, 11)]
    if prior_liability:
        # A responsibility established before this focused action sequence.
        state.player_list[1].liability_payer = 3
        state.player_list[1].liability_kind = prior_liability
    window = act(state, window, **{"0": dict(action_type="cut", TileId=11)})
    window = act(state, window, **{"1": "gang"})
    assert state.player_list[1].kong_liability_payer == (0 if enabled and fresh else None)
    return state, window, kong_tiles


def finish_and_check_payments(state, window, payer):
    assert "hu_self" in window["actions"][1]
    act(state, window, **{"1": "hu_self"})
    changes = state.deferred_hu_settlements[0]["score_changes"]
    credit = changes[1]
    assert credit > 0 and credit % 3 == 0
    expected = [-credit // 3] * 4 if payer is None else [0] * 4
    expected[1] = credit
    if payer is not None:
        expected[payer] = -credit
    assert changes == expected
    check_recorded_payments(state, expected)


def check_recorded_payments(state, expected, winner=1):
    state.apply_deferred_score_changes()
    payload = state.build_final_settlement_payload(winner)["show_result_info"]
    assert list(payload["score_changes"].values()) == expected
    state.machine.transition(P.READY)
    state.finalize_round_recording()
    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    win_ticks = [tick for tick in ticks if tick[0].startswith("hu")]
    assert len(win_ticks) == 1 and win_ticks[0][4] == expected
    assert ticks[-2][0:2] == ["hongkong", "state"]
    assert ticks[-2][3] == expected
    assert [p.score for p in state.player_list] == expected
    assert_conservation(state)


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
@pytest.mark.parametrize("count", [2, 3, 4])
def test_first_fresh_open_kong_payer_survives_every_uninterrupted_kong(kind, count):
    state, window, kong_tiles = first_open_kong(kind, count=count)
    for tile in kong_tiles:
        window = own_kong(state, window, kind, tile)
    assert state.player_list[1].consecutive_kongs == count
    finish_and_check_payments(state, window, payer=0)


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
@pytest.mark.parametrize("enabled,fresh", [(False, False), (False, True), (True, False)])
def test_chain_does_not_create_liability_when_disabled_or_first_kong_is_seen(kind, enabled, fresh):
    state, window, _ = first_open_kong(kind, enabled=enabled, fresh=fresh)
    window = own_kong(state, window, kind, 22)
    assert state.player_list[1].kong_liability_payer is None
    finish_and_check_payments(state, window, payer=None)


@pytest.mark.parametrize("flower_at", ["first", "second"])
def test_flower_replacement_breaks_first_kong_liability(flower_at):
    state, window, _ = first_open_kong(flower_at=flower_at)
    if flower_at == "second":
        window = own_kong(state, window, "angang", 22)
    assert state.machine.phase == P.REPLACEMENT
    window = act(state, window, **{"1": "buhua"})
    assert state.player_list[1].kong_liability_payer is None
    assert state.player_list[1].consecutive_kongs == 0
    if flower_at == "first":
        window = own_kong(state, window, "angang", 22)
        assert state.player_list[1].consecutive_kongs == 1
    finish_and_check_payments(state, window, payer=None)


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_normal_draw_after_discard_prevents_old_payer_leaking_into_new_own_kong(kind):
    state, window, _ = first_open_kong(kind)
    arrange_wall(state, head=[38, 39, 37, 47])
    window = act(state, window, **{"1": dict(action_type="cut", TileId=47)})
    for seat, tile in ((2, 38), (3, 39), (0, 37)):
        window = act(state, window)
        assert state.current_player_index == seat
        window = act(state, window, **{str(seat): dict(action_type="cut", TileId=tile)})
    window = act(state, window)
    p = state.player_list[1]
    assert p.kong_liability_payer is None and p.consecutive_kongs == 0
    window = own_kong(state, window, kind, 22)
    assert p.consecutive_kongs == 1
    finish_and_check_payments(state, window, payer=None)


@pytest.mark.parametrize("fresh", [False, True])
def test_new_independent_open_kong_replaces_old_payer_after_discard(fresh):
    state, window = table(PROFILE, hands={1: list(tiles("111m 222p 345s 6677z"))},
                          tail=22, detailed_config={"liability_kong": True, "flowers": False})
    arrange_wall(state, tail=[38, 46], head=[22])
    if not fresh:
        state.discard_log = [(3, 22)]
    window = act(state, window, **{"0": dict(action_type="cut", TileId=11)})
    window = act(state, window, **{"1": "gang"})
    assert state.player_list[1].kong_liability_payer == 0
    window = act(state, window, **{"1": dict(action_type="cut", TileId=38)})
    window = act(state, window)
    window = act(state, window, **{"2": dict(action_type="cut", TileId=22)})
    window = act(state, window, **{"1": "gang"})
    assert state.player_list[1].consecutive_kongs == 1
    finish_and_check_payments(state, window, payer=2 if fresh else None)


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_chain_starting_with_own_kong_never_bills_an_old_pung_supplier(kind):
    hand = "1111m 2222p 345s 667z" if kind == "angang" else "1m 2p 345s 667z"
    melds = {1: ["k11", "k22"]} if kind == "jiagang" else None
    state, window = table(PROFILE, actor=1, hands={1: list(tiles(hand))}, melds=melds,
                          detailed_config={"liability_kong": True, "flowers": False})
    if kind == "jiagang":
        state.player_list[1].meld_suppliers = [0, 3]
    arrange_wall(state, tail=[47, 46])
    for tile in (11, 22):
        window = own_kong(state, window, kind, tile)
        assert state.player_list[1].kong_liability_payer is None
    finish_and_check_payments(state, window, payer=None)


def test_robbing_second_added_kong_charges_konger_not_first_kong_supplier():
    state, window = table(PROFILE, melds={1: ["k22"]}, hands={
        1: list(tiles("111m 2p 345s 667z")),
        2: list(tiles("13p 111222333s 55z")),
    }, tail=47, detailed_config={"liability_kong": True, "flowers": False})
    window = act(state, window, **{"0": dict(action_type="cut", TileId=11)})
    window = act(state, window, **{"1": "gang"})
    assert state.player_list[1].kong_liability_payer == 0
    window = act(state, window, **{"1": dict(action_type="jiagang", target_tile=22)})
    assert "hu" in window["actions"][2]
    act(state, window, **{"2": "hu"})
    win = state.deferred_hu_settlements[0]
    assert win["source"] == "rob_kong" and win["kong_player"] == 1
    assert "k22" in state.player_list[1].combination_tiles
    expected = [0, -win["score_changes"][2], win["score_changes"][2], 0]
    assert expected[2] > 0 and win["score_changes"] == expected
    check_recorded_payments(state, expected, winner=2)


@pytest.mark.parametrize("liability", ["dragons", "twelve"])
def test_first_kong_liability_keeps_its_priority_over_existing_responsibility(liability):
    state, window, _ = first_open_kong(prior_liability=liability)
    window = own_kong(state, window, "angang", 22)
    assert state.player_list[1].liability_payer == 3
    assert state.player_list[1].liability_kind == liability
    finish_and_check_payments(state, window, payer=0)
