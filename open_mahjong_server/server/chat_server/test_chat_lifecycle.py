"""Chat credentials survive an independent Python restart (no production DB)."""
import asyncio
import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from . import chat_server


class ChatCredentialLifecycleTests(unittest.TestCase):
    def test_restart_preserves_credentials_for_running_go_process(self):
        with tempfile.TemporaryDirectory() as directory:
            module_file = str(Path(directory) / 'chat_server.py')
            async def scenario():
                with patch.object(chat_server, '__file__', module_file):
                    first = chat_server.ChatServer()
                    await first.generate_secret_key()
                    go_key = (Path(directory) / 'secret_key.txt').read_text().strip()
                    expected = hashlib.sha256(('new-user' + go_key).encode()).hexdigest()
                    self.assertEqual(await first.hash_username('new-user'), expected)
                    restarted = chat_server.ChatServer()
                    await restarted.generate_secret_key()
                    self.assertEqual(await restarted.hash_username('new-user'), expected)
                    self.assertEqual((Path(directory) / 'secret_key.txt').read_text().strip(), go_key)
            asyncio.run(scenario())

    def test_failed_key_write_does_not_report_success(self):
        async def scenario():
            service = chat_server.ChatServer()
            with patch('builtins.open', side_effect=PermissionError('isolated denied')):
                with self.assertRaises(PermissionError):
                    await service.save_secret_key_to_file('isolated-test-secret')
        asyncio.run(scenario())


if __name__ == '__main__':
    unittest.main()
