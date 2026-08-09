import asyncio
import random
from functools import lru_cache

from .HongqueGameState import HongqueGameState
from .efficiency_bot import (
    _structural_value,
    choose_claim_plan,
    choose_discard,
    choose_turn_plan,
)
from .group_index import (
    GROUP_MASKS,
    TILE_INDEX,
    mask_from_codes,
    waiting_mask,
    waiting_masks_after_discards,
)
from .rules import call_candidates
from .tile import full_deck


def test_efficiency_discard_preserves_connected_groups() -> None:
    hand = "AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 GY9".split()
    tile, value = choose_discard(hand, [], hand, drawn_tile="GY9")
    assert tile == "GY9"
    assert value.distance == 0
    assert value.live_waits > 0


def test_efficiency_turn_uses_legal_supplement_before_discard() -> None:
    hand = "AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 GY9".split()
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


def test_efficiency_turn_wins_before_optional_supplement() -> None:
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


def test_efficiency_claim_always_accepts_authoritative_win() -> None:
    plan = choose_claim_plan(
        ["AX1", "AX2"],
        [],
        [{"id": "ron", "kind": "win", "priority": 7}],
        ["AX1", "AX2", "AX3"],
    )
    assert plan == {"action": "claim", "candidate_id": "ron"}


def test_efficiency_claim_accepts_advancing_triplet() -> None:
    hand = "FX1 GY5 DX4 FX3 DX1 BX3 BX4 BY4 AX7 AY6 AY8".split()
    candidates = call_candidates(hand, "EX1")
    plan = choose_claim_plan(hand, [], candidates, hand + ["EX1"])
    assert plan["action"] == "claim"
    chosen = next(item for item in candidates if item["id"] == plan["candidate_id"])
    assert chosen["kind"] == "triplet"
    assert set(chosen["hand_tiles"]) == {"DX1", "FX1"}


def test_smart_hongque_bot_gets_a_claim_task_instead_of_auto_pass() -> None:
    asyncio.run(_exercise_smart_claim_schedule())


def test_ordinary_hongque_bot_keeps_the_fast_auto_pass_route() -> None:
    asyncio.run(_exercise_ordinary_claim_route())


def test_packed_structural_distance_matches_reference_dp() -> None:
    rng = random.Random(20260804)
    deck = full_deck()
    for size in (5, 8, 11, 13):
        for _ in range(6):
            mask = mask_from_codes(rng.sample(deck, size))
            assert _structural_value(mask)[0] == _reference_structural_distance(mask)


def test_batched_discard_waits_match_individual_exact_checks() -> None:
    rng = random.Random(1601)
    deck = full_deck()
    for size in (8, 12, 14):
        hand_mask = mask_from_codes(rng.sample(deck, size))
        batched = waiting_masks_after_discards(
            hand_mask,
            used_mask=hand_mask,
            has_open_group=False,
        )
        remaining = hand_mask
        while remaining:
            discard_bit = remaining & -remaining
            assert batched[discard_bit] == waiting_mask(
                hand_mask ^ discard_bit,
                used_mask=hand_mask,
                has_open_group=False,
            )
            remaining ^= discard_bit


def _reference_structural_distance(hand_mask: int) -> int:
    """The original tuple-DP distance, retained only as a regression oracle."""
    hand_size = hand_mask.bit_count()
    if hand_size == 0:
        return 2
    target_size = hand_size + 1
    variants = {
        (overlap, group_mask.bit_count())
        for group_mask in GROUP_MASKS
        if group_mask.bit_count() <= target_size
        if (overlap := group_mask & hand_mask).bit_count() >= 2
    }
    by_anchor: dict[int, list[tuple[int, int]]] = {}
    for overlap, group_size in variants:
        bits = overlap
        while bits:
            bit = bits & -bits
            by_anchor.setdefault(bit, []).append((overlap, group_size))
            bits ^= bit

    impossible = -10_000

    @lru_cache(maxsize=None)
    def best(mask: int, remaining_target: int) -> int:
        if remaining_target < 0:
            return impossible
        if mask == 0:
            return 0 if remaining_target == 0 or remaining_target >= 3 else impossible
        anchor = mask & -mask
        kept = best(mask ^ anchor, remaining_target)
        for overlap, group_size in by_anchor.get(anchor, ()):
            if group_size > remaining_target or overlap & mask != overlap:
                continue
            tail = best(mask ^ overlap, remaining_target - group_size)
            if tail != impossible:
                kept = max(kept, overlap.bit_count() + tail)
        return kept

    kept = best(hand_mask, target_size)
    return hand_size if kept < 0 else hand_size - kept


async def _exercise_smart_claim_schedule() -> None:
    room = {
        "room_id": "smart-claim-route",
        "game_round": 1,
        "player_list": [101, 2, 103, 104],
        "player_settings": {2: {"username": "牌效罗伯特"}},
    }
    state = HongqueGameState(None, room, gamestate_id="smart-claim-route")
    state.phase = "turn"
    state.players[0].discards = ["EX1"]
    state.players[1].hand = "FX1 GY5 DX4 FX3 DX1 BX3 BX4 BY4 AX7 AY6 AY8".split()
    state.last_discard = {"player": 0, "tile": "EX1"}

    await state._open_claim_window()

    assert state.phase == "claim"
    assert 1 in state.claim_options
    assert 1 not in state.claim_responses
    assert 1 in state._bot_claim_tasks

    state._cancel_bot_claim_tasks()
    if state._claim_timeout_task is not None:
        state._claim_timeout_task.cancel()
    await asyncio.sleep(0)


async def _exercise_ordinary_claim_route() -> None:
    room = {
        "room_id": "ordinary-claim-route",
        "game_round": 1,
        "player_list": [101, 1, 103, 104],
        "player_settings": {1: {"username": "普通机器人"}},
    }
    state = HongqueGameState(None, room, gamestate_id="ordinary-claim-route", debug=False)
    state.phase = "turn"
    state.players[0].discards = ["EX1"]
    state.players[1].hand = ["DX1", "FX1"]
    # Keep one human response pending so the claim window remains observable.
    state.players[2].hand = ["DY1", "EY1"]
    state.last_discard = {"player": 0, "tile": "EX1"}

    await state._open_claim_window()

    assert state.phase == "claim"
    assert state.claim_responses[1] == {"action": "pass"}
    assert 1 not in state._bot_claim_tasks

    if state._claim_timeout_task is not None:
        state._claim_timeout_task.cancel()
    await asyncio.sleep(0)
