"""Rulebook-derived action cases using the real joker solver and scorer."""

from collections import Counter
from copy import deepcopy

import pytest

from .HangzhouGameState import HangzhouGameState
from .actions import claim_actions, kong_tiles, legal_cuts
from .state_machine import Phase as P, StateMachine
from ...game_calculation.hangzhou.rules import JOKER, TILES, meld_tiles

PLAIN = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
FLOAT = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, JOKER, JOKER]


def state(seed=1, **config):
    room = dict(HangzhouGameState._default_room_data(), random_seed=seed, **config)
    s = HangzhouGameState(room_data=room)
    s.initialize_round()
    return s


def turn(hand=None, melds=(), *, wall_tail=None, record=True, **config):
    s = state(**config)
    p = s.player_list[0]
    p.hand_tiles = list(PLAIN if hand is None else hand)
    p.combination_tiles = list(melds)
    p.combination_mask = [[v for tile in meld_tiles(code) for v in (2 if code[0] == "G" else 0, tile)] for code in melds]
    for code, mask in zip(melds, p.combination_mask):
        if code[0] != "G":
            mask[0] = 1
    p.pre_draw_tiles = p.hand_tiles[:-1]
    p.has_draw_slot = True
    p.draw_kind = "normal"
    pool = Counter({t: 4 for t in TILES}) - Counter(p.hand_tiles) - Counter(t for code in melds for t in meld_tiles(code))
    bag = list(pool.elements())
    for other in s.player_list[1:]:
        other.hand_tiles, bag = bag[:13], bag[13:]
    s.tiles_list = bag
    if wall_tail:
        for tile in wall_tail:
            s.tiles_list.remove(tile)
        s.tiles_list.extend(wall_tail)
    if record:
        s.start_game_recording()
        s.start_round_recording()
    s.open_action_window(s.begin_turn(0))
    return s


def act(s, index=None, action=None, **data):
    window = s.live_pending_window
    responses = {i: dict(action_type="pass") for i, offered in window["actions"].items() if offered}
    if index is not None:
        responses[index] = dict(action_type=action, **data)
    return s.apply_action_results(window, responses)


def physical(s):
    return Counter(s.tiles_list + s.burned_tiles + [t for p in s.player_list for t in p.hand_tiles + p.discard_tiles]
                   + [t for p in s.player_list for code in p.combination_tiles for t in meld_tiles(code)])


def river(s, actor, tile):
    if s.machine.phase == P.TURN:
        s.machine.transition(P.RESPONSE)
    s.current_player_index = actor
    s.player_list[actor].discard_tiles.append(tile)
    s.player_list[actor].discard_origin_tiles.append(tile)
    return s.open_action_window(s._window(P.RESPONSE, actor, tile, claim_actions(s, actor, tile)))


def test_initial_wall_deal_source_and_seed_are_reproducible():
    a, b = state(19), state(19)
    assert len(a.tiles_list) == 83
    assert [len(p.hand_tiles) for p in a.player_list] == [14, 13, 13, 13]
    assert physical(a) == Counter({t: 4 for t in TILES})
    assert a.tiles_list == b.tiles_list and a.dice == b.dice
    assert a.player_list[0].pre_draw_tiles == a.player_list[0].hand_tiles[:-1]
    assert a.dead_wall_count == 20 and a.dealer_streak == 1
    with pytest.raises(ValueError):
        state().initialize_round(wall=[11] * 136)
    s = HangzhouGameState()
    wall = [t for t in TILES for _ in range(4)]
    s.initialize_round(wall=wall)
    assert physical(s) == Counter(wall)
    s.open_action_window(s.opening_window())
    with pytest.raises(RuntimeError):
        s.initialize_round()


