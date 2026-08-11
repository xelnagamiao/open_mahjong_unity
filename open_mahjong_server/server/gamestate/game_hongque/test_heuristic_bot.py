from server.gamestate.game_hongque.group_index import mask_from_codes
from server.gamestate.game_hongque.heuristic_bot import (
    OpponentView,
    V2Value,
    _claim_advances,
    _is_better_ready,
    _threat_level,
    _tile_danger,
    choose_claim_plan,
    choose_discard,
    choose_turn_plan,
    evaluate_hand,
)
from server.gamestate.game_hongque.rules import call_candidates


class _FakePlayer:
    def __init__(self, discards, melds):
        self.discards = discards
        self.melds = melds


def test_heuristic_discard_tenpai_discards_isolated() -> None:
    hand = "AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 GY9".split()
    tile, value = choose_discard(hand, [], hand, drawn_tile="GY9")
    assert tile == "GY9"
    assert value.distance == 0
    assert value.ukeire > 0


def test_heuristic_turn_wins_when_winning() -> None:
    plan = choose_turn_plan(
        ["AX1", "AX2", "AX3"],
        [],
        ["AX1", "AX2", "AX3"],
        [],
        supplements=0,
        wall_count=50,
        drawn_tile="AX3",
    )
    assert plan == {"action": "win"}


def test_heuristic_turn_supplements_on_scattered_garbage_draw() -> None:
    hand = "AX1 AX2 AX4 BX5 BX7 CX3 CX8 DX2 DX6 EX1 EX9 FX4 GY9".split()
    plan = choose_turn_plan(
        hand,
        [],
        hand,
        [],
        supplements=0,
        wall_count=50,
        drawn_tile="GY9",
    )
    assert plan == {"action": "supplement"}


def test_heuristic_claim_always_accepts_authoritative_win() -> None:
    plan = choose_claim_plan(
        ["AX1", "AX2"],
        [],
        [{"id": "ron", "kind": "win", "priority": 4}],
        ["AX1", "AX2", "AX3"],
    )
    assert plan == {"action": "claim", "candidate_id": "ron"}


def test_heuristic_claim_accepts_advancing_triplet() -> None:
    hand = "FX1 GY5 DX4 FX3 DX1 BX3 BX4 BY4 AX7 AY6 AY8".split()
    candidates = call_candidates(hand, "EX1")
    plan = choose_claim_plan(hand, [], candidates, hand + ["EX1"])
    assert plan["action"] == "claim"
    chosen = next(item for item in candidates if item["id"] == plan["candidate_id"])
    assert chosen["kind"] == "triplet"
    assert set(chosen["hand_tiles"]) == {"DX1", "FX1"}


def test_heuristic_evaluate_tenpai_reports_points() -> None:
    hand = "AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8".split()
    value = evaluate_hand(hand, [], hand)
    assert value.distance == 0
    assert value.ukeire > 0
    assert value.points > 0


def test_heuristic_ready_prefers_more_waits_within_point_slack() -> None:
    more_waits = V2Value(0, 3, 80, 0)
    fewer_waits = V2Value(0, 2, 100, 0)
    assert _is_better_ready(more_waits, fewer_waits)


def test_heuristic_ready_trades_waits_only_for_big_points() -> None:
    fewer_waits_big = V2Value(0, 2, 200, 0)
    more_waits_small = V2Value(0, 3, 80, 0)
    assert _is_better_ready(fewer_waits_big, more_waits_small)
    fewer_waits_small = V2Value(0, 2, 100, 0)
    assert not _is_better_ready(fewer_waits_small, more_waits_small)


def test_heuristic_ready_tie_breaks_on_points_then_style() -> None:
    higher_points = V2Value(0, 2, 150, 0)
    lower_points = V2Value(0, 2, 100, 0)
    assert _is_better_ready(higher_points, lower_points)
    assert not _is_better_ready(lower_points, higher_points)


def test_heuristic_claim_refuses_non_advancing_open() -> None:
    before = V2Value(2, 5, 0, 30)
    after = V2Value(2, 5, 0, 20)
    assert not _claim_advances(before, after, priority=1)


def test_heuristic_claim_accepts_rainbow_style_even() -> None:
    before = V2Value(2, 5, 0, 20)
    after = V2Value(2, 5, 0, 20)
    assert _claim_advances(before, after, priority=3)


# ── Defense layer ────────────────────────────────────────────────────────────

def test_defense_matches_offense_without_opponents() -> None:
    """With no opponents the defense layer must not change the offense play."""
    hand = "AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 GY9".split()
    tile, value = choose_discard(hand, [], hand, drawn_tile="GY9", wall_count=81)
    assert tile == "GY9"
    assert value.distance == 0
    assert value.ukeire > 0


