"""拒绝非法操作、缓存边界和结算边界；不替换成牌或支付算法。"""

import asyncio
from copy import deepcopy
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand, draw, HAND, MODULE, ticks


def test_wrong_sub_rule_and_penalty_player_cannot_use_mil_actions():
    with pytest.raises(ValueError, match="MIL2023"):
        make_state(sub_rule="guangdong/tuidao_mil2024")
    state = make_state()
    p = hand(state, 0, HAND); draw(p, 11)
    p.tag_list.append("peida")
    assert state.score_candidate(0, "self_draw") is None
    assert not state.kong_allowed(0, 11, "concealed")
    assert not state.kong_allowed(1, 11, "unknown")
    p.tag_list.clear()
    assert not state.kong_allowed(0, 11, "unknown")
    hand(state, 1, HAND).tag_list.append("peida")
    assert not state.check_discard_actions(45)[1]


def test_empty_hand_ghost_ron_and_no_waiting_result_do_not_create_pass_lock():
    state = make_state()
    hand(state, 0, [])
    assert state.score_candidate(0, "self_draw") is None
    assert state.score_candidate(1, "discard", 55) is None
    state.enter_water(1)
    assert state.player_list[1].passed_base_score == -1
    state.player_list[1].last_discarded_tile = 12
    state._finalize_ready_after_discard(1, False)
    assert state.player_list[1].passed_base_score == -1
    assert state._revoke_qualification(1) is False
    player = hand(state, 0, HAND); draw(player, 45)
    assert state.score_candidate(0, "self_draw", tile=45)["base_score"] > 0


def test_score_prefetch_is_bounded_and_skips_invalid_or_already_prepared_requests():
    async def run():
        state = make_state(); p = hand(state, 0, HAND); draw(p, 45)
        state._candidate_cache = {("stale", i): None for i in range(32)}
        await state._prepare_self_score()
        assert len(state._candidate_cache) == 1
        before = dict(state._candidate_cache)
        await state._prepare_self_score()
        await state._prepare_other_scores(55, "discard")
        assert state._candidate_cache == before
        assert state.score_candidate(0, "self_draw")["base_score"] > 0
    asyncio.run(run())


def test_rejected_kongs_and_claims_leave_all_physical_tiles_and_scores_unchanged():
    async def run():
        state = make_state(); p = hand(state, 0, HAND); draw(p, 45)
        before = [(list(p.hand_tiles), list(p.combination_tiles), p.score) for p in state.player_list]
        await state.execute_angang(0, 55)
        await state.execute_jiagang(0, 45)
        await state.execute_claim(1, "chi")
        await state.execute_claim(1, "peng")  # 无人出牌
        p.discard_tiles = [55]
        await state.execute_claim(1, "peng")  # 不能碰鬼
        assert before == [(list(p.hand_tiles), list(p.combination_tiles), p.score) for p in state.player_list]
        assert not state.kong_ledger
    asyncio.run(run())


def test_no_op_super_kong_or_claim_cannot_issue_duplicate_payments():
    async def run():
        from ..game_taiwan.TaiwanGameState import TaiwanGameState
        state = make_state(); p = hand(state, 0, HAND); draw(p, 11)
        with patch.object(TaiwanGameState, "execute_angang", new=AsyncMock()):
            await state.execute_angang(0, 11)
        p.discard_tiles = [22]; hand(state, 1, [22, 22])
        with patch.object(TaiwanGameState, "execute_claim", new=AsyncMock()):
            await state.execute_claim(1, "peng")
        assert not state.kong_ledger and all(p.score == 0 for p in state.player_list)
    asyncio.run(run())


def test_coefficient_one_settlement_and_draw_without_any_kong():
    async def run():
        state = make_state(); p = hand(state, 0, HAND); draw(p, 55)
        state.current_round = 4
        state.accept_self_draw(0)
        assert state.pending_winners[0]["detail"]["coefficient"] == 1
        await state._settle_hand({i: 0 for i in range(4)})
        assert not any("GD|coefficient|" in str(t) for t in ticks(state))
        state = make_state(); state.current_round = 4
        with patch(MODULE + ".asyncio.sleep", new=AsyncMock()):
            assert await state._settle_hand({i: 0 for i in range(4)})
        assert all(p.score == 0 for p in state.player_list)
        assert not any(t[:2] == ["guangdong", "refund_kongs"] for t in ticks(state))
    asyncio.run(run())