def test_joker_is_white_never_green_and_cannot_be_called_or_konged():
    assert JOKER == 46
    s = turn([46] * 4 + [11, 12, 13, 21, 22, 23, 31, 32, 33, 41])
    assert 46 not in kong_tiles(s, 0, "angang")
    assert claim_actions(s, 1, 46) == dict.fromkeys(range(4), [])
    s.player_list[0].hand_tiles = [47] * 4 + PLAIN[4:]
    assert 47 in kong_tiles(s, 0, "angang")
    assert not s.can_win(0, "discard", 42) and not s.can_win(0, "rob_kong", 42)


def test_plain_self_draw_is_scored_from_server_and_settled_once():
    s = turn()
    assert "hu_self" in s.action_dict[0]
    before = physical(s)
    act(s, 0, "hu_self")
    assert s.machine.phase == P.END
    assert s.round_settlement["source"] == "self_draw"
    assert s.round_changes == [12, -4, -4, -4]  # 无财神1番 × 一老庄2
    assert physical(s) == before
    s.apply_deferred_score_changes()
    s.apply_deferred_score_changes()
    assert [p.score for p in s.player_list] == [12, -4, -4, -4]
    assert len(s.player_list[0].score_history) == 1
    with pytest.raises(ValueError):
        s.settle_win(0, "self_draw", 42)


def test_cai_lock_forces_physical_drawn_instance_and_expires_on_owner_draw():
    s = turn(FLOAT)
    act(s, 0, "cut", TileId=46, cutClass=True, cutIndex=13)
    assert s.cai_discard_locks == {0}
    assert s.player_list[0].cai_piao_count == 1 and not s.player_list[0].drawn_after_cai
    assert not any(s.action_dict.values())
    act(s)
    assert s.current_player_index == 1 and s.is_cai_locked(1)
    p = s.player_list[1]
    assert legal_cuts(s, 1) == {p.hand_tiles[-1]}
    p.hand_tiles[0] = p.hand_tiles[-1]
    with pytest.raises(ValueError, match="刚摸"):
        act(s, 1, "cut", TileId=p.hand_tiles[-1], cutClass=False)
    act(s, 1, "cut", TileId=p.hand_tiles[-1], cutClass=True)
    act(s)
    for seat in (2, 3):
        assert s.current_player_index == seat
        act(s, seat, "cut", TileId=s.player_list[seat].hand_tiles[-1], cutClass=True)
        act(s)
    assert s.current_player_index == 0 and not s.cai_discard_locks
    assert s.player_list[0].drawn_after_cai
    quote = s.score_win(0, "self_draw", s.player_list[0].hand_tiles[-1])
    assert quote and {"baotou", "cai_piao"} <= set(quote.fan_ids)


def test_overlapping_joker_discards_do_not_overwrite_earlier_lock():
    s = turn(FLOAT)
    s.cai_discard_locks = {0}
    s.player_list[1].hand_tiles = PLAIN[:-1] + [46]
    s.player_list[1].has_draw_slot = True
    s.open_action_window(s.begin_turn(1))
    act(s, 1, "cut", TileId=46, cutClass=True)
    assert s.cai_discard_locks == {0, 1} and all(s.is_cai_locked(i) for i in range(4))
    s.draw_for(0)
    assert s.cai_discard_locks == {1} and s.is_cai_locked(0) and not s.is_cai_locked(1)


@pytest.mark.parametrize("action,tile,held,code", [
    ("chi_left", 13, [11, 12], "s12"), ("chi_mid", 12, [11, 13], "s12"),
    ("chi_right", 11, [12, 13], "s12"), ("peng", 41, [41, 41], "k41"),
    ("gang", 41, [41, 41, 41], "g41"),
])
def test_claim_shapes_direction_and_no_win_after_chi_peng(action, tile, held, code):
    s = turn()
    p = s.player_list[1]
    p.hand_tiles = held + [21, 22, 23, 31, 32, 33, 42, 42, 44, 45, 47][:13 - len(held)]
    river(s, 0, tile)
    assert action in s.action_dict[1]
    act(s, 1, action)
    assert p.combination_tiles == [code]
    assert p.combination_mask[0][0:2] == [1, tile]
    assert not s.player_list[0].discard_tiles
    assert p.had_meld_action and p.chi_count == (action.startswith("chi"))
    assert s.machine.phase == (P.TURN if action == "gang" else P.DISCARD_ONLY)
    if action != "gang":
        assert "hu_self" not in s.action_dict[1]


