"""Exercise auth functions without importing server.py's live servers/database startup."""
import ast
import logging
from pathlib import Path
import re
import secrets
from types import SimpleNamespace
from typing import Any, Dict, Optional
import unittest
from unittest.mock import AsyncMock, Mock

from .username_validation import normalize_username, validate_username


def load_functions(path, names, namespace):
    tree = ast.parse(path.read_text(encoding="utf-8"))
    nodes = [node for node in tree.body if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name in names]
    assert len(nodes) == len(names)
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), "exec"), namespace)


class PlayerRegistrationTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.db = Mock()
        self.db.get_active_ip_ban.return_value = None
        self.db.get_user_by_username.return_value = None
        self.db.create_user.return_value = 42
        self.limiter = Mock()
        self.limiter.can_register.return_value = True
        self.finalize = AsyncMock(return_value=SimpleNamespace(success=True))
        self.ns = dict(
            Response=SimpleNamespace, Optional=Optional, Dict=Dict, Any=Any,
            re=re, secrets=secrets, logging=logging, db_manager=self.db,
            ip_registration_limiter=self.limiter, _finalize_player_login=self.finalize,
            normalize_username=normalize_username, validate_username=validate_username,
        )
        load_functions(Path(__file__).parents[1] / "server.py",
                       {"validate_password", "player_register", "player_login"}, self.ns)

    async def register(self, **overrides):
        values = dict(username="测试玩家", password="Secret123!", confirm_password="Secret123!",
                      email=" Player@Example.COM ", client_ip="127.0.0.1")
        values.update(overrides)
        return await self.ns["player_register"](**values)

    async def test_register_records_normalized_email_and_logs_in_without_mail(self):
        result = await self.register()
        self.assertTrue(result.success)
        self.db.create_user.assert_called_once_with("测试玩家", "Secret123!", is_tourist=False, email="player@example.com")
        self.limiter.record_registration.assert_called_once_with("127.0.0.1")
        self.finalize.assert_awaited_once()

    async def test_invalid_registration_never_creates_account(self):
        for values in [dict(email=""), dict(email="wrong"), dict(email="a b@c.com"),
                       dict(email="a@b.com\nextra"), dict(email="a" * 250 + "@b.com"),
                       dict(username=""), dict(password="short"), dict(password="has space"),
                       dict(confirm_password="different"), dict(email=None), dict(username={})]:
            with self.subTest(values=values):
                self.assertFalse((await self.register(**values)).success)
        self.db.create_user.assert_not_called()
        self.finalize.assert_not_awaited()

    async def test_duplicate_username_rejected(self):
        self.db.get_user_by_username.return_value = {"user_id": 5}
        self.assertFalse((await self.register()).success)
        self.db.create_user.assert_not_called()

    async def test_registration_limit_rejected(self):
        self.limiter.can_register.return_value = False
        self.assertFalse((await self.register()).success)
        self.db.create_user.assert_not_called()

    async def test_banned_ip_rejected(self):
        self.db.get_active_ip_ban.return_value = {"reason": "ban"}
        self.assertFalse((await self.register()).success)
        self.db.create_user.assert_not_called()

    async def test_failed_insert_does_not_start_session_or_consume_quota(self):
        self.db.create_user.return_value = None
        self.assertFalse((await self.register()).success)
        self.finalize.assert_not_awaited()
        self.limiter.record_registration.assert_not_called()

    async def test_unknown_login_does_not_register(self):
        result = await self.ns["player_login"]("newplayer", "Secret123!")
        self.assertFalse(result.success)
        self.db.create_user.assert_not_called()

    async def test_existing_account_without_email_still_logs_in(self):
        self.db.get_user_by_username.return_value = {"user_id": 42, "password": "hash"}
        self.db.verify_password.return_value = True
        self.db.is_login_ban_active.return_value = False
        self.assertTrue((await self.ns["player_login"]("oldplayer", "Secret123!")).success)
        self.db.create_user.assert_not_called()

    async def test_wrong_password_rejected(self):
        self.db.get_user_by_username.return_value = {"user_id": 42, "password": "hash"}
        self.db.verify_password.return_value = False
        self.assertFalse((await self.ns["player_login"]("oldplayer", "Secret123!")).success)
        self.finalize.assert_not_awaited()

    async def test_tourist_login_still_creates_temporary_account(self):
        self.assertTrue((await self.ns["player_login"]("", "", is_tourist=True)).success)
        self.assertTrue(self.db.create_user.call_args.kwargs["is_tourist"])
        self.limiter.record_registration.assert_not_called()


class RegistrationPersistenceTests(unittest.TestCase):
    def setUp(self):
        path = Path(__file__).parents[1] / "database" / "db_manager.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        owner = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "DatabaseManager")
        method = next(node for node in owner.body if isinstance(node, ast.FunctionDef) and node.name == "create_user")
        self.ns = dict(Optional=Optional, Error=RuntimeError, logger=Mock())
        exec(compile(ast.Module(body=[method], type_ignores=[]), str(path), "exec"), self.ns)
        self.db = Mock()
        self.conn = self.db._get_connection.return_value
        self.cursor = self.conn.cursor.return_value
        self.cursor.fetchone.return_value = (42,)
        self.db._hash_password.return_value = "hashed-password"

    def test_email_and_unverified_status_saved_with_account_transaction(self):
        result = self.ns["create_user"](self.db, "player", "Secret123!", email="player@example.com")
        self.assertEqual(result, 42)
        sql, args = self.cursor.execute.call_args_list[0].args
        self.assertIn("email, email_verified_at", sql)
        self.assertIn("%s, NULL)", sql)
        self.assertEqual(args, ("player", "hashed-password", False, "player@example.com"))
        self.assertEqual(self.cursor.execute.call_count, 4)
        self.conn.commit.assert_called_once()

    def test_initialization_failure_rolls_back_whole_account(self):
        self.cursor.execute.side_effect = [None, RuntimeError("settings failed")]
        result = self.ns["create_user"](self.db, "player", "Secret123!", email="player@example.com")
        self.assertIsNone(result)
        self.conn.commit.assert_not_called()
        self.conn.rollback.assert_called_once()


if __name__ == "__main__":
    unittest.main()
