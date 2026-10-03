"""真实算番检查川麻起和边界、情境番、血流仅自摸、查叫与协议。"""
import pytest

from .SichuanGameState import SichuanGameState
from .XueliuGameState import XueliuGameState
from .action_check import check_hepai, refresh_waiting_tiles
from .boardcast import _base_game_info
from ..public.game_record_manager import init_game_record
from ..verifier.host import build_db_manager, build_game_server, build_room_data


STANDARD = [11, 12, 13, 14, 15, 16, 21, 22, 23, 26, 27, 28, 25, 25]
XUELIU = [11, 12, 13, 14, 15, 16, 21, 22, 23, 25, 25]
SUB_RULES = ("sichuan/standard", "sichuan/xueliu", "sichuan/xueliu_exchange")


def state(sub_rule="sichuan/standard", limit=0):
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=20261003, room_id="hepai-limit", hepai_limit=limit)
    room.update(room_rule="sichuan", sub_rule=sub_rule)
    cls = SichuanGameState if sub_rule == "sichuan/standard" else XueliuGameState
    result = cls(server, room, server.calculation_service, db, "hepai-limit")
    result.tiles_list = [19, 29, 39] * 3
    for i, player in enumerate(result.player_list):
        player.player_index = i
        player.dingque_suit = 3
    return result


def actions(game, hand, kind="dianhe", **kwargs):
    game.player_list[0].hand_tiles = list(hand if kind == "handgot" else hand[:-1])
    game.sichuan_hu_results = {}
    result = {i: [] for i in range(4)}
    check_hepai(game, result, hand[-1], 0, kind, **kwargs)
    return result[0]


@pytest.mark.parametrize("sub_rule", SUB_RULES)
@pytest.mark.parametrize("kind", ["dianhe", "handgot", "qianggang"])
def test_exact_fan_boundary_for_every_win_type(sub_rule, kind):
    game = state(sub_rule)
    hand = XUELIU if sub_rule == "sichuan/xueliu" else STANDARD
    assert actions(game, hand, kind)
    fan = game.sichuan_hu_results[0]["fan"]
    game.hepai_limit = fan
    assert actions(game, hand, kind)
    game.hepai_limit = fan + 1
    assert actions(game, hand, kind) == []
    assert game.sichuan_hu_results == {}


def test_standard_zero_fan_and_situational_bonus():
    game = state(limit=0)
    assert actions(game, STANDARD) == ["hu"]
    assert game.sichuan_hu_results[0]["fan"] == 0
    game.hepai_limit = 1
    assert actions(game, STANDARD) == []
    assert actions(game, STANDARD, "handgot") == []
    assert actions(game, STANDARD, "qianggang") == ["hu"]
    assert actions(game, STANDARD, "handgot", is_get_gang_tile=True) == ["hu_self"]
    game.last_action_was_gang = True
    assert actions(game, STANDARD) == ["hu"]


def test_xueliu_threshold_can_allow_only_self_draw():
    game = state("sichuan/xueliu", 2)
    assert actions(game, XUELIU) == []
    assert actions(game, XUELIU, "handgot") == ["hu_self"]
    assert game.sichuan_hu_results[0]["fan"] == 2


def test_invalid_hand_is_not_zero_fan_win():
    game = state()
    assert actions(game, STANDARD[:-1] + [29]) == []


@pytest.mark.parametrize("sub_rule", SUB_RULES)
def test_waiting_shapes_remain_available_for_situational_wins(sub_rule):
    game = state(sub_rule, 64)
    hand = XUELIU if sub_rule == "sichuan/xueliu" else STANDARD
    game.player_list[0].hand_tiles = hand[:-1]
    refresh_waiting_tiles(game, 0)
    assert 25 in game.player_list[0].waiting_tiles


@pytest.mark.parametrize("sub_rule", SUB_RULES)
def test_wall_end_requires_reachable_minimum(sub_rule):
    game = state(sub_rule)
    hand = XUELIU if sub_rule == "sichuan/xueliu" else STANDARD
    player = game.player_list[0]
    player.hand_tiles = hand[:-1]

    def evaluate():
        if game.is_xueliu:
            status, waits, fan, _ = game._evaluate_xueliu_wall_end(player)
            return status == "ting", waits, fan
        ting, waits, fan, _ = game._evaluate_liuju_ting(player.hand_tiles, [], 3, [])
        return ting, waits, fan

    ting, waits, fan = evaluate()
    assert ting and 25 in waits
    game.hepai_limit = fan
    assert evaluate()[0]
    game.hepai_limit = fan + 1
    assert evaluate()[:2] == (False, set())


@pytest.mark.parametrize("sub_rule", SUB_RULES)
@pytest.mark.parametrize("limit", [0, 3, 64])
def test_broadcast_and_record_keep_room_limit(sub_rule, limit):
    game = state(sub_rule, limit)
    assert game.hepai_limit == limit
    assert _base_game_info(game)["hepai_limit"] == limit
    init_game_record(game)
    assert game.game_record["game_title"]["hepai_limit"] == limit
    assert game.game_record["game_title"]["sichuan_hepai_limit_version"] == 1
