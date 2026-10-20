"""Exercise mode policy against real rule windows and automatic settlements."""
import asyncio
import importlib
from dataclasses import replace

import pytest

from .test_bot_modes import regional_choice


REGIONAL_CLASSES = (
    ("hongkong", "HongKongGameState"), ("filipino", "FilipinoGameState"),
    ("vietnamese", "VietnameseGameState"), ("guizhou", "GuizhouGameState"),
    ("hangzhou", "HangzhouGameState"), ("yixing", "YixingGameState"),
    ("wenzhou", "WenzhouGameState"), ("nanchang", "NanchangGameState"),
    ("shangrao", "ShangraoGameState"), ("nanjing", "NanjingGameState"),
    ("fuzhou", "FuzhouGameState"), ("malaysia", "MalaysiaGameState"),
)


@pytest.mark.parametrize("rule,name", REGIONAL_CLASSES)
def test_opening_replacements_and_draw_discards_use_legal_physical_tiles(rule, name):
    cls = getattr(importlib.import_module(f"server.gamestate.game_{rule}.{name}"), name)
    room = cls._default_room_data()
    room.update(random_seed=17, bot_speed="instant", claim_protection=False, allow_spectator=False)
    room["player_list"] = [0]*len(room["player_list"])
    s = cls(room_data=room, calculation_service=object())
    s.start_game_recording()
    s.initialize_round()
    s.start_round_recording()
    s.open_action_window(s.opening_window())
    discards = 0
    # A bounded opening sequence includes mandatory flower/joker replacements;
    # it is not an algorithm-tuning self-play match.
    for _ in range(80):
        replies = {}
        for seat, actions in s.action_dict.items():
            if not actions:
                continue
            if rule == "malaysia":
                data = s.automatic_action(seat, bot=True)
            else:
                choose = importlib.import_module(f"server.gamestate.game_{rule}.bot").choose_action
                data = choose(s, seat) if rule in ("filipino", "vietnamese") else choose(s, seat, actions)
            assert data["action_type"] in ("cut", "pass", "buhua", "vn_replace", "malaysia_fly", "nc_draw")
            if data["action_type"] == "cut":
                p = s.player_list[seat]
                assert data["TileId"] == p.hand_tiles[-1]
                assert data["cutIndex"] == len(p.hand_tiles)-1
                discards += 1
            replies[seat] = data
        s.apply_action_results(s.live_pending_window, replies)
        assert not any(p.is_hu for p in s.player_list)
        if discards >= 4:
            break
    assert discards >= 4


@pytest.mark.parametrize("rob", (False, True))
def test_guizhou_auto_bot_can_pass_hot_discard_and_rob_kong(rob):
    from ...game_guizhou.test_flow import configured_turn, PLAIN
    from ...game_guizhou.actions import claim_actions
    s = configured_turn()
    p = s.player_list[1]
    p.user_id = 0
    p.hand_tiles = PLAIN[:-1]
    s.hot_discard = True
    offered = claim_actions(s, 0, PLAIN[-1], rob=rob)[1]
    assert "hu" in offered and "pass" in offered
    from ...game_guizhou.bot import choose_action
    assert choose_action(s, 1, offered)["action_type"] == "pass"
    p.user_id = 2
    assert claim_actions(s, 0, PLAIN[-1], rob=rob)[1] == ["hu"]


def test_singapore_eighth_flower_replaces_without_auto_win():
    from ...game_singapore.test_state import make_state, drawn
    async def run():
        s = make_state()
        p = s.player_list[1]
        p.user_id = 0
        p.huapai_list = list(range(51,58))
        drawn(p, [11,12,58])
        s.current_player_index = 1
        assert await s._replace_one_flower(1,2,is_drawn=True,opening=False)
        assert s.game_status != "END" and not s.pending_winners
        assert p.huapai_list == list(range(51,59))
        assert len(p.hand_tiles) == 3 and 58 not in p.hand_tiles
    asyncio.run(run())


@pytest.mark.parametrize("last_four", (False, True))
def test_fuzhou_auto_bot_with_twenty_flowers_keeps_playing(last_four):
    from ...game_fuzhou.test_flower_choice import waiting
    from ...game_fuzhou.state_machine import Phase as P
    s = waiting(3)
    s.player_list[0].user_id = 0
    s.tiles_list = [29,21,22,23] + [11]*(s.dead_wall_count if last_four else 50)
    s.open_action_window(s.draw_for(0))
    assert s.machine.phase != P.FLOWER_WIN
    data = regional_choice("fuzhou", s)
    assert data["action_type"] == ("pass" if last_four else "cut")
    s.apply_action_results(s.live_pending_window, {0:data})
    assert not s.player_list[0].is_hu


def test_malaysia_opening_four_flies_replaces_instead_of_winning():
    from ...game_malaysia.test_flow import state
    from ....game_calculation.malaysia.rules import FLY
    s = state()
    for p in s.player_list:
        p.user_id = 0
        p.hand_tiles = [21,22,23,24,25,26,27,28,29,21,22,23,24]
    p = s.player_list[0]
    p.hand_tiles = [FLY]*4 + p.hand_tiles[:10]
    s.opening_queue = []
    s.open_action_window(s.opening_window())
    assert s.round_settlement is None
    assert s.automatic_action(0,bot=True) == dict(action_type="malaysia_fly")
    s.apply_action_results(s.live_pending_window, {0:s.automatic_action(0,bot=True)})
    assert p.hand_tiles.count(FLY) == 3 and s.round_settlement is None


def test_taiwan_natural_ready_cannot_force_auto_bot_to_win():
    from ...game_taiwan.test_taiwan_rules import make_action_state
    from ...game_taiwan.action_check import is_forced_ready_win
    s = make_action_state()
    s.rules = replace(s.rules, qualified_ready_win_policy="force_win")
    p = s.player_list[0]
    p.qualification_alive = p.heavenly_ready = True
    p.user_id = 0
    assert not is_forced_ready_win(s,0)
    p.user_id = 2
    assert is_forced_ready_win(s,0)