def test_defense_threat_gates_on_rivers_and_wall() -> None:
    short_rivers = tuple(OpponentView(discards=("AX1", "BX2")) for _ in range(3))
    long_rivers = tuple(
        OpponentView(discards=tuple(f"{c}X{i}" for c in "ABC" for i in range(1, 8)))
        for _ in range(3)
    )
    assert _threat_level([], 81) == 0.0
    assert _threat_level(short_rivers, 10) == 0.0  # rivers too short
    assert _threat_level(long_rivers, 81) == 0.0  # early game
    assert _threat_level(long_rivers, 20) >= 0.5  # late + long rivers


def test_defense_genbutsu_is_safe() -> None:
    opp = OpponentView(discards=("AX1",), meld_tiles=("BX4", "BX5", "BX6"))
    assert _tile_danger("AX1", (opp,)) == 0.0


def test_defense_meld_affinity_is_dangerous() -> None:
    opp = OpponentView(discards=("AX1",), meld_tiles=("BX4", "BX5", "BX6"))
    assert _tile_danger("BX7", (opp,)) > _tile_danger("AX9", (opp,))


def test_opponent_view_from_player() -> None:
    player = _FakePlayer(
        discards=["AX1", "BX2"],
        melds=[{"tiles": ["CX3", "CX4", "CX5"]}, {"tiles": ["DY6", "DY7"]}],
    )
    view = OpponentView.from_player(player)
    assert view.discards == ("AX1", "BX2")
    assert view.meld_tiles == ("CX3", "CX4", "CX5", "DY6", "DY7")


def test_defense_prefers_safer_tile_without_losing_efficiency() -> None:
    hand = "AX1 AX5 BX2 BX6 CX3 CX7 DX4 DX8 EX1 EX5 FX2 FX6 GY3 GY7".split()
    opponents = (
        OpponentView(discards=("AX1", "BX2", "CX3", "DX5", "EX4", "FX1", "GX2"),
                     meld_tiles=("AY5", "AY6", "AY7")),
        OpponentView(discards=("AX3", "BX4", "CX5", "DX6", "EX7", "FX8", "GX9"),
                     meld_tiles=("BY2", "BY3", "BY4")),
        OpponentView(discards=("AX7", "BX8", "CX9", "DX1", "EX2", "FX3", "GX4"),
                     meld_tiles=("CY1", "CY2", "CY3")),
    )
    wall_count = 15
    tile, _ = choose_discard(hand, [], hand, wall_count=wall_count, opponents=opponents)
    assert _threat_level(opponents, wall_count) >= 0.5

    tile_v2, _ = choose_discard(hand, [], hand)
    dist_v3 = _structural_value_ref(hand, tile)
    dist_v2 = _structural_value_ref(hand, tile_v2)
    danger_v3 = _tile_danger(tile, opponents)
    danger_v2 = _tile_danger(tile_v2, opponents)
    # Defense must not give up efficiency, and must not pick a riskier tile.
    assert dist_v3 <= dist_v2
    assert danger_v3 <= danger_v2


def test_far_hand_folds_to_safest_within_budget() -> None:
    hand = "AX8 GX9 DY2 BY1 AX5 AY3 DX2 CY9 AX9 BY4 DY8 DX1 FY7 EX1".split()
    opponents = (
        OpponentView(discards=("AX1", "BX2", "CX3", "DX5", "EX4", "FX1", "GX2"),
                     meld_tiles=("AY5", "AY6", "AY7")),
        OpponentView(discards=("AX3", "BX4", "CX5", "DX6", "EX7", "FX8", "GX9"),
                     meld_tiles=("BY2", "BY3", "BY4")),
        OpponentView(discards=("AX7", "BX8", "CX9", "DX1", "EX2", "FX3", "GX4"),
                     meld_tiles=("CY1", "CY2", "CY3")),
    )
    wall_count = 15
    tile, _ = choose_discard(hand, [], hand, wall_count=wall_count, opponents=opponents)

    threat = _threat_level(opponents, wall_count)
    distances = {t: _structural_value_ref(hand, t) for t in hand}
    min_distance = min(distances.values())
    assert threat >= 0.5
    assert min_distance >= 2
    slack = 1 if threat < 0.75 else 2
    eligible = [t for t in hand if distances[t] <= min_distance + slack]
    expected = min(
        (distances[t] - min_distance, _tile_danger(t, opponents))
        for t in eligible
    )
    assert (distances[tile] - min_distance, _tile_danger(tile, opponents)) == expected


def _structural_value_ref(hand, tile) -> int:
    from server.gamestate.game_hongque.heuristic_bot import _structural_value

    remaining = [c for c in hand if c != tile]
    return _structural_value(mask_from_codes(remaining))[0]
