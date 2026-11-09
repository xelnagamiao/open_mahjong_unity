"""金锣赛：八种天气、素番边界、实际和牌、配置透传和错和续局。"""
import asyncio
import ast
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from . import guobiao_jinluo as jinluo
from .guobiao_hepai_check import Chinese_Hepai_Check
from ..room.test_guobiao_flowers import room_manager
from ..room.room_validators import GBRoomValidator
from ..gamestate.game_guobiao.test_opening_wins import make_state
from ..gamestate.game_guobiao.action_check import check_hepai
from ..gamestate.public.ai.smart_bot_logic import should_accept_hu
from ..gamestate.public.game_record_manager import build_game_title_data


WEATHER_CASES = [
    ("dragon_pairs", [11, 14, 17, 22, 35, 45, 46], [11, 14, 17, 22, 35, 41, 45]),
    ("seat_round_winds", [11, 14, 17, 22, 35, 41, 42], [11, 14, 17, 22, 35, 41, 43]),
    ("terminal_pairs", [11, 19, 21, 24, 35, 41, 45], [11, 14, 21, 24, 35, 41, 45]),
    ("all_simples", [12, 14, 17, 22, 26, 35, 38], [11, 14, 17, 22, 26, 35, 38]),
    ("missing_suit", [11, 14, 17, 22, 24, 26, 28], [11, 14, 17, 22, 24, 26, 35]),
    ("consecutive_pairs", [11, 12, 17, 22, 26, 35, 45], [19, 21, 24, 27, 35, 41, 45]),
    ("same_rank_pairs", [11, 14, 17, 21, 26, 35, 45], [11, 14, 17, 22, 26, 35, 45]),
    ("four_of_a_kind", [11, 11, 17, 22, 26, 35, 45], [11, 14, 17, 22, 26, 35, 45]),
]


def test_live_server_entry_point_forwards_jinluo_configuration():
    # 不导入会建立数据库连接的 server 模块，直接执行真实入口方法。
    tree = ast.parse((Path(__file__).resolve().parents[1] / "server.py").read_text(encoding="utf-8-sig"))
    cls = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "GameServer")
    method = next(node for node in cls.body if isinstance(node, ast.AsyncFunctionDef) and node.name == "create_GB_room")
    namespace = {"Response": object}
    exec(compile(ast.Module(body=[method], type_ignores=[]), "server.create_GB_room", "exec"), namespace)
    manager = SimpleNamespace(create_GB_room=AsyncMock(return_value="created"))
    result = asyncio.run(namespace["create_GB_room"](SimpleNamespace(room_manager=manager),
        "connection", "金锣赛", 1, "", 20, 5, False, jinluo_mode=True, jinluo_weather="terminal_pairs"))
    assert result == "created"
    assert manager.create_GB_room.await_args.kwargs["jinluo_mode"] is True
    assert manager.create_GB_room.await_args.kwargs["jinluo_weather"] == "terminal_pairs"


@pytest.mark.parametrize("weather,yes,no", WEATHER_CASES)
def test_eight_manual_weathers(weather, yes, no):
    assert jinluo.matches_weather(yes, weather, seat_wind=42, round_wind=41)
    assert not jinluo.matches_weather(no, weather, seat_wind=42, round_wind=41)
    assert not jinluo.matches_weather(yes[:-1], weather)


def test_wind_weather_tracks_seat_round_and_double_wind():
    pairs = [11, 14, 17, 22, 35, 41, 42]
    assert jinluo.matches_weather(pairs, "seat_round_winds", 41, 41)
    assert not jinluo.matches_weather(pairs, "seat_round_winds", 43, 41)
    assert not jinluo.matches_weather(pairs, "seat_round_winds", 42, 43)


@pytest.mark.parametrize("score,flowers,allowed", [(24, 0, False), (27, 0, False), (28, 0, True), (29, 2, False), (30, 2, True)])
def test_seven_pairs_threshold_excludes_flowers(score, flowers, allowed):
    hand = [tile for tile in [11, 14, 17, 22, 26, 35, 45] for _ in range(2)]
    fans = ["七对"] + ([f"花牌*{flowers}"] if flowers else [])
    assert jinluo.is_allowed((score, fans), hand, [], "dragon_pairs") is allowed


@pytest.mark.parametrize("fan", sorted(jinluo.REDUCED_FANS))
@pytest.mark.parametrize("score,allowed", [(15, False), (16, True), (23, True)])
def test_reduced_fans_need_sixteen(fan, score, allowed):
    assert jinluo.is_allowed((score, [fan, "混一色"]), [11], [], "dragon_pairs") is allowed


def test_robbing_kong_is_not_a_sixteen_fan_exemption():
    assert not jinluo.is_allowed((23, ["抢杠和", "混一色"]), [11], [])
    assert jinluo.is_allowed((24, ["抢杠和", "混一色"]), [11], [])


@pytest.mark.parametrize("fan", sorted(jinluo.UNRESTRICTED_FANS))
def test_unrestricted_fans_still_need_eight_core_fan(fan):
    assert jinluo.is_allowed((8, [fan]), [11], [])
    assert not jinluo.is_allowed((7, [fan]), [11], [])


