"""真实回合执行器、响应队列和完整四副牌生命周期；仅传输/数据库为内存替身。"""

import asyncio
import random
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from .test_state import make_state, hand, draw, ticks, HAND, MODULE
from ...game_calculation.guangdong.config import wall_tiles, SUB_RULE


class ActionSocket:
    def __init__(self, state, user_id):
        self.state, self.user_id = state, user_id
        self.messages = []

    async def send_json(self, data):
        self.messages.append(deepcopy(data))
        state = self.state
        player = next(p for p in state.player_list if p.user_id == self.user_id)
        index = player.player_index
        if data["type"].endswith("broadcast_hand_action"):
            allowed = data["ask_hand_action_info"]["action_list"]
            if "hu_self" in allowed:
                action = {"action_type": "hu_self"}
            elif "cut" in allowed:
                action = {"action_type": "cut", "TileId": player.hand_tiles[-1],
                          "cutIndex": len(player.hand_tiles) - 1, "cutClass": True}
            else:
                return
        elif data["type"].endswith("ask_other_action"):
            allowed = data["ask_other_action_info"]["action_list"]
            selected = next((a for a in allowed if a.startswith("hu_")), "pass")
            if selected not in allowed:
                return
            action = {"action_type": selected}
        else:
            return
        await state.action_queues[index].put(action)
        state.action_events[index].set()


def test_four_round_match_queue_actions_winner_rotation_record_and_cleanup():
    state = make_state(random_seed=20231002)
    starts, stored = [], []
    for p in state.player_list:
        state.game_server.user_id_to_connection[p.user_id] = SimpleNamespace(websocket=ActionSocket(state, p.user_id))
    state.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
    state.db_manager = SimpleNamespace(store_guangdong_game_record=lambda *args: stored.append(deepcopy(args[0])) or 903)

    def deal_fixture():
        starts.append([p.original_player_index for p in state.player_list])
        supply = Counter(wall_tiles())
        if state.current_round == 1:
            # 前三刻+东刻，单钓九筒；庄家首切后由上家点和坐庄。
            target = HAND[:-1] + [29]
            hand(state, 3, target)
            supply.subtract(target)
            supply[29] -= 1
            owner = 3
        else:
            target = HAND + [45]
            hand(state, 0, target)
            supply.subtract(target)
            owner = 0
        remaining = list(supply.elements())
        random.Random(2023 + state.current_round).shuffle(remaining)
        for index in range(4):
            if index == owner:
                continue
            cards = [remaining.pop() for _ in range(13)]
            if index == 0:
                cards.append(29)
            hand(state, index, cards)
        state.tiles_list = remaining

    state.init_tiles = deal_fixture
    # 不等待结算展示动画；和牌动作仍由真实响应队列和 _run_hand 处理。
    with patch.object(state, "run_hu_result_ready_phase", new=AsyncMock()):
        asyncio.run(asyncio.wait_for(state.game_loop_chinese(), timeout=20))
    assert len(stored) == 1 and len(stored[0]["game_round"]) == 4
    assert stored[0]["game_title"]["sub_rule"] == SUB_RULE
    assert starts[1][0] == starts[0][3]
    assert all(order == starts[1] for order in starts[1:])
    assert sum(p.score for p in state.player_list) == 0
    assert all(len(p.score_history) == 4 for p in state.player_list)
    assert state.game_server.gamestate_manager.cleanup_game_state_complete.await_count == 1
    for conn in state.game_server.user_id_to_connection.values():
        types = [message["type"].rsplit("/", 1)[-1] for message in conn.websocket.messages]
        assert types.count("game_start") == 4 and types.count("show_result") == 4 and types[-1] == "game_end"


def test_direct_kong_replacement_win_and_chained_concealed_kong_change_payer():
    async def run(chain):
        state = make_state(); hand(state, 0, []).discard_tiles = [11]
        p = hand(state, 1, HAND)
        await state.execute_claim(1, "gang")
        state.tiles_list = [12, 26, 43, 55, 45 if not chain else 22]
        await state._deal_supplement()
        assert state.direct_kong_payer == 0 and state.last_draw_after_kong
        if chain:
            await state.execute_angang(1, 22)
            assert state.direct_kong_payer is None
            state.tiles_list.append(45)
            await state._deal_supplement()
        state.accept_self_draw(1)
        item = state.pending_winners[0]
        assert item["liable_payer"] == (None if chain else 0)
        base = item["detail"]["base_score"]
        state.current_round = 4
        await state._settle_hand({i: 0 for i in range(4)})
        horse_count = 2  # 闲家1中二万、六筒。
        if chain:
            assert [p.score for p in state.player_list] == [-5-base-horse_count, 9+3*(base+horse_count), -2-base-horse_count, -2-base-horse_count]
        else:
            assert [p.score for p in state.player_list] == [-3-3*(base+horse_count), 3+3*(base+horse_count), 0, 0]
    asyncio.run(run(False)); asyncio.run(run(True))


def test_normal_and_supplement_draw_clear_pass_and_take_different_wall_ends():
    async def run():
        state = make_state()
        p = hand(state, 1, HAND)
        p.passed_base_score = 40
        state.tiles_list = [55, 56]
        await state._deal_normal()
        assert p.hand_tiles[-1] == 55 and state.tiles_list == [56] and p.passed_base_score == -1
        assert p.normal_draw_count == 1 and "buhua" not in state.action_dict[1]
        state.tiles_list = [57, 58]
        p.hand_tiles.pop()
        p.passed_base_score = 40
        state.next_supplement_kind = "angang"
        await state._deal_supplement()
        assert p.hand_tiles[-1] == 58 and state.tiles_list == [57] and p.passed_base_score == -1
        assert state.last_draw_after_kong and not p.huapai_list
    asyncio.run(run())


def test_last_discard_no_win_reaches_exhaustive_draw_and_has_no_false_ready():
    async def run():
        state = make_state(step_timer=0, round_timer=0)
        hand(state, 0, [11, 12, 14, 17, 21, 22, 24, 27, 31, 32, 34, 37, 41, 55])
        for i in range(1, 4):
            hand(state, i, [11, 12, 14, 17, 21, 22, 24, 27, 31, 32, 34, 37, 41])
        state.tiles_list = []
        await state._run_hand()
        assert state.game_status == "END" and not state.pending_winners
        assert state.player_list[0].discard_tiles[-1] == 55
        assert state.player_list[0].discarded_ghosts == 1
        assert not state.ready_candidate_cuts(0)
    asyncio.run(asyncio.wait_for(run(), timeout=5))
