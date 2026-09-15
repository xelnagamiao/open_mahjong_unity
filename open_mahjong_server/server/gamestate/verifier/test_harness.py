"""无头回归：契约、UnitySim 轨迹、turbo 自动跑、战鸣桥接。"""
from __future__ import annotations

import asyncio
import os
import sys

_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.gamestate.verifier.autoplay import autoplay, step_once
from server.gamestate.verifier.catalog import list_catalog
from server.gamestate.verifier.check_contract import check_lock
from server.gamestate.verifier.session import VerifierError, VerifierSession
from server.gamestate.verifier.tactical_bridge import run_presubmit
from server.gamestate.verifier.unity_sim import UnitySim
from server.gamestate.verifier.verdict import build_verdict


def test_contract_lock_matches_unity():
    assert check_lock(update=False) == 0


def test_unity_sim_ask_hand_updates_gsm_and_buttons():
    sim = UnitySim(viewer_user_id=101)
    sim.apply(
        {
            "type": "gamestate/guobiao/game_start",
            "game_info": {
                "room_rule": "guobiao",
                "tile_count": 83,
                "current_round": 1,
                "players_info": [
                    {
                        "user_id": 101,
                        "username": "P0",
                        "player_index": 0,
                        "score": 0,
                        "hand_tiles": [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24],
                        "hand_tiles_count": 13,
                        "discard_tiles": [],
                        "combination_tiles": [],
                    },
                    {"user_id": 102, "username": "P1", "player_index": 1, "score": 0, "hand_tiles_count": 13},
                    {"user_id": 103, "username": "P2", "player_index": 2, "score": 0, "hand_tiles_count": 13},
                    {"user_id": 104, "username": "P3", "player_index": 3, "score": 0, "hand_tiles_count": 13},
                ],
            },
        }
    )
    sim.apply(
        {
            "type": "gamestate/guobiao/broadcast_hand_action",
            "ask_hand_action_info": {
                "player_index": 0,
                "remain_tiles": 83,
                "remaining_time": 5,
                "action_tick": 1,
                "action_list": ["cut", "angang"],
            },
        }
    )
    gsm = sim.snapshot()["GsmSim"]
    assert gsm["IsSelfActionRequired"] is True
    assert "cut" in gsm["allowActionList"]
    buttons = sim.snapshot()["ActionButtonSim"]["buttons"]
    assert any(btn["text"] == "暗杠" for btn in buttons)
    assert not any("cut" in btn["actions"] for btn in buttons)


def test_tactical_bridge_shows_chi_then_peng():
    out = run_presubmit("peng")
    displays = [frame["components"]["ActionDisplaySim"] for frame in out["frames"]]
    assert any(item.get("self") == "吃" for item in displays)
    assert any(item.get("right") == "碰" for item in displays)
    assert out["frames"], "战鸣桥接应留下组件轨迹"
    verdict = build_verdict(
        kind="tactical",
        trace_meta=[{"index": i, "type": frame.get("type")} for i, frame in enumerate(out["frames"])],
        frames=out["frames"],
    )
    assert verdict["ok"] is True
    assert verdict["verdict"]["summary"] == "通过"


def test_catalog_groups_by_rule_with_comments():
    catalog = list_catalog(refresh=True)
    ids = [group["id"] for group in catalog["groups"]]
    assert "guobiao_tactical" in ids
    assert "guobiao_debug" in ids
    hongque = next((group for group in catalog["groups"] if group["id"] == "hongque"), None)
    assert hongque and hongque.get("files")
    tactical = next(group for group in catalog["groups"] if group["id"] == "guobiao_tactical")
    titles = [item["title"] for folder in tactical["files"] for item in folder["items"]]
    comments = [item.get("comment") or "" for folder in tactical["files"] for item in folder["items"]]
    assert any("预提交碰" in title or "抢吃" in title for title in titles)
    assert any("预提交更高动作" in text or "鸣牌保护开关" in text for text in titles + comments)


def test_qi_dui_turbo_trace_and_illegal_rejected():
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
            turbo=True,
        )
        assert session.trace, "开局应录到 Unity 组件帧"
        types = [frame.get("type") for frame in session.trace]
        assert any(item and item.endswith("game_start") for item in types)
        unity = session.sim.snapshot()
        assert "cut" in unity["GsmSim"]["allowActionList"]
        assert unity["GsmSim"]["IsSelfActionRequired"] is True

        try:
            await session.submit({"player_index": 0, "action_type": "peng"})
            raise AssertionError("非法 peng 应当被拒绝")
        except VerifierError as exc:
            assert "不允许" in str(exc) or "不会画出" in str(exc)

        after = await autoplay(session, max_steps=80, policy="simple")
        assert after["trace_len"] > len(types)
        assert after["ok"] is True
        assert after["verdict"]["summary"] == "通过"
        do_types = [item["type"] for item in after["trace_meta"] if item.get("type") and item["type"].endswith("do_action")]
        assert do_types, after["trace_meta"][:12]
        last = session.frame(-1)["components"]
        assert last["Game3DSim"]["self"]["river"] or last["GsmSim"]["selfHandTiles"]
    finally:
        await session.stop()


def test_step_once_advances_without_finishing():
    asyncio.run(_step_once_flow())


async def _step_once_flow() -> None:
    session = VerifierSession("steponce")
    try:
        await session.start(
            seed=1,
            debug_scenario="qi_dui_tenpai_1m",
            tactical_call=False,
            game_round=1,
            hepai_limit=1,
            turbo=True,
        )
        before = len(session.trace)
        after = await step_once(session, policy="simple")
        assert after["trace_len"] >= before
        assert after.get("step_actions")
        assert not session.ended()
    finally:
        await session.stop()
