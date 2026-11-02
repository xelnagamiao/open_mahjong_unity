"""Rename notifications refresh only the renamed player's private inventory."""
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, patch

from fastapi import FastAPI
from server.webapi.admin_user import AdminSyncUsernameBody, register_admin_user_routes


class RenameInventoryNotificationTests(unittest.IsolatedAsyncioTestCase):
    def route(self, players):
        app = FastAPI()
        server = SimpleNamespace(user_id_to_connection=players, db_manager=object())
        register_admin_user_routes(app, server)
        return server, next(route.endpoint for route in app.routes if route.path == '/admin/user/sync-username')

    async def test_online_rename_sends_current_inventory_only_to_owner(self):
        owner = SimpleNamespace(username='OldName', websocket=SimpleNamespace(send_json=AsyncMock()))
        observer = SimpleNamespace(username='Observer', websocket=SimpleNamespace(send_json=AsyncMock()))
        server, sync = self.route({101: owner, 102: observer})
        state = dict(user_id=101, rename_card_count=2, rename_card_usable_count=2)
        with patch('server.database.inventory.get_inventory', return_value=state) as read:
            result = await sync(AdminSyncUsernameBody(user_id=101, username='NewName'))
        self.assertTrue(result['online'])
        self.assertEqual(owner.username, 'NewName')
        read.assert_called_once_with(server.db_manager, 101)
        owner.websocket.send_json.assert_awaited_once_with(
            dict(type='inventory/state', success=True, inventory_state=state))
        observer.websocket.send_json.assert_not_awaited()

    async def test_offline_rename_needs_no_inventory_notification(self):
        _, sync = self.route({})
        with patch('server.database.inventory.get_inventory') as read:
            result = await sync(AdminSyncUsernameBody(user_id=101, username='NewName'))
        self.assertFalse(result['online'])
        read.assert_not_called()

    async def test_notification_failure_does_not_turn_committed_rename_into_failure(self):
        owner = SimpleNamespace(username='OldName', websocket=SimpleNamespace(send_json=AsyncMock(side_effect=RuntimeError('disconnected'))))
        _, sync = self.route({101: owner})
        with patch('server.database.inventory.get_inventory', return_value={'user_id': 101}), self.assertLogs('server.webapi.admin_user', level='ERROR'):
            result = await sync(AdminSyncUsernameBody(user_id=101, username='NewName'))
        self.assertTrue(result['success'])
        self.assertEqual(owner.username, 'NewName')
