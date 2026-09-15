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
        self.db.get_users_by_login_email.return_value = []
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

    def email_account(self):
        self.db.get_users_by_login_email.return_value = [
            {"user_id": 42, "username": "实际用户名", "password": "hash", "email_verified_at": None}]
        self.db.verify_password.return_value = True
        self.db.is_login_ban_active.return_value = False

    async def test_email_login_normalizes_and_uses_canonical_username(self):
        self.email_account()
        result = await self.ns["player_login"](" Player@Example.COM ", "Secret123!", login_type="account")
        self.assertTrue(result.success)
        self.db.get_users_by_login_email.assert_called_once_with("player@example.com")
        self.finalize.assert_awaited_once_with(42, "实际用户名", is_tourist=False,
                                              client_ip="unknown", success_message="登录成功")
        self.db.verify_password.assert_called_once_with("Secret123!", "hash")
        self.db.create_user.assert_not_called()

    async def test_email_wrong_password_and_ban_cannot_start_session(self):
        self.email_account()
        self.db.verify_password.return_value = False
        self.assertFalse((await self.ns["player_login"]("a@example.com", "Secret123!", login_type="account")).success)
        self.db.verify_password.return_value = True
        self.db.is_login_ban_active.return_value = True
        self.assertFalse((await self.ns["player_login"]("a@example.com", "Secret123!", login_type="account")).success)
        self.finalize.assert_not_awaited()

    async def test_duplicate_email_is_never_resolved_by_first_row_or_password(self):
        self.email_account()
        self.db.get_users_by_login_email.return_value *= 2
        result = await self.ns["player_login"]("a@example.com", "Secret123!", login_type="account")
        self.assertFalse(result.success)
        self.assertIn("用户名", result.message)
        self.db.verify_password.assert_not_called()
        self.finalize.assert_not_awaited()

    async def test_email_shaped_username_has_priority_without_password_fallback(self):
        self.db.get_user_by_username.return_value = {"user_id": 9, "password": "hash"}
        self.db.verify_password.return_value = False
        self.assertFalse((await self.ns["player_login"]("a@example.com", "Secret123!", login_type="account")).success)
        self.db.get_users_by_login_email.assert_not_called()

    async def test_legacy_clients_do_not_change_login_resolution(self):
        self.assertFalse((await self.ns["player_login"]("a@example.com", "Secret123!")).success)
        self.db.get_users_by_login_email.assert_not_called()

    async def test_missing_email_never_creates_account(self):
        self.assertFalse((await self.ns["player_login"]("a@example.com", "Secret123!", login_type="account")).success)
        self.db.create_user.assert_not_called()

    async def test_invalid_login_types_and_identifiers_rejected(self):
        for identifier, mode in [(None, "account"), ({}, "account"), ("a@example.com", "invalid"),
                                 ("a@example.com", {}), ("a" * 250 + "@b.com", "account")]:
            with self.subTest(identifier=identifier, mode=mode):
                self.assertFalse((await self.ns["player_login"](identifier, "Secret123!", login_type=mode)).success)
        self.db.get_users_by_login_email.assert_not_called()

    async def test_email_login_respects_ip_bans_before_lookup(self):
        self.db.get_active_ip_ban.return_value = {"reason": "ban"}
        self.assertFalse((await self.ns["player_login"]("a@example.com", "Secret123!", login_type="account")).success)
        self.db.get_users_by_login_email.assert_not_called()


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


class EmailLookupPersistenceTests(unittest.TestCase):
    def setUp(self):
        path = Path(__file__).parents[1] / "database" / "db_manager.py"
        owner = next(n for n in ast.parse(path.read_text(encoding="utf-8")).body
                     if isinstance(n, ast.ClassDef) and n.name == "DatabaseManager")
        method = next(n for n in owner.body if isinstance(n, ast.FunctionDef) and n.name == "get_users_by_login_email")
        self.ns = dict(Error=RuntimeError, RealDictCursor=object(), logger=Mock())
        exec(compile(ast.Module(body=[method], type_ignores=[]), str(path), "exec"), self.ns)
        self.db = Mock()
        self.conn = self.db._get_connection.return_value
        self.cursor = self.conn.cursor.return_value

    def test_parameterized_case_insensitive_non_tourist_lookup_keeps_duplicates(self):
        self.cursor.fetchall.return_value = [{"user_id": 1}, {"user_id": 2}]
        self.assertEqual(len(self.ns["get_users_by_login_email"](self.db, " A@Example.com ")), 2)
        sql, args = self.cursor.execute.call_args.args
        self.assertIn("LOWER(email) = %s", sql)
        self.assertIn("is_tourist = FALSE", sql)
        self.assertIn("LIMIT 2", sql)
        self.assertEqual(args, ("a@example.com",))
        self.cursor.close.assert_called_once()
        self.db._put_connection.assert_called_once_with(self.conn)

    def test_lookup_failure_fails_closed_and_returns_connection(self):
        self.cursor.execute.side_effect = RuntimeError("query failed")
        self.assertEqual(self.ns["get_users_by_login_email"](self.db, "a@example.com"), [])
        self.conn.rollback.assert_called_once()
        self.cursor.close.assert_called_once()
        self.db._put_connection.assert_called_once_with(self.conn)


if __name__ == "__main__":
    unittest.main()
