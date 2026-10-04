"""固定种子差分：实体鬼合并后的最高分须等于完整实体见证枚举。"""

import random
from collections import Counter
import pytest

from .. import rules
from ..config import GHOSTS, TILES
from ..scoring import Context, Interpretation, score_interpretation
from ...jokers import iter_winning_shapes


def test_canonical_ghost_projection_matches_exhaustive_physical_witnesses():
    rng = random.Random(20231002)
    for ghost_count in range(5):
        for sample in range(8):
            while True:
                groups = []
                for _ in range(4):
                    if rng.randrange(2):
                        tile = rng.choice(TILES)
                        groups.append([tile] * 3)
                    else:
                        first = rng.choice((10, 20, 30)) + rng.randrange(1, 8)
                        groups.append([first, first + 1, first + 2])
                pair = rng.choice(TILES)
                physical = [t for group in groups for t in group] + [pair, pair]
                if max(Counter(physical).values()) <= 4:
                    break
            for index, ghost in zip(rng.sample(range(14), ghost_count), GHOSTS):
                physical[index] = ghost
            winning = physical[rng.randrange(14)]
            context = Context(self_draw=True, last_tile=sample % 3 == 0,
                kong_flower=sample % 4 == 0, discarded_ghosts=rng.randrange(5 - ghost_count),
                require_minimum_score=sample % 2 == 0)
            candidates = []
            if ghost_count == 4:
                candidates.append(score_interpretation(physical, [], Interpretation("four_ghosts", ()), context))
            for witness in iter_winning_shapes(physical, [], rules.POLICY, winning_tile=winning):
                interpretation = Interpretation(witness.kind, witness.closed_logical_tiles,
                    tuple(group.logical for group in witness.melds), witness.pair or None,
                    tuple((use.physical, use.logical) for use in witness.joker_uses),
                    witness.winning_logical_tile or None)
                candidates.append(score_interpretation(physical, [], interpretation, context))
            expected = max((x["base_score"] for x in candidates if x["is_win"]), default=None)
            actual = rules.score(physical, winning_tile=winning, context=context)
            assert (actual["base_score"] if actual else None) == expected, (physical, winning, context)
            if actual and actual["shape"] != "four_ghosts":
                assert Counter(x["physical"] for x in actual["substitutions"]) == Counter(t for t in physical if t in GHOSTS)


def test_invalid_wait_inventory_and_cache_reset_never_retain_mutable_results():
    assert rules.waiting_scores([11] * 13) == []
    assert not rules._structural_waits((11,) * 13, ())
    hand = [11, 11, 11, 22, 22, 22, 33, 33, 33, 41, 41, 41, 45, 45]
    expected = rules.score(hand, context=Context(self_draw=True))
    rules.clear_caches()
    assert rules.score(hand, context=Context(self_draw=True)) == expected


@pytest.mark.parametrize("winning", GHOSTS)
def test_four_ghost_winning_identity_projection_preserves_real_nine_gate_explanation(winning):
    hand = [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 55, 56, 57, 58]
    detail = rules.score(hand, winning_tile=winning, context=Context(self_draw=True))
    assert "nine_gates" in detail["fan_ids"] and detail["fan"] == 17
    substitutions = {item["physical"]: item["logical"] for item in detail["substitutions"]}
    assert set(substitutions) == set(GHOSTS)
    before_win = Counter(detail["logical_hand"])
    before_win[substitutions[winning]] -= 1
    assert before_win == Counter([11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19])


def test_rule_adapter_maximum_is_unchanged_when_core_uses_full_witness_mode(monkeypatch):
    hand = [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 55, 56, 57, 58]
    context = Context(self_draw=True)
    projected = rules.score(hand, winning_tile=56, context=context)
    def full_iterator(*args, **kwargs):
        kwargs["equivalence"] = "full"
        return iter_winning_shapes(*args, **kwargs)
    # 切换真实核心的完整枚举模式，重复实体见证不能让规则适配器改变最高分。
    rules.clear_caches()
    monkeypatch.setattr(rules, "iter_winning_shapes", full_iterator)
    complete = rules.score(hand, winning_tile=56, context=context)
    assert (complete["fan_ids"], complete["fan"], complete["base_score"]) == (
        projected["fan_ids"], projected["fan"], projected["base_score"])
    rules.clear_caches()
