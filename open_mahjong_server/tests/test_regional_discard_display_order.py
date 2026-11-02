import asyncio
from collections import Counter
from types import SimpleNamespace

import pytest

from server.gamestate.game_beijing.test_flow import game as beijing_game, turn as beijing_turn
from server.gamestate.game_beijing.get_action import handle_action as beijing_action
from server.gamestate.game_duocai.test_flow import game as duocai_game, turn as duocai_turn
from server.gamestate.game_duocai.get_action import handle_action as duocai_action


HAND = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 42, 45, 46, 47]


@pytest.fixture(params=['beijing', 'duocai/guobiao', 'duocai/riichi'])
def rules(request):
    if request.param == 'beijing':
        return beijing_game, beijing_turn, beijing_action
    return lambda: duocai_game(request.param.split('/')[1]), duocai_turn, duocai_action


class Socket:
    def __init__(self):
        self.messages = []

    async def send_json(self, message):
        self.messages.append(message)


def submit(rules, hand, tile, display_index, drawn, *, after_meld=False):
    factory, turn, handler = rules
    state = factory()
    turn(state, 0, hand)
    if after_meld:
        state.player_list[0].has_draw_slot = False
        state.machine.transition(type(state.machine.phase).RESPONSE)
        state.open_action_window(state.begin_turn(0, after_meld=True))
    socket = Socket()
    server = SimpleNamespace(players={'client': SimpleNamespace(user_id=state.player_list[0].user_id)},
        gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: state))
    packet = dict(type=f'gamestate/{state.room_rule}/cut_tile', gamestate_id='test',
        action_tick=state.server_action_tick, TileId=tile, cutIndex=display_index, cutClass=drawn)
    asyncio.run(handler(server, 'client', packet, socket))
    return state, socket


@pytest.mark.parametrize('display_index', range(13))
def test_reordered_display_can_discard_every_held_tile(rules, display_index):
    tile = (HAND[1:13] + HAND[:1])[display_index]
    state, socket = submit(rules, HAND, tile, display_index, False)
    assert not socket.messages
    response = state.action_queues[0].get_nowait()
    state.apply_action_results(state.live_pending_window, {0: response})
    assert Counter(state.player_list[0].hand_tiles) == Counter(HAND) - Counter([tile])
    event = next(e for e in state.domain_events if e['action'] == 'cut')
    assert (event['tile'], event['cutIndex'], event['cutClass']) == (tile, display_index, False)


def test_drawn_tile_can_be_moved_into_the_displayed_hand(rules):
    state, socket = submit(rules, HAND, HAND[-1], 0, True)
    assert not socket.messages
    response = state.action_queues[0].get_nowait()
    assert state._cut_identity(0, response) == (HAND[-1], 13, True)


@pytest.mark.parametrize('drawn', [False, True])
def test_duplicate_tiles_keep_cut_identity_separate_from_display_index(rules, drawn):
    hand = HAND[:-1] + [11]
    state, socket = submit(rules, hand, 11, 7, drawn)
    assert not socket.messages
    response = state.action_queues[0].get_nowait()
    assert state._cut_identity(0, response) == (11, 13 if drawn else 0, drawn)


def test_after_meld_last_held_tile_is_not_a_draw(rules):
    state, socket = submit(rules, HAND[:11], HAND[10], 0, False, after_meld=True)
    assert not socket.messages
    assert state._cut_identity(0, state.action_queues[0].get_nowait()) == (HAND[10], 10, False)


@pytest.mark.parametrize('fields', [
    dict(tile=99, display_index=0, drawn=False),
    dict(tile=11, display_index=0, drawn=True),
    dict(tile=47, display_index=0, drawn=False),
    dict(tile=True, display_index=0, drawn=False),
    dict(tile=11, display_index='0', drawn=False),
    dict(tile=11, display_index=-2, drawn=False),
    dict(tile=11, display_index=0, drawn='false'),
])
def test_invalid_physical_tile_or_cut_identity_is_rejected_without_mutation(rules, fields):
    state, socket = submit(rules, HAND, **fields)
    assert socket.messages[-1]['success'] is False
    assert state.player_list[0].hand_tiles == HAND
    assert state.action_queues[0].empty()


def test_duocai_bonus_copy_is_not_replaced_by_an_equivalent_face():
    rules = lambda: duocai_game('guobiao'), duocai_turn, duocai_action
    hand = [1013, 2013] + HAND[2:]
    state, socket = submit(rules, hand, 2013, 0, False)
    assert not socket.messages
    response = state.action_queues[0].get_nowait()
    state.apply_action_results(state.live_pending_window, {0: response})
    assert 1013 in state.player_list[0].hand_tiles
    assert 2013 not in state.player_list[0].hand_tiles


@pytest.mark.parametrize('drawn', [False, True])
def test_duocai_riichi_uses_draw_identity_even_when_display_is_reordered(drawn):
    state = duocai_game('riichi')
    duocai_turn(state, 0, HAND[:-1] + [11])
    state.player_list[0].riichi = 1
    data = dict(action_type='cut', TileId=11, cutIndex=7, cutClass=drawn)
    if drawn:
        assert state._cut_identity(0, data) == (11, 13, True)
    else:
        with pytest.raises(ValueError, match='立直后必须摸切'):
            state._cut_identity(0, data)
