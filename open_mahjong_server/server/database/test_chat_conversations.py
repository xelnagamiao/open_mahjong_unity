"""Real PostgreSQL chat transactions in disposable schemas; never live players."""
import asyncio
from concurrent.futures import ThreadPoolExecutor
import os
from types import SimpleNamespace
import unittest
import uuid

import psycopg2
from psycopg2.pool import ThreadedConnectionPool

from server.database import chat_conversations as chat
from server.chat_server.conversation_router import handle_conversation_message


@unittest.skipUnless(os.environ.get('CHAT_TEST_DATABASE_URL'), 'requires PostgreSQL disposable-schema connection')
class ChatConversationTests(unittest.TestCase):
    def setUp(self):
        self.schema = 'chat_test_' + uuid.uuid4().hex
        self.admin = psycopg2.connect(os.environ['CHAT_TEST_DATABASE_URL'])
        with self.admin.cursor() as c: c.execute('CREATE SCHEMA ' + self.schema)
        self.admin.commit()
        self.pool = ThreadedConnectionPool(1, 8, os.environ['CHAT_TEST_DATABASE_URL'], options='-c search_path=' + self.schema)
        self.db = SimpleNamespace(_get_connection=self.pool.getconn, _put_connection=self.pool.putconn)
        self.sql('''CREATE TABLE users(user_id BIGINT PRIMARY KEY,username TEXT NOT NULL,is_tourist BOOLEAN DEFAULT FALSE,
                ban_type TEXT,ban_expires_at TIMESTAMP);
            CREATE TABLE user_settings(user_id BIGINT PRIMARY KEY REFERENCES users,profile_image_id INT DEFAULT 1);
            CREATE TABLE user_friendships(user_id_small BIGINT REFERENCES users,user_id_large BIGINT REFERENCES users,
                PRIMARY KEY(user_id_small,user_id_large));
            CREATE TABLE events(event_id VARCHAR(32) PRIMARY KEY,name TEXT,status TEXT,kind TEXT,created_by BIGINT,entry_config JSONB DEFAULT '{}');
            CREATE TABLE event_admins(event_id VARCHAR(32) REFERENCES events,user_id BIGINT,role TEXT,PRIMARY KEY(event_id,user_id));
            CREATE TABLE event_registrations(event_id VARCHAR(32) REFERENCES events,user_id BIGINT,status TEXT,PRIMARY KEY(event_id,user_id));
            INSERT INTO users(user_id,username) VALUES (101,'isolated A'),(102,'isolated B'),(103,'outsider'),(104,'pending'),(105,'tourist');
            UPDATE users SET is_tourist=TRUE WHERE user_id=105;
            INSERT INTO user_settings(user_id,profile_image_id) VALUES(101,2302),(102,2303);
            INSERT INTO user_friendships VALUES(101,102);
            INSERT INTO events(event_id,name,status,kind,created_by) VALUES('e1','isolated event','active','event',101),('b1','isolated base','registered','base',101);
            INSERT INTO event_registrations VALUES('e1',102,'approved'),('b1',102,'approved'),('e1',104,'pending'),('e1',105,'approved');''')
        connection = self.pool.getconn()
        try:
            with connection.cursor() as c: chat.ensure_chat_tables(c)
            connection.commit()
        finally: self.pool.putconn(connection)

    def tearDown(self):
        self.pool.closeall()
        with self.admin.cursor() as c: c.execute('DROP SCHEMA ' + self.schema + ' CASCADE')
        self.admin.commit(); self.admin.close()

    def sql(self, statement, values=None):
        connection = self.pool.getconn()
        try:
            with connection.cursor() as c:
                c.execute(statement, values)
                result = c.fetchall() if c.description else None
            connection.commit(); return result
        except Exception:
            connection.rollback(); raise
        finally: self.pool.putconn(connection)

    def send(self, uid, key, text='isolated message', request=None):
        return chat.send_message(self.db, uid, key, text, request or uuid.uuid4().hex)

    def age_messages(self):
        self.sql("UPDATE chat_messages SET created_at=NOW()-INTERVAL '2 seconds'")

    def test_two_friends_exchange_read_and_identity(self):
        first = chat.open_conversation(self.db, 101, peer_user_id=102)
        key = first['conversation']['key']
        self.assertEqual(key, 'dm:101:102')
        self.assertEqual(first['conversation']['title'], 'isolated B')
        self.assertEqual(first['conversation']['profile_image_id'], 2303)
        self.assertEqual(chat.list_conversations(self.db, 102), [])
        a = self.send(101, key)
        self.assertEqual(a['chat_message']['sender_name'], 'isolated A')
        self.assertEqual(a['chat_message']['sender_user_id'], 101)
        self.assertEqual(chat.list_conversations(self.db, 102)[0]['unread_count'], 1)
        b = self.send(102, key, 'reply')
        history = chat.history(self.db, 101, key)['messages']
        self.assertEqual([row['sender_user_id'] for row in history], [101, 102])
        self.assertEqual(chat.mark_read(self.db, 101, key, b['chat_message']['id'])['unread_count'], 0)
        with self.assertRaises(chat.ChatError): chat.history(self.db, 103, key)
        with self.assertRaises(chat.ChatError): self.send(103, key)

    def test_nonfriend_nonmember_and_forged_membership_rejected(self):
        with self.assertRaisesRegex(chat.ChatError, '好友'): chat.open_conversation(self.db, 103, peer_user_id=102)
        with self.assertRaisesRegex(chat.ChatError, '加入'): chat.open_conversation(self.db, 103, event_id='e1')
        with self.assertRaisesRegex(chat.ChatError, '加入'): chat.open_conversation(self.db, 104, event_id='e1')
        key = chat.open_conversation(self.db, 101, event_id='e1')['conversation']['key']
        self.sql('INSERT INTO chat_conversation_members(conversation_key,user_id) VALUES(%s,103)', (key,))
        with self.assertRaisesRegex(chat.ChatError, '加入'): chat.history(self.db, 103, key)
        with self.assertRaisesRegex(chat.ChatError, '加入'): self.send(103, key)
        self.assertEqual(chat.list_conversations(self.db, 103), [])
        with self.assertRaises(chat.ChatError): chat.open_conversation(self.db, 101, event_id='does-not-exist')
        with self.assertRaises(chat.ChatError): chat.remove_conversation(self.db, 101, 'hall')

    def test_all_member_venues_lazy_open_and_closed_history(self):
        self.assertEqual({r['event_id'] for r in chat.list_chat_venues(self.db, 102)}, {'e1', 'b1'})
        self.assertEqual(chat.list_chat_venues(self.db, 103), [])
        a = chat.open_conversation(self.db, 102, event_id='e1')
        b = chat.open_conversation(self.db, 102, event_id='b1')
        self.assertEqual(a['conversation']['key'], 'venue:e1')
        self.assertEqual(b['conversation']['title'], 'isolated base')
        sent = self.send(102, 'venue:e1')
        self.sql("UPDATE events SET status='closed' WHERE event_id='e1'")
        self.assertEqual(chat.history(self.db, 102, 'venue:e1')['messages'][0]['id'], sent['chat_message']['id'])
        self.assertFalse(chat.history(self.db, 102, 'venue:e1')['conversation']['can_send'])
        with self.assertRaisesRegex(chat.ChatError, '关闭'): self.send(102, 'venue:e1')
        self.sql("UPDATE events SET entry_config='{\"forbid_tourist\":true}' WHERE event_id='e1'")
        with self.assertRaisesRegex(chat.ChatError, '游客'): chat.open_conversation(self.db, 105, event_id='e1')
        self.sql("INSERT INTO event_admins VALUES('e1',103,'admin')")
        self.assertEqual(chat.open_conversation(self.db, 103, event_id='e1')['conversation']['title'], 'isolated event')

    def test_both_venue_kinds_allow_creator_admin_and_approved_members(self):
        self.sql("INSERT INTO event_admins VALUES('e1',103,'admin'),('b1',103,'admin')")
        for uid in (101, 102, 103):
            with self.subTest(uid=uid):
                self.assertEqual({r['event_id'] for r in chat.list_chat_venues(self.db, uid)}, {'e1', 'b1'})
                for event_id in ('e1', 'b1'):
                    key = chat.open_conversation(self.db, uid, event_id=event_id)['conversation']['key']
                    self.assertEqual(key, 'venue:' + event_id)
                    self.age_messages(); self.send(uid, key)
                    self.assertTrue(chat.history(self.db, uid, key)['conversation']['can_send'])

    def test_both_venue_kinds_reject_nonmembers_despite_existing_conversation(self):
        for kind, event_id in (('event', 'e1'), ('base', 'b1')):
            with self.subTest(kind=kind):
                key = chat.open_conversation(self.db, 102, event_id=event_id)['conversation']['key']
                self.sql('INSERT INTO chat_conversation_members(conversation_key,user_id) VALUES(%s,103)', (key,))
                for status in (None, 'pending', 'rejected', 'cancelled'):
                    if status is not None:
                        self.sql('''INSERT INTO event_registrations VALUES(%s,103,%s)
                            ON CONFLICT(event_id,user_id) DO UPDATE SET status=EXCLUDED.status''', (event_id, status))
                    with self.subTest(status=status):
                        self.assertNotIn(event_id, [r['event_id'] for r in chat.list_chat_venues(self.db, 103)])
                        self.assertNotIn(key, [r['key'] for r in chat.list_conversations(self.db, 103)])
                        with self.assertRaisesRegex(chat.ChatError, '加入'): chat.open_conversation(self.db, 103, event_id=event_id)
                        with self.assertRaisesRegex(chat.ChatError, '加入'): chat.history(self.db, 103, key)
                        with self.assertRaisesRegex(chat.ChatError, '加入'): self.send(103, key)
                        with self.assertRaisesRegex(chat.ChatError, '加入'): chat.recipient_summary(self.db, 103, key)
                self.sql("UPDATE event_registrations SET status='approved' WHERE event_id=%s AND user_id=103", (event_id,))
                self.assertEqual(chat.open_conversation(self.db, 103, event_id=event_id)['conversation']['key'], key)
                self.assertIn(event_id, [r['event_id'] for r in chat.list_chat_venues(self.db, 103)])

    def test_both_venue_kinds_disappear_and_stop_delivery_after_membership_removed(self):
        for event_id in ('e1', 'b1'):
            with self.subTest(event_id=event_id):
                key = chat.open_conversation(self.db, 102, event_id=event_id)['conversation']['key']
                self.age_messages(); self.send(102, key)
                self.sql('DELETE FROM event_registrations WHERE event_id=%s AND user_id=102', (event_id,))
                self.assertNotIn(event_id, [r['event_id'] for r in chat.list_chat_venues(self.db, 102)])
                self.assertNotIn(key, [r['key'] for r in chat.list_conversations(self.db, 102)])
                with self.assertRaisesRegex(chat.ChatError, '加入'): chat.history(self.db, 102, key)
                with self.assertRaisesRegex(chat.ChatError, '加入'): self.send(102, key)
                with self.assertRaisesRegex(chat.ChatError, '加入'): chat.recipient_summary(self.db, 102, key)

    def test_remove_is_personal_and_new_message_reappears(self):
        key = chat.open_conversation(self.db, 101, peer_user_id=102)['conversation']['key']
        self.send(101, key)
        chat.remove_conversation(self.db, 101, key)
        self.assertEqual(chat.list_conversations(self.db, 101), [])
        self.assertEqual(len(chat.list_conversations(self.db, 102)), 1)
        self.assertEqual(len(chat.history(self.db, 102, key)['messages']), 1)
        self.send(102, key)
        self.assertEqual(len(chat.list_conversations(self.db, 101)), 1)
        for uid in (101, 102): chat.open_conversation(self.db, uid, event_id='b1')
        chat.remove_conversation(self.db, 101, 'venue:b1')
        self.assertEqual(self.sql("SELECT status FROM events WHERE event_id='b1'"), [('registered',)])
        self.age_messages(); self.send(102, 'venue:b1')
        self.assertIn('venue:b1', [row['key'] for row in chat.list_conversations(self.db, 101)])
        self.assertEqual(self.sql('SELECT COUNT(*) FROM chat_messages'), [(3,)])

    def test_membership_revoked_and_friend_removed(self):
        key = chat.open_conversation(self.db, 102, event_id='e1')['conversation']['key']
        self.send(102, key)
        self.sql("UPDATE event_registrations SET status='rejected' WHERE event_id='e1' AND user_id=102")
        with self.assertRaises(chat.ChatError): chat.history(self.db, 102, key)
        with self.assertRaises(chat.ChatError): chat.recipient_summary(self.db, 102, key)
        self.assertEqual(chat.list_conversations(self.db, 102), [])
        key = chat.open_conversation(self.db, 101, peer_user_id=102)['conversation']['key']
        self.sql('DELETE FROM user_friendships')
        self.assertFalse(chat.history(self.db, 101, key)['conversation']['can_send'])
        with self.assertRaisesRegex(chat.ChatError, '好友'): self.send(101, key)

    def test_bans_expiry_and_rate_rejections_do_not_write(self):
        key = chat.open_conversation(self.db, 101, peer_user_id=102)['conversation']['key']
        self.sql("UPDATE users SET ban_type='chat' WHERE user_id=101")
        self.assertFalse(chat.history(self.db, 101, key)['conversation']['can_send'])
        with self.assertRaisesRegex(chat.ChatError, '禁言'): self.send(101, key)
        self.sql("UPDATE users SET ban_expires_at=NOW()-INTERVAL '1 second' WHERE user_id=101")
        self.send(101, key)
        with self.assertRaisesRegex(chat.ChatError, '频繁'): self.send(101, key)
        self.assertEqual(self.sql('SELECT COUNT(*) FROM chat_messages'), [(1,)])
        self.sql("UPDATE users SET ban_type='full',ban_expires_at=NULL WHERE user_id=101")
        with self.assertRaisesRegex(chat.ChatError, '账号'): chat.history(self.db, 101, key)

    def test_concurrent_retry_once_and_conflicting_retry_rollback(self):
        key = chat.open_conversation(self.db, 101, peer_user_id=102)['conversation']['key']
        request = uuid.uuid4().hex
        with ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(lambda _: self.send(101, key, request=request), range(4)))
        self.assertEqual(len({r['chat_message']['id'] for r in results}), 1)
        self.assertEqual(sum(r['inserted'] for r in results), 1)
        with self.assertRaisesRegex(chat.ChatError, '编号'): self.send(101, key, 'different', request)
        self.assertEqual(self.sql('SELECT COUNT(*) FROM chat_messages'), [(1,)])
        with self.assertRaises(chat.ChatError): self.send(101, key, 'x' * 501)
        self.assertEqual(self.sql('SELECT COUNT(*) FROM chat_messages'), [(1,)])

    def test_history_page_and_read_cursor_does_not_swallow_next_message(self):
        key = chat.open_conversation(self.db, 101, peer_user_id=102)['conversation']['key']
        self.sql('''INSERT INTO chat_messages(conversation_key,sender_user_id,content,request_id,created_at)
            SELECT %s,102,'isolated history '||n,n::text,NOW()-INTERVAL '2 seconds' FROM generate_series(1,61) n''', (key,))
        first = chat.history(self.db, 101, key)
        self.assertEqual(len(first['messages']), 50); self.assertTrue(first['has_more'])
        second = chat.history(self.db, 101, key, first['messages'][0]['id'])
        self.assertEqual(len(second['messages']), 11); self.assertFalse(second['has_more'])
        last_seen = first['messages'][-1]['id']
        self.send(102, key)
        self.assertEqual(chat.mark_read(self.db, 101, key, last_seen)['unread_count'], 1)

    def test_production_router_two_sessions_ignores_forged_identity_and_stale_session(self):
        class Socket:
            def __init__(self): self.messages = []
            async def send_json(self, message): self.messages.append(message)
        sockets = {uid: Socket() for uid in (101, 102, 103)}
        players = {str(uid): SimpleNamespace(user_id=uid, websocket=socket) for uid, socket in sockets.items()}
        server = SimpleNamespace(db_manager=self.db, players=players,
                                 user_id_to_connection={p.user_id: p for p in players.values()})
        async def run():
            await handle_conversation_message(server, '101', dict(type='chat/open_direct', request_id='open101a', peer_user_id=102, user_id=103), sockets[101])
            key = sockets[101].messages[-1]['conversation']['key']
            await handle_conversation_message(server, '101', dict(type='chat/send', request_id=uuid.uuid4().hex, conversation_key=key,
                content='isolated router message', sender_user_id=103, sender_name='forged'), sockets[101])
            self.assertEqual(sockets[102].messages[-1]['chat_message']['sender_user_id'], 101)
            self.assertEqual(sockets[102].messages[-1]['conversation']['title'], 'isolated A')
            self.assertEqual(sockets[101].messages[-1]['conversation']['title'], 'isolated B')
            await handle_conversation_message(server, '103', dict(type='chat/history', request_id='outsider1', conversation_key=key), sockets[103])
            self.assertFalse(sockets[103].messages[-1]['success']); self.assertNotIn('messages', sockets[103].messages[-1])
            await handle_conversation_message(server, '101', dict(type='chat/list', request_id='stale001'), sockets[103])
            self.assertFalse(sockets[103].messages[-1]['success'])
            await handle_conversation_message(server, 'unknown', dict(type='chat/list', user_id=101), sockets[103])
            self.assertFalse(sockets[103].messages[-1]['success'])
            self.assertEqual(len(sockets[102].messages), 1)
        asyncio.run(run())

    def test_two_real_asgi_websockets_delivery_and_offline_history(self):
        from fastapi import FastAPI, WebSocket, WebSocketDisconnect
        from fastapi.testclient import TestClient
        app = FastAPI()
        server = SimpleNamespace(db_manager=self.db, players={}, user_id_to_connection={})
        tickets = {uuid.uuid4().hex: uid for uid in (101, 102, 103)}
        by_user = {uid: ticket for ticket, uid in tickets.items()}

        @app.websocket('/chat-test')
        async def endpoint(websocket: WebSocket):
            await websocket.accept()
            uid = tickets.get(websocket.headers.get('authorization'))
            cid = uuid.uuid4().hex
            player = SimpleNamespace(user_id=uid, websocket=websocket)
            if uid:
                server.players[cid] = player; server.user_id_to_connection[uid] = player
            try:
                while True:
                    await handle_conversation_message(server, cid, await websocket.receive_json(), websocket)
            except WebSocketDisconnect:
                pass
            finally:
                server.players.pop(cid, None)
                if server.user_id_to_connection.get(uid) is player: server.user_id_to_connection.pop(uid, None)

        with TestClient(app) as client:
            with client.websocket_connect('/chat-test', headers={'authorization': by_user[101]}) as a:
                with client.websocket_connect('/chat-test', headers={'authorization': by_user[102]}) as b:
                    a.send_json(dict(type='chat/open_direct', request_id='socketop', peer_user_id=102))
                    key = a.receive_json()['conversation']['key']
                    a.send_json(dict(type='chat/send', request_id=uuid.uuid4().hex, conversation_key=key, content='isolated websocket A'))
                    ack = a.receive_json(); received = b.receive_json()
                    self.assertEqual(ack['chat_message']['id'], received['chat_message']['id'])
                    self.assertEqual(received['user_id'], 102)
                    b.send_json(dict(type='chat/send', request_id=uuid.uuid4().hex, conversation_key=key, content='isolated websocket B'))
                    b.receive_json(); self.assertEqual(a.receive_json()['chat_message']['sender_user_id'], 102)
                self.age_messages()
                a.send_json(dict(type='chat/send', request_id=uuid.uuid4().hex, conversation_key=key, content='isolated offline message'))
                self.assertTrue(a.receive_json()['success'])
            with client.websocket_connect('/chat-test', headers={'authorization': by_user[102]}) as b:
                b.send_json(dict(type='chat/history', request_id='sockhist', conversation_key=key))
                self.assertEqual(len(b.receive_json()['messages']), 3)
            with client.websocket_connect('/chat-test', headers={'authorization': by_user[103]}) as outsider:
                outsider.send_json(dict(type='chat/history', request_id='sockdeny', conversation_key=key, user_id=101))
                self.assertFalse(outsider.receive_json()['success'])
            with client.websocket_connect('/chat-test') as anonymous:
                anonymous.send_json(dict(type='chat/list', request_id='sockanon', user_id=101))
                self.assertFalse(anonymous.receive_json()['success'])
