"""Shop administration shares the purchase catalog lock and audit transaction."""
import re
from datetime import datetime

from psycopg2.extras import Json, RealDictCursor

from .inventory import CATALOG_LOCK, InventoryError, _definition, _int, _public, _read, _text

_OFFER_ID = re.compile(r'[A-Za-z0-9_-]{1,64}\Z')
_EDITABLE = ('item_id', 'unit_price_minor', 'max_purchase_quantity',
             'prevent_duplicate_purchase', 'enabled', 'sort_order')


def _offer(row):
    return {**{key: row[key] for key in ('offer_id', 'price_version', *_EDITABLE)},
            'item': _public(row['item']), 'updated_at': row['updated_at'].isoformat()}


def _select_offer(cursor, offer_id):
    cursor.execute('''SELECT s.*,to_jsonb(d) AS item FROM shop_offers s
        JOIN item_definitions d USING(item_id) WHERE s.offer_id=%s''', (offer_id,))
    return cursor.fetchone()


def get_admin_shop(db):
    def read(cursor):
        cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
        cursor.execute('''SELECT s.*,to_jsonb(d) AS item FROM shop_offers s
            JOIN item_definitions d USING(item_id) ORDER BY s.sort_order,s.offer_id''')
        offers = [_offer(row) for row in cursor.fetchall()]
        cursor.execute('SELECT * FROM item_definitions WHERE NOT is_default ORDER BY sort_order,item_id')
        return dict(offers=offers, items=[_public(row) for row in cursor.fetchall()])
    return _read(db, read)


def save_offer(db, actor_id, offer_id, body):
    _int(actor_id, '管理员 ID')
    creating = offer_id is None
    offer_id = body.get('offer_id') if creating else offer_id
    if not isinstance(offer_id, str) or not _OFFER_ID.fullmatch(offer_id):
        raise InventoryError('商品编号须为 1–64 位字母、数字、下划线或短横线')
    values = dict(item_id=_int(body.get('item_id'), '物品 ID'),
                  unit_price_minor=_int(body.get('unit_price_minor'), '萨拉币价格', 1, 100000000),
                  max_purchase_quantity=_int(body.get('max_purchase_quantity', 1), '单次购买上限', 1, 100),
                  sort_order=_int(body.get('sort_order', 0), '排序', -1000000, 1000000),
                  prevent_duplicate_purchase=body.get('prevent_duplicate_purchase', False),
                  enabled=body.get('enabled', True))
    if any(type(values[key]) is not bool for key in ('enabled', 'prevent_duplicate_purchase')):
        raise InventoryError('上架与禁止重复购买须为布尔值')
    reason = _text(body.get('reason'), '变更原因', 500)
    expected = None if creating else _int(body.get('expected_version'), '商品版本')
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            # Same first lock as purchases; catalog changes cannot race a debit.
            cursor.execute('SELECT pg_advisory_xact_lock(%s)', (CATALOG_LOCK,))
            before = _select_offer(cursor, offer_id)
            if creating and before:
                raise InventoryError('商品编号已存在', 409)
            if not creating and not before:
                raise InventoryError('商品不存在', 404)
            if before and before['price_version'] != expected:
                raise InventoryError('商品已被其他管理员修改，请重新载入后编辑', 409)
            if before and before['item_id'] != values['item_id']:
                raise InventoryError('商品创建后不能替换关联物品；请下架后新增商品')
            item = _definition(cursor, values['item_id'])
            if item['is_default']:
                raise InventoryError('默认物品不能上架出售')
            if values['enabled'] and not (item['grant_enabled'] and item['use_enabled']):
                raise InventoryError('物品已停用或停止发放，请先在物品管理中启用')
            if values['prevent_duplicate_purchase'] or item['category'] != 'item':
                values['max_purchase_quantity'] = 1
            if before and all(before[key] == values[key] for key in _EDITABLE):
                conn.commit()
                return _offer(before)
            if creating:
                cursor.execute('''INSERT INTO shop_offers
                    (offer_id,item_id,unit_price_minor,max_purchase_quantity,prevent_duplicate_purchase,enabled,sort_order)
                    VALUES(%s,%s,%s,%s,%s,%s,%s)''',
                    (offer_id, *(values[key] for key in _EDITABLE)))
            else:
                cursor.execute('''UPDATE shop_offers SET item_id=%s,unit_price_minor=%s,max_purchase_quantity=%s,
                    prevent_duplicate_purchase=%s,enabled=%s,sort_order=%s,
                    price_version=price_version+1,updated_at=CURRENT_TIMESTAMP WHERE offer_id=%s''',
                    (*(values[key] for key in _EDITABLE), offer_id))
            after = _offer(_select_offer(cursor, offer_id))
            cursor.execute('''INSERT INTO admin_audit_log(admin_user_id,action,target_type,target_id,payload,reason)
                VALUES(%s,%s,'shop_offer',%s,%s,%s)''',
                (actor_id, 'shop.offer_create' if creating else 'shop.offer_update', offer_id,
                 Json(dict(before=_offer(before) if before else None, after=after)), reason))
        conn.commit()
        return after
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def _time(value, label):
    if value in (None, ''):
        return None
    if not isinstance(value, str) or len(value) > 40:
        raise InventoryError(label + '无效')
    try:
        parsed = datetime.fromisoformat(value.replace('Z', '+00:00'))
        if parsed.tzinfo is None:
            raise ValueError()
        return parsed
    except ValueError:
        raise InventoryError(label + '须为含时区的 ISO 时间') from None


