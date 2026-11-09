"""Mail claims use an isolated PostgreSQL schema, never existing players."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
import os
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import psycopg2
from psycopg2.pool import ThreadedConnectionPool

from server.database.inventory import InventoryError, ensure_inventory_tables, get_inventory
from server.database.mail import ensure_mail_tables, list_mail, read_mail, claim_mail, send_mail
from server.database.mail_router import handle_mail_message
from server.database.mail_router import register_mail_routes


@unittest.skipUnless(os.environ.get('INVENTORY_TEST_DATABASE_URL'), 'requires PostgreSQL test connection')
class MailDatabaseTests(unittest.TestCase):
    def setUp(self):
        self.schema = 'mail_test_' + uuid.uuid4().hex
        self.admin = psycopg2.connect(os.environ['INVENTORY_TEST_DATABASE_URL'])
        with self.admin.cursor() as cursor:
            cursor.execute('CREATE SCHEMA ' + self.schema)
        self.admin.commit()
        self.pool = ThreadedConnectionPool(1, 8, os.environ['INVENTORY_TEST_DATABASE_URL'], options='-c search_path=' + self.schema)
        self.db = SimpleNamespace(_get_connection=self.pool.getconn, _put_connection=self.pool.putconn)
        self.sql('''CREATE TABLE users(user_id BIGINT PRIMARY KEY,is_tourist BOOLEAN DEFAULT FALSE);
            CREATE TABLE user_settings(user_id BIGINT PRIMARY KEY REFERENCES users ON DELETE CASCADE,
                character_id INT DEFAULT 1,voice_id INT DEFAULT 1,profile_image_id INT DEFAULT 1,
                updated_at TIMESTAMP DEFAULT NOW());
            INSERT INTO users(user_id) VALUES(101),(102);''')
        conn = self.pool.getconn()
        try:
            with conn.cursor() as cursor:
                ensure_inventory_tables(cursor)
                ensure_mail_tables(cursor)
            conn.commit()
        finally:
            self.pool.putconn(conn)

    def tearDown(self):
        self.pool.closeall()
        with self.admin.cursor() as cursor:
            cursor.execute('DROP SCHEMA ' + self.schema + ' CASCADE')
        self.admin.commit()
        self.admin.close()

    def sql(self, statement, args=None):
        conn = self.pool.getconn()
        try:
            with conn.cursor() as cursor:
                cursor.execute(statement, args)
                result = cursor.fetchall() if cursor.description else None
            conn.commit()
            return result
        except Exception:
            conn.rollback()
            raise
        finally:
            self.pool.putconn(conn)

    def send(self, **changes):
        body = dict(request_id=uuid.uuid4().hex, user_id=101, title='领取奖励', content='欢迎回来\n请领取附件。',
                    rewards=[dict(item_id=3011, quantity=3)])
        body.update(changes)
        return send_mail(self.db, 999, body)['mail']

    def qty(self, item_id):
        return next((i['quantity'] for i in get_inventory(self.db, 101)['owned'] if i['item_id'] == item_id), 0)

    def test_owner_only_and_persistent_read(self):
        mail = self.send()
        self.assertEqual(list_mail(self.db, 102)['mails'], [])
        self.assertEqual(list_mail(self.db, 101)['unread_count'], 1)
        for action in (read_mail, claim_mail):
            with self.assertRaisesRegex(InventoryError, '邮件不存在'):
                action(self.db, 102, mail['mail_id'])
        result = read_mail(self.db, 101, mail['mail_id'])
        self.assertTrue(result['mail']['is_read'])
        self.assertEqual(result['unread_count'], 0)
        self.assertTrue(list_mail(self.db, 101)['mails'][0]['is_read'])
        self.assertEqual(self.qty(3011), 0)

    def test_claim_retry_grants_once_and_syncs_inventory(self):
        mail = self.send()
        first = claim_mail(self.db, 101, mail['mail_id'])
        again = claim_mail(self.db, 101, mail['mail_id'])
        self.assertEqual(first['inventory_state'], again['inventory_state'])
        self.assertTrue(again['mail']['claimed'])
        self.assertEqual(again['unread_count'], 0)
        self.assertEqual(self.qty(3011), 3)
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_ledger'), [(1,)])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_operations'), [(1,)])

    def test_concurrent_claims_are_atomic(self):
        mail = self.send()
        with ThreadPoolExecutor(max_workers=4) as executor:
            results = list(executor.map(lambda _: claim_mail(self.db, 101, mail['mail_id']), range(4)))
        self.assertTrue(all(r['mail']['claimed'] for r in results))
        self.assertEqual(self.qty(3011), 3)
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_ledger'), [(1,)])

    def test_failed_attachment_rolls_back_entire_claim(self):
        mail = self.send(rewards=[dict(item_id=3011, quantity=3), dict(item_id=3012, quantity=2)])
        self.sql('INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,3012,1000000)')
        with self.assertRaisesRegex(InventoryError, '超过持有上限'):
            claim_mail(self.db, 101, mail['mail_id'])
        self.assertEqual(self.qty(3011), 0)
        self.assertFalse(list_mail(self.db, 101)['mails'][0]['claimed'])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_operations'), [(0,)])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_ledger'), [(0,)])
        self.sql('UPDATE user_inventory SET quantity=0 WHERE user_id=101 AND item_id=3012')
        self.assertTrue(claim_mail(self.db, 101, mail['mail_id'])['mail']['claimed'])
        self.assertEqual(self.qty(3011), 3)
        self.assertEqual(self.qty(3012), 2)

    def test_expiry_blocks_unclaimed_and_preserves_completed_retry(self):
        mail = self.send(expires_at=(datetime.now(timezone.utc) + timedelta(days=1)).isoformat())
        self.sql("UPDATE player_mail SET expires_at=NOW()-INTERVAL '1 second' WHERE mail_id=%s", (mail['mail_id'],))
        self.assertTrue(list_mail(self.db, 101)['mails'][0]['expired'])
        self.assertEqual(list_mail(self.db, 101)['unread_count'], 0)
        with self.assertRaisesRegex(InventoryError, '已过期'):
            claim_mail(self.db, 101, mail['mail_id'])
        claimed = self.send()
        claim_mail(self.db, 101, claimed['mail_id'])
        self.sql("UPDATE player_mail SET expires_at=NOW()-INTERVAL '1 second' WHERE mail_id=%s", (claimed['mail_id'],))
        self.assertTrue(claim_mail(self.db, 101, claimed['mail_id'])['mail']['claimed'])
        self.assertEqual(self.qty(3011), 3)

    def test_delivery_retry_and_conflict(self):
        key = uuid.uuid4().hex
        first = self.send(request_id=key)
        again = self.send(request_id=key)
        self.assertEqual(first['mail_id'], again['mail_id'])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM player_mail'), [(1,)])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM admin_audit_log'), [(1,)])
        with self.assertRaisesRegex(InventoryError, '投递编号'):
            self.send(request_id=key, title='另一封邮件')
        self.assertEqual(self.sql('SELECT COUNT(*) FROM player_mail'), [(1,)])

    def test_eight_rewards_limit_and_audit_channel(self):
        self.sql('''INSERT INTO item_definitions
            (item_id,code,name,category,asset_key,slot,max_quantity,is_default,grant_enabled,use_enabled)
            SELECT n,'mail_test_'||n,'测试附件 '||n,'item','item.grass_tile','',1000000,FALSE,TRUE,TRUE
            FROM generate_series(9001,9009) n''')
        rewards = [dict(item_id=i, quantity=2) for i in range(9001, 9009)]
        mail = self.send(rewards=rewards)
        self.assertEqual(len(mail['rewards']), 8)
        claimed = claim_mail(self.db, 101, mail['mail_id'])
        self.assertTrue(claimed['mail']['claimed'])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_ledger'), [(8,)])
        self.assertEqual(self.sql('SELECT action FROM admin_audit_log'), [('mail.send',)])
        for bad in (rewards + [dict(item_id=9009,quantity=1)],
                    [dict(item_id=9001,quantity=1),dict(item_id=9001,quantity=2)]):
            with self.assertRaises(InventoryError):
                self.send(rewards=bad)

    def test_plain_notification_is_readable_without_inventory_mutation(self):
        mail = self.send(rewards=[])
        self.assertEqual(read_mail(self.db, 101, mail['mail_id'])['mail']['rewards'], [])
        with self.assertRaisesRegex(InventoryError, '没有可领取'):
            claim_mail(self.db, 101, mail['mail_id'])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_operations'), [(0,)])


class MailTransportTests(unittest.IsolatedAsyncioTestCase):
    async def test_admin_delivery_notifies_online_player_once(self):
        from fastapi import FastAPI
        import httpx
        sent = []
        async def send_json(value): sent.append(value)
        server = SimpleNamespace(db_manager=object(), user_id_to_connection={
            101: SimpleNamespace(websocket=SimpleNamespace(send_json=send_json))})
        app = FastAPI()
        register_mail_routes(app, server)
        with patch('server.database.mail_router._verify_admin', return_value=999), \
                patch('server.database.mail_router.send_mail', side_effect=[
                    dict(mail=dict(mail_id=1), duplicate=False),
                    dict(mail=dict(mail_id=1), duplicate=True)]):
            async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url='http://test') as client:
                self.assertEqual((await client.post('/admin/mail', json={})).status_code, 401)
                first = await client.post('/admin/mail', headers={'Authorization':'Bearer test'}, json={'user_id':101})
                again = await client.post('/admin/mail', headers={'Authorization':'Bearer test'}, json={'user_id':101})
        self.assertTrue(first.json()['synced_online'])
        self.assertFalse(again.json()['synced_online'])
        self.assertEqual(sent, [dict(type='mail/changed', success=True)])

    async def test_requires_login_and_ignores_forged_recipient(self):
        sent = []
        async def send_json(value): sent.append(value)
        socket = SimpleNamespace(send_json=send_json)
        server = SimpleNamespace(players={}, db_manager=object())
        await handle_mail_message(server, 'c', dict(type='mail/list'), socket)
        self.assertFalse(sent[-1]['success'])
        server.players['c'] = SimpleNamespace(user_id=101)
        with patch('server.database.mail_router.list_mail', return_value=dict(mails=[], unread_count=0)) as work:
            await handle_mail_message(server, 'c', dict(type='mail/list', user_id=102), socket)
            work.assert_called_once_with(server.db_manager, 101)
        self.assertTrue(sent[-1]['success'])
