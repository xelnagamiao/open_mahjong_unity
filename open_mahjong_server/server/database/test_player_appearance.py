"""Equipment appearance survives login and player-info response serialization."""
import ast
from contextlib import ExitStack
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, Mock, patch

from server.database import data_router


class PlayerAppearanceResponseTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.settings = dict(user_id=101, username='appearance-test', title_id=1,
                             profile_image_id=9, character_id=4, voice_id=7,
                             avatar_frame_id=2202)
        self.db = Mock()
        self.db.get_user_settings.side_effect = lambda _: self.settings.copy()
        self.db.get_rank_data.return_value = {'ratings': {}}
        self.db.get_user_config.return_value = None
        self.db.get_user_sponsor_mcrpl.return_value = None
        self.server = SimpleNamespace(db_manager=self.db)
        self.socket = SimpleNamespace(send_json=AsyncMock())

    def assert_appearance(self, settings):
        for field in ('profile_image_id', 'character_id', 'voice_id'):
            self.assertEqual(settings[field], self.settings[field])
        self.assertEqual(settings['avatar_frame_id'], self.settings.get('avatar_frame_id', 0))

    async def test_player_info_in_each_stats_route_preserves_equipped_frame(self):
        routes = (
            ('guobiao', 'get_guobiao_fan_stats_split', ({}, {})),
            ('riichi', 'get_riichi_fan_stats_total', {}),
            ('qingque', 'get_qingque_fan_stats_total', {}),
            ('classical', 'get_classical_fan_stats_total', {}),
            ('jiandan', 'get_jiandan_fan_stats_total', {}),
        )
        for frame in (2202, 2201, None):
            if frame is None:
                self.settings.pop('avatar_frame_id', None)
            else:
                self.settings['avatar_frame_id'] = frame
            for rule, fan_function, fans in routes:
                with self.subTest(rule=rule, frame=frame), ExitStack() as patches:
                    module = f'server.database.{rule}.get_{rule}_stats'
                    patches.enter_context(patch(f'{module}.get_{rule}_history_stats', return_value=[]))
                    patches.enter_context(patch(f'{module}.{fan_function}', return_value=fans))
                    self.socket.send_json.reset_mock()
                    handler = getattr(data_router, f'handle_get_{rule}_stats')
                    await handler(self.server, 'connection', {'userid': '101', 'need_player_info': True}, self.socket)
                    self.socket.send_json.assert_awaited_once()
                    response = self.socket.send_json.call_args.args[0]
                    self.assertTrue(response['success'])
                    self.assert_appearance(response['player_info']['user_settings'])

    async def test_login_response_preserves_equipped_frame(self):
        # Isolate the real response builder from server.py's startup side effects.
        source_path = Path(__file__).resolve().parents[1] / 'server.py'
        source = ast.parse(source_path.read_text(encoding='utf-8'), filename=str(source_path))
        function = next(node for node in source.body
                        if isinstance(node, ast.AsyncFunctionDef) and node.name == '_finalize_player_login')
        namespace = dict(__package__='server', Response=data_router.Response,
                         db_manager=self.db, logging=Mock(),
                         chat_server=SimpleNamespace(hash_username=AsyncMock(return_value='test-only-key')))
        exec(compile(ast.Module(body=[function], type_ignores=[]), str(source_path), 'exec'), namespace)
        for frame in (2202, 2201, None):
            if frame is None:
                self.settings.pop('avatar_frame_id', None)
            else:
                self.settings['avatar_frame_id'] = frame
            with self.subTest(frame=frame):
                response = await namespace['_finalize_player_login'](
                    101, 'appearance-test', is_tourist=False, client_ip='127.0.0.1', success_message='ok')
                self.assertTrue(response.success)
                self.assert_appearance(response.model_dump(exclude_none=True)['user_settings'])


if __name__ == '__main__':
    unittest.main()
