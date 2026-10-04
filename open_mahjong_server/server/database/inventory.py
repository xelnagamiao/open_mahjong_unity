"""Authoritative inventory mutations shared by Web administration and Unity.

Lock order: catalog advisory lock, user row, inventory/equipment rows.
Every accepted mutation and its ledger/audit commit together. Retried request IDs
must describe exactly the same operation and never grant or consume twice.
"""
import hashlib
import json
from pathlib import Path
import re
import uuid

from psycopg2.extras import Json, RealDictCursor
from .inventory_assets import ASSETS, SEEDS, DEFAULT_AVATAR_FRAME_ID, LEGACY_FRAME_DESCRIPTIONS, LEGACY_FRAME_NAMES

CATALOG_LOCK = 220922041


class InventoryError(ValueError):
    def __init__(self, message, status=400):
        super().__init__(message)
        self.status = status


def _int(value, label, minimum=1, maximum=2147483647):
    if type(value) is not int or not minimum <= value <= maximum:
        raise InventoryError(f'{label}无效')
    return value


def _text(value, label, maximum, required=True):
    if not isinstance(value, str):
        raise InventoryError(f'{label}必须为文本')
    value = value.strip()
    if (required and not value) or len(value) > maximum or re.search(r'[<>\x00-\x1f\x7f]', value):
        raise InventoryError(f'{label}须为 {1 if required else 0}–{maximum} 字，不能包含标签或控制字符')
    return value


def ensure_inventory_tables(cursor):
    cursor.execute('SELECT pg_advisory_xact_lock(%s)', (CATALOG_LOCK,))
    cursor.execute(Path(__file__).with_name('inventory_schema.sql').read_text(encoding='utf-8'))
    cursor.execute('''CREATE TABLE IF NOT EXISTS admin_audit_log (
        id BIGSERIAL PRIMARY KEY, admin_user_id BIGINT NOT NULL, action VARCHAR(64) NOT NULL,
        target_type VARCHAR(32), target_id VARCHAR(64), payload JSONB, reason TEXT,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP)''')
    # Complete the historical one-time cleanup before adding current built-ins.
    cursor.execute(Path(__file__).with_name('store_catalog_migration.sql').read_text(encoding='utf-8'))
    for iid, code, name, description, key, default, config in SEEDS:
        asset = ASSETS[key]
        cursor.execute('''INSERT INTO item_definitions
            (item_id,code,name,description,category,asset_key,slot,max_quantity,is_default,config)
            VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s) ON CONFLICT (item_id) DO NOTHING''',
            (iid, code, name, description, asset['category'], key, asset['slot'],
             1000000 if asset['category'] == 'item' else 1, default, Json(config)))
        if key in LEGACY_FRAME_DESCRIPTIONS:
            # Refresh only our previous built-in copy, preserving administrator edits.
            cursor.execute('''UPDATE item_definitions SET description=%s
                WHERE item_id=%s AND code=%s AND asset_key=%s AND description=ANY(%s)''',
                (description, iid, code, key, list(LEGACY_FRAME_DESCRIPTIONS[key])))
            cursor.execute('''UPDATE item_definitions SET name=%s
                WHERE item_id=%s AND code=%s AND asset_key=%s AND name=%s''',
                (name, iid, code, key, LEGACY_FRAME_NAMES[key]))


def _public(row):
    value = {key: row[key] for key in ('item_id','code','name','description','category','asset_key','slot',
                                      'max_quantity','is_default','grant_enabled','use_enabled','sort_order','config')}
    value.update({key: val for key, val in ASSETS[value['asset_key']].items() if key not in ('label','category','slot')})
    return value


def _catalog(cursor):
    cursor.execute('SELECT * FROM item_definitions ORDER BY sort_order,item_id')
    return [_public(row) for row in cursor.fetchall()]