def test_pung_has_priority_over_chow_and_closest_pung_wins():
    s = turn()
    s.player_list[1].hand_tiles = [11, 12] + PLAIN[2:13]
    s.player_list[2].hand_tiles = [13, 13] + PLAIN[2:13]
    river(s, 0, 13)
    responses = {i: dict(action_type="pass") for i, a in s.action_dict.items() if a}
    responses[1] = dict(action_type="chi_left")
    responses[2] = dict(action_type="peng")
    s.apply_action_results(s.live_pending_window, responses)
    assert s.current_player_index == 2


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_kong_uses_lower_tail_burns_upper_and_is_allowed_during_lock(kind):
    hand = [11] * (4 if kind == "angang" else 1) + [21, 22, 23, 31, 32, 33, 41, 41, 42, 42]
    s = turn(hand, () if kind == "angang" else ("k11",), wall_tail=[45, 47])
    before = physical(s)
    s.cai_discard_locks = {1}
    s.open_action_window(s.begin_turn(0))
    act(s, 0, kind, target_tile=11)
    assert s.machine.phase == P.TURN and s.player_list[0].after_kong
    assert s.player_list[0].hand_tiles[-1] == 47 and s.burned_tiles == [45]
    assert s.cai_discard_locks == {1} and s.is_cai_locked(0)
    assert physical(s) == before
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    burn = next(i for i, t in enumerate(ticks) if t[:2] == ["hangzhou", "tail_burn"])
    assert ticks[burn][3] == 45 and ticks[burn + 2] == ["gd", 47]


def test_kong_immediately_after_chow_and_owner_meld_releases_lock():
    s = turn()
    p = s.player_list[1]
    p.hand_tiles = [11, 12, 21, 21, 21, 21, 31, 32, 33, 41, 41, 42, 42]
    s.cai_discard_locks = {1}
    river(s, 0, 13)
    act(s, 1, "chi_left")
    assert not s.cai_discard_locks and "angang" in s.action_dict[1]
    act(s, 1, "angang", target_tile=21)
    assert p.combination_tiles == ["s12", "G21"] and p.has_draw_slot


@pytest.mark.parametrize("claim,held,called,kong,tile", [
    ("chi_left", [11,12]+[21]*4+[31,32,33]+[41]*3+[46,46], 13, "angang", 21),
    ("peng", [11]*3+[21,22,23,31,32,33,41,41,46,46,46], 11, "jiagang", 11),
])
def test_cai_history_survives_own_claim_then_kong_until_non_white_cut(claim, held, called, kong, tile):
    s = turn(held)
    p = s.player_list[0]
    act(s, 0, "cut", TileId=46, cutClass=True)
    assert p.cai_piao_count == 1 and s.cai_discard_locks == {0}
    # The next upper-player discard can be claimed by the lock owner.
    river(s, 3, called)
    act(s, 0, claim)
    assert not s.cai_discard_locks and p.cai_piao_count == 1 and not p.drawn_after_cai
    assert "hu_self" not in s.action_dict[0]
    s.tiles_list[-1] = 43
    act(s, 0, kong, target_tile=tile)
    score = s.score_win(0, "self_draw", 43)
    assert score and {"baotou", "cai_piao", "kong_draw"} <= set(score.fan_ids)
    assert p.cai_piao_count == 1 and p.drawn_after_cai
    act(s, 0, "cut", TileId=43, cutClass=True)
    assert p.cai_piao_count == 0 and not p.drawn_after_cai


