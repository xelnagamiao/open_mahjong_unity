"""Inventory authentication must follow the deployed Web port and fail closed."""
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError, URLError

from fastapi import HTTPException
from server.database.inventory_router import _admin_auth_url, _verify_admin


class InventoryAdminAuthTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / '.env'
        self.env = patch.dict('os.environ', {'INVENTORY_ADMIN_AUTH_URL': ''})
        self.env.start()
        self.addCleanup(self.env.stop)
        self.file = patch('server.database.inventory_router.WEB_ENV_PATH', self.path)
        self.file.start()
        self.addCleanup(self.file.stop)

    def test_default_local_port_without_web_env(self):
        self.assertEqual(_admin_auth_url(), 'http://127.0.0.1:3000/api/admin/auth/me')

    def test_production_web_port_and_bearer_identity_are_preserved(self):
        self.path.write_text('NODE_ENV=production\nPORT="8082" # web service\n', encoding='utf-8')
        response = io.BytesIO(json.dumps({'success': True, 'data': {'user_id': 101}}).encode())
        with patch('server.database.inventory_router.urlopen', return_value=response) as opened:
            self.assertEqual(_verify_admin('Bearer test-token'), 101)
        request = opened.call_args.args[0]
        self.assertEqual(request.full_url, 'http://127.0.0.1:8082/api/admin/auth/me')
        self.assertEqual(request.get_header('Authorization'), 'Bearer test-token')

    def test_separate_host_can_use_explicit_auth_url(self):
        self.path.write_text('PORT=8082', encoding='utf-8')
        with patch.dict('os.environ', {'INVENTORY_ADMIN_AUTH_URL': 'https://admin.internal/api/admin/auth/me'}):
            self.assertEqual(_admin_auth_url(), 'https://admin.internal/api/admin/auth/me')

    def test_invalid_port_is_not_silently_replaced(self):
        for value in ['0', '65536', '8082/path', 'invalid', '']:
            with self.subTest(value=value):
                self.path.write_text('PORT=' + value, encoding='utf-8')
                with self.assertRaises(HTTPException) as error:
                    _admin_auth_url()
                self.assertEqual(error.exception.status_code, 503)

    def test_rejected_authentication_keeps_status(self):
        for code in [401, 403, 429]:
            with self.subTest(code=code), patch('server.database.inventory_router.urlopen',
                    side_effect=HTTPError('http://local', code, 'rejected', {}, None)):
                with self.assertRaises(HTTPException) as error:
                    _verify_admin('Bearer rejected')
                self.assertEqual(error.exception.status_code, code)

    def test_outage_and_invalid_auth_response_do_not_authorize(self):
        with patch('server.database.inventory_router.urlopen', side_effect=URLError('offline')):
            with self.assertRaises(HTTPException) as error:
                _verify_admin('Bearer test')
            self.assertEqual(error.exception.status_code, 503)
        for data in [{'data': {'user_id': 101}}, {'success': True, 'data': {'user_id': True}},
                     {'success': True, 'data': {'user_id': 0}}, {'success': True, 'data': None}]:
            with self.subTest(data=data), patch('server.database.inventory_router.urlopen',
                    return_value=io.BytesIO(json.dumps(data).encode())):
                with self.assertRaises(HTTPException) as error:
                    _verify_admin('Bearer test')
                self.assertEqual(error.exception.status_code, 503)


if __name__ == '__main__':
    unittest.main()
