"""Physical wall occupancy is public, stable across snapshots and draw sources."""
from copy import deepcopy
from types import SimpleNamespace
import random

import pytest

from ...game_calculation.sichuan.duplicate_rules import make_board
from ...response import Do_action_info, GameInfo
from .SichuanGameState import SichuanPlayer
from .duplicate_wall import init_duplicate_board, wall_display_masks
from .boardcast import _build_do_action_payload, _base_game_info, _build_players_info
from .test_xueliu_integration import full_state


def make_state(layout, number=1):
    players = [SichuanPlayer(i, str(i), [], 0) for i in range(4)]
    for i, player in enumerate(players):
        player.player_index = player.original_player_index = i
    state = SimpleNamespace(player_list=players, current_player_index=0,
                            is_xueliu=layout == 'xueliu', dead_wall_count=0, server_action_tick=1)
    init_duplicate_board(state, make_board('wall-display', number, layout=layout))
    return state


def remaining_slots(masks):
    return {(seat, slot) for seat in range(4) for slot in range(14) if masks[seat] & (1 << slot)}


@pytest.mark.parametrize('layout', ['standard', 'xueliu'])
@pytest.mark.parametrize('number', range(1, 5))
def test_initial_dealer_slot_and_snapshot_does_not_mutate_wall(layout, number):
    state = make_state(layout, number)
    before = deepcopy((state.tiles_list.parts, list(state.tiles_list), state.tiles_list.consumed))
    masks = wall_display_masks(state)
    expected = {(s, i) for s in range(4) for i in range(14)} - {(number - 1, 0)}
    assert remaining_slots(masks) == expected
    masks[0] = 0
    assert remaining_slots(wall_display_masks(state)) == expected
    assert before == (state.tiles_list.parts, list(state.tiles_list), state.tiles_list.consumed)


@pytest.mark.parametrize('layout', ['standard', 'xueliu'])
@pytest.mark.parametrize('seed', range(12))
def test_every_actual_draw_removes_exactly_one_original_slot(layout, seed):
    state = make_state(layout, seed % 4 + 1)
    rng = random.Random(seed)
    wall = state.tiles_list
    while wall:
        previous = remaining_slots(wall_display_masks(state))
        candidates = [p for p in state.player_list if wall.can_draw(p)]
        player = rng.choice(candidates)
        tile = wall.pop_for_player(player)
        source = state.duplicate_last_draw
        seat, slot = source['draw_from_seat'], source['wall_index']
        if seat == -1:
            seat, slot = slot // 6, 8 + slot % 6
        current = remaining_slots(wall_display_masks(state))
        assert previous - current == {(seat, slot)}
        assert current <= previous and len(current) == len(wall)
        # Other players receive identical public positions, but no concealed face.
        for viewer in range(4):
            packet = Do_action_info(**_build_do_action_payload(
                state, ['deal_tile'], player.player_index, viewer, deal_tile=tile)).model_dump(exclude_none=True)
            assert remaining_slots(packet['sichuan_wall_masks']) == current
            assert packet.get('deal_tile') == (tile if viewer == player.player_index else None)
    assert wall_display_masks(state) == [0, 0, 0, 0]


@pytest.mark.parametrize('seat', range(4))
def test_inheritance_changes_partner_wall_only(seat):
    state = make_state('standard', seat + 1)
    wall, player = state.tiles_list, state.player_list[seat]
    while wall.parts[seat]:
        wall.pop_for_player(player)
    assert not wall.can_draw(player)
    partner = state.player_list[(seat + 2) % 4]
    partner.is_hu = True
    before = remaining_slots(wall_display_masks(state))
    wall.pop_for_player(player)
    assert before - remaining_slots(wall_display_masks(state)) == {((seat + 2) % 4, 0)}
    assert wall_display_masks(state)[seat] == 0


def test_shared_rollover_reconnect_has_same_positions_despite_zero_personal_counts():
    state, _ = full_state()
    state.duplicate_key, state.duplicate_round_count = 'dup_display_test', 4
    state.duplicate_wall_type = 'key'
    init_duplicate_board(state, make_board('wall-display', 1, layout='xueliu'))
    wall = state.tiles_list
    for _ in range(7):
        wall.pop_for_player(state.player_list[0])
    assert wall.common_started and wall.remaining_counts() == [0, 0, 0, 0]
    for _ in range(11):
        wall.pop_for_player(state.player_list[3])
    masks = wall_display_masks(state)
    for viewer in state.player_list:
        packet = GameInfo(**{**_base_game_info(state), 'players_info': _build_players_info(state, viewer.user_id)})
        assert packet.sichuan_wall_masks == masks
        assert len(remaining_slots(packet.sichuan_wall_masks)) == len(wall)
    assert 'sichuan_wall_masks' not in state.game_record['game_round']['round_index_1']


def test_regular_room_has_no_physical_duplicate_wall():
    state, _ = full_state()
    assert wall_display_masks(state) is None
    assert _base_game_info(state).get('sichuan_wall_masks') is None
