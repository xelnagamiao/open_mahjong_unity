"""战鸣规则的真实动作入口和等待循环：取消可再问，放弃退出本张弃牌。"""
import asyncio
import importlib

import pytest

from .ai.get_action import get_action, get_ai_action
from .game_record_manager import init_game_record, init_game_round
from .lifecycle import cancel_auxiliary_tasks
from .tactical_claim import get_higher_priority_snapshot, init_tactical_round_state
from ..verifier.host import build_db_manager, build_game_server, build_room_data


RULES = [
    ("guobiao", "GuobiaoGameState", "guobiao/standard"),
    ("mmcr", "QingqueGameState", "qingque/standard"),
    ("changsha", "ChangshaGameState", "changsha/standard"),
    ("sichuan", "SichuanGameState", "sichuan/standard"),
    ("sichuan", "XueliuGameState", "sichuan/xueliu"),
    ("sichuan", "XueliuGameState", "sichuan/xueliu_exchange"),
    ("guobiao", "GuobiaoGameState", "guobiao/sanma"),
]


def make_state(rule, tactical=True):
    family, class_name, sub_rule = rule
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=20261005, room_id="force-pass-rules", tactical_call=tactical, hepai_limit=0)
    room.update(room_rule="qingque" if family == "mmcr" else family, sub_rule=sub_rule, step_timer=2, round_timer=2)
    if sub_rule == "guobiao/sanma":
        room["player_list"] = room["player_list"][:3]
    cls = getattr(importlib.import_module(f"server.gamestate.game_{family}.{class_name}"), class_name)
    state = cls(server, room, server.calculation_service, db, "force-pass-rules")
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
        player.hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 14, 14, 15, 15]
        player.waiting_tiles = set()
        player.dingque_suit = 3
    state.current_player_index = 0
    state.player_list[0].discard_tiles = [15]
    state.tiles_list = [19, 29, 39] * 10
    init_game_record(state)
    init_game_round(state)
    return state


@pytest.mark.parametrize("rule", RULES, ids=[item[2] for item in RULES])
@pytest.mark.parametrize("tactical", [False, True])
@pytest.mark.parametrize("phase", ["after_cut", "jiagang"])
def test_every_claim_window_offers_force_pass_only_when_tactical(rule, tactical, phase, monkeypatch):
    state = make_state(rule, tactical)
    module = importlib.import_module(f"server.gamestate.game_{rule[0]}.action_check")
    # 和牌成立条件由各规则的独立牌型测试覆盖；这里验证真实询问生成的协议。
    def winning_player(gs, actions, tile, seat, *args, **kwargs):
        actions[seat].append("hu" if rule[0] == "sichuan" else "hu_first")
    monkeypatch.setattr(module, "check_hepai", winning_player)
    monkeypatch.setattr(module, "refresh_waiting_tiles", lambda *args, **kwargs: None)
    state.player_list[1].waiting_tiles = {15}
    actions = getattr(module, "check_action_" + phase)(state, 15)
    assert actions[0] == []
    assert actions[1]
    for offered in actions.values():
        if not offered:
            continue
        assert offered.count("force_pass") == int(tactical)
        assert offered[-2:] == ["pass", "force_pass"] if tactical else offered[-1:] == ["pass"]


@pytest.mark.parametrize("rule", RULES, ids=[item[2] for item in RULES])
def test_force_pass_is_not_offered_for_own_draw_or_flower_replacement(rule):
    state = make_state(rule)
    module = importlib.import_module(f"server.gamestate.game_{rule[0]}.action_check")
    state.player_list[0].hand_tiles = [11] * 4 + [12, 13, 21, 22, 23, 31, 32, 33, 41, 41]
    for actions in module.check_action_hand_action(state, 0).values():
        assert "force_pass" not in actions
    if hasattr(module, "check_action_buhua"):
        state.player_list[0].hand_tiles.append(51)
        for actions in module.check_action_buhua(state, 0).values():
            assert "force_pass" not in actions


