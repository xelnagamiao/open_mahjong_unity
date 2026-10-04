"""Real PostgreSQL transactions in disposable schemas; never modify live users."""
import ast
from concurrent.futures import ThreadPoolExecutor
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
from server.database.inventory_assets import DEFAULT_AVATAR_FRAME_ID, B1_AVATAR_FRAME_ID
from server.database.db_manager import DatabaseManager


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
        self.migrate()

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

    def migrate(self):
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
        self.migrate()
        self.assertEqual([(i['item_id'],i['name']) for i in get_catalog(self.db)],[(2201,'金橙色'),(2202,'青色'),(3001,'改名卡')])
        self.assertEqual(self.sql('SELECT title_id,name FROM titles'),[(2,'最初的初段')])
        self.assertEqual(self.qty(1002,103),0)
        self.assertEqual(get_inventory(self.db,103)['appearance']['voice_id'],2)
        self.assertEqual(get_inventory(self.db,103)['equipment'],[{'slot':'avatar_frame','item_id':2201}])
        self.assertEqual(get_inventory(self.db,103)['owned'],[{'item_id':2201,'quantity':1},{'item_id':2202,'quantity':1}])
        self.assertEqual(self.sql("SELECT COUNT(*) FROM admin_audit_log WHERE action='store.catalog_reset'"),[(1,)])

    def test_cleanup_archives_retired_content_and_preserves_rename_cards(self):
        self.call('grant',3001,4)
        self.sql("""UPDATE users SET rename_count=7 WHERE user_id=101;
            DELETE FROM admin_audit_log WHERE action='store.catalog_reset';
            INSERT INTO titles(title_id,name) VALUES(3,'旧头衔');
            INSERT INTO user_titles(user_id,title_id) VALUES(101,3),(103,2);
            INSERT INTO item_definitions(item_id,code,name,category,asset_key,slot,max_quantity)
                VALUES(1002,'old_character','旧角色','character','character.qiuqiu','character',1),
                      (2001,'old_avatar','旧头像','cosmetic','avatar.pixel_teal','avatar',1);
            INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,1002,1),(101,2001,1);
            INSERT INTO user_equipment(user_id,slot,item_id) VALUES(101,'character',1002),(101,'avatar',2001);
            UPDATE user_settings SET title_id=3,character_id=2,voice_id=2,profile_image_id=2001 WHERE user_id=101;
            INSERT INTO inventory_ledger(operation_id,user_id,item_id,delta,quantity_after)
                SELECT operation_id,101,1002,1,1 FROM inventory_operations LIMIT 1;""")
        self.migrate()
        state=get_inventory(self.db,101)
        self.assertEqual(state['owned'],[{'item_id':3001,'quantity':4},{'item_id':2201,'quantity':1},{'item_id':2202,'quantity':1}])
        self.assertEqual(state['rename_count'],7)
        self.assertEqual(state['equipment'],[{'slot':'avatar_frame','item_id':2201}])
        self.assertEqual(state['appearance'],dict(user_id=101,character_id=1,voice_id=1,profile_image_id=1,avatar_frame_id=2201))
        self.assertEqual(self.sql('SELECT title_id FROM user_settings WHERE user_id=101'),[(1,)])
        self.assertEqual(self.sql('SELECT user_id,title_id FROM user_titles'),[(103,2)])
        archived=self.sql("SELECT payload FROM admin_audit_log WHERE action='store.catalog_reset'")[0][0]
        self.assertEqual(len(archived['user_inventory']),2)
        self.assertEqual(len(archived['inventory_ledger']),1)
        self.assertEqual(len(get_ledger(self.db,101)),1)
        self.migrate()
        self.assertEqual(self.qty(3001),4)

    def test_removed_items_and_equipping_consumables_are_rejected(self):
        with self.assertRaises(InventoryError): self.call('equip',1002,slot='character')
        for iid in (1001,1002,2001,2002,2101,2102,3002):
            with self.assertRaises(InventoryError): self.call('grant',iid)
        self.call('grant',3001)
        with self.assertRaises(InventoryError): self.call('equip',3001,slot='character')
        self.assertEqual(get_inventory(self.db,101)['equipment'],[{'slot':'avatar_frame','item_id':2201}])

    def test_builtin_frames_default_switch_persist_and_reset(self):
        for uid in (101,102):
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(self.qty(DEFAULT_AVATAR_FRAME_ID,uid),1)
            self.assertEqual(self.qty(B1_AVATAR_FRAME_ID,uid),1)
            self.call('equip',B1_AVATAR_FRAME_ID,uid=uid,slot='avatar_frame')
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],B1_AVATAR_FRAME_ID)
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],B1_AVATAR_FRAME_ID)
            self.migrate()
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],B1_AVATAR_FRAME_ID)
            self.call('equip',0,uid=uid,slot='avatar_frame')
            self.assertEqual(get_inventory(self.db,uid)['appearance']['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.call('equip',DEFAULT_AVATAR_FRAME_ID,uid=uid,slot='avatar_frame')
            self.assertEqual(DatabaseManager.get_user_settings(self.db,uid)['avatar_frame_id'],DEFAULT_AVATAR_FRAME_ID)
            self.assertEqual(get_ledger(self.db,uid),[])

    def test_idempotency_replay_conflict_and_revoke(self):
        body=dict(request_id=uuid.uuid4().hex,item_id=3001,quantity=3,reason='retry')
        first=mutate_inventory(self.db,101,'grant',body,999)
        again=mutate_inventory(self.db,101,'grant',body,999)
        self.assertEqual(first['operation_id'],again['operation_id']);self.assertEqual(self.qty(3001),3)
        with self.assertRaises(InventoryError): mutate_inventory(self.db,101,'grant',dict(body,quantity=4),999)
        self.call('revoke',3001)
        self.assertEqual(self.qty(3001),2)
        self.assertEqual(len(get_ledger(self.db,101)),2)

    def test_consume_is_atomic_and_cannot_race_below_zero(self):
        self.call('grant',3001)
        def use():
            try: self.call('use',3001); return True
            except InventoryError: return False
        with ThreadPoolExecutor(max_workers=2) as pool: results=list(pool.map(lambda _:use(),range(2)))
        self.assertEqual(sorted(results),[False,True])
        self.assertEqual(self.qty(3001),0)
        self.assertEqual(get_inventory(self.db,101)['rename_count'],1)
        self.assertEqual(len(get_ledger(self.db,101)),2)

    def test_use_retry_after_commit_does_not_consume_twice(self):
        self.call('grant',3001,2)
        body=dict(request_id=uuid.uuid4().hex,item_id=3001,quantity=1)
        mutate_inventory(self.db,101,'use',body)
        mutate_inventory(self.db,101,'use',body)
        self.assertEqual(self.qty(3001),1)
        self.assertEqual(get_inventory(self.db,101)['rename_count'],1)

    def test_batch_use_adds_to_existing_rename_credits_and_persists(self):
        self.sql('UPDATE users SET rename_count=5 WHERE user_id=101')
        self.call('grant',3001,4)
        result=self.call('use',3001,3)
        self.assertEqual(result['inventory_state']['rename_count'],8)
        self.assertEqual(result['inventory_state']['owned'],[{'item_id':3001,'quantity':1},{'item_id':2201,'quantity':1},{'item_id':2202,'quantity':1}])
        self.assertEqual(get_inventory(self.db,101)['rename_count'],8)
        self.assertEqual(self.sql('SELECT rename_count FROM users WHERE user_id=101'),[(8,)])
        ledger=get_ledger(self.db,101)
        self.assertEqual([(row['delta'],row['quantity_after']) for row in ledger],[(-3,1),(4,4)])

    def test_rename_credit_limit_rolls_back_card_consumption(self):
        self.sql('UPDATE users SET rename_count=2147483646 WHERE user_id=101')
        self.call('grant',3001,2)
        with self.assertRaises(InventoryError): self.call('use',3001,2)
        self.assertEqual(self.qty(3001),2)
        self.assertEqual(get_inventory(self.db,101)['rename_count'],2147483646)
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
            self.assertEqual(get_inventory(self.db,uid)['rename_count'],0)
            with self.assertRaises(InventoryError): self.call('use',3001,uid=uid)

    def test_retired_templates_rejected_and_future_additions_survive_restart(self):
        for asset in ('character.xiaoxiao','character.qiuqiu','avatar.pixel_teal','avatar.pixel_sunset','frame.jade','frame.gold','item.fixed_bundle'):
            body=self.definition(3001,asset_key=asset)
            with self.assertRaises(InventoryError): save_definition(self.db,999,None,body)
        body=self.definition(3001);body.update(code='test_rename',name='测试改名卡')
        created=save_definition(self.db,999,None,body)['item']
        self.assertGreaterEqual(created['item_id'],4000)
        self.call('grant',created['item_id'])
        self.migrate()
        self.assertEqual(self.qty(created['item_id']),1)
        self.call('use',created['item_id'])
        self.assertEqual(get_inventory(self.db,101)['rename_count'],1)

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