def test_last_twenty_tile_boundary_and_kong_two_tile_requirement():
    s = turn([11] * 4 + PLAIN[4:])
    s.tiles_list = s.tiles_list[:21]
    assert not kong_tiles(s, 0, "angang")
    act(s, 0, "cut", TileId=s.player_list[0].hand_tiles[-1], cutClass=True)
    act(s)
    assert len(s.tiles_list) == 20 and s.machine.phase == P.TURN
    assert not any(claim_actions(s, 1, 41).values())
    act(s, 1, "cut", TileId=s.player_list[1].hand_tiles[-1], cutClass=True)
    assert s.machine.phase == P.END and s.round_changes == [0] * 4
    assert s.next_dealer == 0


@pytest.mark.parametrize("win", [False, True])
def test_ten_winds_declaration_after_tenth_discard_preserves_physical_hand(win):
    s = turn(PLAIN[:-1] + [43])
    p = s.player_list[0]
    p.discard_origin_tiles = [41, 42, 42, 43, 43, 44, 44, 45, 47]
    p.discard_tiles = list(p.discard_origin_tiles)
    act(s, 0, "cut", TileId=43, cutClass=True)
    assert s.machine.phase == P.TEN_WINDS and s.action_dict[0] == ["hu_self", "pass"]
    assert not p.has_draw_slot and len(p.hand_tiles) == 13
    act(s, 0, "hu_self" if win else "pass")
    if win:
        assert s.round_settlement["source"] == "ten_winds"
        assert len(p.hand_tiles) == 13 and len(p.discard_tiles) == 10
        assert s.round_settlement["score"]["points"] == 16  # ten winds + no white
    else:
        assert s.machine.phase == P.RESPONSE


def test_ten_winds_not_awarded_after_any_meld_or_non_honor():
    for bad_meld, bad_discard in ((True, False), (False, True)):
        s = turn(PLAIN[:-1] + [43])
        p = s.player_list[0]
        p.discard_origin_tiles = [41] * 9
        p.had_meld_action = bad_meld
        if bad_discard:
            p.discard_origin_tiles[0] = 11
        act(s, 0, "cut", TileId=43, cutClass=True)
        assert s.machine.phase == P.RESPONSE


def test_validation_is_atomic_for_stale_illegal_duplicate_and_client_score():
    s = turn()
    before = deepcopy(s.player_list[0].hand_tiles)
    window = s.live_pending_window
    with pytest.raises(ValueError):
        s.apply_action_results(dict(window), {0: dict(action_type="hu_self")})
    with pytest.raises(ValueError):
        s.apply_action_results(window, {0: dict(action_type="hu_self")}, settlements={0: {"points": 999}})
    with pytest.raises(ValueError):
        s.apply_action_results(window, {})
    for data in (dict(action_type="hu"), dict(action_type="cut", TileId=99),
                 dict(action_type="cut", TileId=42, cutIndex=True),
                 dict(action_type="cut", TileId=42, cutClass=1),
                 dict(action_type="angang", target_tile=42)):
        with pytest.raises(ValueError):
            s.apply_action_results(window, {0: data})
        assert s.player_list[0].hand_tiles == before
    with pytest.raises(ValueError):
        s.validate_response(True, {}, [])
    with pytest.raises(ValueError):
        s.validate_response(0, [], [])


def test_invalid_state_transitions_and_profile_are_rejected():
    machine = StateMachine()
    machine.transition(P.START)
    assert machine.version == 0
    with pytest.raises(RuntimeError):
        machine.transition(P.READY)
    with pytest.raises(ValueError):
        HangzhouGameState(room_data=dict(HangzhouGameState._default_room_data(), sub_rule="hangzhou/other"))
    for key in ("use_flowers", "open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he"):
        with pytest.raises(ValueError):
            HangzhouGameState(room_data=dict(HangzhouGameState._default_room_data(), **{key: True}))
    with pytest.raises(ValueError):
        HangzhouGameState(room_data=dict(HangzhouGameState._default_room_data(), player_list=[101]))
