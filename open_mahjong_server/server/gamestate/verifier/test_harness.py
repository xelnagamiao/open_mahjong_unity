"""无头回归：合法操作推进、非法操作拒绝、回退重放。"""
from __future__ import annotations

import asyncio
import os
import sys

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.gamestate.verifier.check_contract import check_lock
from server.gamestate.verifier.session import VerifierError, VerifierSession


def test_contract_lock_matches_unity():
    assert check_lock(update=False) == 0


def test_qi_dui_cut_and_illegal_rejected_and_restore():
    asyncio.run(_qi_dui_flow())


async def _qi_dui_flow() -> None:
    session = VerifierSession("testharness")
    try:
        await session.start(
            seed=1,
            debug_scenario="qi_dui_tenpai_1m",
            tactical_call=False,
            game_round=1,
            hepai_limit=1,
        )
        snap = session.snapshot()
        assert snap["paused"] is True
        legal = snap["legal"]
        assert legal["seats"]["0"]["waiting"] is True
        assert "cut" in legal["seats"]["0"]["client_actions"]
        junk = 19
        assert junk in legal["seats"]["0"]["cut_tiles"]

        try:
            await session.submit({"player_index": 0, "action_type": "peng"})
            raise AssertionError("非法 peng 应当被拒绝")
        except VerifierError as exc:
            assert "不允许" in str(exc) or "不会画出" in str(exc)

        after = await session.submit({"player_index": 0, "action_type": "cut", "tile_id": junk})
        ticks = []
        record = after["record"]["record"]
        for round_data in (record.get("game_round") or {}).values():
            ticks.extend(round_data.get("action_ticks") or [])
        assert any(tick and tick[0] == "c" for tick in ticks), ticks
        assert after["action_count"] == 1

        restored = await session.restore(0)
        assert restored["action_count"] == 0
        assert restored["legal"]["seats"]["0"]["waiting"] is True
        assert 19 in restored["legal"]["seats"]["0"]["cut_tiles"]
    finally:
        await session.stop()
