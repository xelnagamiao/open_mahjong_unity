import asyncio
from unittest.mock import patch

import pytest

from .test_lifecycle import server_fixture, USERS


@pytest.mark.parametrize('queue', ['qingque_elo_quanzhuang', 'qingque_elo_unlimited'])
@pytest.mark.parametrize('abandon', [False, True])
def test_all_offline_keeps_match_reserved_until_current_hand_is_settled(queue, abandon):
    async def run():
        server = server_fixture()
        server.match_manager.winning_queues = {uid: queue for uid in USERS}
        async def hold(*args):
            await asyncio.Event().wait()
        server.gamestate_manager.run_game = hold
        with patch.object(server.match_manager, 'MATCH_FOUND_DELAY_SECONDS', 0):
            await server.match_manager._start_game(queue, USERS)
        state = next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
        try:
            assert state.finish_round_when_offline
            for uid in USERS:
                if abandon:
                    await server.gamestate_manager.abandon_player_game(str(uid), uid)
                else:
                    await server.gamestate_manager.player_disconnect(uid)
            assert state.unlimited_session.forced_end
            assert state.unlimited_session.should_end_after_round()
            assert state.lifecycle_state == 'running'
            assert server.match_manager.committed_users == set(USERS)
            assert all(server.gamestate_manager.user_id_to_game_state[uid] is state for uid in USERS)
        finally:
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
        assert not server.match_manager.committed_users
    asyncio.run(run())