def get_shop_orders(db, *, page=1, limit=30, user_id=None, offer_id=None, order_id=None,
                    from_time=None, to_time=None):
    page = _int(page, '页码', 1, 1000000)
    limit = _int(limit, '每页条数', 1, 100)
    conditions, params = [], []
    if user_id is not None:
        conditions.append('o.user_id=%s')
        params.append(_int(user_id, '玩家 UID', 1, 9223372036854775807))
    if offer_id:
        if not isinstance(offer_id, str) or not _OFFER_ID.fullmatch(offer_id):
            raise InventoryError('商品编号无效')
        conditions.append('o.offer_id=%s')
        params.append(offer_id)
    if order_id:
        import uuid
        try:
            order_id = str(uuid.UUID(order_id))
        except (ValueError, TypeError, AttributeError):
            raise InventoryError('订单编号无效') from None
        conditions.append('o.order_id=%s')
        params.append(order_id)
    start, end = _time(from_time, '开始时间'), _time(to_time, '结束时间')
    if start and end and start >= end:
        raise InventoryError('结束时间须晚于开始时间')
    for value, operator in ((start, '>='), (end, '<')):
        if value:
            conditions.append('o.created_at' + operator + '%s')
            params.append(value)
    where = ' WHERE ' + ' AND '.join(conditions) if conditions else ''

    def read(cursor):
        cursor.execute('SELECT COUNT(*) AS total FROM shop_orders o' + where, params)
        total = cursor.fetchone()['total']
        cursor.execute('''SELECT o.order_id,o.request_id,o.user_id,u.username,o.offer_id,o.item_id,
            COALESCE(o.item_snapshot->>'name',d.name) AS item_name,o.item_snapshot,
            o.quantity,o.unit_price_minor,o.total_minor,o.price_version,o.created_at,l.balance_after
            FROM shop_orders o JOIN users u USING(user_id) JOIN item_definitions d USING(item_id)
            LEFT JOIN wallet_ledger l ON l.user_id=o.user_id AND l.source='purchase' AND l.source_id=o.request_id'''
            + where + ' ORDER BY o.created_at DESC,o.order_id DESC LIMIT %s OFFSET %s',
            (*params, limit, (page - 1) * limit))
        return dict(items=[dict(row, order_id=str(row['order_id']), created_at=row['created_at'].isoformat())
                           for row in cursor.fetchall()], total=total, page=page, limit=limit)
    return _read(db, read)
