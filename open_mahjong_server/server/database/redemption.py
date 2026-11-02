"""Code campaigns and atomic claims share the existing inventory operation ledger."""
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import uuid

from cryptography.fernet import Fernet, InvalidToken
from psycopg2.extras import RealDictCursor, Json, execute_values
from .inventory import CATALOG_LOCK, InventoryError, _int, _text, _read, _state, _definition, _change
from .reward_receipts import make_reward_receipt


def ensure_redemption_tables(cursor):
    cursor.execute(Path(__file__).with_name('redemption_schema.sql').read_text(encoding='utf-8'))


def _cipher():
    # Operators supply and back up one key shared by all instances. No generated
    # source-code secret, auth-secret reuse, or silent key change on restart.
    key = os.environ.get('REDEEM_CODE_KEY', '')
    if not key:
        key_file = os.environ.get('REDEEM_CODE_KEY_FILE', '')
        if not key_file:
            try:
                from ..local_config import Config
                key_file = getattr(Config,'redeem_code_key_file','')
            except ImportError:
                pass
        if key_file:
            try:
                key = Path(key_file).read_text(encoding='ascii').strip()
            except OSError:
                pass
    try:
        return Fernet(key.encode('ascii'))
    except (ValueError, UnicodeError):
        raise InventoryError('兑换码加密密钥未配置，请设置 REDEEM_CODE_KEY 或 REDEEM_CODE_KEY_FILE', 503)


def normalize_code(value):
    if not isinstance(value, str) or len(value) > 128:
        raise InventoryError('请输入 8–32 位字母或数字兑换码')
    code = re.sub(r'[\s-]', '', value)
    if not code.isascii():
        raise InventoryError('请输入 8–32 位字母或数字兑换码')
    code = code.upper()
    if not re.fullmatch(r'[A-Z0-9]{8,32}', code, flags=re.ASCII):
        raise InventoryError('请输入 8–32 位字母或数字兑换码')
    return code


def _request(value):
    if not isinstance(value, str) or not re.fullmatch(r'[A-Za-z0-9_-]{8,64}', value):
        raise InventoryError('缺少有效的操作编号')
    return value


def _date(value):
    if value is None or value == '':
        return None
    try:
        date = datetime.fromisoformat(value.replace('Z', '+00:00'))
        if date.tzinfo is None:
            raise ValueError()
        return date
    except (ValueError, TypeError, AttributeError):
        raise InventoryError('日期须包含时区')


def _reward_list(cursor, rewards):
    if not isinstance(rewards, list) or not 1 <= len(rewards) <= 20:
        raise InventoryError('请配置 1–20 种奖励')
    result, seen = [], set()
    for reward in rewards:
        if not isinstance(reward, dict):
            raise InventoryError('奖励格式无效')
        item = _definition(cursor, reward.get('item_id'))
        quantity = _int(reward.get('quantity'), '奖励数量', 1, 1000000)
        if item['is_default'] or not item['grant_enabled']:
            raise InventoryError('不能选择默认物品或停止发放的物品')
        if item['item_id'] in seen:
            raise InventoryError('奖励不可重复')
        if quantity > item['max_quantity'] or (item['category'] != 'item' and quantity != 1):
            raise InventoryError('奖励数量超过物品上限')
        seen.add(item['item_id'])
        result.append(dict(item_id=item['item_id'], quantity=quantity))
    return sorted(result, key=lambda r: r['item_id'])


def _campaign(cursor, cid, lock=False):
    cursor.execute('SELECT * FROM redeem_campaigns WHERE campaign_id=%s' + (' FOR UPDATE' if lock else ''),
                   (_int(cid, '批次 ID', 1, 9223372036854775807),))
    row = cursor.fetchone()
    if not row:
        raise InventoryError('兑换码批次不存在', 404)
    return row


def _public_campaign(cursor, row):
    data = {k: (v.isoformat() if isinstance(v, datetime) else v) for k, v in row.items()
            if k not in ('creation_key', 'creation_hash')}
    cursor.execute('''SELECT r.item_id,r.quantity,d.name FROM redeem_rewards r JOIN item_definitions d USING(item_id)
        WHERE r.campaign_id=%s ORDER BY r.item_id''', (row['campaign_id'],))
    data['rewards'] = [dict(r) for r in cursor.fetchall()]
    return data


