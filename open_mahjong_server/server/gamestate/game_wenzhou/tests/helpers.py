"""Physical fixtures only: scoring always uses the production joker core."""

import copy
import random
from collections import Counter

from ..WenzhouGameState import TILES, WenzhouGameState
from ..actions import natural
from ..state_machine import Phase as P


BASE = [11, 12, 13, 21, 22, 23, 31, 32, 33, 17, 18, 19, 41, 41, 41]
STANDARD = BASE + [43, 43]
WAIT = BASE + [43]
SOFT = BASE[:-1] + [45, 43, 43]


def make_state(**room_changes):
    room = WenzhouGameState._default_room_data()
    room.update(random_seed=20241002, allow_spectator=False)
    room.update(room_changes)
    # The legacy calculation service is unused by this domain. Supplying a
    # sentinel avoids importing its unrelated native rule engines, not the
    # actual Wenzhou scorer or joker core used by every rule assertion.
    return WenzhouGameState(room_data=room, calculation_service=object())


def physical_tiles(state):
    return ([state.indicator] + list(state.tiles_list)
            + [t for p in state.player_list for t in p.hand_tiles]
            + [t for p in state.player_list for t in p.discard_tiles]
            + [t for p in state.player_list for m in p.meld_records for t in m["physical"]]
            + [t for p in state.player_list for t in p.won_tiles])


def assert_conserved(state):
    assert Counter(physical_tiles(state)) == Counter({t: 4 for t in TILES})


def fixture_state(hands=None, *, caishen=45, actor=0, phase=P.TURN,
                  drawn=True, melds=None, river=None, tips=False):
    """Place specified hands and melds in a valid 136-tile inventory.

    Other hands are filled with a reproducibly shuffled remaining supply.
    All explicitly supplied hands must have the size required by their open
    meld count and draw slot; impossible fifth copies fail fixture creation.
    """
    state = make_state(tips=tips)
    state.initialize_round()
    state.caishen = state.indicator = caishen
    state.dice = [1, 2, 3, 4]
    state.indicator_index = 72
    pool = Counter({t: 4 for t in TILES})
    pool[caishen] -= 1
    hands, melds, river = hands or {}, melds or {}, river or {}
    for index, player in enumerate(state.player_list):
        player.reset_for_round(0)
        player.has_draw_slot = drawn and actor == index
        player.draw_kind = "normal" if player.has_draw_slot else ""
        for code, physical in melds.get(index, []):
            mask = [value for n, t in enumerate(physical) for value in (2 if code[0] == "G" else int(n == 0), t)]
            state._add_meld(player, code, mask)
            pool.subtract(physical)
        player.discard_tiles = list(river.get(index, []))
        player.discard_origin_tiles = list(player.discard_tiles)
        pool.subtract(player.discard_tiles)
        if index in hands:
            player.hand_tiles = list(hands[index])
            assert len(player.hand_tiles) == 16 - 3 * len(player.meld_records) + int(player.has_draw_slot)
            pool.subtract(player.hand_tiles)
    assert all(count >= 0 for count in pool.values()), "test fixture exceeds physical tile inventory"
    remaining = list(pool.elements())
    random.Random(702024).shuffle(remaining)
    for index, player in enumerate(state.player_list):
        if index not in hands:
            size = 16 - 3 * len(player.meld_records) + int(player.has_draw_slot)
            player.hand_tiles = remaining[:size]
            del remaining[:size]
        if not player.has_draw_slot:
            player.hand_tiles.sort()
    state.tiles_list = remaining
    state.current_player_index = actor
    state.machine.transition(P.TURN)
    if phase != P.TURN:
        state.machine.transition(phase)
    assert_conserved(state)
    return state


def snapshot(state):
    """Relevant domain state for transactional rejection assertions."""
    return copy.deepcopy((state.machine.phase, state.machine.version, state.server_action_tick,
                          state.current_player_index, state.tiles_list, state.pending_kong,
                          state.round_changes, state.ledger, state.domain_events,
                          [dict(vars(p), record_counter=dict(vars(p.record_counter))) for p in state.player_list]))


def response_map(window, **choices):
    return {index: {"action_type": choices.get(str(index), "pass")}
            for index, offered in window["actions"].items() if offered}


class SocketRecorder:
    def __init__(self):
        self.messages = []

    async def send_json(self, payload):
        self.messages.append(copy.deepcopy(payload))
