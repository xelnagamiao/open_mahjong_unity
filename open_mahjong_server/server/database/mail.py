"""Private player mail; attachment claims commit with the inventory ledger."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import uuid

from psycopg2.extras import Json, RealDictCursor
from .reward_receipts import make_reward_receipt
from .inventory import (CATALOG_LOCK, InventoryError, _change, _definition, _int,
                        _public, _read, _state, _text)


def ensure_mail_tables(cursor):
    cursor.execute(Path(__file__).with_name('mail_schema.sql').read_text(encoding='utf-8'))


def _entry(cursor, row):
    cursor.execute('''SELECT r.quantity,d.* FROM player_mail_rewards r
        JOIN item_definitions d USING(item_id) WHERE r.mail_id=%s ORDER BY r.item_id''', (row['mail_id'],))
    rewards = []
    for reward in cursor.fetchall():
        item = _public(reward)
        rewards.append(dict(item_id=reward['item_id'], quantity=reward['quantity'],
                            name=reward['name'], icon_path=item.get('icon_path', '')))
    return dict(mail_id=row['mail_id'], title=row['title'], content=row['content'],
                sender=row['sender'], created_at=row['created_at'].isoformat(),
                expires_at=row['expires_at'].isoformat() if row['expires_at'] else None,
                is_read=row['read_at'] is not None, claimed=row['claimed_at'] is not None,
                expired=bool(row['expired']), rewards=rewards)


def _mail(cursor, user_id, mail_id, lock=False):
    cursor.execute('''SELECT *,expires_at IS NOT NULL AND expires_at<=CURRENT_TIMESTAMP AS expired
        FROM player_mail WHERE user_id=%s AND mail_id=%s''' + (' FOR UPDATE' if lock else ''),
        (user_id, _int(mail_id, '邮件 ID', 1, 9223372036854775807)))
    row = cursor.fetchone()
    if not row:
        raise InventoryError('邮件不存在', 404)
    return row


def _unread(cursor, user_id):
    cursor.execute('''SELECT COUNT(*) AS count FROM player_mail WHERE user_id=%s AND read_at IS NULL
        AND (expires_at IS NULL OR expires_at>CURRENT_TIMESTAMP)''', (user_id,))
    return cursor.fetchone()['count']


def list_mail(db, user_id):
    _int(user_id, '用户 ID', 11, 9223372036854775807)
    def work(cursor):
        cursor.execute('''SELECT *,expires_at IS NOT NULL AND expires_at<=CURRENT_TIMESTAMP AS expired
            FROM player_mail WHERE user_id=%s ORDER BY mail_id DESC''', (user_id,))
        rows = cursor.fetchall()
        return dict(mails=[_entry(cursor, row) for row in rows], unread_count=_unread(cursor, user_id))
    return _read(db, work)


def read_mail(db, user_id, mail_id):
    _int(user_id, '用户 ID', 11, 9223372036854775807)
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            row = _mail(cursor, user_id, mail_id, True)
            if row['read_at'] is None:
                cursor.execute('UPDATE player_mail SET read_at=CURRENT_TIMESTAMP WHERE mail_id=%s', (mail_id,))
                row = _mail(cursor, user_id, mail_id)
            result = dict(mail=_entry(cursor, row), unread_count=_unread(cursor, user_id))
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def claim_mail(db, user_id, mail_id):
    _int(user_id, '用户 ID', 11, 9223372036854775807)
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
            cursor.execute('SELECT user_id FROM users WHERE user_id=%s FOR UPDATE', (user_id,))
            if not cursor.fetchone():
                raise InventoryError('用户不存在', 404)
            row = _mail(cursor, user_id, mail_id, True)
            if row['claimed_at'] is None:
                if row['expired']:
                    raise InventoryError('邮件已过期', 409)
                cursor.execute('SELECT item_id,quantity FROM player_mail_rewards WHERE mail_id=%s ORDER BY item_id', (mail_id,))
                rewards = cursor.fetchall()
                if not rewards:
                    raise InventoryError('此邮件没有可领取的物品', 409)
                operation = str(uuid.uuid4())
                digest = hashlib.sha256(json.dumps([dict(r) for r in rewards], sort_keys=True).encode()).hexdigest()
                cursor.execute('''INSERT INTO inventory_operations
                    (operation_id,user_id,request_id,request_hash,action,actor_id,reason)
                    VALUES (%s,%s,%s,%s,'mail_claim',%s,%s)''',
                    (operation, user_id, f'mail:{mail_id}', digest, row['created_by'], f'领取邮件 {mail_id}'))
                changes = []
                for reward in rewards:
                    item = _definition(cursor, reward['item_id'])
                    if not item['grant_enabled']:
                        raise InventoryError('邮件物品暂不可领取，请稍后重试', 409)
                    changes.append(_change(cursor, operation, user_id, item, reward['quantity'], True))
                cursor.execute('UPDATE inventory_operations SET result=%s WHERE operation_id=%s',
                               (Json(dict(mail_id=mail_id, changes=changes, reward_receipt=make_reward_receipt(
                                   cursor,operation,user_id,'mail',mail_id,changes))), operation))
                cursor.execute('''UPDATE player_mail SET claimed_at=CURRENT_TIMESTAMP,
                    read_at=COALESCE(read_at,CURRENT_TIMESTAMP),claim_operation_id=%s WHERE mail_id=%s''',
                    (operation, mail_id))
            row = _mail(cursor, user_id, mail_id)
            result = dict(mail=_entry(cursor, row), unread_count=_unread(cursor, user_id),
                          inventory_state=_state(cursor, user_id), message='物品已放入背包')
            cursor.execute('SELECT result FROM inventory_operations WHERE operation_id=%s', (row['claim_operation_id'],))
            operation = cursor.fetchone()
            if operation and operation['result'].get('reward_receipt'):
                result['reward_receipt'] = operation['result']['reward_receipt']
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def send_mail(db, actor_id, body):
    _int(actor_id, '管理员 ID', 1, 9223372036854775807)
    if not isinstance(body, dict):
        raise InventoryError('请求须为对象')
    user_id = _int(body.get('user_id'), '用户 ID', 11, 9223372036854775807)
    key = body.get('request_id')
    if not isinstance(key, str) or not re.fullmatch(r'[A-Za-z0-9_-]{8,64}', key):
        raise InventoryError('缺少有效的投递编号')
    title = _text(body.get('title'), '邮件标题', 160)
    sender = _text(body.get('sender', 'Salasasa'), '发件人', 80)
    reason = _text(body.get('reason', '发送玩家邮件'), '操作原因', 500)
    content = body.get('content', '')
    if not isinstance(content, str) or not content.strip() or len(content) > 20000 or re.search(r'[<>\x00-\x08\x0b\x0c\x0e-\x1f\x7f]', content):
        raise InventoryError('邮件内容须为 1–20000 字，不能包含标签或控制字符')
    rewards = body.get('rewards', [])
    if not isinstance(rewards, list) or len(rewards) > 8:
        raise InventoryError('每封邮件最多包含 8 种物品')
    normalized_rewards, seen = [], set()
    for reward in rewards:
        if not isinstance(reward, dict):
            raise InventoryError('物品格式无效')
        iid = _int(reward.get('item_id'), '物品 ID')
        quantity = _int(reward.get('quantity'), '物品数量', 1, 1000000)
        if iid in seen:
            raise InventoryError('物品不可重复')
        seen.add(iid)
        normalized_rewards.append(dict(item_id=iid, quantity=quantity))
    expires = body.get('expires_at')
    if expires is not None:
        try:
            expires = datetime.fromisoformat(expires.replace('Z', '+00:00'))
            if expires.tzinfo is None:
                raise ValueError()
        except (ValueError, TypeError, AttributeError):
            raise InventoryError('过期时间须包含时区')
    normalized = dict(actor=actor_id, user_id=user_id, title=title, content=content,
                      sender=sender, rewards=normalized_rewards, expires_at=expires.isoformat() if expires else None)
    digest = hashlib.sha256(json.dumps(normalized, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
            cursor.execute('SELECT user_id FROM users WHERE user_id=%s FOR UPDATE', (user_id,))
            if not cursor.fetchone():
                raise InventoryError('用户不存在', 404)
            cursor.execute('SELECT mail_id,delivery_hash FROM player_mail WHERE delivery_key=%s', (key,))
            existing = cursor.fetchone()
            if existing:
                if existing['delivery_hash'] != digest:
                    raise InventoryError('投递编号已用于其他邮件', 409)
                return dict(mail=_entry(cursor, _mail(cursor, user_id, existing['mail_id'])), duplicate=True)
            if expires and expires <= datetime.now(timezone.utc):
                raise InventoryError('过期时间须晚于当前时间')
            for reward in normalized_rewards:
                item = _definition(cursor, reward['item_id'])
                if item['is_default'] or not item['grant_enabled']:
                    raise InventoryError('此物品无需或不可发放', 409)
                if item['category'] != 'item' and reward['quantity'] != 1:
                    raise InventoryError('永久物品只能发放一份')
                if reward['quantity'] > item['max_quantity']:
                    raise InventoryError('物品数量超过持有上限')
            cursor.execute('''INSERT INTO player_mail
                (user_id,sender,title,content,created_by,delivery_key,delivery_hash,expires_at)
                VALUES (%s,%s,%s,%s,%s,%s,%s,%s) RETURNING mail_id''',
                (user_id, sender, title, content, actor_id, key, digest, expires))
            mail_id = cursor.fetchone()['mail_id']
            for reward in normalized_rewards:
                cursor.execute('INSERT INTO player_mail_rewards(mail_id,item_id,quantity) VALUES(%s,%s,%s)',
                               (mail_id, reward['item_id'], reward['quantity']))
            cursor.execute('''INSERT INTO admin_audit_log(admin_user_id,action,target_type,target_id,payload,reason)
                VALUES(%s,'mail.send','user',%s,%s,%s)''',
                (actor_id, str(user_id), Json(dict(mail_id=mail_id, title=title, rewards=normalized_rewards)), reason))
            result = dict(mail=_entry(cursor, _mail(cursor, user_id, mail_id)), duplicate=False)
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        # Duplicate delivery only reads; release its row lock before pooling.
        conn.rollback()
        db._put_connection(conn)