def test_small_fan_exemptions_and_forbidden_combinations():
    assert jinluo.is_allowed((8, ["平和", "双同刻", "一般高*2", "老少副", "自摸"]), [11], [])
    assert not jinluo.is_allowed((8, ["门前清", "平和", "一般高*2", "老少副", "嵌张"]), [11], [])
    for forbidden in ("和绝张", "不求人", "双明杠"):
        assert not jinluo.is_allowed((12, [forbidden, "断幺", "一般高*2"]), [11], [])
    assert jinluo.is_allowed((14, ["花牌*6", "无番和"]), [11], [])
    assert not jinluo.is_allowed((13, ["花牌*6", "平和"]), [11], [])


@pytest.mark.parametrize("pairs", [
    [11, 12, 13, 24, 25, 26, 45],  # 两杯口
    [11, 11, 21, 21, 31, 12, 22],  # 二数
    [11, 12, 13, 21, 22, 31, 33],  # 三数
    [12, 14, 16, 22, 24, 32, 38],  # 全双
    [11, 22, 13, 24, 35, 16, 27],  # 杂连
    [11, 14, 22, 35, 45, 46, 47],  # 三元
    [11, 22, 35, 41, 42, 43, 44],  # 四喜
])
def test_special_seven_pairs(pairs):
    hand = [tile for tile in pairs for _ in range(2)]
    assert jinluo.is_allowed((24, ["七对"]), hand, [], "four_of_a_kind")


def test_clear_two_numbers_cannot_include_honors():
    assert jinluo.is_allowed((12, ["碰碰和"]), [11, 21, 31, 12, 22], [])
    assert not jinluo.is_allowed((12, ["碰碰和"]), [11, 21, 31, 12, 41], [])


@pytest.mark.parametrize("weather,yes,no", WEATHER_CASES)
@pytest.mark.parametrize("event", [False, True])
def test_room_settings_and_forced_wrong_win_mode(weather, yes, no, event):
    manager = room_manager()
    config = dict(sub_rule="guobiao/standard", jinluo_mode=True, jinluo_weather=weather, open_cuohe=False, hepai_limit=64)
    response = asyncio.run(manager.create_empty_event_room("event1", "guobiao", config) if event else
                            manager.create_GB_room("connection", "金锣赛", 1, "", 20, 5, False, **config))
    assert response.success, response.message
    assert response.room_info["jinluo_mode"] is True
    assert response.room_info["jinluo_weather"] == weather
    assert response.room_info["open_cuohe"] is True
    assert response.room_info["hepai_limit"] == 8


@pytest.mark.parametrize("config", [{"jinluo_mode": "true"}, {"jinluo_weather": "auto"}, {"jinluo_weather": None}, {"jinluo_weather": []}])
def test_invalid_configuration_rejected(config):
    with pytest.raises(ValueError):
        GBRoomValidator(room_name="金锣赛", game_round=1, round_timer=20, step_timer=5, **config)


def test_only_standard_supports_jinluo_and_old_rooms_default_off():
    manager = room_manager()
    response = asyncio.run(manager.create_GB_room("connection", "普通国标", 1, "", 20, 5, False, hepai_limit=64))
    assert response.success and not response.room_info["jinluo_mode"] and response.room_info["hepai_limit"] == 64
    manager = room_manager()
    response = asyncio.run(manager.create_GB_room("connection", "错误配置", 1, "", 20, 5, False, sub_rule="guobiao/xiaolin", jinluo_mode=True))
    assert not response.success and "仅适用于标准国标" in response.message


@pytest.mark.parametrize("weather,legal", [("dragon_pairs", False), ("four_of_a_kind", True)])
def test_real_seven_pairs_action_and_bot_use_the_same_qualification(weather, legal):
    state = make_state(enabled=False)
    state.jinluo_mode, state.jinluo_weather, state.open_cuohe = True, weather, True
    hand = [11, 11, 11, 11, 17, 17, 22, 22, 26, 26, 35, 35, 45, 45]
    player = state.player_list[0]
    player.hand_tiles, player.waiting_tiles = hand, {45}
    actions = {index: [] for index in range(4)}
    check_hepai(state, actions, 45, 0, "handgot")
    assert "hu_self" in actions[0]  # 不合规时仍可主动报错和。
    result = state.result_dict["hu_self"]
    assert jinluo.allows_state_win(state, result, hand, [], 0) is legal
    assert should_accept_hu(state, 0, "hu_self") is legal
    title = build_game_title_data(state)
    assert title["jinluo_mode"] and title["jinluo_weather"] == weather


@pytest.mark.parametrize("penalty", [0, 1])
def test_jinluo_wrong_rob_uses_existing_penalty_and_resumes_game(monkeypatch, penalty):
    from ..gamestate.game_guobiao import test_qianggang_cuohe as existing
    original = existing.make_state
    def make_jinluo_state(*args, **kwargs):
        state = original(*args, **kwargs)
        state.jinluo_mode, state.jinluo_weather = True, "dragon_pairs"
        return state
    monkeypatch.setattr(existing, "make_state", make_jinluo_state)
    existing.test_real_loop_wrong_rob_settlement_and_resume(monkeypatch, False, penalty, False, 16, 0)