def _state(cursor, user_id):
    cursor.execute('SELECT user_id,rename_count,is_tourist FROM users WHERE user_id=%s', (user_id,))
    user = cursor.fetchone()
    if not user:
        raise InventoryError('用户不存在', 404)
    catalog = _catalog(cursor)
    cursor.execute('SELECT item_id,quantity FROM user_inventory WHERE user_id=%s AND quantity>0 ORDER BY item_id', (user_id,))
    owned = [dict(row) for row in cursor.fetchall()]
    owned_ids = {row['item_id'] for row in owned}
    for item in catalog:
        if item['is_default'] and item['item_id'] not in owned_ids:
            owned.append({'item_id': item['item_id'], 'quantity': 1})
    cursor.execute('''SELECT e.slot,e.item_id FROM user_equipment e JOIN item_definitions d USING(item_id)
        LEFT JOIN user_inventory i ON i.user_id=e.user_id AND i.item_id=e.item_id
        WHERE e.user_id=%s AND d.use_enabled AND (d.is_default OR i.quantity>0)''', (user_id,))
    equipment = [dict(row) for row in cursor.fetchall()]
    if not any(item['slot'] == 'avatar_frame' for item in equipment) and any(
            item['item_id'] == DEFAULT_AVATAR_FRAME_ID and item['is_default'] and item['use_enabled'] for item in catalog):
        equipment.append({'slot': 'avatar_frame', 'item_id': DEFAULT_AVATAR_FRAME_ID})
    cursor.execute('SELECT character_id,voice_id,profile_image_id FROM user_settings WHERE user_id=%s',(user_id,))
    settings = cursor.fetchone() or {'character_id':1,'voice_id':1,'profile_image_id':1}
    appearance = dict(settings,user_id=user_id,avatar_frame_id=next((e['item_id'] for e in equipment if e['slot']=='avatar_frame'),0))
    return {'user_id':user_id, 'catalog':catalog, 'owned':owned, 'equipment':equipment,
            'appearance':appearance,'rename_count':user['rename_count'], 'is_tourist':bool(user['is_tourist'])}


def _read(db, work):
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            return work(cursor)
    finally:
        conn.rollback()
        db._put_connection(conn)


def get_inventory(db, user_id):
    return _read(db, lambda cursor: _state(cursor, user_id))


def get_catalog(db):
    return _read(db, _catalog)


def get_admin_catalog(db):
    return {'items': get_catalog(db), 'assets':[dict(asset_key=key, **asset) for key,asset in ASSETS.items()]}


def _definition(cursor, item_id):
    cursor.execute('SELECT * FROM item_definitions WHERE item_id=%s', (_int(item_id, '物品 ID'),))
    item = cursor.fetchone()
    if not item:
        raise InventoryError('物品不存在', 404)
    return item


def _quantity(cursor, user_id, item_id):
    cursor.execute('SELECT quantity FROM user_inventory WHERE user_id=%s AND item_id=%s', (user_id,item_id))
    row = cursor.fetchone()
    return row['quantity'] if row else 0


def _change(cursor, operation_id, user_id, item, delta, clamp_permanent=False):
    before = _quantity(cursor, user_id, item['item_id'])
    after = before + delta
    if clamp_permanent and item['category'] != 'item':
        after = min(1, after)
    if not 0 <= after <= item['max_quantity']:
        raise InventoryError(f'“{item["name"]}”数量不足或超过持有上限', 409)
    cursor.execute('''INSERT INTO user_inventory (user_id,item_id,quantity) VALUES (%s,%s,%s)
        ON CONFLICT (user_id,item_id) DO UPDATE SET quantity=EXCLUDED.quantity,updated_at=CURRENT_TIMESTAMP''',
        (user_id,item['item_id'],after))
    cursor.execute('''INSERT INTO inventory_ledger (operation_id,user_id,item_id,delta,quantity_after)
        VALUES (%s,%s,%s,%s,%s)''', (operation_id,user_id,item['item_id'],after-before,after))
    return {'item_id':item['item_id'],'delta':after-before,'quantity_after':after}


