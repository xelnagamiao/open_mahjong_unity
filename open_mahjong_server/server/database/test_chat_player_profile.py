"""Legacy room/lobby messages identify profile targets by their exact username."""
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, Mock, patch

from server.database import data_router


class ChatPlayerProfileTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.db = Mock()
        self.db.get_user_by_username.return_value = {'user_id': 101}
        self.db.get_user_settings.return_value = dict(
            user_id=101, username='12345', title_id=1, profile_image_id=9,
            avatar_frame_id=2202, character_id=4, voice_id=7)
        self.db.get_rank_data.return_value = {'ratings': {}}
        self.server = SimpleNamespace(db_manager=self.db)
        self.socket = SimpleNamespace(send_json=AsyncMock())

    async def lookup(self, username):
        with patch('server.database.guobiao.get_guobiao_stats.get_guobiao_history_stats', return_value=[]), \
             patch('server.database.guobiao.get_guobiao_stats.get_guobiao_fan_stats_split', return_value=({}, {})):
            await data_router.handle_data_message(self.server, 'connection', {
                'type': 'data/get_player_info_by_name', 'username': username,
                'data_request_id': 'profile-request'}, self.socket)
        self.socket.send_json.assert_awaited_once()
        return self.socket.send_json.call_args.args[0]

    async def test_numeric_username_is_resolved_as_a_name_and_keeps_profile_and_stats(self):
        result = await self.lookup('12345')
        self.db.get_user_by_username.assert_called_once_with('12345')
        self.db.get_user_settings.assert_called_once_with(101)
        self.assertTrue(result['success'])
        self.assertEqual(result['type'], 'data/get_player_info_by_name')
        self.assertEqual(result['data_request_id'], 'profile-request')
        self.assertEqual(result['player_info']['user_id'], 101)
        self.assertEqual(result['player_info']['user_settings']['avatar_frame_id'], 2202)
        self.assertEqual(result['rule_stats']['rule'], 'guobiao')

    async def test_unicode_name_is_looked_up_exactly(self):
        result = await self.lookup('玖柒、97')
        self.db.get_user_by_username.assert_called_once_with('玖柒、97')
        self.assertTrue(result['success'])

    async def test_unknown_or_renamed_user_returns_correlated_failure(self):
        self.db.get_user_by_username.return_value = None
        result = await self.lookup('old-name')
        self.assertFalse(result['success'])
        self.assertEqual(result['type'], 'data/get_player_info_by_name')
        self.assertEqual(result['data_request_id'], 'profile-request')
        self.assertNotIn('player_info', result)
        self.db.get_user_settings.assert_not_called()

    async def test_invalid_names_never_query_the_database(self):
        for username in (None, '', '   ', 101, {'name': 'player'}):
            with self.subTest(username=username):
                self.socket.send_json.reset_mock()
                result = await self.lookup(username)
                self.assertFalse(result['success'])
                self.assertEqual(result['data_request_id'], 'profile-request')
        self.db.get_user_by_username.assert_not_called()


if __name__ == '__main__':
    unittest.main()
