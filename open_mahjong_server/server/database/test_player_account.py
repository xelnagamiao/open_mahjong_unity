"""Private Web account routes and transactional application notices."""
import os
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, patch
import httpx
from fastapi import FastAPI
from . import test_mail
from .mail import list_mail, mail_summary, read_mail, claim_mail
from .player_account_router import register_player_account_routes


class AccountRouteTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.server = SimpleNamespace(db_manager=object(), players={})
        app = FastAPI()
        register_player_account_routes(app, self.server)
        self.client = httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url='http://test')
        self.verify = patch('server.database.player_account_router._verify_player', return_value=101)
        self.verify.start()

    async def asyncTearDown(self):
        self.verify.stop()
        await self.client.aclose()

    async def test_auth_and_actor_cannot_be_forged(self):
        self.assertEqual((await self.client.get('/player/inventory')).status_code, 401)
        with patch('server.database.player_account_router.get_inventory', return_value={}) as get:
            response = await self.client.get('/player/inventory?user_id=102', headers={'Authorization': 'Bearer test'})
            self.assertEqual(response.status_code, 200)
            get.assert_called_once_with(self.server.db_manager, 101)
        self.assertEqual((await self.client.post('/player/inventory/grant', headers={'Authorization': 'Bearer test'}, json={})).status_code, 404)

    async def test_mutation_whitelist_and_postcommit_failure(self):
        with patch('server.database.player_account_router.mutate_inventory', return_value={'inventory_state': {}}) as mutate, \
             patch('server.database.player_account_router._sync', new=AsyncMock(side_effect=RuntimeError('offline'))):
            response = await self.client.post('/player/inventory/equip', headers={'Authorization': 'Bearer test'},
                json={'request_id': 'test-request', 'item_id': 2302, 'slot': 'avatar', 'user_id': 102, 'actor_id': 999, 'reason': 'forged'})
            self.assertEqual(response.status_code, 200)
            self.assertEqual(mutate.call_args.args[1:], (101, 'equip', {'request_id': 'test-request', 'item_id': 2302, 'slot': 'avatar'}))

    async def test_mail_filters_and_duplicate_claim(self):
        headers = {'Authorization': 'Bearer test'}
        self.assertEqual((await self.client.get('/player/mail?page=0', headers=headers)).status_code, 400)
        self.assertEqual((await self.client.get('/player/mail?filter=invalid', headers=headers)).status_code, 400)
        with patch('server.database.player_account_router.list_mail', return_value={'mails': []}) as listing:
            self.assertEqual((await self.client.get('/player/mail?page=2&filter=unread&user_id=102', headers=headers)).status_code, 200)
            listing.assert_called_once_with(self.server.db_manager, 101, limit=20, offset=20, filter_by='unread')
        with patch('server.database.player_account_router.claim_mail', return_value={'already_claimed': True}), \
             patch('server.database.player_account_router.notify_reward', new=AsyncMock()) as notify:
            self.assertEqual((await self.client.post('/player/mail/1/claim', headers=headers)).status_code, 200)
            notify.assert_not_called()


@unittest.skipUnless(os.environ.get('INVENTORY_TEST_DATABASE_URL'), 'requires PostgreSQL test connection')
class ApplicationMailTests(unittest.TestCase):
    setUp = test_mail.MailDatabaseTests.setUp
    tearDown = test_mail.MailDatabaseTests.tearDown
    sql = test_mail.MailDatabaseTests.sql
    send = test_mail.MailDatabaseTests.send

    def install(self):
        self.sql('''CREATE TABLE event_applications(application_id SERIAL PRIMARY KEY,applicant_user_id BIGINT,
            kind TEXT,name TEXT,status TEXT DEFAULT 'pending',review_note TEXT,reviewer_user_id BIGINT,event_id TEXT)''')
        self.sql(Path(__file__).with_name('application_mail.sql').read_text(encoding='utf-8'))

    def test_application_lifecycle_notifies_only_committed_transitions(self):
        self.install()
        self.sql("INSERT INTO event_applications(applicant_user_id,kind,name) VALUES(101,'base','测试基地')")
        self.assertEqual(mail_summary(self.db, 101)['unread_count'], 1)
        self.sql("UPDATE event_applications SET name='更新名称',status='pending'")
        self.assertEqual(mail_summary(self.db, 101)['total'], 1)
        self.sql("UPDATE event_applications SET status='rejected',review_note='请补充赛制',reviewer_user_id=999")
        latest = list_mail(self.db, 101)['mails'][0]
        self.assertIn('请补充赛制', latest['content'])
        self.assertEqual(latest['action_path'], '/account?application=1#sec-manage-base')
        self.sql("UPDATE event_applications SET status='pending'")
        self.assertEqual(mail_summary(self.db, 101)['total'], 3)
        conn = self.pool.getconn()
        try:
            with conn.cursor() as cursor:
                cursor.execute("UPDATE event_applications SET status='approved',event_id='base-101'")
            conn.rollback()
        finally:
            self.pool.putconn(conn)
        self.assertEqual(mail_summary(self.db, 101)['total'], 3)
        self.sql("UPDATE event_applications SET status='approved',event_id='base-101'")
        self.assertEqual(list_mail(self.db, 101)['mails'][0]['action_path'], '/account?manage=base-101#sec-manage-base')
        self.assertEqual(list_mail(self.db, 102)['mails'], [])

    def test_pagination_and_reward_filters(self):
        ids = [self.send()['mail_id'] for _ in range(23)]
        read_mail(self.db, 101, ids[0])
        claim_mail(self.db, 101, ids[1])
        self.sql('UPDATE player_mail SET expires_at=NOW()-INTERVAL \'1 day\' WHERE mail_id=%s', (ids[2],))
        first = list_mail(self.db, 101, limit=20)
        second = list_mail(self.db, 101, limit=20, offset=20)
        self.assertEqual((len(first['mails']), len(second['mails']), first['total']), (20, 3, 23))
        self.assertEqual(first['unread_count'], 20)
        self.assertEqual(first['claimable_count'], 21)
        self.assertEqual(list_mail(self.db, 101, filter_by='unread')['total'], 20)
        self.assertEqual(list_mail(self.db, 101, filter_by='rewards')['total'], 21)
        self.assertTrue(claim_mail(self.db, 101, ids[1])['already_claimed'])
