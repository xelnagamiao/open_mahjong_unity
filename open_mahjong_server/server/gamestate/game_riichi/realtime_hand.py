"""Authoritative presentation order for Washizu's realtime-hand variant.

The calculator's hand (whose last tile is the draw) is never reordered. Public
keys identify positions/copies without encoding the face of an opaque tile.
"""
from collections import Counter
import json

from ...game_calculation.riichi.washizu_tiles import REALTIME_SUB_RULE, is_transparent


def enabled(game):
    return getattr(game, 'sub_rule', None) in (REALTIME_SUB_RULE, 'guobiao/transparent_realtime')


def initialize_hand(game, player):
    # Dealing order carries no sorting information. Assign copy keys in that order.
    tiles = list(player.hand_tiles)
    player._realtime_hand = {
        'round_index': game.round_index, 'revision': 0,
        'entries': list(enumerate(tiles, 1)), 'next_key': len(tiles) + 1,
        'logical': tuple(player.hand_tiles), 'has_draw': False,
        'drawn_key': 0, 'draw_pinned': False,
    }
    return player._realtime_hand


def sync_hand(game, player):
    state = getattr(player, '_realtime_hand', None)
    if state is None or state['round_index'] != game.round_index:
        state = initialize_hand(game, player)
    logical = tuple(player.hand_tiles)
    has_draw = bool(getattr(player, 'has_draw_slot', False) and logical)
    if state['logical'] == logical and state['has_draw'] == has_draw:
        return state
    remaining = Counter(logical)
    excess = Counter(tile for _, tile in state['entries']) - remaining
    removed_keys = set()
    for tile, count in excess.items():
        candidates = [key for key, face in state['entries'] if face == tile]
        candidates.sort(key=lambda key: key == state['drawn_key'])
        removed_keys.update(candidates[:count])
    entries = []
    for key, tile in state['entries']:
        if key not in removed_keys:
            entries.append((key, tile))
            remaining[tile] -= 1
    added = []
    for tile in logical:
        if remaining[tile] > 0:
            added.append((state['next_key'], tile))
            state['next_key'] += 1
            remaining[tile] -= 1
    entries.extend(added)
    if has_draw:
        old_draw = next((key for key, tile in entries if key == state['drawn_key'] and tile == logical[-1]), 0)
        state['drawn_key'] = old_draw or next(key for key, tile in reversed(entries) if tile == logical[-1])
        if added or not state['has_draw']:
            state['draw_pinned'] = True
    else:
        state['drawn_key'] = 0
        state['draw_pinned'] = False
    state.update(entries=entries, logical=logical, has_draw=has_draw, revision=state['revision'] + 1)
    return state


def layout_for_viewer(game, player, viewer_index):
    state = sync_hand(game, player)
    owner = viewer_index == player.player_index
    return {
        'round_index': state['round_index'], 'revision': state['revision'],
        'tiles': [tile if owner or is_transparent(tile) else 0 for _, tile in state['entries']],
        'keys': [key for key, _ in state['entries']],
        'drawn_key': state['drawn_key'], 'draw_pinned': state['draw_pinned'],
    }


def layouts_for_viewer(game, viewer_index):
    if not enabled(game):
        return None
    return {p.player_index: layout_for_viewer(game, p, viewer_index) for p in game.player_list}


def ordered_reveal(game, player_index, tiles, winning_tile=False):
    """Reveal opaque faces in their established positions, preserving the win slot."""
    if not enabled(game) or not tiles:
        return tiles
    player = next((p for p in game.player_list if p.player_index == player_index), None)
    if player is None:
        return tiles
    remaining = list(tiles[:-1] if winning_tile else tiles)
    ordered = []
    state = sync_hand(game, player)
    for key, tile in state['entries']:
        if winning_tile and key == state['drawn_key'] and tile == tiles[-1]:
            continue
        if tile in remaining:
            remaining.remove(tile)
            ordered.append(tile)
    ordered.extend(remaining)
    if winning_tile:
        ordered.append(tiles[-1])
    return ordered


def note_cut(game, player, tile, is_moqie, display_index=None):
    """Remove the actual displayed copy after the rule engine accepted the cut."""
    if not enabled(game):
        return None
    state = getattr(player, '_realtime_hand', None)
    if state is None:
        return None
    entries = state['entries']
    chosen = None
    if is_moqie:
        chosen = next((e for e in entries if e[0] == state['drawn_key'] and e[1] == tile), None)
    elif type(display_index) is int and 0 <= display_index < len(entries):
        candidate = entries[display_index]
        if candidate[1] == tile and candidate[0] != state['drawn_key']:
            chosen = candidate
    if chosen is None:
        chosen = next((e for e in entries if e[1] == tile and e[0] != state['drawn_key']), None)
    if chosen is not None:
        entries.remove(chosen)
    return chosen[0] if chosen else None


