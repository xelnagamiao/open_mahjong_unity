"""Hongque offers eight and sixteen actual hands, an independent Elo pool and shared settlement."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .rank_calculator import queue_type_to_room_config
from .test_lifecycle import server_fixture, USERS
from ..gamestate.game_hongque.HongqueGameState import HongqueGameState
from ..gamestate.game_hongque.boardcast import broadcast_game_end, build_state
from ..gamestate.game_hongque.record import persist_and_remember
from ..gamestate.public.game_record_manager import init_game_record

QUEUE = 'hongque_elo_quanzhuang'


@pytest.mark.parametrize('room_type', ['match', 'custom'])
@pytest.mark.parametrize('queue,rounds', [('hongque_elo_banzhuang', 8), ('hongque_elo_quanzhuang', 16)])
def test_final_record_and_packet_include_elo_only_for_ranked_games(room_type, queue, rounds):
    async def run():
        server = server_fixture()
        config = dict(queue_type_to_room_config(queue), room_id=12345,
                      room_type=room_type, player_list=USERS, player_settings={})
        game = HongqueGameState(server, config, db_manager=server.db_manager)
        game.match_queue_type = queue
        for mode in ('round_start', 'reconnect'):
            snapshot = build_state(game, 0, sync_mode=mode)
            assert snapshot['room_type'] == room_type and snapshot['max_round'] == rounds
        init_game_record(game)
        for player, score in zip(game.players, [40, 30, 20, 10]):
            player.score = score
        deltas = [8, 2.67, -2.67, -8] if rounds == 8 else [16, 5.33, -5.33, -16]
        result = {str(uid): dict(rating_rule='hongque', rating_system='elo',
            rank_before='', rank_after='', score_before=0, score_after=0, rating_pt=0,
            elo_before=1500, elo_after=1500+delta, elo_delta=delta, rating_games=1)
            for uid, delta in zip(USERS, deltas)}
        server.db_manager.settle_rated_game.return_value = result
        server.db_manager.store_hongque_game_record.return_value = 'hongque-record'
        assert persist_and_remember(game) == 'hongque-record'
        stored = server.db_manager.store_hongque_game_record.call_args.args
        assert stored[3] == (f'{rounds}/4_rank' if room_type == 'match' else f'{rounds}/4')
        if room_type == 'match':
            call = server.db_manager.settle_rated_game.call_args
            assert call.args[:2] == (game.gamestate_id, queue)
            assert call.args[2] == [dict(user_id=uid, place=i+1) for i, uid in enumerate(USERS)]
            assert stored[0]['game_title']['rating_result'] == result
            assert stored[0]['game_title']['rating_rule'] == 'hongque'
        else:
            server.db_manager.settle_rated_game.assert_not_called()
            assert 'rating_result' not in stored[0]['game_title']
        await broadcast_game_end(game)
        packet = server.user_id_to_connection[USERS[0]].websocket.send_json.call_args.args[0]
        assert packet['type'] == 'gamestate/hongque/game_end'
        final = packet['game_end_info']['player_final_data']['0']
        assert final['user_id'] == USERS[0] and final['rank'] == 1
        if room_type == 'match':
            assert final['rating_rule'] == 'hongque' and final['elo_after'] == 1500 + deltas[0]
            assert final['elo_delta'] == deltas[0] and final['rating_games'] == 1
        else:
            assert 'rating_rule' not in final and 'elo_after' not in final
    asyncio.run(run())


def test_disconnected_hongque_player_is_offline_when_match_starts():
    async def run():
        server = server_fixture()
        server.match_manager.winning_queues = dict.fromkeys(USERS, QUEUE)
        server.user_id_to_connection.pop(USERS[0])
        server.players.pop(str(USERS[0]))
        async def hold(*args):
            await asyncio.Event().wait()
        server.gamestate_manager.run_game = hold
        with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()):
            await server.match_manager._start_game(QUEUE, USERS)
        game = next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
        assert isinstance(game, HongqueGameState)
        assert game.max_round == 16 and game.sub_rule == 'hongque/v1.6'
        assert not next(p for p in game.players if p.user_id == USERS[0]).online
        await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
        assert not server.match_manager.committed_users
        assert server.match_manager.playing_counts[QUEUE] == 0
    asyncio.run(run())
