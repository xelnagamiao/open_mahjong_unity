"""实时观战 UnitySim：视角座位 + 摸切后手牌数量。"""
from __future__ import annotations

import pytest

from server.gamestate.verifier.unity_sim import UnitySim

_RULES = (
    "guobiao",
    "riichi",
    "sichuan",
    "classical",
    "qingque",
    "changsha",
    "taiwan",
    "jiandan",
    "hongque",
)


def _spectator_game_start(rule: str, view_player_index: int) -> dict:
    hands = {
        0: None,
        1: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24],
        2: None,
        3: None,
    }
    players = []
    for seat in range(4):
        players.append(
            {
                "user_id": 201 + seat,
                "username": f"P{seat}",
                "player_index": seat,
                "score": 0,
                "hand_tiles": hands[seat],
                "hand_tiles_count": 13,
                "discard_tiles": [],
                "combination_tiles": [],
            }
        )
    return {
        "type": f"gamestate/{rule}/game_start",
        "game_info": {
            "room_rule": rule,
            "tile_count": 83,
            "current_round": 1,
            "view_player_index": view_player_index,
            "players_info": players,
        },
    }


def _do_action(rule: str, info: dict) -> dict:
    return {"type": f"gamestate/{rule}/do_action", "do_action_info": info}


@pytest.mark.parametrize("rule", _RULES)
def test_unity_sim_spectator_draw_cut_keeps_hand_count(rule):
    sim = UnitySim(viewer_user_id=999)
    sim.apply(_spectator_game_start(rule, 1))
    snap = sim.snapshot()
    assert snap["GsmSim"]["selfIndex"] == 1
    assert len(snap["GsmSim"]["selfHandTiles"]) == 13
    assert snap["Game3DSim"]["right"]["hand_count"] == 13

    sim.apply(_do_action(rule, {"action_list": ["deal_tile"], "action_player": 1, "deal_tile": 31}))
    sim.apply(
        _do_action(
            rule,
            {"action_list": ["cut"], "action_player": 1, "cut_tile": 31, "cut_class": True},
        )
    )
    after = sim.snapshot()
    assert len(after["GsmSim"]["selfHandTiles"]) == 13
    assert 31 not in after["GsmSim"]["selfHandTiles"]
    assert after["Game3DSim"]["self"]["river"] == [31]

    sim.apply(_do_action(rule, {"action_list": ["deal_tile"], "action_player": 2}))
    sim.apply(
        _do_action(
            rule,
            {"action_list": ["cut"], "action_player": 2, "cut_tile": 41, "cut_class": False},
        )
    )
    after_other = sim.snapshot()
    assert after_other["Game3DSim"]["right"]["hand_count"] == 13
    assert after_other["Game3DSim"]["right"]["river"] == [41]


def test_unity_sim_deal_tile_zero_still_adds_for_self():
    sim = UnitySim(viewer_user_id=101)
    sim.apply(
        {
            "type": "gamestate/guobiao/game_start",
            "game_info": {
                "room_rule": "guobiao",
                "tile_count": 83,
                "players_info": [
                    {
                        "user_id": 101,
                        "player_index": 0,
                        "hand_tiles": [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24],
                        "hand_tiles_count": 13,
                    },
                    {"user_id": 102, "player_index": 1, "hand_tiles_count": 13},
                    {"user_id": 103, "player_index": 2, "hand_tiles_count": 13},
                    {"user_id": 104, "player_index": 3, "hand_tiles_count": 13},
                ],
            },
        }
    )
    sim.apply(
        {
            "type": "gamestate/guobiao/do_action",
            "do_action_info": {
                "action_list": ["deal_tile"],
                "action_player": 0,
                "deal_tile": 0,
            },
        }
    )
    assert sim.snapshot()["GsmSim"]["selfHandTiles"][-1] == 0
    assert len(sim.snapshot()["GsmSim"]["selfHandTiles"]) == 14
