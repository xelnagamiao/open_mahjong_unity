"""Atomic reward claims against a fresh isolated schema, not real player data."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
import os
import unittest
from unittest.mock import patch, AsyncMock
from types import SimpleNamespace
import uuid
import asyncio
import json

from cryptography.fernet import Fernet
from server.database import test_mail as mail_tests
from server.database.inventory import InventoryError, get_inventory, mutate_inventory
from server.database.mail import claim_mail
from server.database.redemption import (ensure_redemption_tables, save_campaign, set_campaign_status,
    export_codes, redeem_code, list_redemptions, check_redemption_rate)
from server.database.redemption_router import handle_redemption_message
from server.database.reward_receipts import notify_reward


@unittest.skipUnless(os.environ.get('INVENTORY_TEST_DATABASE_URL'), 'requires PostgreSQL test connection')
class RedemptionDatabaseTests(unittest.TestCase):
    sql = mail_tests.MailDatabaseTests.sql
    qty = mail_tests.MailDatabaseTests.qty
    send = mail_tests.MailDatabaseTests.send
    tearDown = mail_tests.MailDatabaseTests.tearDown

    def setUp(self):
        mail_tests.MailDatabaseTests.setUp(self)
        key = patch.dict(os.environ, REDEEM_CODE_KEY=Fernet.generate_key().decode())
        key.start(); self.addCleanup(key.stop)
        self.sql('INSERT INTO users(user_id,is_tourist) VALUES(103,TRUE),(104,FALSE)')
        conn = self.pool.getconn()
        try:
            with conn.cursor() as c: ensure_redemption_tables(c)
            conn.commit()
        finally: self.pool.putconn(conn)

    def campaign(self, **changes):
        body = dict(request_id=uuid.uuid4().hex,name='测试活动',kind='shared',code='REWARD2026',
                    code_count=1,rewards=[dict(item_id=3011,quantity=3)],reason='隔离测试')
        body.update(changes)
        row = save_campaign(self.db,999,body)
        set_campaign_status(self.db,999,row['campaign_id'],'active','隔离测试启用')
        return row,body

    def redeem(self, code='REWARD2026', user=101, request_id=None):
        return redeem_code(self.db,user,dict(code=code,request_id=request_id or uuid.uuid4().hex))

    def test_shared_normalization_snapshot_idempotency_and_user_cap(self):
        row,_ = self.campaign()
        key = uuid.uuid4().hex
        first = self.redeem(' reward-2026 ',request_id=key)
        again = self.redeem(request_id=key)
        self.assertEqual(first['reward_receipt'],again['reward_receipt'])
        self.assertEqual(self.qty(3011),3)
        self.assertEqual(first['reward_receipt']['items'][0]['quantity'],3)
        self.sql("UPDATE item_definitions SET name='后来改名' WHERE item_id=3011")
        self.assertEqual(self.redeem(request_id=key)['reward_receipt'],first['reward_receipt'])
        with self.assertRaisesRegex(InventoryError,'已领取过'): self.redeem()
        with self.assertRaisesRegex(InventoryError,'其他请求'): self.redeem('OTHER2026',request_id=key)
        with self.assertRaisesRegex(InventoryError,'注册账号'): self.redeem(user=103)
        self.redeem(user=102)
        self.assertEqual(list_redemptions(self.db,row['campaign_id'])['total'],2)

    def test_parallel_unique_claim_only_one_account_and_retry(self):
        row,_ = self.campaign(kind='unique',code_count=2)
        code = export_codes(self.db,999,row['campaign_id'])[0]['code']
        def claim(uid):
            try: return self.redeem(code,uid)
            except InventoryError as e: return str(e)
        with ThreadPoolExecutor(2) as pool: results=list(pool.map(claim,[101,102]))
        self.assertEqual(sum(isinstance(r,dict) for r in results),1)
        other=export_codes(self.db,999,row['campaign_id'])[1]['code']
        winner=next(r['reward_receipt']['user_id'] for r in results if isinstance(r,dict))
        with self.assertRaisesRegex(InventoryError,'已领取过'): self.redeem(other,winner)

    def test_last_quota_concurrent_and_same_request_retry(self):
        self.campaign(max_claims=1)
        def claim(uid):
            try: return self.redeem(user=uid)
            except InventoryError: return None
        with ThreadPoolExecutor(2) as pool: results=list(pool.map(claim,[101,102]))
        self.assertEqual(sum(r is not None for r in results),1)
        self.assertEqual(self.sql('SELECT claimed_count FROM redeem_campaigns')[0][0],1)

    def test_ordinary_batch_allows_same_player_different_codes(self):
        row,_ = self.campaign(purpose='ordinary',kind='unique',code_count=3,
                              rewards=[dict(item_id=3011,quantity=1)])
        codes = export_codes(self.db,999,row['campaign_id'])
        request = uuid.uuid4().hex
        first = self.redeem(codes[0]['code'],request_id=request)
        self.assertEqual(first['reward_receipt'],self.redeem(codes[0]['code'],request_id=request)['reward_receipt'])
        with self.assertRaisesRegex(InventoryError,'已被使用'): self.redeem(codes[0]['code'])
        with self.assertRaisesRegex(InventoryError,'已被使用'): self.redeem(codes[0]['code'],102)
        for entry in codes[1:]: self.redeem(entry['code'])
        self.assertEqual(self.qty(3011),3)
        self.assertEqual(list_redemptions(self.db,row['campaign_id'])['total'],3)
        self.assertEqual(self.sql('SELECT claimed_count FROM redeem_campaigns')[0][0],3)
        self.assertEqual([r['claimed_count'] for r in export_codes(self.db,999,row['campaign_id'])],[1,1,1])

    def test_ordinary_code_concurrency_and_activity_batch_concurrency(self):
        ordinary,_ = self.campaign(purpose='ordinary',kind='unique',code_count=1)
        code = export_codes(self.db,999,ordinary['campaign_id'])[0]['code']
        def claim(args):
            try: return self.redeem(*args)
            except InventoryError: return None
        with ThreadPoolExecutor(2) as pool: results=list(pool.map(claim,[(code,101),(code,102)]))
        self.assertEqual(sum(r is not None for r in results),1)
        activity,_ = self.campaign(purpose='activity',kind='unique',code_count=2)
        codes = export_codes(self.db,999,activity['campaign_id'])
        with ThreadPoolExecutor(2) as pool: results=list(pool.map(claim,[(c['code'],104) for c in codes]))
        self.assertEqual(sum(r is not None for r in results),1)
        self.assertEqual(list_redemptions(self.db,activity['campaign_id'])['total'],1)

    def test_ordinary_rules_validation_and_activation_freeze(self):
        with self.assertRaisesRegex(InventoryError,'一次性码'): self.campaign(purpose='ordinary',kind='shared')
        with self.assertRaisesRegex(InventoryError,'用途无效'): self.campaign(purpose='unknown')
        row,body = self.campaign(purpose='ordinary',kind='unique',code_count=1)
        body['purpose']='activity'
        with self.assertRaisesRegex(InventoryError,'不能修改'): save_campaign(self.db,999,body,row['campaign_id'])

    def test_schema_migration_preserves_existing_activity_claims(self):
        row,_ = self.campaign(kind='unique',code_count=3)
        codes=export_codes(self.db,999,row['campaign_id'])
        original=self.redeem(codes[0]['code'])
        # Recreate the schema constraints present before ordinary codes were introduced.
        self.sql('ALTER TABLE redeem_campaigns DROP COLUMN purpose')
        self.sql('DROP INDEX redeem_redemptions_code_user_idx')
        self.sql('ALTER TABLE redeem_redemptions ADD CONSTRAINT redeem_redemptions_campaign_id_user_id_key UNIQUE(campaign_id,user_id)')
        conn=self.pool.getconn()
        try:
            with conn.cursor() as cursor:
                ensure_redemption_tables(cursor)
                ensure_redemption_tables(cursor)
            conn.commit()
        finally: self.pool.putconn(conn)
        self.assertEqual(self.sql('SELECT purpose FROM redeem_campaigns')[0][0],'activity')
        self.assertEqual(list_redemptions(self.db,row['campaign_id'])['items'][0]['result']['reward_receipt'],original['reward_receipt'])
        with self.assertRaisesRegex(InventoryError,'已领取过'): self.redeem(codes[1]['code'])
        ordinary,_ = self.campaign(purpose='ordinary',kind='unique',code_count=2)
        for entry in export_codes(self.db,999,ordinary['campaign_id']): self.redeem(entry['code'])
        self.assertEqual(self.qty(3011),9)

    def test_reward_limit_rolls_back_inventory_code_and_quota(self):
        self.campaign(rewards=[dict(item_id=3011,quantity=3),dict(item_id=3012,quantity=2)])
        self.sql('INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,3012,999999)')
        with self.assertRaisesRegex(InventoryError,'上限'): self.redeem()
        self.assertEqual(self.qty(3011),0)
        self.assertEqual(self.sql('SELECT claimed_count FROM redeem_codes')[0][0],0)
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_operations')[0][0],0)

    def test_campaign_creation_retry_export_and_frozen_rewards(self):
        row,body=self.campaign(kind='unique',code_count=5)
        again=save_campaign(self.db,999,body)
        self.assertEqual(row['campaign_id'],again['campaign_id'])
        codes=export_codes(self.db,999,row['campaign_id'])
        self.assertEqual(codes,export_codes(self.db,999,row['campaign_id']))
        self.assertEqual(len({c['code'] for c in codes}),5)
        with self.assertRaisesRegex(InventoryError,'不能修改'): save_campaign(self.db,999,body,row['campaign_id'])
        set_campaign_status(self.db,999,row['campaign_id'],'paused','测试暂停')
        with self.assertRaisesRegex(InventoryError,'未开放'): self.redeem(codes[0]['code'])
        set_campaign_status(self.db,999,row['campaign_id'],'active','测试恢复')
        self.redeem(codes[0]['code'])
        set_campaign_status(self.db,999,row['campaign_id'],'ended','测试结束')
        with self.assertRaises(InventoryError): set_campaign_status(self.db,999,row['campaign_id'],'active','测试禁止恢复')

    def test_expiry_and_future(self):
        self.campaign(ends_at=(datetime.now(timezone.utc)-timedelta(seconds=1)).isoformat())
        with self.assertRaisesRegex(InventoryError,'过期'): self.redeem()
        self.campaign(code='FUTURE2026',starts_at=(datetime.now(timezone.utc)+timedelta(days=1)).isoformat())
        with self.assertRaisesRegex(InventoryError,'尚未开始'): self.redeem('FUTURE2026')

    def test_permanent_duplicate_not_consumed_and_mixed_skips(self):
        self.sql("UPDATE item_definitions SET is_default=FALSE WHERE item_id=2202")
        self.sql('INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,2202,1)')
        self.campaign(rewards=[dict(item_id=2202,quantity=1)])
        with self.assertRaisesRegex(InventoryError,'未消耗'): self.redeem()
        self.campaign(code='MIXED2026',rewards=[dict(item_id=2202,quantity=1),dict(item_id=3011,quantity=1)])
        receipt=self.redeem('MIXED2026')['reward_receipt']
        self.assertEqual([i['item_id'] for i in receipt['items']],[3011])
        self.assertEqual([i['item_id'] for i in receipt['skipped_items']],[2202])

    def test_mail_and_admin_grant_share_receipt_contract(self):
        mail=self.send()
        first=claim_mail(self.db,101,mail['mail_id'])
        again=claim_mail(self.db,101,mail['mail_id'])
        self.assertEqual(first['reward_receipt'],again['reward_receipt'])
        self.assertEqual(first['reward_receipt']['source'],'mail')
        grant=mutate_inventory(self.db,101,'grant',dict(request_id=uuid.uuid4().hex,item_id=3011,quantity=2,reason='测试'),999)
        self.assertEqual(grant['reward_receipt']['source'],'grant')
        self.assertEqual(grant['reward_receipt']['items'][0]['quantity'],2)
        self.assertEqual(grant['reward_receipt']['version'],1)

    def test_rate_limit_is_shared_and_persists(self):
        for _ in range(10): check_redemption_rate(self.db,101,'127.0.0.1')
        with self.assertRaisesRegex(InventoryError,'频繁'): check_redemption_rate(self.db,101,'127.0.0.1')

    def test_http_routes_auth_crud_claim_and_export(self):
        from fastapi import FastAPI
        from server.database.redemption_router import register_redemption_routes
        app=FastAPI()
        register_redemption_routes(app,SimpleNamespace(db_manager=self.db,user_id_to_connection={}))
        async def request(method,path,body=None,authorized=True):
            sent=[]
            async def receive(): return dict(type='http.request',body=json.dumps(body or {}).encode(),more_body=False)
            async def send(message): sent.append(message)
            headers=[(b'content-type',b'application/json')]
            if authorized: headers.append((b'authorization',b'Bearer local-test-token'))
            scope=dict(type='http',asgi=dict(version='3.0'),http_version='1.1',method=method,path=path,
                       raw_path=path.encode(),query_string=b'',headers=headers,client=('127.0.0.1',123),server=('test',80),scheme='http')
            await app(scope,receive,send)
            return sent[0]['status'],json.loads(b''.join(m.get('body',b'') for m in sent[1:]))
        def call(*args,**kwargs): return asyncio.run(request(*args,**kwargs))
        self.assertEqual(call('GET','/admin/redemption/campaigns',authorized=False)[0],401)
        with patch('server.database.redemption_router._verify_admin',return_value=999), patch('server.database.redemption_router._verify_player',return_value=101):
            status,data=call('POST','/admin/redemption/campaigns',dict(request_id=uuid.uuid4().hex,name='HTTP测试',kind='shared',code='HTTP2026',rewards=[dict(item_id=3011,quantity=2)],reason='测试'))
            self.assertEqual(status,200);cid=data['data']['campaign_id']
            self.assertEqual(call('POST',f'/admin/redemption/campaigns/{cid}/status',dict(status='active',reason='启用'))[0],200)
            status,data=call('POST','/player/redeem',dict(code='HTTP2026',request_id=uuid.uuid4().hex,user_id=102))
            self.assertEqual(status,200);self.assertEqual(data['data']['reward_receipt']['user_id'],101)
            self.assertEqual(self.qty(3011),2)
            self.assertEqual(call('GET',f'/admin/redemption/campaigns/{cid}/claims')[1]['data']['total'],1)
            status,data=call('POST',f'/admin/redemption/campaigns/{cid}/export')
            self.assertEqual(status,200);self.assertEqual(data['data'][0]['code'],'HTTP2026')


class RedemptionTransportTests(unittest.IsolatedAsyncioTestCase):
    async def test_push_delivers_common_receipt_only_to_owner(self):
        owner=SimpleNamespace(send_json=AsyncMock())
        other=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(user_id_to_connection={101:SimpleNamespace(websocket=owner),102:SimpleNamespace(websocket=other)})
        result=dict(reward_receipt=dict(user_id=101,items=[dict(item_id=3011,quantity=2)]),inventory_state=dict(user_id=101))
        self.assertTrue(await notify_reward(server,result))
        self.assertEqual(owner.send_json.call_args.args[0]['type'],'reward/received')
        self.assertEqual(owner.send_json.call_args.args[0]['reward_receipt'],result['reward_receipt'])
        other.send_json.assert_not_called()
        self.assertFalse(await notify_reward(server,result,exclude=owner))
        server.user_id_to_connection.clear()
        self.assertFalse(await notify_reward(server,result))

    async def test_session_identity_overrides_body_and_receipt_survives(self):
        socket=SimpleNamespace(send_json=AsyncMock(),client=SimpleNamespace(host='127.0.0.1'))
        server=SimpleNamespace(players={'c':SimpleNamespace(user_id=101)},db_manager=object())
        result=dict(reward_receipt=dict(receipt_id='receipt',user_id=101,items=[]))
        with patch('server.database.redemption_router.claim',AsyncMock(return_value=result)) as claim:
            await handle_redemption_message(server,'c',dict(type='reward/redeem',request_id='request123',user_id=102,code='CODE2026'),socket)
            self.assertEqual(claim.call_args.args[1],101)
        self.assertEqual(socket.send_json.call_args.args[0]['reward_receipt'],result['reward_receipt'])
