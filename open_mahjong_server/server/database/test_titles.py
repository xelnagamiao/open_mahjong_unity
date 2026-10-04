"""Run with TITLES_TEST_DATABASE_URL; each run owns a disposable schema only."""
import os
import unittest
import uuid
from types import SimpleNamespace
from unittest.mock import patch

import psycopg2

from server.database.titles import ensure_title_tables, equip_title, get_title_state
from server.database.title_router import handle_title_message, sync_titles


@unittest.skipUnless(os.environ.get("TITLES_TEST_DATABASE_URL"), "requires PostgreSQL test connection")
class TitleDatabaseTests(unittest.TestCase):
    def setUp(self):
        self.schema = "title_py_test_" + uuid.uuid4().hex
        self.conn = psycopg2.connect(os.environ["TITLES_TEST_DATABASE_URL"])
        with self.conn.cursor() as cur:
            cur.execute('CREATE SCHEMA ' + self.schema)
            cur.execute('SET search_path TO ' + self.schema)
            cur.execute('CREATE TABLE users (user_id bigint PRIMARY KEY)')
            cur.execute('CREATE TABLE user_settings (user_id bigint PRIMARY KEY, title_id int DEFAULT 1, updated_at timestamp DEFAULT NOW())')
            cur.execute('INSERT INTO users VALUES (101), (102)')
            cur.execute('INSERT INTO user_settings (user_id,title_id) VALUES (101,2)')
            ensure_title_tables(cur)
            ensure_title_tables(cur)
            cur.execute("INSERT INTO titles (name) VALUES ('测试冠军') RETURNING title_id")
            self.title = cur.fetchone()[0]
        self.conn.commit()
        self.db = SimpleNamespace(_get_connection=lambda: self.conn, _put_connection=lambda conn: None)

    def tearDown(self):
        self.conn.rollback()
        with self.conn.cursor() as cur:
            cur.execute('DROP SCHEMA ' + self.schema + ' CASCADE')
        self.conn.commit()
        self.conn.close()

    def test_legacy_grant_and_single_selection_persist(self):
        state = get_title_state(self.db, 101)
        self.assertEqual(state['equipped_title_id'], 2)
        self.assertEqual(len(state['owned']), 1)
        with self.conn.cursor() as cur:
            cur.execute('INSERT INTO user_titles (user_id,title_id) VALUES (%s,%s)', (101,self.title))
        self.conn.commit()
        self.assertEqual(equip_title(self.db,101,self.title)['equipped_title_id'],self.title)
        self.assertEqual(get_title_state(self.db,101)['equipped_title_id'],self.title)
        self.assertEqual(equip_title(self.db,101,1)['equipped_title_id'],1)
        self.assertEqual(len(get_title_state(self.db,101)['owned']),2)

    def test_unowned_and_invalid_selection_rejected(self):
        for invalid in (self.title, 999999, 0, -1, None, '2', 2.5, True, 2147483648):
            with self.assertRaises(ValueError):
                equip_title(self.db,101,invalid)
        self.assertEqual(get_title_state(self.db,101)['equipped_title_id'],2)
        with self.assertRaises(ValueError):
            equip_title(self.db,102,2)

    def test_disabled_title_is_hidden_and_unwearable(self):
        with self.conn.cursor() as cur:
            cur.execute('UPDATE titles SET is_enabled=FALSE WHERE title_id=2')
        self.conn.commit()
        state=get_title_state(self.db,101)
        self.assertEqual(state['equipped_title_id'],1)
        self.assertEqual(len(state['owned']),1)
        with self.assertRaises(ValueError):
            equip_title(self.db,101,2)


class Socket:
    def __init__(self): self.messages = []
    async def send_json(self, payload): self.messages.append(payload)


class TitleRouterTests(unittest.IsolatedAsyncioTestCase):
    async def test_unauthenticated_and_foreign_user_requests(self):
        socket=Socket()
        server=SimpleNamespace(players={},db_manager=object())
        await handle_title_message(server,'guest',{'type':'title/equip','title_id':2,'user_id':101},socket)
        self.assertFalse(socket.messages[-1]['success'])
        server.players['self']=SimpleNamespace(user_id=102)
        state={'user_id':102,'equipped_title_id':1,'catalog':[],'owned':[]}
        with patch('server.database.title_router.get_title_state',return_value=state) as get_state:
            await handle_title_message(server,'self',{'type':'title/get','user_id':101},socket)
            get_state.assert_called_once_with(server.db_manager,102)
        self.assertEqual(socket.messages[-1]['title_state']['user_id'],102)

    async def test_sync_updates_room_live_game_and_observer_without_leaking_grants(self):
        owner,observer=Socket(),Socket()
        game=SimpleNamespace(player_list=[SimpleNamespace(user_id=101,title_used=2)])
        server=SimpleNamespace(db_manager=object(), players={'a':SimpleNamespace(websocket=owner),'b':SimpleNamespace(websocket=observer)},
            user_id_to_connection={101:SimpleNamespace(websocket=owner)},
            room_manager=SimpleNamespace(rooms={'room':{'player_list':[101], 'player_settings':{101:{'title_id':2}}}}),
            gamestate_manager=SimpleNamespace(user_id_to_game_state={101:game},get_game_state_by_user_id=lambda uid:game))
        state={'user_id':101,'equipped_title_id':1,'catalog':[],'owned':[]}
        with patch('server.database.title_router.get_title_state',return_value=state), patch('server.database.title_router.get_title_catalog',return_value=[]):
            await sync_titles(server,[101])
        self.assertEqual(game.player_list[0].title_used,1)
        self.assertEqual(server.room_manager.rooms['room']['player_settings'][101]['title_id'],1)
        self.assertEqual(owner.messages[0]['title_state'],state)
        self.assertNotIn('title_state',observer.messages[0])
        self.assertEqual(observer.messages[0]['title_changes'],[{'user_id':101,'title_id':1}])


if __name__ == '__main__': unittest.main()