def list_campaigns(db, page=1):
    page = _int(page, '页码', 1, 1000000)
    def read(cursor):
        cursor.execute('SELECT COUNT(*) AS count FROM redeem_campaigns')
        count = cursor.fetchone()['count']
        cursor.execute('SELECT * FROM redeem_campaigns ORDER BY campaign_id DESC LIMIT 20 OFFSET %s', ((page-1)*20,))
        rows = cursor.fetchall()
        return dict(items=[_public_campaign(cursor, r) for r in rows], total=count, page=page)
    return _read(db, read)


def save_campaign(db, actor, body, campaign_id=None):
    if not isinstance(body, dict):
        raise InventoryError('请求须为对象')
    key = _request(body.get('request_id'))
    name = _text(body.get('name'), '名称', 100)
    reason = _text(body.get('reason'), '操作原因', 500)
    kind = body.get('kind')
    if kind not in ('shared', 'unique'):
        raise InventoryError('兑换码类型无效')
    # Older clients created activity batches. New admin UI explicitly chooses a purpose.
    purpose = body.get('purpose', 'activity')
    if purpose not in ('ordinary', 'activity'):
        raise InventoryError('兑换码用途无效')
    if purpose == 'ordinary' and kind != 'unique':
        raise InventoryError('普通兑换码必须使用一次性码')
    count = 1 if kind == 'shared' else _int(body.get('code_count'), '生成数量', 1, 10000)
    code = normalize_code(body.get('code')) if kind == 'shared' and campaign_id is None else None
    starts, ends = _date(body.get('starts_at')), _date(body.get('ends_at'))
    if starts and ends and ends <= starts:
        raise InventoryError('结束时间须晚于开始时间')
    maximum = body.get('max_claims')
    if maximum is not None:
        maximum = _int(maximum, '总领取上限', 1, 10000000)
    digest = hashlib.sha256(json.dumps(dict(body, actor=actor), sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
            # Serialize create retries even when two administrators/instances race.
            cursor.execute('SELECT pg_advisory_xact_lock(hashtextextended(%s,0))', ('redeem-create:'+key,))
            if campaign_id is None:
                cursor.execute('SELECT * FROM redeem_campaigns WHERE creation_key=%s', (key,))
                existing = cursor.fetchone()
                if existing:
                    if existing['creation_hash'] != digest:
                        raise InventoryError('操作编号已用于其他请求', 409)
                    return _public_campaign(cursor, existing)
            rewards = _reward_list(cursor, body.get('rewards'))
            if campaign_id is None:
                cipher = _cipher()
                cursor.execute('''INSERT INTO redeem_campaigns(creation_key,creation_hash,name,kind,purpose,starts_at,ends_at,
                    max_claims,code_count,created_by) VALUES(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s) RETURNING campaign_id''',
                    (key,digest,name,kind,purpose,starts,ends,maximum,count,actor))
                campaign_id = cursor.fetchone()['campaign_id']
                generated = set()
                while len(generated) < count:
                    generated.add(code if kind == 'shared' else ''.join(secrets.choice('ABCDEFGHJKLMNPQRSTUVWXYZ23456789') for _ in range(16)))
                execute_values(cursor,'''INSERT INTO redeem_codes(campaign_id,code_hash,code_cipher,code_hint) VALUES %s''',
                    [(campaign_id,hashlib.sha256(value.encode()).hexdigest(),cipher.encrypt(value.encode()).decode(),
                      value[:3]+'…'+value[-4:]) for value in sorted(generated)],page_size=1000)
            else:
                old = _campaign(cursor, campaign_id, True)
                if old['status'] != 'draft':
                    raise InventoryError('启用后不能修改奖励或领取规则', 409)
                if old['kind'] != kind or old['code_count'] != count:
                    raise InventoryError('已生成的兑换码类型和数量不能修改', 409)
                cursor.execute('''UPDATE redeem_campaigns SET name=%s,purpose=%s,starts_at=%s,ends_at=%s,max_claims=%s,
                    updated_at=NOW() WHERE campaign_id=%s''',(name,purpose,starts,ends,maximum,campaign_id))
                cursor.execute('DELETE FROM redeem_rewards WHERE campaign_id=%s',(campaign_id,))
            for reward in rewards:
                cursor.execute('INSERT INTO redeem_rewards(campaign_id,item_id,quantity) VALUES(%s,%s,%s)',
                               (campaign_id,reward['item_id'],reward['quantity']))
            _audit(cursor,actor,'redeem.save',campaign_id,dict(name=name,kind=kind,purpose=purpose,code_count=count,rewards=rewards),reason)
            result = _public_campaign(cursor, _campaign(cursor, campaign_id))
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.rollback()
        db._put_connection(conn)


def _audit(cursor, actor, action, cid, payload, reason):
    cursor.execute('''INSERT INTO admin_audit_log(admin_user_id,action,target_type,target_id,payload,reason)
        VALUES(%s,%s,'redeem_campaign',%s,%s,%s)''',(actor,action,str(cid),Json(payload),reason))


def set_campaign_status(db, actor, cid, status, reason):
    reason = _text(reason,'操作原因',500)
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
            row = _campaign(cursor,cid,True)
            if status != row['status']:
                allowed = {'draft':('active','ended'), 'active':('paused','ended'), 'paused':('active','ended'), 'ended':()}
                if status not in allowed[row['status']]:
                    raise InventoryError('不允许此状态变更',409)
                if status == 'active':
                    cursor.execute('SELECT item_id,quantity FROM redeem_rewards WHERE campaign_id=%s',(cid,))
                    _reward_list(cursor,[dict(r) for r in cursor.fetchall()])
                cursor.execute('UPDATE redeem_campaigns SET status=%s,updated_at=NOW() WHERE campaign_id=%s',(status,cid))
                _audit(cursor,actor,'redeem.status',cid,dict(before=row['status'],after=status),reason)
            result = _public_campaign(cursor,_campaign(cursor,cid))
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def export_codes(db, actor, cid):
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            _campaign(cursor,cid)
            cursor.execute('SELECT code_id,code_cipher,claimed_count FROM redeem_codes WHERE campaign_id=%s ORDER BY code_id',(cid,))
            cipher = _cipher()
            try:
                rows = [dict(code_id=r['code_id'],code=cipher.decrypt(r['code_cipher'].encode()).decode(),claimed_count=r['claimed_count']) for r in cursor.fetchall()]
            except InvalidToken:
                raise InventoryError('兑换码密钥与保存时不一致，无法导出',503)
            _audit(cursor,actor,'redeem.export',cid,dict(count=len(rows)),'导出兑换码')
        conn.commit()
        return rows
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def list_redemptions(db, cid, page=1):
    page = _int(page,'页码',1,1000000)
    def read(cursor):
        _campaign(cursor,cid)
        cursor.execute('SELECT COUNT(*) AS count FROM redeem_redemptions WHERE campaign_id=%s',(cid,))
        total = cursor.fetchone()['count']
        cursor.execute('''SELECT r.user_id,r.operation_id,r.created_at,c.code_hint,o.result FROM redeem_redemptions r
            JOIN redeem_codes c USING(code_id) JOIN inventory_operations o USING(operation_id)
            WHERE r.campaign_id=%s ORDER BY r.redemption_id DESC LIMIT 100 OFFSET %s''',(cid,(page-1)*100))
        return dict(total=total,page=page,items=[dict(r,operation_id=str(r['operation_id']),created_at=r['created_at'].isoformat()) for r in cursor.fetchall()])
    return _read(db,read)


def check_redemption_rate(db, user_id, ip):
    """Database-backed limit is shared by Web, WebSocket and multiple workers."""
    conn = db._get_connection()
    limited = False
    try:
        with conn.cursor() as cursor:
            buckets = [(f'user:{user_id}',10)]
            if ip:
                buckets.append(('ip:'+hashlib.sha256(ip.encode()).hexdigest(),50))
            for bucket,limit in sorted(buckets):
                cursor.execute('''INSERT INTO redeem_rate_limits(bucket,window_start,attempts) VALUES(%s,NOW(),1)
                    ON CONFLICT(bucket) DO UPDATE SET
                    attempts=CASE WHEN redeem_rate_limits.window_start < NOW()-INTERVAL '1 minute' THEN 1 ELSE redeem_rate_limits.attempts+1 END,
                    window_start=CASE WHEN redeem_rate_limits.window_start < NOW()-INTERVAL '1 minute' THEN NOW() ELSE redeem_rate_limits.window_start END
                    RETURNING attempts''',(bucket,))
                limited |= cursor.fetchone()[0] > limit
            cursor.execute("DELETE FROM redeem_rate_limits WHERE window_start < NOW()-INTERVAL '1 day'")
        conn.commit()
    finally:
        conn.rollback()
        db._put_connection(conn)
    if limited:
        raise InventoryError('操作过于频繁，请一分钟后重试',429)


def redeem_code(db, user_id, body):
    if not isinstance(body,dict):
        raise InventoryError('请求须为对象')
    key = _request(body.get('request_id'))
    code = normalize_code(body.get('code'))
    digest = hashlib.sha256(code.encode()).hexdigest()
    request_hash = hashlib.sha256(('redeem:'+digest).encode()).hexdigest()
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)',(CATALOG_LOCK,))
            cursor.execute('SELECT user_id,is_tourist FROM users WHERE user_id=%s FOR UPDATE',
                           (_int(user_id,'用户 ID',11,9223372036854775807),))
            user = cursor.fetchone()
            if not user or user['is_tourist']:
                raise InventoryError('请使用注册账号兑换物品',403)
            cursor.execute('SELECT request_hash,result FROM inventory_operations WHERE user_id=%s AND request_id=%s',(user_id,key))
            previous = cursor.fetchone()
            if previous:
                if previous['request_hash'] != request_hash:
                    raise InventoryError('操作编号已用于其他请求',409)
                result = previous['result']
            else:
                cursor.execute('SELECT campaign_id FROM redeem_codes WHERE code_hash=%s',(digest,))
                found = cursor.fetchone()
                if not found:
                    raise InventoryError('兑换码无效',404)
                campaign = _campaign(cursor,found['campaign_id'],True)
                cid = campaign['campaign_id']
                cursor.execute('SELECT * FROM redeem_codes WHERE code_hash=%s FOR UPDATE',(digest,))
                entry = cursor.fetchone()
                if campaign['purpose'] == 'activity':
                    cursor.execute('SELECT 1 FROM redeem_redemptions WHERE campaign_id=%s AND user_id=%s',(cid,user_id))
                    if cursor.fetchone():
                        raise InventoryError('你已领取过此活动的奖励',409)
                cursor.execute('SELECT NOW() AS now')
                now = cursor.fetchone()['now']
                if campaign['status'] != 'active':
                    raise InventoryError('此兑换码暂未开放或已结束',409)
                if campaign['starts_at'] and now < campaign['starts_at']:
                    raise InventoryError('此兑换码尚未开始',409)
                if campaign['ends_at'] and now >= campaign['ends_at']:
                    raise InventoryError('兑换码已过期',409)
                if campaign['max_claims'] and campaign['claimed_count'] >= campaign['max_claims']:
                    raise InventoryError('此批次的领取名额已用完',409)
                if campaign['kind'] == 'unique' and entry['claimed_count']:
                    raise InventoryError('兑换码已被使用',409)
                cursor.execute('SELECT item_id,quantity FROM redeem_rewards WHERE campaign_id=%s ORDER BY item_id',(cid,))
                rewards = _reward_list(cursor,[dict(r) for r in cursor.fetchall()])
                op = str(uuid.uuid4())
                cursor.execute('''INSERT INTO inventory_operations(operation_id,user_id,request_id,request_hash,action,reason)
                    VALUES(%s,%s,%s,%s,'redeem',%s)''',(op,user_id,key,request_hash,f'兑换码批次 {cid}'))
                changes = [_change(cursor,op,user_id,_definition(cursor,r['item_id']),r['quantity'],True) for r in rewards]
                if not any(c['delta'] > 0 for c in changes):
                    raise InventoryError('你已拥有全部奖励，未消耗兑换码',409)
                receipt = make_reward_receipt(cursor,op,user_id,'redeem',cid,changes)
                result = dict(operation_id=op,message='兑换成功，物品已放入仓库',reward_receipt=receipt)
                cursor.execute('UPDATE inventory_operations SET result=%s WHERE operation_id=%s',(Json(result),op))
                cursor.execute('INSERT INTO redeem_redemptions(campaign_id,code_id,user_id,operation_id) VALUES(%s,%s,%s,%s)',(cid,entry['code_id'],user_id,op))
                cursor.execute('UPDATE redeem_campaigns SET claimed_count=claimed_count+1 WHERE campaign_id=%s',(cid,))
                cursor.execute('UPDATE redeem_codes SET claimed_count=claimed_count+1 WHERE code_id=%s',(entry['code_id'],))
            result = dict(result,inventory_state=_state(cursor,user_id))
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