@pytest.mark.parametrize("rule", RULES, ids=[item[2] for item in RULES])
@pytest.mark.parametrize("phase", ["waiting_action_after_cut", "waiting_action_qianggang"])
@pytest.mark.parametrize("source", ["human", "ai"])
@pytest.mark.parametrize("decline", ["pass", "force_pass"])
def test_decline_waits_for_other_players_and_advances_without_executing_a_claim(rule, phase, source, decline):
    async def exercise():
        state = make_state(rule)
        state.game_status = phase
        state.jiagang_tile = 15
        state.action_dict = {0: [], 1: ["peng", "pass", "force_pass"], 2: ["peng", "pass", "force_pass"], 3: []}
        module = importlib.import_module(f"server.gamestate.game_{rule[0]}.wait_action")
        waiter = asyncio.create_task(module.wait_action(state))
        try:
            for _ in range(100):
                if state.waiting_players_list == [1, 2]:
                    break
                await asyncio.sleep(0)
            assert state.waiting_players_list == [1, 2]
            if source == "human":
                await get_action(state, "conn-102", decline, None, None, None, 0,
                                 action_tick=state.server_action_tick)
            else:
                await get_ai_action(state, 1, decline, None, None, None, 0)
            for _ in range(20):
                if 1 not in state.waiting_players_list:
                    break
                await asyncio.sleep(0)
            assert not waiter.done(), "放弃不能抢先结束其他玩家的回应"
            await get_action(state, "conn-103", "pass", None, None, None, 0,
                             action_tick=state.server_action_tick)
            await asyncio.wait_for(waiter, 1)
            assert state.game_status == ("deal_card_after_gang" if phase.endswith("qianggang") else "deal_card")
            assert all(not player.combination_tiles for player in state.player_list)
        finally:
            if not waiter.done():
                waiter.cancel()
                await asyncio.gather(waiter, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(exercise())


@pytest.mark.parametrize("rule", RULES, ids=[item[2] for item in RULES])
@pytest.mark.parametrize("decline", ["pass", "force_pass"])
def test_human_decline_recheck_and_next_discard_reset(rule, decline):
    async def exercise():
        state = make_state(rule)
        state.game_status = "waiting_action_after_cut"
        ron = "hu" if rule[0] == "sichuan" else "hu_first"
        state.action_dict = {0: [], 1: ["peng", "pass", "force_pass"], 2: [ron, "pass", "force_pass"], 3: []}
        init_tactical_round_state(state)
        state.waiting_players_list = [1, 2]
        try:
            await get_action(state, "conn-103", decline, None, None, None, 0,
                             action_tick=state.server_action_tick)
            higher, _ = get_higher_priority_snapshot(state, "peng", 1)
            assert bool(higher.get(2)) is (decline == "pass")
            # 下一张弃牌重建窗口，退出决定必须清空。
            init_tactical_round_state(state)
            higher, _ = get_higher_priority_snapshot(state, "peng", 1)
            assert higher[2] == [ron, "pass", "force_pass"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(exercise())


@pytest.mark.parametrize("tactical", [False, True])
def test_changsha_batch_kong_priority_filter_preserves_give_up(tactical):
    state = make_state(RULES[2], tactical)
    module = importlib.import_module("server.gamestate.game_changsha.action_check")
    actions = {0: [], 1: ["hu_first", "pass", "force_pass"], 2: ["peng", "pass", "force_pass"], 3: []}
    highest = module._max_claim_priority(state, actions)
    filtered = module._filter_action_dict_to_priority(state, actions, highest)
    assert filtered[1] == ["hu_first", "pass", "force_pass"] if tactical else filtered[1] == ["hu_first", "pass"]
    assert filtered[2] == []
    assert not any(module._filter_action_dict_to_priority(state, {i: ["pass", "force_pass"] for i in range(4)}, 0).values())
