"""四局真实状态机、轮庄、逐局提示与 JSON 牌谱持久化回归。

仅替换发牌位置、I/O 存储介质和展示等待；真实摸打、动作队列、共用
癞子核心、自摸、结算、记录及结束流程均运行正式实现。
"""
import asyncio
import json
import random
from collections import Counter
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_flow import HAND, draw, hand, make_state
from ...game_calculation.hongzhong import rules as book
from ..public.ai.get_action import get_ai_action
from ..public.game_record_manager import jsonable_game_record

STATE = "server.gamestate.game_hongzhong.HongzhongGameState"


def round_fixture(state):
    """前3局分别由座位0/1/2赢，末局海底流局；112张始终守恒。"""
    winner = state.round_index - 1
    pool = Counter({tile: 4 for tile in book.TILES})
    for player in state.player_list:
        player.hand_tiles.clear()
    if winner < 3:
        state.player_list[winner].hand_tiles = HAND + ([45] if winner == 0 else [])
        pool.subtract(state.player_list[winner].hand_tiles)
        front = [19] * max(0, winner - 1) + ([45] if winner else []) + [21, 39]
    else:
        front = [45]
    pool.subtract(front)
    remaining = list(pool.elements())
    random.Random(8917 + state.round_index).shuffle(remaining)
    for i, player in enumerate(state.player_list):
        target = 14 if i == 0 else 13
        need = target - len(player.hand_tiles)
        player.hand_tiles.extend(remaining[:need])
        del remaining[:need]
    # Rebuild the unused inventory from complete dealt hands to keep all 112
    # physical tiles, including four red centers, accounted for.
    rest = Counter({tile: 4 for tile in book.TILES})
    for player in state.player_list: rest.subtract(player.hand_tiles)
    rest.subtract(front)
    assert min(rest.values()) >= 0
    state.tiles_list = list(front) + list(rest.elements())
    if winner == 3:
        discarded = state.tiles_list[1:]
        state.tiles_list = state.tiles_list[:1]
        for i, tile in enumerate(discarded):
            state.player_list[i % 4].discard_tiles.append(tile)
            state.player_list[i % 4].discard_origin_tiles.append(tile)
    actual = Counter(state.tiles_list)
    for player in state.player_list: actual.update(player.hand_tiles + player.discard_tiles)
    assert actual == Counter({tile: 4 for tile in book.TILES})


@pytest.mark.parametrize("storage", ["file", "local_only"])
def test_complete_four_round_match_and_persisted_replay(storage, tmp_path):
    async def scenario():
        state = make_state(room_id=987002, random_seed=2024)
        state.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
        state.spectator_manager.send_final_record_and_close = AsyncMock()
        state.run_hu_result_ready_phase = AsyncMock()
        path = tmp_path / "hongzhong-record.json"
        def store(record, players, room_type, match_type):
            assert room_type == "custom" and match_type == "1/4"
            assert len(players) == 4
            path.write_text(json.dumps(jsonable_game_record(record), ensure_ascii=False), encoding="utf-8")
            return "HZ-UNIT-FILE"
        state.db_manager = SimpleNamespace(store_hongzhong_game_record=store) if storage == "file" else None
        state.init_tiles = lambda: round_fixture(state)
        original_wait = state.wait_action
        action_counts = Counter()
        async def queued_player_decisions():
            state.waiting_players_list = [i for i, actions in state.action_dict.items() if actions]
            for index in state.waiting_players_list:
                actions = state.action_dict[index]
                winner = state.round_index - 1
                action = "hu_self" if index == winner < 3 and "hu_self" in actions else "cut" if "cut" in actions else "pass"
                player = state.player_list[index]
                tile = player.hand_tiles[-1] if action == "cut" else None
                action_counts[action] += 1
                await get_ai_action(state, index, action, player.has_draw_slot if action == "cut" else None,
                                    tile, len(player.hand_tiles)-1 if action == "cut" else None, None)
            await original_wait()
        state.wait_action = queued_player_decisions
        with patch(STATE + ".liuju_ready_wait_seconds", return_value=0):
            await state.game_loop_chinese()
        assert state.current_round == 4 and state.round_index == 4
        assert action_counts["hu_self"] == 3 and action_counts["cut"] >= 4
        assert sum(p.score for p in state.player_list) == 0
        assert all(len(p.score_history) == 4 for p in state.player_list)
        assert all(p.record_counter.round_score_total == p.score for p in state.player_list)
        rounds = state.game_record["game_round"]
        assert list(rounds) == [f"round_index_{i}" for i in range(1, 5)]
        # First winner retains dealer, second winner rotates seat1, third seat2.
        assert [rounds[f"round_index_{i}"]["seats"] for i in range(1,5)] == [
            [0,1,2,3], [0,1,2,3], [3,0,1,2], [1,2,3,0]]
        for number, round_data in enumerate(rounds.values(), 1):
            assert round_data["detailed_config"] == book.CONFIG
            assert round_data["action_ticks"][-1] == ["end"]
            initial = round_data["action_ticks"][:4]
            assert [t[:3] for t in initial] == [["hongzhong", "hints", seat] for seat in range(4)]
            for seat, tick in enumerate(initial):
                assert tick[3]["source_hand_tiles"] == round_data[f"p{seat}_tiles"]
            birds = [t for t in round_data["action_ticks"] if t[:2] == ["hongzhong", "birds"]]
            assert len(birds) == (number < 4)
        detail = state._local_record_detail
        assert detail.rule == "hongzhong" and detail.sub_rule == book.SUB_RULE
        assert detail.cloud_saved is (storage == "file")
        if storage == "file":
            loaded = json.loads(path.read_text(encoding="utf-8"))
            assert loaded == detail.record
        assert detail.record["game_title"]["master_seed_hex"] == f"{2024:064x}"
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(gamestate_id=state.gamestate_id)
        state.spectator_manager.send_final_record_and_close.assert_awaited_once()
        assert state.run_hu_result_ready_phase.await_count == 3
    asyncio.run(scenario())


def test_kong_then_win_reports_round_net_in_original_seats():
    async def scenario():
        state = make_state()
        for p in state.player_list:
            p.original_player_index = (p.player_index + 2) % 4
            p.score = 100 + p.original_player_index
        before = {p.original_player_index: p.score for p in state.player_list}
        player = hand(state, 1, HAND); draw(player, 45)
        await state._pay_kong(1, "direct", 19, payer=0)
        detail = state.score_candidate(1, "self_draw")
        state.pending_winners = [{"index": 1, "detail": detail, "tile": 45}]
        state.tiles_list = [21,39,22]
        state.current_round = 4
        assert await state._settle_hand(before)
        info = state._terminal_result["show_result_info"]["hongzhong_info"]
        expected = {p.original_player_index: p.score - before[p.original_player_index] for p in state.player_list}
        assert info["round_score_changes"] == expected and sum(expected.values()) == 0
        assert info["start_scores"] == before and state.tiles_list == [22]
        assert [int(p.score_history[-1]) for p in state.player_list] == [expected[p.original_player_index] for p in state.player_list]
    asyncio.run(scenario())
