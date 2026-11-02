"""Authenticated private and venue chat; visibility belongs to each participant."""
from pathlib import Path
import re

from psycopg2.extras import RealDictCursor


class ChatError(ValueError):
    pass


def ensure_chat_tables(cursor):
    cursor.execute(Path(__file__).with_name('chat_schema.sql').read_text(encoding='utf-8'))


def _transaction(db, work):
    connection = db._get_connection()
    try:
        with connection.cursor(cursor_factory=RealDictCursor) as cursor:
            result = work(cursor)
        connection.commit()
        return result
    except Exception:
        connection.rollback()
        raise
    finally:
        db._put_connection(connection)


def _id(value):
    if isinstance(value, bool):
        raise ChatError('用户 ID 无效')
    try:
        result = int(value)
    except (ValueError, TypeError, OverflowError):
        raise ChatError('用户 ID 无效') from None
    if result <= 0 or result > 2147483647 or str(result) != str(value):
        raise ChatError('用户 ID 无效')
    return result


def _key(value):
    if not isinstance(value, str) or len(value) > 96 or not re.fullmatch(r'(?:dm:[1-9][0-9]*:[1-9][0-9]*|venue:[A-Za-z0-9_-]{1,32})', value):
        raise ChatError('会话不存在')
    return value


def _user(cursor, uid, sending=False):
    cursor.execute('''SELECT user_id,username,is_tourist,ban_type,
        ban_expires_at IS NULL OR ban_expires_at>CURRENT_TIMESTAMP AS ban_active
        FROM users WHERE user_id=%s''' + (' FOR UPDATE' if sending else ''), (uid,))
    user = cursor.fetchone()
    if not user:
        raise ChatError('请先登录')
    if user['ban_active'] and user['ban_type'] in ('login', 'full'):
        raise ChatError('账号当前不可使用聊天')
    if sending and user['ban_active'] and user['ban_type'] == 'chat':
        raise ChatError('账号当前已被禁言')
    return user


def _can_talk(user):
    return not (user['ban_active'] and user['ban_type'] in ('chat', 'login', 'full'))


def _friend(cursor, low, high, lock=False):
    cursor.execute('''SELECT 1 FROM user_friendships WHERE user_id_small=%s AND user_id_large=%s'''
                   + (' FOR SHARE' if lock else ''), (low, high))
    return cursor.fetchone() is not None


def _venue(cursor, event_id, uid, user, sending=False):
    cursor.execute('SELECT * FROM events WHERE event_id=%s' + (' FOR SHARE' if sending else ''), (event_id,))
    event = cursor.fetchone()
    if not event:
        raise ChatError('赛事或基地不存在')
    cursor.execute('SELECT 1 FROM event_admins WHERE event_id=%s AND user_id=%s'
                   + (' FOR SHARE' if sending else ''), (event_id, uid))
    admin = cursor.fetchone() is not None or event['created_by'] == uid
    cursor.execute("SELECT 1 FROM event_registrations WHERE event_id=%s AND user_id=%s AND status='approved'"
                   + (' FOR SHARE' if sending else ''), (event_id, uid))
    member = cursor.fetchone() is not None
    if not admin and not member:
        raise ChatError('请先加入该赛事或基地')
    from .db_manager import DatabaseManager
    config = DatabaseManager.parse_entry_config(event.get('entry_config'))
    if config['forbid_tourist'] and user['is_tourist'] and not admin:
        raise ChatError('该场馆不允许游客参加')
    if sending and event['status'] == 'closed':
        raise ChatError('赛事或基地已关闭，仅可查看历史')
    return event


def _conversation(cursor, uid, key, user, sending=False):
    cursor.execute('SELECT * FROM chat_conversations WHERE conversation_key=%s', (key,))
    row = cursor.fetchone()
    if not row:
        raise ChatError('会话不存在')
    cursor.execute('SELECT 1 FROM chat_conversation_members WHERE conversation_key=%s AND user_id=%s', (key, uid))
    if not cursor.fetchone():
        raise ChatError('无权访问此会话')
    if row['kind'] == 'dm':
        if uid not in (row['peer_low'], row['peer_high']):
            raise ChatError('无权访问此会话')
        if sending and not _friend(cursor, row['peer_low'], row['peer_high'], True):
            raise ChatError('请先添加对方为好友')
    else:
        _venue(cursor, row['event_id'], uid, user, sending)
    return row