def _write_settings(cursor, user_id):
    """Only project slots this system owns. Legacy avatar remains until explicitly replaced."""
    cursor.execute('INSERT INTO user_settings (user_id) VALUES (%s) ON CONFLICT DO NOTHING', (user_id,))
    cursor.execute('''SELECT e.slot,d.* FROM user_equipment e JOIN item_definitions d USING(item_id)
        WHERE e.user_id=%s''', (user_id,))
    equipment = {row['slot']:row for row in cursor.fetchall()}
    character = equipment.get('character')
    asset = ASSETS[character['asset_key']] if character else {'character_id':1,'voice_id':1}
    cursor.execute('UPDATE user_settings SET character_id=%s,voice_id=%s,updated_at=CURRENT_TIMESTAMP WHERE user_id=%s',
                   (asset['character_id'],asset['voice_id'],user_id))
    if 'avatar' in equipment:
        cursor.execute('UPDATE user_settings SET profile_image_id=%s WHERE user_id=%s',
                       (equipment['avatar']['item_id'],user_id))


def _remove_equipment(cursor, user_id, item_id):
    cursor.execute('DELETE FROM user_equipment WHERE user_id=%s AND item_id=%s RETURNING slot', (user_id,item_id))
    slots = [row['slot'] for row in cursor.fetchall()]
    if 'avatar' in slots:
        cursor.execute('UPDATE user_settings SET profile_image_id=1,updated_at=CURRENT_TIMESTAMP WHERE user_id=%s', (user_id,))
    if slots:
        _write_settings(cursor,user_id)


