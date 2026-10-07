from copy import deepcopy

import pytest

from . import NanqueGameState, ZhongyongGameState
from .boardcast import ask_action_payload, visible_action_payload


TILE = 13
REAL_MASK = [2, TILE] * 4
HIDDEN_MASK = [2, 0] * 4


@pytest.fixture(params=[ZhongyongGameState, NanqueGameState], ids=["standard", "nanque"])
def game(request):
    state = request.param()
    state.initialize_round()
    owner = state.player_list[0]
    owner.combination_tiles = [f"G{TILE}"]
    owner.combination_mask = [list(REAL_MASK)]
    return state


def assert_kong_snapshot(payload, hidden):
    owner = payload["game_info"]["players_info"][0]
    assert owner["combination_tiles"] == ["G0" if hidden else f"G{TILE}"]
    assert owner["combination_mask"] == [HIDDEN_MASK if hidden else REAL_MASK]


@pytest.mark.parametrize("viewer", [0, 1, 2, 3, None])
@pytest.mark.parametrize("status", ["waiting_hand_action", "END", "waiting_ready"])
def test_reconnect_snapshot_hides_kong_until_round_end(game, viewer, status):
    game.game_status = status
    hidden = game.is_nanque and viewer != 0 and status == "waiting_hand_action"
    assert_kong_snapshot(game.build_game_start_payload(viewer), hidden)
    assert game.player_list[0].combination_tiles == [f"G{TILE}"]
    assert game.player_list[0].combination_mask == [REAL_MASK]


@pytest.mark.parametrize("viewer", [0, 1, 2, 3, None])
@pytest.mark.parametrize("reveal_final", [False, True])
def test_kong_action_hides_every_tile_field(game, viewer, reveal_final):
    action = {
        "action": "angang",
        "player": 0,
        "tile": TILE,
        "meld_code": f"G{TILE}",
        "combination_mask": list(REAL_MASK),
        "is_mo_gang": True,
    }
    original = deepcopy(action)
    payload = visible_action_payload(game, viewer, action, reveal_final=reveal_final)
    hidden = game.is_nanque and viewer != 0 and not reveal_final
    assert payload["tile"] == (None if hidden else TILE)
    assert payload["meld_code"] == ("G0" if hidden else f"G{TILE}")
    info = payload["do_action_info"]
    assert info["combination_target"] == payload["meld_code"]
    assert info["combination_mask"] == (HIDDEN_MASK if hidden else REAL_MASK)
    assert info["action_list"] == ["angang"] and info["is_mo_gang"]
    assert_kong_snapshot(payload, hidden)
    assert action == original


@pytest.mark.parametrize("viewer", [0, 1, 2, 3])
def test_action_prompt_snapshot_keeps_other_players_kongs_hidden(game, viewer):
    assert_kong_snapshot(ask_action_payload(game, viewer, ["cut"]), game.is_nanque and viewer != 0)
    game.action_dict[viewer] = ["cut"]
    assert_kong_snapshot(game.build_pending_action_payload(viewer), game.is_nanque and viewer != 0)


@pytest.mark.parametrize("action,code,mask", [
    ("peng", "k45", [1, 45, 0, 45, 0, 45]),
    ("gang", "g45", [1, 45, 0, 45, 0, 45, 0, 45]),
    ("jiagang", "g45", [3, 45, 1, 45, 0, 45, 0, 45]),
])
def test_public_meld_actions_remain_visible(game, action, code, mask):
    owner = game.player_list[0]
    owner.combination_tiles.append(code)
    owner.combination_mask.append(mask)
    payload = visible_action_payload(game, 1, {
        "action": action, "player": 0, "tile": 45,
        "meld_code": code, "combination_mask": mask,
    })
    assert payload["tile"] == 45 and payload["meld_code"] == code
    assert payload["do_action_info"]["combination_target"] == code
    assert payload["do_action_info"]["combination_mask"] == mask
    snapshot = payload["game_info"]["players_info"][0]
    assert snapshot["combination_tiles"] == ["G0" if game.is_nanque else f"G{TILE}", code]
    assert snapshot["combination_mask"] == [HIDDEN_MASK if game.is_nanque else REAL_MASK, mask]


def test_live_kong_broadcast_prompt_reconnect_and_record_views(game):
    owner = game.player_list[0]
    owner.combination_tiles = []
    owner.combination_mask = []
    owner.hand_tiles = [TILE] * 4 + [14, 15, 16, 24, 25, 26, 37, 38, 39, 45]
    game.player_list[1].hand_tiles = [11, 12, 21, 22, 23, 31, 32, 33, 34, 35, 36, 41, 41]
    game.action_policy.refresh_waiting_tiles(game, 1)
    assert TILE in game.player_list[1].waiting_tiles
    game.start_game_recording()
    game.start_round_recording()
    window = game.begin_hand_action(0)
    game.apply_action_results(window, {0: {"action_type": "angang", "target_tile": TILE}})

    kongs = [payload for payload in game.outbound_payloads if payload.get("action") == "angang"]
    assert len(kongs) == 4
    for payload in kongs:
        hidden = game.is_nanque and payload["player_index"] != 0
        assert payload["tile"] == (None if hidden else TILE)
        assert payload["do_action_info"]["combination_target"] == ("G0" if hidden else f"G{TILE}")
        assert_kong_snapshot(payload, hidden)
    for payload in game.outbound_payloads:
        if "game_info" in payload:
            assert_kong_snapshot(payload, game.is_nanque and payload["player_index"] != 0)
    assert_kong_snapshot(game.build_game_start_payload(1), game.is_nanque)

    ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
    kong_tick = next(tick for tick in ticks if tick[0] == "ag")
    assert kong_tick[1] == TILE
    assert owner.combination_tiles == [f"G{TILE}"] and owner.combination_mask == [REAL_MASK]