_SUMMARY_SQL = '''SELECT c.*,p.hidden,p.last_read_message_id,
    peer.user_id AS peer_id,peer.username AS peer_name,COALESCE(settings.profile_image_id,1) AS profile,
    e.name AS event_name,e.status AS event_status,
    EXISTS(SELECT 1 FROM user_friendships f WHERE f.user_id_small=c.peer_low AND f.user_id_large=c.peer_high) AS are_friends,
    last.content AS last_content,last.created_at AS last_at,
    (SELECT COUNT(*) FROM chat_messages m WHERE m.conversation_key=c.conversation_key
      AND m.message_id>p.last_read_message_id AND m.sender_user_id<>p.user_id) AS unread
    FROM chat_conversations c JOIN chat_conversation_members p USING(conversation_key)
    LEFT JOIN users peer ON peer.user_id=CASE WHEN c.peer_low=p.user_id THEN c.peer_high ELSE c.peer_low END
    LEFT JOIN user_settings settings ON settings.user_id=peer.user_id
    LEFT JOIN events e ON e.event_id=c.event_id
    LEFT JOIN LATERAL (SELECT content,created_at FROM chat_messages m
      WHERE m.conversation_key=c.conversation_key ORDER BY message_id DESC LIMIT 1) last ON TRUE
    WHERE p.user_id=%s AND (%s IS NULL OR c.conversation_key=%s)
      AND (%s OR NOT p.hidden)
      AND ((c.kind='dm' AND p.user_id IN(c.peer_low,c.peer_high)) OR
        (c.kind='venue' AND (e.created_by=p.user_id OR
          EXISTS(SELECT 1 FROM event_admins a WHERE a.event_id=c.event_id AND a.user_id=p.user_id) OR
          EXISTS(SELECT 1 FROM event_registrations r WHERE r.event_id=c.event_id AND r.user_id=p.user_id AND r.status='approved'))
          AND (NOT (lower(COALESCE(e.entry_config->>'forbid_tourist','')) IN ('1','true','t','yes','y','on')) OR NOT %s OR e.created_by=p.user_id OR
            EXISTS(SELECT 1 FROM event_admins a WHERE a.event_id=c.event_id AND a.user_id=p.user_id))))
    ORDER BY COALESCE(last.created_at,c.created_at) DESC,c.conversation_key'''


def _summaries(cursor, uid, user, key=None, include_hidden=False):
    cursor.execute(_SUMMARY_SQL, (uid, key, key, include_hidden, user['is_tourist']))
    return [dict(key=r['conversation_key'], kind=r['kind'],
                 title=r['peer_name'] if r['kind'] == 'dm' else r['event_name'],
                 peer_user_id=r['peer_id'] or 0, event_id=r['event_id'] or '',
                 profile_image_id=r['profile'] if r['kind'] == 'dm' else 0,
                 last_message=r['last_content'] or '', last_message_at=(r['last_at'] or r['created_at']).isoformat(),
                 unread_count=int(r['unread']), can_send=_can_talk(user) and
                 (r['are_friends'] if r['kind'] == 'dm' else r['event_status'] != 'closed')) for r in cursor.fetchall()]


def list_conversations(db, uid):
    uid = _id(uid)
    return _transaction(db, lambda c: _summaries(c, uid, _user(c, uid)))


