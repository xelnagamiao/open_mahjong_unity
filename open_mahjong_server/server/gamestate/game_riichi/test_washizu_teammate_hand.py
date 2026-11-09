"""Per-viewer opaque teammate faces without disclosing enemy or private hand order."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_washizu_doubles import captain_game
from .boardcast import broadcast_game_start, broadcast_do_action
from .washizu import public_hands
from . import doubles
from ..public.ai.smart_bot_logic import count_visible_tiles, tile_to_34
from ...game_calculation.riichi.rule_config import normalize_riichi_config


def setup_game(enabled=True):
    game, sockets = captain_game()
    game.detailed_config['show_teammate_hand'] = enabled
    for p in game.player_list:
        p.hand_tiles = [44 + p.player_index, 2011 + p.player_index, 21 + p.player_index]
    return game, sockets


def check_projection(game, hands, viewer, enabled=True):
    for subject in game.player_list:
        expected = [2011 + subject.player_index, 0, 0]
        if enabled and viewer != subject.player_index and game.player_list[viewer].team_id == subject.team_id:
            expected = [2011 + subject.player_index, 21 + subject.player_index, 44 + subject.player_index]
        assert hands[subject.player_index] == expected


@pytest.mark.parametrize('enabled', [False, True])
@pytest.mark.parametrize('viewer', range(4))
def test_visibility_is_team_scoped_and_keeps_private_order(enabled, viewer):
    game, _ = setup_game(enabled)
    before = [list(p.hand_tiles) for p in game.player_list]
    check_projection(game, public_hands(game, viewer), viewer, enabled)
    assert before == [p.hand_tiles for p in game.player_list]
    # Global and delayed spectator projections never inherit one team's permission.
    assert all(hand == [2011 + i, 0, 0] for i, hand in public_hands(game).items())
    assert public_hands(game, -1) == public_hands(game, 999)


@pytest.mark.parametrize('bad', ['true', 1, None])
def test_invalid_or_unknown_team_does_not_grant_visibility(bad):
    game, _ = setup_game(bad)
    check_projection(game, public_hands(game, 0), 0, False)
    game.detailed_config['show_teammate_hand'] = True
    game.player_list[0].team_id = None
    assert public_hands(game, 0)[1] == [2012, 0, 0]
    game.sub_rule = 'riichi/washizu'
    assert public_hands(game, 0)[1] == [2012, 0, 0]


@pytest.mark.parametrize('enabled', [False, True])
def test_start_reconnect_spectator_and_actions_apply_the_same_visibility(enabled):
    async def run():
        game, sockets = setup_game(enabled)
        await broadcast_game_start(game)
        for viewer in range(4):
            info = next(c.args[0]['game_info'] for c in reversed(sockets[100 + viewer].websocket.send_json.call_args_list)
                        if 'game_info' in c.args[0])
            check_projection(game, info['washizu_hands'], viewer, enabled)
            assert info['detailed_config']['show_teammate_hand'] is enabled
            assert sum('hand_tiles' in p for p in info['players_info']) == 1
        game.game_status = 'waiting_hand_action'
        game.action_dict = {i: [] for i in range(4)}
        for viewer in range(4):
            await game.player_reconnect(100 + viewer)
            info = next(c.args[0]['game_info'] for c in reversed(sockets[100 + viewer].websocket.send_json.call_args_list)
                        if 'game_info' in c.args[0])
            check_projection(game, info['washizu_hands'], viewer, enabled)
            spectator = SimpleNamespace(websocket=AsyncMock())
            game.game_server.user_id_to_connection[500] = spectator
            await game.send_realtime_spectator_snapshot(500, viewer)
            info = spectator.websocket.send_json.call_args_list[0].args[0]['game_info']
            check_projection(game, info['washizu_hands'], viewer, enabled)
        await broadcast_do_action(game, ['deal'], 0, deal_tile=44)
        for viewer in range(4):
            action = sockets[100 + viewer].websocket.send_json.call_args_list[-1].args[0]['do_action_info']
            check_projection(game, action['washizu_hands'], viewer, enabled)
            assert action.get('deal_tile') == (44 if viewer == 0 else None)
        # A subsequent discard replaces the authorized face instead of retaining it.
        game.player_list[0].hand_tiles.remove(44)
        await broadcast_do_action(game, ['cut'], 0, cut_tile=44)
        action = sockets[101].websocket.send_json.call_args_list[-1].args[0]['do_action_info']
        assert action['washizu_hands'][0] == ([2011, 21] if enabled else [2011, 0])
        assert all(44 not in hand for hand in action['washizu_hands'].values())
    asyncio.run(run())


def test_rotation_uses_current_team_and_record_profile_saves_the_option():
    game, _ = setup_game()
    # Move the roster to different seats. Team membership must not use seat parity.
    for _ in range(4):
        game._rotate_seats()
        for viewer in game.player_list:
            hands = public_hands(game, viewer.player_index)
            for subject in game.player_list:
                known = viewer is not subject and viewer.team_id == subject.team_id
                assert (0 not in hands[subject.player_index]) is known
    doubles.record_profile(game)
    assert game.game_record['game_title']['washizu_doubles_profile']['teammate_hidden_hands'] is True
    assert normalize_riichi_config({'show_teammate_hand': True}, game.sub_rule)['show_teammate_hand'] is True
    assert normalize_riichi_config({}, game.sub_rule)['show_teammate_hand'] is False


def test_remaining_counts_use_visible_ally_tiles_once_and_ignore_enemy_opaque():
    game, _ = setup_game(False)
    public = count_visible_tiles(game, 0)
    game.detailed_config['show_teammate_hand'] = True
    known = count_visible_tiles(game, 0)
    expected = deepcopy(public)
    for tile in [22, 45]:
        expected[tile_to_34(tile)] += 1
    assert known == expected
