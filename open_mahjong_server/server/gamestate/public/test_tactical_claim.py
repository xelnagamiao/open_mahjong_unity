import asyncio

import pytest

from server.gamestate.public.tactical_claim import (
    apply_tactical_claim_if_needed,
    tactical_grace_phase,
)


class _FakeGameState:
    pass


def _run(coro):
    return asyncio.run(coro)


def _make_grace_state(replacement_action):
    gs = _FakeGameState()
    gs.tactical_grace_seconds = 0.01
    gs.action_priority = {
        "pass": 0,
        "chi_left": 1,
        "peng": 2,
        "gang": 2,
        "hu": 3,
    }
    gs._tactical_action_snapshot = {
        0: ["chi_left"],
        1: [replacement_action],
        2: [],
        3: [],
    }
    gs._tactical_passed_players = set()
    gs._tactical_committed_players = set()
    gs.action_dict = {
        0: ["chi_left"],
        1: [replacement_action],
        2: [],
        3: [],
    }
    gs.action_events = {index: asyncio.Event() for index in range(4)}
    gs.action_queues = {index: asyncio.Queue() for index in range(4)}
    return gs


@pytest.mark.parametrize("replacement_action", ["peng", "gang", "hu"])
def test_pre_submitted_higher_action_gets_its_own_claim_broadcast(replacement_action):
    """A 已广播吃后，B 预提交更高动作时，最终动作不能继承 A 的跳过标记。"""
    gs = _make_grace_state(replacement_action)
    _run(gs.action_queues[1].put({"action_type": replacement_action}))
    sent = []

    async def broadcast_do_action(*args, **kwargs):
        sent.append(kwargs)

    async def broadcast_ask_other_action(*args, **kwargs):
        raise AssertionError("预提交抢断后不应再次询问")

    result = _run(
        tactical_grace_phase(
            gs,
            "chi_left",
            0,
            {"action_type": "chi_left"},
            33,
            broadcast_do_action=broadcast_do_action,
            broadcast_ask_other_action=broadcast_ask_other_action,
            initial_claim_broadcasted=True,
        )
    )

    assert result[0:2] == (replacement_action, 1)
    assert result[3] is True
    assert sent == [{
        "action_list": [replacement_action],
        "action_player": 1,
        "cut_tile": 33,
        "is_claim": True,
    }]


@pytest.mark.parametrize("tactical_call", [False, True])
@pytest.mark.parametrize("claim_protection", [False, True])
def test_tactical_and_claim_protection_switches(tactical_call, claim_protection):
    """鸣牌保护开关不应改变战术申请是否广播；两者是正交配置。"""
    gs = _FakeGameState()
    gs.tactical_call = tactical_call
    gs.claim_protection = claim_protection
    gs.game_status = "waiting_action_after_cut"
    gs.tactical_pre_grace_delay = 0
    gs.action_priority = {"pass": 0, "hu": 3}
    gs._tactical_action_snapshot = {0: ["hu"], 1: [], 2: [], 3: []}
    gs._tactical_passed_players = set()
    gs.player_list = [type("Player", (), {"discard_tiles": [33]})() for _ in range(4)]
    gs.current_player_index = 0
    sent = []

    async def broadcast_do_action(*args, **kwargs):
        sent.append(kwargs)

    async def broadcast_ask_other_action(*args, **kwargs):
        pass

    result = _run(
        apply_tactical_claim_if_needed(
            gs,
            "hu",
            0,
            {"action_type": "hu"},
            broadcast_do_action=broadcast_do_action,
            broadcast_ask_other_action=broadcast_ask_other_action,
        )
    )

    assert bool(sent) is tactical_call
    assert result[3] is tactical_call
    if tactical_call:
        assert sent[0]["action_list"] == ["hu"]
        assert sent[0]["action_player"] == 0
        assert sent[0]["is_claim"] is True