def list_chat_venues(db, uid):
    uid = _id(uid)
    def work(c):
        user = _user(c, uid)
        c.execute('''SELECT e.event_id,e.name,e.kind,e.status FROM events e WHERE
            (e.created_by=%s OR EXISTS(SELECT 1 FROM event_admins a WHERE a.event_id=e.event_id AND a.user_id=%s) OR
             EXISTS(SELECT 1 FROM event_registrations r WHERE r.event_id=e.event_id AND r.user_id=%s AND r.status='approved'))
            AND (NOT (lower(COALESCE(e.entry_config->>'forbid_tourist','')) IN ('1','true','t','yes','y','on')) OR NOT %s OR e.created_by=%s OR
             EXISTS(SELECT 1 FROM event_admins a WHERE a.event_id=e.event_id AND a.user_id=%s))
            ORDER BY CASE e.status WHEN 'active' THEN 0 WHEN 'registered' THEN 1 ELSE 2 END,e.kind,e.name,e.event_id''',
            (uid, uid, uid, user['is_tourist'], uid, uid))
        return [dict(event_id=r['event_id'], name=r['name'], kind=r['kind'], status=r['status'],
                     can_send=_can_talk(user) and r['status'] != 'closed') for r in c.fetchall()]
    return _transaction(db, work)


def open_conversation(db, uid, *, peer_user_id=None, event_id=None):
    uid = _id(uid)
    def work(c):
        user = _user(c, uid)
        if peer_user_id is not None:
            peer = _id(peer_user_id)
            if peer == uid:
                raise ChatError('不能与自己发起私聊')
            low, high = sorted((uid, peer))
            if not _friend(c, low, high, True):
                raise ChatError('请先添加对方为好友')
            key = f'dm:{low}:{high}'
            c.execute("INSERT INTO chat_conversations(conversation_key,kind,peer_low,peer_high) VALUES(%s,'dm',%s,%s) ON CONFLICT DO NOTHING", (key, low, high))
            c.execute('''INSERT INTO chat_conversation_members(conversation_key,user_id,hidden) VALUES(%s,%s,TRUE)
                ON CONFLICT DO NOTHING''', (key, peer))
        else:
            if not isinstance(event_id, str) or not re.fullmatch(r'[A-Za-z0-9_-]{1,32}', event_id):
                raise ChatError('赛事或基地不存在')
            _venue(c, event_id, uid, user)
            key = 'venue:' + event_id
            c.execute("INSERT INTO chat_conversations(conversation_key,kind,event_id) VALUES(%s,'venue',%s) ON CONFLICT DO NOTHING", (key, event_id))
        c.execute('''INSERT INTO chat_conversation_members(conversation_key,user_id,hidden) VALUES(%s,%s,FALSE)
            ON CONFLICT(conversation_key,user_id) DO UPDATE SET hidden=FALSE''', (key, uid))
        return dict(conversation=_summaries(c, uid, user, key)[0], messages=_messages(c, key, 0), has_more=_has_earlier(c, key))
    return _transaction(db, work)


def _message(row):
    return dict(id=row['message_id'], conversation_key=row['conversation_key'], sender_user_id=row['sender_user_id'],
                sender_name=row['username'], profile_image_id=row['profile_image_id'], content=row['content'], sent_at=row['created_at'].isoformat())


def _messages(cursor, key, before):
    cursor.execute('''SELECT m.*,u.username,COALESCE(s.profile_image_id,1) AS profile_image_id FROM chat_messages m
        JOIN users u ON u.user_id=m.sender_user_id LEFT JOIN user_settings s ON s.user_id=u.user_id
        WHERE conversation_key=%s AND (%s=0 OR message_id<%s) ORDER BY message_id DESC LIMIT 50''', (key, before, before))
    return [_message(r) for r in reversed(cursor.fetchall())]


def _has_earlier(cursor, key, before=0):
    cursor.execute('SELECT 1 FROM chat_messages WHERE conversation_key=%s AND (%s=0 OR message_id<%s) ORDER BY message_id DESC OFFSET 50 LIMIT 1', (key, before, before))
    return cursor.fetchone() is not None


