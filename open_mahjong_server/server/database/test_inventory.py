"""Real PostgreSQL transactions in disposable schemas; never modify live users."""
import ast
import os
from pathlib import Path
import re
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import psycopg2
from psycopg2.pool import ThreadedConnectionPool
from server.database.inventory import (InventoryError, ensure_inventory_tables, get_inventory,
    mutate_inventory, save_definition, get_catalog, get_ledger)
from server.database.inventory_router import handle_inventory_message, _sync, attach_game_frames
from server.database.titles import ensure_title_tables
from server.database.inventory_assets import DEFAULT_AVATAR_FRAME_ID, A1_AVATAR_FRAME_ID, B1_AVATAR_FRAME_ID
from server.database.db_manager import DatabaseManager
from scripts.migrate_rename_cards import migrate_database


@unittest.skipUnless(os.environ.get('INVENTORY_TEST_DATABASE_URL'), 'requires PostgreSQL test connection')
class InventoryDatabaseTests(unittest.TestCase):
    def setUp(self):
        self.schema='inventory_test_'+uuid.uuid4().hex
        self.admin=psycopg2.connect(os.environ['INVENTORY_TEST_DATABASE_URL'])
        with self.admin.cursor() as c:
            c.execute('CREATE SCHEMA '+self.schema)
        self.admin.commit()
        self.pool=ThreadedConnectionPool(1,8,os.environ['INVENTORY_TEST_DATABASE_URL'],options='-c search_path='+self.schema)
        self.db=SimpleNamespace(_get_connection=self.pool.getconn,_put_connection=self.pool.putconn)
        self.sql('''CREATE TABLE users (user_id bigint PRIMARY KEY,username text DEFAULT 'test player',rename_count integer NOT NULL DEFAULT 0,is_tourist boolean DEFAULT FALSE);
            CREATE TABLE user_settings (user_id bigint PRIMARY KEY REFERENCES users ON DELETE CASCADE,
                title_id int DEFAULT 1,profile_image_id int DEFAULT 1,character_id int DEFAULT 1,voice_id int DEFAULT 1,updated_at timestamp DEFAULT NOW());
            INSERT INTO users(user_id,is_tourist) VALUES (101,FALSE),(102,TRUE),(103,FALSE);
            INSERT INTO user_settings(user_id,character_id,voice_id) VALUES (101,1,1),(102,1,1),(103,2,2);''')
        self.ensure_schema()

    def tearDown(self):
        self.pool.closeall()
        with self.admin.cursor() as c: c.execute('DROP SCHEMA '+self.schema+' CASCADE')
        self.admin.commit(); self.admin.close()

    def sql(self,sql,args=None):
        c=self.pool.getconn()
        try:
            with c.cursor() as cursor:
                cursor.execute(sql,args)
                value=cursor.fetchall() if cursor.description else None
            c.commit(); return value
        except Exception: c.rollback(); raise
        finally: self.pool.putconn(c)

    def ensure_schema(self):
        c=self.pool.getconn()
        try:
            with c.cursor() as cursor:
                ensure_title_tables(cursor)
                ensure_inventory_tables(cursor)
            c.commit()
        finally: self.pool.putconn(c)

    def call(self,action,iid=0,quantity=1,uid=101,**extra):
        body=dict(item_id=iid,quantity=quantity,request_id=uuid.uuid4().hex,**extra)
        if action in ('grant','revoke'): body.setdefault('reason','inventory integration test')
        return mutate_inventory(self.db,uid,action,body,999 if action in ('grant','revoke') else None)

    def qty(self,iid,uid=101):
        return next((r['quantity'] for r in get_inventory(self.db,uid)['owned'] if r['item_id']==iid),0)

    def definition(self,iid,**changes):
        body=next(i for i in get_catalog(self.db) if i['item_id']==iid)
        return dict(body,reason='test definition',**changes)

    def test_minimal_catalog_does_not_reseed_retired_presets(self):
        self.ensure_schema()
        self.assertEqual([(i['item_id'],i['name']) for i in get_catalog(self.db)],[(2201,'金橙色'),(2202,'青色'),(2301,'默认头像'),(2302,'博丽灵梦'),(2303,'琪露诺'),(3001,'改名卡'),(3011,'草牌'),(3012,'好牌')])
        self.assertEqual(self.sql('SELECT name FROM titles ORDER BY title_id'),[('最初的初段',),('冠绝一世',),('千恋万花',)])
        self.assertEqual(self.qty(1002,103),0)
        self.assertEqual(get_inventory(self.db,103)['appearance']['voice_id'],2)
        self.assertEqual(get_inventory(self.db,103)['equipment'],[{'slot':'avatar','item_id':2301}])
        self.assertEqual(get_inventory(self.db,103)['owned'],[{'item_id':iid,'quantity':1} for iid in (2201,2202,2301,2302,2303)])
        self.assertEqual(self.sql("SELECT COUNT(*) FROM admin_audit_log WHERE action='store.catalog_reset'"),[(0,)])

    def test_schema_bootstrap_preserves_catalog_ownership_and_appearance(self):
        self.call('grant',3001,4)
        self.sql("""UPDATE users SET rename_count=7 WHERE user_id=101;
            INSERT INTO titles(title_id,name) VALUES(50,'管理员新增头衔');
            INSERT INTO user_titles(user_id,title_id) VALUES(101,50);
            UPDATE user_settings SET title_id=50,character_id=2,voice_id=2 WHERE user_id=101;""")
        before=get_inventory(self.db,101)
        self.ensure_schema()
        self.ensure_schema()
        self.assertEqual(get_inventory(self.db,101),before)
        self.assertEqual(self.sql('SELECT title_id FROM user_settings WHERE user_id=101'),[(50,)])
        self.assertEqual(self.sql("SELECT COUNT(*) FROM admin_audit_log WHERE action='store.catalog_reset'"),[(0,)])

    def test_collectible_gifts_require_manual_grants_and_cannot_be_consumed(self):
        for iid, name in ((3011, '草牌'), (3012, '好牌')):
            self.assertEqual(self.qty(iid), 0)
            item = next(i for i in get_catalog(self.db) if i['item_id'] == iid)
            self.assertEqual(item['name'], name)
            self.assertEqual(item['effect'], 'collectible')
            self.assertTrue(item['icon_path'].startswith('image/Inventory/'))
            self.call('grant', iid, 3)
            before = self.sql('SELECT COUNT(*) FROM inventory_ledger')
            with self.assertRaisesRegex(InventoryError, '收藏礼物'):
                self.call('use', iid)
            with self.assertRaises(InventoryError):
                self.call('equip', iid, slot='character')
            self.assertEqual(self.qty(iid), 3)
            self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_ledger'), before)
            self.ensure_schema()
            self.assertEqual(self.qty(iid), 3)
            self.call('revoke', iid, 1)
            self.assertEqual(self.qty(iid), 2)

    def test_removed_items_and_equipping_consumables_are_rejected(self):
        with self.assertRaises(InventoryError): self.call('equip',1002,slot='character')
        for iid in (1001,1002,2001,2002,2101,2102,3002):
            with self.assertRaises(InventoryError): self.call('grant',iid)
        self.call('grant',3001)
        with self.assertRaises(InventoryError): self.call('equip',3001,slot='character')
        self.assertEqual(get_inventory(self.db,101)['equipment'],[{'slot':'avatar','item_id':2301}])

    def test_builtin_frames_default_switch_persist_and_reset(self):
        for uid in (101,102):
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(self.qty(A1_AVATAR_FRAME_ID,uid),1)
            self.assertEqual(self.qty(B1_AVATAR_FRAME_ID,uid),1)
            self.call('equip',B1_AVATAR_FRAME_ID,uid=uid,slot='avatar_frame')
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],B1_AVATAR_FRAME_ID)
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],B1_AVATAR_FRAME_ID)
            self.ensure_schema()
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],B1_AVATAR_FRAME_ID)
            self.call('equip',0,uid=uid,slot='avatar_frame')
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.call('equip',A1_AVATAR_FRAME_ID,uid=uid,slot='avatar_frame')
            self.ensure_schema()
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],A1_AVATAR_FRAME_ID)
            self.call('equip',DEFAULT_AVATAR_FRAME_ID,uid=uid,slot='avatar_frame')
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],0)
            self.assertEqual(self.sql("SELECT COUNT(*) FROM user_equipment WHERE user_id=%s AND slot='avatar_frame'",(uid,)),[(0,)])
            self.assertEqual(get_ledger(self.db,uid),[])

    def test_default_avatars_are_owned_and_only_change_the_avatar(self):
        for iid in (2301, 2302, 2303):
            self.assertEqual(self.qty(iid, 103), 1)
        for iid, expected in ((2302, 2302), (2303, 2303), (2301, 1), (0, 1)):
            state = self.call('equip', iid, uid=103, slot='avatar')['inventory_state']
            self.assertEqual(state['appearance']['profile_image_id'], expected)
            self.assertEqual(state['appearance']['character_id'], 2)
            self.assertEqual(state['appearance']['voice_id'], 2)
            self.ensure_schema()
            self.assertEqual(get_inventory(self.db, 103)['appearance'], state['appearance'])
        self.assertEqual(get_ledger(self.db, 103), [])
        with self.assertRaises(InventoryError):
            self.call('equip', 999999, uid=103, slot='avatar')

    def test_avatar_frames_do_not_reset_other_appearance_slots(self):
        self.call('equip', 2302, uid=103, slot='avatar')
        for iid in (2202, 2201, 0):
            state = self.call('equip', iid, uid=103, slot='avatar_frame')['inventory_state']
            self.assertEqual((state['appearance']['character_id'], state['appearance']['voice_id']), (2, 2))
            self.assertEqual(state['appearance']['profile_image_id'], 2302)

    def test_idempotency_replay_conflict_and_revoke(self):
        body=dict(request_id=uuid.uuid4().hex,item_id=3001,quantity=3,reason='retry')
        first=mutate_inventory(self.db,101,'grant',body,999)
        again=mutate_inventory(self.db,101,'grant',body,999)
        self.assertEqual(first['operation_id'],again['operation_id']);self.assertEqual(self.qty(3001),3)
        with self.assertRaises(InventoryError): mutate_inventory(self.db,101,'grant',dict(body,quantity=4),999)
        self.call('revoke',3001)
        self.assertEqual(self.qty(3001),2)
        self.assertEqual(len(get_ledger(self.db,101)),2)

    def test_legacy_use_and_retries_never_exchange_cards_for_credits(self):
        self.sql('UPDATE users SET rename_count=5 WHERE user_id=101')
        self.call('grant',3001,4)
        body=dict(request_id=uuid.uuid4().hex,item_id=3001,quantity=3)
        for _ in range(2):
            with self.assertRaisesRegex(InventoryError,'网页账号中心'):
                mutate_inventory(self.db,101,'use',body)
        state=get_inventory(self.db,101)
        self.assertEqual(state['rename_card_count'],4)
        self.assertEqual(state['rename_card_usable_count'],4)
        self.assertNotIn('rename_count',state)
        self.assertEqual(self.sql('SELECT rename_count FROM users WHERE user_id=101'),[(5,)])
        self.assertEqual(self.qty(3001),4)
        self.assertEqual(len(get_ledger(self.db,101)),1)

    def test_guest_and_invalid_amount_leave_no_partial_ledger(self):
        self.call('grant',3001,uid=102)
        with self.assertRaises(InventoryError): self.call('use',3001,uid=102)
        for value in (0,-1,True,'1',1.5,101):
            with self.assertRaises(InventoryError): self.call('use',3001,value)
        with self.assertRaises(InventoryError): self.call('grant',3001,reason='')
        with self.assertRaises(InventoryError): mutate_inventory(self.db,101,'grant',{},None)
        self.assertEqual(self.qty(3001,102),1)
        self.assertEqual(len(get_ledger(self.db,102)),1)

    def test_disabled_grants_do_not_change_balances_or_ledger(self):
        self.call('grant',3001,3)
        body=self.definition(3001);body['grant_enabled']=False
        save_definition(self.db,999,3001,body)
        before=len(get_ledger(self.db,101))
        with self.assertRaises(InventoryError): self.call('grant',3001)
        self.assertEqual(self.qty(3001),3)
        self.assertEqual(len(get_ledger(self.db,101)),before)

    def test_disabling_card_preserves_ownership_and_blocks_consumption(self):
        for uid in (101,102):
            self.call('grant',3001,uid=uid)
        body=self.definition(3001);body['use_enabled']=False
        save_definition(self.db,999,3001,body)
        for uid in (101,102):
            self.assertEqual(self.qty(3001,uid),1)
            self.assertEqual(get_inventory(self.db,uid)['rename_card_count'],1)
            self.assertEqual(get_inventory(self.db,uid)['rename_card_usable_count'],0)
            with self.assertRaises(InventoryError): self.call('use',3001,uid=uid)

    def test_retired_templates_rejected_and_future_additions_survive_restart(self):
        for asset in ('character.xiaoxiao','character.qiuqiu','avatar.pixel_teal','avatar.pixel_sunset','frame.jade','frame.gold','item.fixed_bundle'):
            body=self.definition(3001,asset_key=asset)
            with self.assertRaises(InventoryError): save_definition(self.db,999,None,body)
        body=self.definition(3001);body.update(code='test_rename',name='测试改名卡')
        created=save_definition(self.db,999,None,body)['item']
        self.assertGreaterEqual(created['item_id'],4000)
        self.call('grant',created['item_id'])
        self.ensure_schema()
        self.assertEqual(self.qty(created['item_id']),1)
        with self.assertRaisesRegex(InventoryError,'网页账号中心'): self.call('use',created['item_id'])
        self.assertEqual(get_inventory(self.db,101)['rename_card_count'],1)

    def migrate(self,**kwargs):
        conn=self.pool.getconn()
        try: return migrate_database(conn,**kwargs)
        finally:
            conn.set_session(readonly=False,isolation_level='READ COMMITTED')
            self.pool.putconn(conn)

    def test_migration_preview_is_read_only_and_apply_preserves_all_balances(self):
        self.call('grant',3001,2)
        self.call('grant',3001,1,uid=102)
        self.call('grant',3001,7,uid=103)
        self.sql('UPDATE users SET rename_count=CASE user_id WHEN 101 THEN 3 WHEN 102 THEN 2 ELSE 0 END')
        before=self.sql('SELECT user_id,rename_count FROM users ORDER BY user_id')
        preview=self.migrate()
        self.assertEqual((preview['affected_users'],preview['cards_to_add'],preview['tourist_users']),(2,5,1))
        self.assertFalse(preview['applied'])
        self.assertEqual(self.sql('SELECT user_id,rename_count FROM users ORDER BY user_id'),before)
        self.assertEqual(self.qty(3001),2)
        self.assertEqual(len(get_ledger(self.db,101)),1)
        result=self.migrate(apply=True,expected_fingerprint=preview['fingerprint'])
        self.assertEqual(result['status'],'complete')
        self.assertEqual([self.qty(3001,uid) for uid in (101,102,103)],[5,3,7])
        self.assertEqual(self.sql('SELECT SUM(rename_count) FROM users'),[(0,)])
        self.assertEqual([(r['delta'],r['quantity_after']) for r in get_ledger(self.db,101)],[(3,5),(2,2)])
        self.assertEqual(self.migrate(apply=True)['status'],'already_complete')
        self.assertEqual(self.qty(3001),5)
        self.sql('UPDATE users SET rename_count=1 WHERE user_id=101')
        with self.assertRaisesRegex(ValueError,'又出现'): self.migrate(apply=True)
        self.assertEqual(self.qty(3001),5)

    def test_migration_overflow_or_invalid_credit_aborts_entire_batch(self):
        for invalid in (1000001,-1):
            self.sql('UPDATE users SET rename_count=CASE user_id WHEN 101 THEN 2 WHEN 102 THEN %s ELSE 0 END',(invalid,))
            with self.assertRaises(ValueError): self.migrate(apply=True)
            self.assertEqual(self.qty(3001),0)
            self.assertEqual(get_ledger(self.db,101),[])
            self.assertEqual(self.sql('SELECT rename_count FROM users WHERE user_id=101'),[(2,)])

    def test_migration_adds_to_disabled_card_without_enabling_or_clipping(self):
        self.call('grant',3001,999999)
        self.sql('UPDATE users SET rename_count=1 WHERE user_id=101')
        self.sql('UPDATE item_definitions SET use_enabled=FALSE,grant_enabled=FALSE WHERE item_id=3001')
        self.migrate(apply=True)
        state=get_inventory(self.db,101)
        self.assertEqual(state['rename_card_count'],1000000)
        self.assertEqual(state['rename_card_usable_count'],0)

    def test_migration_ledger_failure_rolls_back_previous_users_and_completion_marker(self):
        self.sql('UPDATE users SET rename_count=CASE user_id WHEN 101 THEN 1 WHEN 102 THEN 2 ELSE 0 END')
        self.sql('ALTER TABLE inventory_ledger ADD CONSTRAINT reject_migration CHECK (delta<>2)')
        with self.assertRaises(psycopg2.IntegrityError): self.migrate(apply=True)
        self.assertEqual(self.qty(3001),0)
        self.assertEqual(self.sql('SELECT SUM(rename_count) FROM users'),[(3,)])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM inventory_operations'),[(0,)])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM admin_audit_log'),[(0,)])

    def test_migration_preview_fingerprint_rejects_drift(self):
        self.sql('UPDATE users SET rename_count=2 WHERE user_id=101')
        preview=self.migrate()
        self.call('grant',3001)
        with self.assertRaisesRegex(ValueError,'指纹'): self.migrate(apply=True,expected_fingerprint=preview['fingerprint'])
        self.assertEqual(self.qty(3001),1)
        self.assertEqual(self.sql('SELECT rename_count FROM users WHERE user_id=101'),[(2,)])

    def test_audit_failure_rolls_grant_back(self):
        self.sql("ALTER TABLE admin_audit_log ADD CONSTRAINT fail_audit CHECK (reason <> 'fail')")
        with self.assertRaises(psycopg2.IntegrityError): self.call('grant',3001,reason='fail')
        self.assertEqual(self.qty(3001),0)
        self.assertEqual(get_ledger(self.db,101),[])

    def test_every_persisted_rule_keeps_the_equipped_frame_snapshot(self):
        self.sql('''CREATE TABLE game_player_records (game_id text,user_id bigint,username text,score int,rank int,
            original_player_index int,rule text,sub_rule text,match_type text,room_type text,match_tier text,event_id text,
                title_used int,character_used int,profile_used int,voice_used int,avatar_frame_used int,pt_change numeric(12,2))''')
        checked=[]
        for path in Path(__file__).parent.rglob('store*.py'):
            tree=ast.parse(path.read_text(encoding='utf-8-sig'))
            for call in ast.walk(tree):
                if not isinstance(call,ast.Call) or len(call.args)!=2 or not isinstance(call.args[0],ast.Constant): continue
                sql=call.args[0].value
                if not isinstance(sql,str) or 'INSERT INTO game_player_records' not in sql: continue
                columns=[s.strip() for s in re.search(r'game_player_records\s*\((.*?)\)',sql,re.S)[1].split(',')]
                self.assertIn('avatar_frame_used',columns,path.name)
                self.assertEqual(len(columns),len(call.args[1].elts),path.name)
                strings={'game_id','username','rule','sub_rule','match_type','room_type','match_tier','event_id'}
                values=[path.stem if c in strings else 2102 if c=='avatar_frame_used' else 1 for c in columns]
                self.sql(sql,values)
                checked.append(path.name)
        self.assertEqual(len(checked),9)
        self.assertEqual(self.sql('SELECT DISTINCT avatar_frame_used FROM game_player_records'),[(2102,)])


