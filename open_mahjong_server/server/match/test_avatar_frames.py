"""Ranked games preserve equipped frames through the actual match startup path."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .rating_rules import QUEUES
from .test_lifecycle import USERS, server_fixture
from ..gamestate.game_guobiao.boardcast import _build_game_start_payload_for_viewer as guobiao_snapshot
from ..gamestate.game_mmcr.boardcast import _build_game_start_payload_for_viewer as qingque_snapshot
from ..gamestate.game_riichi.boardcast import _build_player_info as riichi_player_info
from ..gamestate.game_sichuan.boardcast import _build_players_info as sichuan_players_info
from ..gamestate.game_hongque.boardcast import build_state as hongque_snapshot
from ..gamestate.public.game_record_manager import build_player_record_infos
from ..response import PlayerInfo


def player_snapshots(game, viewer):
    """Use the same appearance serializers as game start, reconnect and live spectators."""
    if game.room_rule == 'hongque':
        return [hongque_snapshot(game, viewer.player_index, sync_mode=mode)['players']
                for mode in ('round_start', 'reconnect')]
    if game.room_rule == 'guobiao':
        players = guobiao_snapshot(game, viewer.player_index)['players_info']
    elif game.room_rule == 'qingque':
        players = qingque_snapshot(game, viewer.player_index)['players_info']
    elif game.room_rule == 'riichi':
        players = [riichi_player_info(player, viewer.user_id, viewer.player_index)
                   for player in game.player_list]
    else:
        players = sichuan_players_info(game, viewer.user_id)
    return [[PlayerInfo(**player).model_dump(exclude_none=True) for player in players]]


@pytest.mark.parametrize('queue_type', list(QUEUES))
def test_match_start_preserves_each_players_frame_in_client_snapshots(queue_type):
    async def run():
        server = server_fixture()
        users = USERS[:QUEUES[queue_type].player_count]
        settings = {
            USERS[0]: {'avatar_frame_id': 2202},
            USERS[1]: {'avatar_frame_id': 2201},
            USERS[2]: {},  # Settings from before avatar frames were introduced.
            USERS[3]: None,
        }
        expected = {uid: (settings[uid] or {}).get('avatar_frame_id', 0) for uid in users}
        server.db_manager.get_user_settings.side_effect = settings.get

        async def hold_game(*args):
            await asyncio.Event().wait()

        server.gamestate_manager.run_game = hold_game
        try:
            with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()):
                await server.match_manager._start_game(queue_type, users)
            games = server.gamestate_manager.gamestate_id_to_game_state
            assert len(games) == 1, 'Match startup must succeed'
            game = next(iter(games.values()))
            assert {p.user_id: getattr(p, 'avatar_frame_used', 0) for p in game.player_list} == expected
            for viewer in game.player_list:
                for snapshot in player_snapshots(game, viewer):
                    assert {p['user_id']: p['avatar_frame_used'] for p in snapshot} == expected

            game.spectator_manager.record_game_title()
            assert {p['user_id']: p['avatar_frame_used']
                    for p in game.spectator_manager.players_settings} == expected
            assert {p.user_id: p.avatar_frame_used
                    for p in build_player_record_infos(game.player_list)} == expected
        finally:
            for game_id in list(server.gamestate_manager.gamestate_id_to_game_state):
                await server.gamestate_manager.cleanup_game_state_complete(game_id)

    asyncio.run(run())