def history(db, uid, key, before=0):
    uid, key = _id(uid), _key(key)
    try:
        before = int(before or 0)
        if before < 0 or before > 9223372036854775807: raise ValueError()
    except (ValueError, TypeError, OverflowError):
        raise ChatError('消息分页无效') from None
    def work(c):
        user = _user(c, uid)
        _conversation(c, uid, key, user)
        return dict(conversation=_summaries(c, uid, user, key, True)[0], messages=_messages(c, key, before), has_more=_has_earlier(c, key, before))
    return _transaction(db, work)


def send_message(db, uid, key, content, request_id):
    uid, key = _id(uid), _key(key)
    if not isinstance(content, str) or not content.strip() or len(content.strip()) > 500 or '\x00' in content:
        raise ChatError('消息需为 1–500 个字符')
    content = content.strip()
    if not isinstance(request_id, str) or not re.fullmatch(r'[A-Za-z0-9_-]{8,64}', request_id):
        raise ChatError('消息编号无效')
    def work(c):
        user = _user(c, uid, True)
        row = _conversation(c, uid, key, user, True)
        c.execute('SELECT message_id,conversation_key,content FROM chat_messages WHERE sender_user_id=%s AND request_id=%s', (uid, request_id))
        previous = c.fetchone()
        if previous:
            if previous['conversation_key'] != key or previous['content'] != content:
                raise ChatError('消息编号已用于其他消息')
            message_id, inserted = previous['message_id'], False
        else:
            c.execute("SELECT 1 FROM chat_messages WHERE sender_user_id=%s AND created_at>CURRENT_TIMESTAMP-INTERVAL '350 milliseconds' LIMIT 1", (uid,))
            if c.fetchone(): raise ChatError('发送过于频繁，请稍后重试')
            c.execute('''INSERT INTO chat_messages(conversation_key,sender_user_id,content,request_id)
                VALUES(%s,%s,%s,%s) RETURNING message_id''', (key, uid, content, request_id))
            message_id, inserted = c.fetchone()['message_id'], True
            c.execute('UPDATE chat_conversation_members SET hidden=FALSE WHERE conversation_key=%s', (key,))
        c.execute('''SELECT m.*,u.username,COALESCE(s.profile_image_id,1) AS profile_image_id FROM chat_messages m
            JOIN users u ON u.user_id=m.sender_user_id LEFT JOIN user_settings s ON s.user_id=u.user_id
            WHERE message_id=%s''', (message_id,))
        message = _message(c.fetchone())
        c.execute('SELECT user_id FROM chat_conversation_members WHERE conversation_key=%s', (key,))
        recipients = [r['user_id'] for r in c.fetchall()]
        return dict(chat_message=message, conversation=_summaries(c, uid, user, key, True)[0], recipient_ids=recipients, inserted=inserted)
    return _transaction(db, work)


def recipient_summary(db, uid, key):
    def work(c):
        user = _user(c, uid)
        _conversation(c, uid, key, user)
        rows = _summaries(c, uid, user, key)
        return rows[0] if rows else None
    return _transaction(db, work)


def mark_read(db, uid, key, last_message_id):
    uid, key = _id(uid), _key(key)
    try:
        last_message_id = int(last_message_id or 0)
        if last_message_id < 0 or last_message_id > 9223372036854775807: raise ValueError()
    except (ValueError, TypeError, OverflowError): raise ChatError('消息编号无效') from None
    def work(c):
        user = _user(c, uid)
        _conversation(c, uid, key, user)
        c.execute('''UPDATE chat_conversation_members SET last_read_message_id=GREATEST(last_read_message_id,
            COALESCE((SELECT MAX(message_id) FROM chat_messages WHERE conversation_key=%s AND message_id<=%s),0))
            WHERE conversation_key=%s AND user_id=%s''', (key, last_message_id, key, uid))
        return _summaries(c, uid, user, key, True)[0]
    return _transaction(db, work)


def remove_conversation(db, uid, key):
    uid, key = _id(uid), _key(key)
    def work(c):
        _user(c, uid)
        c.execute('UPDATE chat_conversation_members SET hidden=TRUE WHERE conversation_key=%s AND user_id=%s', (key, uid))
        if c.rowcount != 1: raise ChatError('会话不存在')
        return key
    return _transaction(db, work)