class Socket:
    def __init__(self): self.messages=[]
    async def send_json(self,payload): self.messages.append(payload)


class InventoryTransportTests(unittest.IsolatedAsyncioTestCase):
    async def test_socket_identity_cannot_be_spoofed(self):
        socket=Socket();server=SimpleNamespace(players={},db_manager=object())
        await handle_inventory_message(server,'anon',{'type':'inventory/use','user_id':101},socket)
        self.assertFalse(socket.messages[-1]['success'])
        server.players['own']=SimpleNamespace(user_id=102)
        with patch('server.database.inventory_router.get_inventory',return_value={'user_id':102}) as get:
            await handle_inventory_message(server,'own',{'type':'inventory/get','user_id':101},socket)
            get.assert_called_once_with(server.db_manager,102)

    async def test_private_balances_and_public_appearance_update(self):
        owner,observer=Socket(),Socket();player=SimpleNamespace(user_id=101)
        game=SimpleNamespace(player_list=[player])
        server=SimpleNamespace(db_manager=object(),players={'own':SimpleNamespace(websocket=owner),'other':SimpleNamespace(websocket=observer)},
            user_id_to_connection={101:SimpleNamespace(websocket=owner)},room_manager=SimpleNamespace(rooms={1:{'player_settings':{101:{}}}}),
            gamestate_manager=SimpleNamespace(user_id_to_game_state={101:game},get_game_state_by_user_id=lambda _:game))
        appearance=dict(user_id=101,character_id=2,voice_id=2,profile_image_id=2001,avatar_frame_id=2101)
        state=dict(user_id=101,owned=[{'item_id':3001,'quantity':9}],appearance=appearance)
        with patch('server.database.inventory_router.get_inventory',return_value=state),patch('server.database.inventory_router.get_catalog',return_value=[]):
            await _sync(server,[101])
        self.assertEqual(len(owner.messages),2);self.assertEqual(len(observer.messages),1)
        self.assertNotIn('inventory_state',observer.messages[0]);self.assertNotIn('owned',observer.messages[0])
        self.assertEqual(observer.messages[0]['inventory_appearances'],[appearance])
        self.assertEqual(player.avatar_frame_used,2101);self.assertEqual(player.voice_used,2)
        self.assertEqual(server.room_manager.rooms[1]['player_settings'][101]['avatar_frame_id'],2101)
        other=SimpleNamespace(user_id=101)
        attach_game_frames(SimpleNamespace(player_list=[other]),server.room_manager.rooms[1])
        self.assertEqual(other.avatar_frame_used,2101)


if __name__=='__main__': unittest.main()
