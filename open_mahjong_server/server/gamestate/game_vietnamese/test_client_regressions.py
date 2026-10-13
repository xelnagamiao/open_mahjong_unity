"""Regression inputs use Unity's visual sibling index, not server list indices."""
import asyncio
from types import SimpleNamespace

import pytest

from .get_action import handle_action
from .state_machine import Phase as P
from .test_flow import act, state, turn
from ...game_calculation.vietnamese.tiles import ANY, BONUS, JOKERS
from ...game_calculation.vietnamese.test_rules import NORMAL


@pytest.mark.parametrize('joker', JOKERS)
def test_opening_timeout_keeps_optional_joker(joker):
    s = state()
    s.initialize_round()
    s.player_list[0].hand_tiles = list(NORMAL[:-1]) + [joker]
    s.opening_queue = [0]
    s.open_action_window(s.opening_window())
    assert s.action_dict[0] == ['vn_replace', 'pass']
    response = s._build_timeout_action(0)
    assert response['action_type'] == 'pass'
    act(s, {0: response})
    assert joker in s.player_list[0].hand_tiles
    assert not s.player_list[0].huapai_list


def test_unity_draw_slot_accepts_independent_visual_index():
    s = turn(NORMAL)
    act(s, {0: dict(action_type='cut', TileId=NORMAL[-1], cutClass=True, cutIndex=0)})
    assert s.player_list[0].discard_tiles == [NORMAL[-1]]


def test_held_tile_can_have_last_visual_index_after_draw_is_arranged():
    s = turn(NORMAL)
    act(s, {0: dict(action_type='cut', TileId=11, cutClass=False, cutIndex=len(NORMAL)-1)})
    assert s.player_list[0].discard_tiles == [11]
    assert s.player_list[0].hand_tiles.count(NORMAL[-1]) == NORMAL.count(NORMAL[-1])


def test_sorted_or_manually_arranged_hand_after_replacement():
    s = turn([21,22,23,31,32,33,41,41,41,45,45,51,52,29])
    s.tiles_list[-2:] = [19,11]
    act(s, {0: dict(action_type='buhua')})
    p = s.player_list[0]
    assert p.hand_tiles[-2:] == [11,19]
    # Auto-arrange displays 11 first, despite it being near the server list tail.
    act(s, {0: dict(action_type='cut', TileId=11, cutClass=False, cutIndex=0)})
    assert p.discard_tiles == [11] and 11 not in p.hand_tiles


def test_flower_replacement_does_not_select_jokers():
    s = turn(list(NORMAL[:-2]) + [51, ANY])
    act(s, {0: dict(action_type='buhua')})
    assert ANY in s.player_list[0].hand_tiles
    assert s.player_list[0].huapai_list == [51]


def test_rejected_choice_restores_authoritative_action_window():
    async def run():
        s = turn(list(NORMAL[:-1]) + [ANY])
        packets = []
        class Socket:
            async def send_json(self, packet): packets.append(packet)
        server = SimpleNamespace(
            gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s),
            players={'client': SimpleNamespace(user_id=s.player_list[0].user_id)})
        await handle_action(server, 'client', dict(type='gamestate/vietnamese/send_action',
            gamestate_id=s.gamestate_id, action_tick=s.server_action_tick,
            action='vn_replace', targetTile=0), Socket())
        assert packets[0]['type'] == 'tips'
        assert packets[-1]['type'] == 'gamestate/vietnamese/broadcast_hand_action'
        assert packets[-1]['ask_hand_action_info']['action_list'] == s.action_dict[0]
        assert s.action_queues[0].empty()
    asyncio.run(run())