def mutate_inventory(db, user_id, action, body, actor_id=None):
    _int(user_id,'用户 ID',11,9223372036854775807)
    if action not in ('equip','use','grant','revoke'):
        raise InventoryError('不支持的物品操作')
    if action in ('grant','revoke') and actor_id is None:
        raise InventoryError('需要管理员权限',403)
    request_id = body.get('request_id')
    if not isinstance(request_id,str) or not re.fullmatch(r'[A-Za-z0-9_-]{8,64}',request_id):
        raise InventoryError('缺少有效的操作编号')
    reason = _text(body.get('reason',''), '操作原因',500,actor_id is not None)
    normalized = {'action':action,'actor':actor_id,'body':body}
    digest = hashlib.sha256(json.dumps(normalized,sort_keys=True,ensure_ascii=False).encode()).hexdigest()
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
            cursor.execute('SELECT user_id,is_tourist FROM users WHERE user_id=%s FOR UPDATE',(user_id,))
            user = cursor.fetchone()
            if not user: raise InventoryError('用户不存在',404)
            cursor.execute('SELECT request_hash,result FROM inventory_operations WHERE user_id=%s AND request_id=%s',(user_id,request_id))
            existing = cursor.fetchone()
            if existing:
                if existing['request_hash'] != digest: raise InventoryError('操作编号已用于其他请求',409)
                result = existing['result']
            else:
                op = str(uuid.uuid4())
                cursor.execute('''INSERT INTO inventory_operations (operation_id,user_id,request_id,request_hash,action,actor_id,reason)
                    VALUES (%s,%s,%s,%s,%s,%s,%s)''',(op,user_id,request_id,digest,action,actor_id,reason))
                changes = []
                if action == 'equip':
                    slot = body.get('slot')
                    if slot not in ('character','avatar','avatar_frame'): raise InventoryError('装备槽无效')
                    iid = _int(body.get('item_id'),'物品 ID',0)
                    if iid:
                        item = _definition(cursor,iid)
                        if item['slot'] != slot or not item['use_enabled']: raise InventoryError('物品不适用此装备槽或已停用',409)
                        if not item['is_default'] and not _quantity(cursor,user_id,iid): raise InventoryError('尚未拥有此物品',403)
                        cursor.execute('''INSERT INTO user_equipment (user_id,slot,item_id) VALUES (%s,%s,%s)
                            ON CONFLICT (user_id,slot) DO UPDATE SET item_id=EXCLUDED.item_id,updated_at=CURRENT_TIMESTAMP''',(user_id,slot,iid))
                    else:
                        cursor.execute('DELETE FROM user_equipment WHERE user_id=%s AND slot=%s',(user_id,slot))
                        if slot == 'avatar': cursor.execute('UPDATE user_settings SET profile_image_id=1 WHERE user_id=%s',(user_id,))
                    _write_settings(cursor,user_id)
                    message = '装备已保存'
                else:
                    item = _definition(cursor,body.get('item_id'))
                    quantity = _int(body.get('quantity',1),'数量',1,1000000 if actor_id else 100)
                    if item['is_default']: raise InventoryError('默认装扮始终可用，无需发放或回收')
                    if action == 'grant':
                        if not item['grant_enabled']: raise InventoryError('此物品已停止发放',409)
                        if item['category'] != 'item' and quantity != 1: raise InventoryError('永久物品每次只能发放一份')
                        changes.append(_change(cursor,op,user_id,item,quantity,True))
                        message = '物品已发放' if changes[-1]['delta'] else '用户已拥有此永久物品'
                    elif action == 'revoke':
                        changes.append(_change(cursor,op,user_id,item,-quantity))
                        if not changes[-1]['quantity_after']: _remove_equipment(cursor,user_id,item['item_id'])
                        message = '物品已回收'
                    else:
                        if item['category'] != 'item' or not item['use_enabled']: raise InventoryError('此物品不能使用',409)
                        effect = ASSETS[item['asset_key']]['effect']
                        if effect == 'rename_credit' and user['is_tourist']: raise InventoryError('游客账号不能兑换改名次数')
                        changes.append(_change(cursor,op,user_id,item,-quantity))
                        if effect == 'rename_credit':
                            cursor.execute('''UPDATE users SET rename_count=rename_count+%s
                                WHERE user_id=%s AND rename_count<=2147483647-%s RETURNING rename_count''',(quantity,user_id,quantity))
                            if not cursor.fetchone(): raise InventoryError('改名次数已达上限',409)
                            message = f'已增加 {quantity} 次改名机会，可在网页账号中心使用'
                        else:
                            for reward in item['config'].get('rewards',[]):
                                target = _definition(cursor,reward['item_id'])
                                if not target['grant_enabled']: raise InventoryError('礼包奖励暂不可发放，请稍后重试',409)
                                changes.append(_change(cursor,op,user_id,target,reward['quantity']*quantity,True))
                            message = f'已打开 {quantity} 份礼包，奖励已放入背包；重复装扮不累计'
                result = {'operation_id':op,'message':message,'changes':changes}
                cursor.execute('UPDATE inventory_operations SET result=%s WHERE operation_id=%s',(Json(result),op))
                if actor_id is not None:
                    cursor.execute('''INSERT INTO admin_audit_log (admin_user_id,action,target_type,target_id,payload,reason)
                        VALUES (%s,%s,'user',%s,%s,%s)''',(actor_id,'inventory.'+action,str(user_id),Json(dict(body,operation_id=op)),reason))
            state = _state(cursor,user_id)
        conn.commit()
        return dict(result,inventory_state=state)
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def save_definition(db, actor_id, item_id, body):
    name = _text(body.get('name'),'名称',32)
    description = _text(body.get('description',''),'说明',300,False)
    code = body.get('code')
    if not isinstance(code,str) or not re.fullmatch('[a-z][a-z0-9_]{2,63}',code): raise InventoryError('物品编码须为 3–64 位小写字母、数字和下划线')
    key = body.get('asset_key')
    if key not in ASSETS: raise InventoryError('请选择受支持的资源或道具效果')
    asset = ASSETS[key]
    reason = _text(body.get('reason'),'变更原因',500)
    grant, usable = body.get('grant_enabled',True), body.get('use_enabled',True)
    if type(grant) is not bool or type(usable) is not bool: raise InventoryError('状态必须为布尔值')
    order = _int(body.get('sort_order',0),'排序',-1000000,1000000)
    config = {}
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock(%s)',(CATALOG_LOCK,))
            before = _definition(cursor,item_id) if item_id is not None else None
            if before and (before['code'] != code or before['asset_key'] != key): raise InventoryError('创建后编码和资源类型不能修改')
            if before and before['is_default'] and not usable: raise InventoryError('默认装扮不能停用')
            if asset.get('effect') == 'fixed_bundle':
                rewards = body.get('config',{}).get('rewards') if isinstance(body.get('config',{}),dict) else None
                if not isinstance(rewards,list) or not 1 <= len(rewards) <= 20: raise InventoryError('礼包须配置 1–20 种固定奖励')
                seen = set()
                for reward in rewards:
                    if not isinstance(reward,dict): raise InventoryError('奖励格式无效')
                    target = _definition(cursor,reward.get('item_id'))
                    count = _int(reward.get('quantity'),'奖励数量',1,1000)
                    if target['item_id'] in seen or target['item_id'] == item_id or target['is_default'] or target['asset_key'] == 'item.fixed_bundle':
                        raise InventoryError('礼包奖励不能重复、包含默认角色或其他礼包')
                    if target['category'] != 'item' and count != 1: raise InventoryError('永久奖励数量只能为 1')
                    seen.add(target['item_id'])
                config = {'rewards':[{'item_id':r['item_id'],'quantity':r['quantity']} for r in rewards]}
            values = (code,name,description,asset['category'],key,asset['slot'],1000000 if asset['category']=='item' else 1,
                      grant,usable,order,Json(config))
            if before:
                cursor.execute('''UPDATE item_definitions SET code=%s,name=%s,description=%s,category=%s,asset_key=%s,
                    slot=%s,max_quantity=%s,grant_enabled=%s,use_enabled=%s,sort_order=%s,config=%s,updated_at=CURRENT_TIMESTAMP
                    WHERE item_id=%s RETURNING *''',values+(item_id,))
            else:
                cursor.execute('''INSERT INTO item_definitions
                    (code,name,description,category,asset_key,slot,max_quantity,grant_enabled,use_enabled,sort_order,config)
                    VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s) RETURNING *''',values)
            after = cursor.fetchone()
            cursor.execute('SELECT user_id FROM user_inventory WHERE item_id=%s AND quantity>0',(after['item_id'],))
            users = [row['user_id'] for row in cursor.fetchall()]
            if not usable:
                for uid in sorted(users):
                    cursor.execute('SELECT user_id FROM users WHERE user_id=%s FOR UPDATE',(uid,))
                    _remove_equipment(cursor,uid,after['item_id'])
            cursor.execute('''INSERT INTO admin_audit_log (admin_user_id,action,target_type,target_id,payload,reason)
                VALUES (%s,%s,'item',%s,%s,%s)''',(actor_id,'item.update' if before else 'item.create',str(after['item_id']),
                    Json({'before':_public(before) if before else None,'after':_public(after)}),reason))
        conn.commit()
        return {'item':_public(after),'user_ids':users}
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def get_ledger(db,user_id,limit=100):
    def query(cursor):
        cursor.execute('''SELECT l.entry_id,l.item_id,d.name,l.delta,l.quantity_after,l.created_at,
            o.action,o.actor_id,o.reason,o.operation_id FROM inventory_ledger l
            JOIN inventory_operations o USING(operation_id) JOIN item_definitions d ON d.item_id=l.item_id
            WHERE l.user_id=%s ORDER BY l.entry_id DESC LIMIT %s''',(user_id,min(limit,200)))
        return [dict(row,created_at=row['created_at'].isoformat(),operation_id=str(row['operation_id'])) for row in cursor.fetchall()]
    return _read(db,query)