def reorder(game, player, message):
    """Validate and atomically accept an order; return (accepted, reason, changed)."""
    if not enabled(game):
        return False, 'rule', False
    if getattr(game, 'game_status', '') in ('waiting', 'waiting_ready', 'END'):
        return False, 'inactive', False
    if getattr(game, '_cp_active', False):
        return False, 'busy', False
    state = sync_hand(game, player)
    if type(message.get('round_index')) is not int or message['round_index'] != game.round_index:
        return False, 'round', False
    if type(message.get('revision')) is not int or message['revision'] != state['revision']:
        return False, 'stale', False
    order = message.get('tiles')
    if not isinstance(order, list) or len(order) > 14 or any(type(t) is not int for t in order):
        return False, 'tiles', False
    if Counter(order) != Counter(player.hand_tiles):
        return False, 'tiles', False
    draw_index, pinned = message.get('draw_index', -1), message.get('draw_pinned', False)
    if type(draw_index) is not int or type(pinned) is not bool:
        return False, 'draw', False
    if state['has_draw']:
        if not 0 <= draw_index < len(order) or order[draw_index] != player.hand_tiles[-1]:
            return False, 'draw', False
        if pinned and draw_index != len(order) - 1:
            return False, 'draw', False
    elif draw_index != -1 or pinned:
        return False, 'draw', False
    requested_keys = message.get('keys')
    if requested_keys is not None:
        faces = dict(state['entries'])
        if (not isinstance(requested_keys, list) or any(type(k) is not int for k in requested_keys)
                or len(requested_keys) != len(faces) or set(requested_keys) != set(faces)
                or any(faces[k] != tile for k, tile in zip(requested_keys, order))):
            return False, 'keys', False
        if state['has_draw'] and requested_keys[draw_index] != state['drawn_key']:
            return False, 'draw', False
        entries = [(key, faces[key]) for key in requested_keys]
        changed = entries != state['entries'] or pinned != state['draw_pinned']
        if changed:
            state.update(entries=entries, draw_pinned=pinned, revision=state['revision'] + 1)
        return True, '', changed
    remaining = list(state['entries'])
    drawn = next((entry for entry in remaining if entry[0] == state['drawn_key']), None)
    if drawn is not None:
        remaining.remove(drawn)
    entries = []
    for index, tile in enumerate(order):
        if index == draw_index:
            entries.append(drawn)
        else:
            match = next(entry for entry in remaining if entry[1] == tile)
            remaining.remove(match)
            entries.append(match)
    changed = entries != state['entries'] or pinned != state['draw_pinned']
    if changed:
        state.update(entries=entries, draw_pinned=pinned, revision=state['revision'] + 1)
    return True, '', changed


async def handle_reorder(game_server, connection_id, message, websocket):
    game = game_server.gamestate_manager.get_game_state_by_gamestate_id(message.get('gamestate_id'))
    connection = game_server.players.get(connection_id)
    player = next((p for p in getattr(game, 'player_list', ())
                   if connection is not None and p.user_id == connection.user_id), None)
    family = 'guobiao' if message.get('type') == 'gamestate/guobiao/reorder_hand' else 'riichi'
    response_type = f'gamestate/{family}/hand_order'
    if player is None or not enabled(game) or getattr(game, 'room_rule', None) != family:
        await websocket.send_json({'type': response_type, 'success': False, 'reason': 'unauthorized'})
        return
    accepted, reason, changed = reorder(game, player, message)
    request_id = message.get('request_id')
    if type(request_id) is not int:
        request_id = None
    from .boardcast import _send_response
    if changed:
        from ..public.game_record_manager import append_action_tick
        append_action_tick(game, ['wo', player.player_index, json.dumps(message['tiles']),
                                 message.get('draw_index', -1), 'T' if message.get('draw_pinned') else 'F',
                                 json.dumps([key for key, _ in player._realtime_hand['entries']])])
    # Snapshot before awaiting any queued send. A later action must not change
    # the revision/order associated with this acknowledgement.
    viewers = game.player_list if changed else [player]
    payloads = [(viewer.player_index, {
        'type': response_type, 'success': accepted, 'reason': reason,
        'gamestate_id': game.gamestate_id,
        'hand_order_request_id': request_id if viewer.player_index == player.player_index else None,
        'washizu_layouts': {player.player_index: layout_for_viewer(game, player, viewer.player_index)},
    }) for viewer in viewers]
    for viewer, payload in payloads:
        await _send_response(game, viewer, payload)
