import asyncio

import pytest

from server.gamestate.public.tactical_claim import (
    apply_tactical_claim_if_needed,
    get_higher_priority_snapshot,
    tactical_grace_phase,
    tactical_mark_player_force_passed,
)
from server.gamestate.game_guobiao.wait_action import select_tactical_initial_submission
from server.gamestate.public.ai.get_action import get_ai_action


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


def test_simultaneous_chi_and_peng_starts_with_chi_application():
    """同一事件循环批次内吃碰同时到达，也应先展示吃，再让碰进入抢断窗口。"""
    gs = _FakeGameState()
    gs.current_player_index = 0
    gs.action_priority = {"pass": 0, "chi_left": 1, "peng": 2}
    submissions = [
        (2, {"action_type": "peng"}),
        (1, {"action_type": "chi_left"}),
    ]

    assert select_tactical_initial_submission(gs, submissions) == submissions[1]


def test_claimant_cannot_interrupt_own_chi_with_peng():
    gs = _FakeGameState()
    gs.tactical_commit_lock = True
    gs.action_priority = {"pass": 0, "chi_left": 1, "peng": 2}
    gs._tactical_action_snapshot = {
        0: [],
        1: ["chi_left", "peng"],
        2: [],
        3: [],
    }
    gs._tactical_passed_players = set()
    gs._tactical_committed_players = {1}

    higher, any_higher = get_higher_priority_snapshot(gs, "chi_left", 1)

    assert any_higher is False
    assert higher == {0: [], 1: [], 2: [], 3: []}


def test_force_pass_excludes_peng_from_chi_recheck():
    """主询问 force_pass 后，吃牌申请不再询问该座碰牌。"""
    gs = _FakeGameState()
    gs.action_priority = {"pass": 0, "force_pass": 0, "chi_left": 1, "peng": 2}
    gs._tactical_action_snapshot = {
        0: [],
        1: ["chi_left", "pass", "force_pass"],
        2: ["peng", "pass", "force_pass"],
        3: [],
    }
    gs._tactical_passed_players = set()
    gs._tactical_force_passed_players = set()
    gs._tactical_committed_players = set()
    tactical_mark_player_force_passed(gs, 2)

    higher, any_higher = get_higher_priority_snapshot(gs, "chi_left", 1)

    assert any_higher is False
    assert higher == {0: [], 1: [], 2: [], 3: []}


def test_pass_still_allows_peng_recheck_after_chi():
    """主询问普通 pass 不排除重问；快照回拷 pass/force_pass 后缀。"""
    gs = _FakeGameState()
    gs.action_priority = {"pass": 0, "force_pass": 0, "chi_left": 1, "peng": 2}
    gs._tactical_action_snapshot = {
        0: [],
        1: ["chi_left", "pass", "force_pass"],
        2: ["peng", "pass", "force_pass"],
        3: [],
    }
    gs._tactical_passed_players = set()
    gs._tactical_force_passed_players = set()
    gs._tactical_committed_players = set()

    higher, any_higher = get_higher_priority_snapshot(gs, "chi_left", 1)

    assert any_higher is True
    assert higher[2] == ["peng", "pass", "force_pass"]


def test_committed_bot_cannot_queue_a_second_claim():
    gs = _FakeGameState()
    gs.game_status = "waiting_action_after_cut"
    gs.current_player_index = 0
    gs.tactical_commit_lock = True
    gs._tactical_committed_players = {1}
    gs.waiting_players_list = [1]
    gs.action_dict = {0: [], 1: ["peng"], 2: [], 3: []}
    gs.player_list = [type("Player", (), {})() for _ in range(4)]
    gs.action_queues = {index: asyncio.Queue() for index in range(4)}
    gs.action_events = {index: asyncio.Event() for index in range(4)}

    _run(get_ai_action(gs, 1, "peng", False, 0, 0, 0))

    assert gs.action_queues[1].empty()
    assert not gs.action_events[1].is_set()
