"""双立直从首张宣告、客户端广播和牌谱到和牌计分的回归测试。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from server.game_calculation.riichi.riichi_hepai_check import Riichi_Hepai_Check
from server.game_calculation.riichi.riichi_tingpai_check import Riichi_Tingpai_Check
from .RiichiGameState import RiichiGameState
from .action_check import check_hepai
from .wait_action import _clear_ippatsu, _commit_pending_riichi, _execute_cut, wait_action


WAITING_HAND = [11, 12, 13, 24, 25, 26, 37, 38, 39, 14, 15, 22, 22]


def make_game(seat):
    websocket = SimpleNamespace(send_json=AsyncMock())
    server = SimpleNamespace(user_id_to_connection={100 + seat: SimpleNamespace(websocket=websocket)})
    calculation = SimpleNamespace(
        Riichi_hepai_check=Riichi_Hepai_Check().hepai_check,
        Riichi_tingpai_check=Riichi_Tingpai_Check().tingpai_check,
    )
    game = RiichiGameState(server, {
        "player_list": [100, 101, 102, 103],
        "room_id": "double-riichi-test", "room_rule": "riichi", "room_type": "custom",
        "round_timer": 20, "step_timer": 5, "game_round": 1, "tips": False,
        "allow_spectator": False,
    }, calculation, None, "double-riichi-test")
    for index, player in enumerate(game.player_list):
        player.player_index = index
        player.original_player_index = index
        if index < seat:
            player.discard_tiles = [45]
            player.discard_origin_tiles = [45]
            player.discard_riichi_flags = [False]
    game.current_player_index = seat
    # 现有巡目在庄家第一次切牌后就从 1 增至 2。
    game.xunmu = 1 if seat == 0 else 2
    game.tiles_list = [11] * 60
    game.game_record = {"game_round": {"round_index_1": {"action_ticks": []}}}
    game.player_list[seat].hand_tiles = WAITING_HAND + [47]
    game.player_list[seat].has_draw_slot = True
    return game, websocket


def declare(game, *, is_riichi=True):
    assert asyncio.run(_execute_cut(game, game.current_player_index, 47, True, None, is_riichi))


@pytest.mark.parametrize("seat", range(4))
def test_first_discard_declares_double_riichi_for_every_seat(seat):
    game, websocket = make_game(seat)
    declare(game)

    player = game.player_list[seat]
    assert {"riichi", "daburu_riichi", "ippatsu"} <= set(player.tag_list)
    assert player.score == 24000
    assert game.riichi_sticks == 1
    assert player.discard_riichi_flags == [True]
    assert game.game_record["game_round"]["round_index_1"]["action_ticks"] == [
        ["c", 47, "T", "H"], ["riichi", seat, 1],
    ]
    declaration = next(call.args[0] for call in websocket.send_json.call_args_list
                       if call.args[0]["type"] == "gamestate/riichi/declare_riichi")
    assert "daburu_riichi" in declaration["refresh_player_tag_list_info"]["player_to_tag_list"][seat]
    _commit_pending_riichi(game)
    assert player.score == 24000
    assert game.riichi_sticks == 1


@pytest.mark.parametrize("seat", range(4))
def test_second_discard_is_only_normal_riichi(seat):
    game, _ = make_game(seat)
    declare(game, is_riichi=False)
    assert "daburu_riichi" not in game.player_list[seat].tag_list
    game.xunmu += 1
    game.player_list[seat].hand_tiles.append(47)
    declare(game)
    assert "riichi" in game.player_list[seat].tag_list
    assert "daburu_riichi" not in game.player_list[seat].tag_list
    assert game.game_record["game_round"]["round_index_1"]["action_ticks"][-1] == ["riichi", seat, 0]


@pytest.mark.parametrize("caller, meld", [(0, "s12"), (0, "k41"), (0, "g41"), (0, "G41"), (1, "G41")])
def test_call_before_first_discard_prevents_double_riichi(caller, meld):
    game, _ = make_game(1)
    game.player_list[caller].combination_tiles = [meld]
    if caller == 1:
        game.player_list[caller].hand_tiles = [11, 12, 13, 24, 25, 26, 14, 15, 22, 22, 47]
    declare(game)
    assert "riichi" in game.player_list[1].tag_list
    assert "daburu_riichi" not in game.player_list[1].tag_list
    assert game.game_record["game_round"]["round_index_1"]["action_ticks"][-1] == ["riichi", 1, 0]


def test_claimed_first_discard_does_not_restore_double_riichi_chance():
    game, _ = make_game(1)
    player = game.player_list[1]
    player.discard_origin_tiles = [45]
    player.discard_tiles = []
    game.player_list[2].combination_tiles = ["k45"]
    declare(game)
    assert "daburu_riichi" not in player.tag_list


def test_another_double_riichi_declaration_does_not_interrupt_first_turn():
    game, _ = make_game(0)
    declare(game)
    game.current_player_index = 1
    game.player_list[1].hand_tiles = WAITING_HAND + [47]
    declare(game)
    assert all("daburu_riichi" in player.tag_list for player in game.player_list[:2])
    assert game.riichi_sticks == 2


def test_call_on_declaration_discard_preserves_double_riichi_but_cancels_ippatsu():
    game, _ = make_game(1)
    game.player_list[2].hand_tiles = [47, 47, 11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45]
    declare(game)
    player = game.player_list[1]
    assert player.pending_riichi
    assert player.score == 25000
    assert not any(t[0] == 'riichi' for t in game.game_record['game_round']['round_index_1']['action_ticks'])

    async def call_and_discard():
        waiter = asyncio.create_task(wait_action(game))
        await asyncio.sleep(0)  # 等待入口先清空旧队列，再提交本次鸣牌。
        await game.action_queues[2].put({"action_type": "peng"})
        game.action_events[2].set()
        await asyncio.wait_for(waiter, timeout=2)
        assert game.current_player_index == 2
        assert game.game_status == "onlycut_after_action"
        assert await _execute_cut(game, 2, 45, False, None, False)

    asyncio.run(call_and_discard())
    assert player.discard_tiles == []
    assert player.discard_origin_tiles == [47]
    assert "daburu_riichi" in player.tag_list
    assert "ippatsu" not in player.tag_list
    assert player.score == 24000
    assert game.riichi_sticks == 1
    actions = {i: [] for i in range(4)}
    check_hepai(game, actions, 16, 1, "ron")
    result = game.result_dict[actions[1][0]]
    assert "双立直" in result["yaku"]
    assert result["han"] == 3


def test_double_riichi_still_counts_ura_dora():
    game, _ = make_game(1)
    declare(game)
    _clear_ippatsu(game)
    game.ura_dora_indicators = [21]  # 手牌中的两张 22 各算一番。
    game.current_player_index = 2
    actions = {i: [] for i in range(4)}
    check_hepai(game, actions, 16, 1, "ron")
    result = game.result_dict[actions[1][0]]
    assert "双立直" in result["yaku"]
    assert "里宝牌*2" in result["yaku"]
    assert result["han"] == 5


@pytest.mark.parametrize("seat", range(4))
@pytest.mark.parametrize("win_type", ["ron", "tsumo"])
@pytest.mark.parametrize("ippatsu", [False, True])
def test_double_riichi_scores_two_han_without_counting_normal_riichi(seat, win_type, ippatsu):
    game, _ = make_game(seat)
    declare(game)
    if not ippatsu:
        _clear_ippatsu(game)
    player = game.player_list[seat]
    if win_type == "tsumo":
        player.hand_tiles.append(16)
    else:
        game.current_player_index = (seat + 1) % 4
    actions = {i: [] for i in range(4)}
    check_hepai(game, actions, 16, seat, win_type)
    result = game.result_dict[actions[seat][0]]
    assert "双立直" in result["yaku"]
    assert "立直" not in result["yaku"]
    assert result["han"] == 3 + int(ippatsu) + int(win_type == "tsumo")  # 双立直 + 平和

    player.tag_list.remove("daburu_riichi")
    normal_actions = {i: [] for i in range(4)}
    check_hepai(game, normal_actions, 16, seat, win_type)
    normal = game.result_dict[normal_actions[seat][0]]
    assert "立直" in normal["yaku"]
    assert normal["han"] == result["han"] - 1
