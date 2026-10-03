import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

from server.gamestate.gamestate_manager import GameStateManager
from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
from server.game_calculation.game_calculation_service import GameCalculationService
from server.match.match_manager import MatchManager
from server.room.room_manager import RoomManager

USERS = [910001, 910002, 910003, 910004]
QUEUE = 'beginner_dongfeng'


def server_fixture():
    db = MagicMock()
    db.get_rank_data.return_value = {'guobiao_rank': '四段', 'guobiao_score': 100}
    db.get_user_sponsor_mcrpl.return_value = {'is_mcrpl_qualified': True}
    db.get_user_settings.return_value = {}
    server = SimpleNamespace(db_manager=db, calculation_service=GameCalculationService(),
                             players={}, user_id_to_connection={}, friend_manager=None)
    server.room_manager = RoomManager(server)
    server.gamestate_manager = GameStateManager(server)
    server.match_manager = MatchManager(server)
    for uid in USERS:
        player = SimpleNamespace(user_id=uid, username=f'QA{uid}', current_room_id=None,
                                 is_tourist=False, websocket=SimpleNamespace(send_json=AsyncMock()))
        server.players[str(uid)] = player
        server.user_id_to_connection[uid] = player
    server.match_manager.committed_users.update(USERS)
    server.match_manager.winning_queues.update({uid: QUEUE for uid in USERS})
    return server


def test_before_start_all_disconnect_releases_commit_and_room():
    async def run():
        server = server_fixture()
        server.user_id_to_connection.clear()
        server.players.clear()
        with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()), \
             patch.object(GuobiaoGameState, 'run_game_loop', new=AsyncMock()):
            await server.match_manager._start_game(QUEUE, USERS)
        assert not server.match_manager.committed_users
        assert not server.match_manager.gamestate_to_match
        assert not server.gamestate_manager.gamestate_id_to_game_state
        assert not server.room_manager.match_room_ids
        assert server.match_manager.playing_counts[QUEUE] == 0
    asyncio.run(run())


def test_before_start_one_disconnect_marks_player_offline():
    async def run():
        server = server_fixture()
        server.user_id_to_connection.pop(USERS[0])
        server.players.pop(str(USERS[0]))
        with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()), \
             patch.object(GuobiaoGameState, 'run_game_loop', new=AsyncMock()):
            await server.match_manager._start_game(QUEUE, USERS)
        game = next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
        offline = next(p for p in game.player_list if p.user_id == USERS[0])
        assert 'offline' in offline.tag_list
        await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
        assert not server.match_manager.committed_users
    asyncio.run(run())


def test_unhandled_game_error_releases_match_mappings():
    async def run():
        server = server_fixture()
        with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()), \
             patch.object(GuobiaoGameState, 'game_loop_chinese', new=AsyncMock(side_effect=RuntimeError('injected runtime failure'))):
            await server.match_manager._start_game(QUEUE, USERS)
            game = next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
            await game.game_task
        assert game.close_reason == 'runtime_error'
        assert not server.match_manager.committed_users
        assert not server.match_manager.gamestate_to_match
        assert not server.gamestate_manager.gamestate_id_to_game_state
        assert not server.gamestate_manager.user_id_to_game_state
        assert not server.room_manager.match_room_ids
        assert server.match_manager.playing_counts[QUEUE] == 0
    asyncio.run(run())
