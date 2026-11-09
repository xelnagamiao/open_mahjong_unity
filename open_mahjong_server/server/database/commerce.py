"""Server-authoritative game currency and atomic purchases in the inventory DB.

Money is integer hundredths throughout. Lock order matches inventory: catalog,
users in ascending ID order, then wallets and inventory. History and live games
share the same game_reward ledger key, so retries cannot pay twice.
"""
import hashlib
import json
from pathlib import Path
import re
import uuid

from psycopg2.extras import Json, RealDictCursor

from .inventory import (CATALOG_LOCK, InventoryError, _change, _definition,
                        _int, _public, _quantity, _read, _state)
from .game_rewards import (REWARD_MINOR_PER_HAND, REWARD_VERSION, build_currency_reward,
                           eligible_reward_ids)
from .round_progress import completed_hand_count, participant_ids

CALCULATION_VERSION = REWARD_VERSION
_REQUEST_ID = re.compile(r'[A-Za-z0-9_-]{8,64}\Z')
_OFFER_SEEDS = (('rename_card', 3001, 1000, 10),
                ('grass_tile', 3011, 10, 20),
                ('good_tile', 3012, 10, 30))


def ensure_commerce_tables(cursor):
    cursor.execute(Path(__file__).with_name('commerce_schema.sql').read_text(encoding='utf-8'))
    for offer, item, price, order in _OFFER_SEEDS:
        cursor.execute('''INSERT INTO shop_offers(offer_id,item_id,unit_price_minor,sort_order)
            SELECT %s,item_id,%s,%s FROM item_definitions WHERE item_id=%s AND NOT is_default
            ON CONFLICT(offer_id) DO NOTHING''', (offer, price, order, item))


def _dict_rows(cursor):
    names = [column[0] for column in cursor.description]
    return [dict(row) if isinstance(row, dict) else dict(zip(names, row)) for row in cursor.fetchall()]


def wallet_state(cursor, user_id, *, registered=None):
    if registered is None:
        cursor.execute('SELECT is_tourist FROM users WHERE user_id=%s', (user_id,))
        rows = _dict_rows(cursor)
        if not rows:
            raise InventoryError('用户不存在', 404)
        registered = rows[0]['is_tourist'] is False
    cursor.execute('SELECT balance_minor,earned_minor,spent_minor,version FROM user_wallets WHERE user_id=%s', (user_id,))
    rows = _dict_rows(cursor)
    amounts = rows[0] if rows else dict(balance_minor=0, earned_minor=0, spent_minor=0, version=0)
    return dict(user_id=user_id, currency='game_yuan', unit='coin', scale=100,
                reward_minor_per_hand=REWARD_MINOR_PER_HAND, registered=bool(registered), **amounts)


def _wallet_change(cursor, user_id, delta, source, source_id, *, hands=None, metadata=None, calculation_version=None):
    """Caller already owns the user row lock. A conflicting entitlement is an error."""
    cursor.execute('SELECT delta_minor FROM wallet_ledger WHERE user_id=%s AND source=%s AND source_id=%s',
                   (user_id, source, source_id))
    rows = _dict_rows(cursor)
    if rows:
        if rows[0]['delta_minor'] != delta:
            raise InventoryError('同一来源的萨拉币结算金额冲突，需核对原始记录', 409)
        return False
    cursor.execute('INSERT INTO user_wallets(user_id) VALUES(%s) ON CONFLICT DO NOTHING', (user_id,))
    cursor.execute('SELECT balance_minor FROM user_wallets WHERE user_id=%s FOR UPDATE', (user_id,))
    before = _dict_rows(cursor)[0]['balance_minor']
    after = before + delta
    if after < 0:
        raise InventoryError('持有萨拉币不足', 409)
    cursor.execute('''INSERT INTO wallet_ledger
        (user_id,source,source_id,delta_minor,balance_after,completed_hands,calculation_version,metadata)
        VALUES(%s,%s,%s,%s,%s,%s,%s,%s)''',
        (user_id, source, source_id, delta, after, hands,
         CALCULATION_VERSION if calculation_version is None else _int(calculation_version, '结算版本'), Json(metadata or {})))
    cursor.execute('''UPDATE user_wallets SET balance_minor=%s,
        earned_minor=earned_minor+%s,spent_minor=spent_minor+%s,version=version+1,
        updated_at=CURRENT_TIMESTAMP WHERE user_id=%s''',
        (after, delta if source == 'game_reward_recalculation' else max(0, delta),
         0 if source == 'game_reward_recalculation' else max(0, -delta), user_id))
    return True


def _saved_game_reward(cursor, game_id, record):
    """An accepted replay/ledger fixes its entitlement across reward rule changes."""
    cursor.execute('SELECT record FROM game_records WHERE game_id=%s', (game_id,))
    rows = _dict_rows(cursor)
    if rows:
        stored = rows[0]['record']
        stored = json.loads(stored) if isinstance(stored, str) else stored
        if stored.get('currency_reward') is not None:
            return stored['currency_reward']
        record = stored
    cursor.execute('''SELECT user_id,delta_minor,completed_hands,calculation_version FROM wallet_ledger
        WHERE source='game_reward' AND source_id=%s ORDER BY user_id''', (game_id,))
    ledger = _dict_rows(cursor)
    if not ledger:
        return None
    ids = participant_ids(record)
    if any(row['user_id'] not in ids or row['delta_minor'] < 0 for row in ledger):
        raise InventoryError('已保存对局与奖励账本身份或金额冲突', 409)
    versions = {row['calculation_version'] for row in ledger}
    hands = {row['completed_hands'] for row in ledger if row['completed_hands'] is not None}
    if len(versions) != 1 or len(hands) > 1:
        raise InventoryError('已保存对局的奖励账本版本或小局数冲突', 409)
    amounts = {row['user_id']: row['delta_minor'] for row in ledger}
    return dict(version=versions.pop(), completed_hands=hands.pop() if hands else completed_hand_count(record),
                eligible=True, players=[dict(user_id=uid, currency_delta_minor=amounts.get(uid, 0),
                    currency_reward_status='saved' if uid in amounts else 'not_applicable') for uid in ids])


def prepare_game_reward(cursor, record, indexed_ids, *, game_id=None):
    """Lock authoritative identities before writing a replay and its entitlement."""
    saved = _saved_game_reward(cursor, game_id, record) if game_id is not None else None
    humans = eligible_reward_ids(record, indexed_ids)
    locked_ids = sorted(set(humans) | {row['user_id'] for row in (saved or {}).get('players', [])
                                      if row.get('currency_delta_minor', 0) > 0})
    if not locked_ids:
        if saved is not None:
            return saved
        return build_currency_reward(record, indexed_ids, [])
    cursor.execute('SELECT user_id,is_tourist FROM users WHERE user_id=ANY(%s) ORDER BY user_id FOR UPDATE', (locked_ids,))
    users = _dict_rows(cursor)
    # Missing old guest accounts do not invalidate the other three human seats.
    # Never create or guess a missing registered identity from a replay.
    found = {row['user_id'] for row in users}
    if any(uid > 10000000 and uid not in found for uid in humans):
        raise InventoryError('对局的注册玩家身份缺失，无法完成萨拉币结算', 409)
    if saved is not None:
        return saved
    registered = [row['user_id'] for row in users if row['is_tourist'] is False]
    return build_currency_reward(record, indexed_ids, registered)


def settle_game_reward(cursor, game_id, record, *, backfill=False):
    """Human matchmaking tables pay; committed games retain their original reward."""
    cursor.execute('SELECT user_id FROM game_player_records WHERE game_id=%s ORDER BY user_id', (game_id,))
    snapshot = prepare_game_reward(cursor, record, [row['user_id'] for row in _dict_rows(cursor)], game_id=game_id)
    hands = snapshot['completed_hands']
    title = record.get('game_title') or {}
    cursor.execute("SELECT user_id FROM wallet_ledger WHERE source='game_reward' AND source_id=%s", (game_id,))
    paid = {row['user_id'] for row in _dict_rows(cursor)}
    credited = credited_minor = 0
    for player in snapshot['players']:
        amount = player['currency_delta_minor']
        if not amount or player['user_id'] in paid:
            continue
        changed = _wallet_change(cursor, player['user_id'], amount, 'game_reward', game_id,
                                  hands=hands, calculation_version=snapshot['version'], metadata={'rule': title.get('rule'),
                                                         'sub_rule': title.get('sub_rule'),
                                                         'room_type': title.get('room_type'),
                                                         'backfill': bool(backfill),
                                                         'end_time': str(title.get('end_time') or '')})
        credited += bool(changed)
        credited_minor += amount if changed else 0
    return dict(credited_players=credited, credited_minor=credited_minor)


def _offers(cursor, user_id):
    cursor.execute('''SELECT d.*,s.offer_id,s.unit_price_minor,s.price_version,s.max_purchase_quantity,s.prevent_duplicate_purchase,
        COALESCE(i.quantity,0) AS owned_quantity FROM shop_offers s
        JOIN item_definitions d USING(item_id)
        LEFT JOIN user_inventory i ON i.user_id=%s AND i.item_id=d.item_id
        WHERE s.enabled AND d.grant_enabled AND d.use_enabled AND NOT d.is_default
        ORDER BY s.sort_order,s.offer_id''', (user_id,))
    return [dict(offer_id=row['offer_id'], item=_public(row), unit_price_minor=row['unit_price_minor'],
                 price_version=row['price_version'], prevent_duplicate_purchase=row['prevent_duplicate_purchase'],
                 max_purchase_quantity=1 if row['prevent_duplicate_purchase'] else row['max_purchase_quantity'],
                 owned_quantity=row['owned_quantity']) for row in _dict_rows(cursor)]


def _ledger(cursor, user_id, *, before=None, limit=30):
    limit = _int(limit, '条数', 1, 100)
    params = [user_id]
    predicate = ''
    if before is not None:
        predicate = ' AND entry_id<%s'
        params.append(_int(before, '流水编号', 1, 9223372036854775807))
    params.append(limit)
    cursor.execute('''SELECT entry_id,source,source_id,delta_minor,balance_after,
        completed_hands,metadata,created_at FROM wallet_ledger WHERE user_id=%s'''
        + predicate + ' ORDER BY entry_id DESC LIMIT %s', params)
    return [dict(row, created_at=row['created_at'].isoformat()) for row in _dict_rows(cursor)]


def get_shop(db, user_id):
    def read(cursor):
        return dict(wallet_state=wallet_state(cursor, user_id), shop_offers=_offers(cursor, user_id),
                    wallet_entries=_ledger(cursor, user_id), inventory_state=_state(cursor, user_id))
    return _read(db, read)


def get_wallet(db, user_id, *, before=None):
    def read(cursor):
        return dict(wallet_state=wallet_state(cursor, user_id), wallet_entries=_ledger(cursor, user_id, before=before))
    return _read(db, read)


def purchase(db, user_id, body):
    request_id = body.get('request_id')
    if not isinstance(request_id, str) or not _REQUEST_ID.fullmatch(request_id):
        raise InventoryError('缺少有效的购买编号')
    offer_id = body.get('offer_id')
    if not isinstance(offer_id, str) or not re.fullmatch(r'[A-Za-z0-9_-]{1,64}', offer_id):
        raise InventoryError('商品编号无效')
    quantity = _int(body.get('quantity', 1), '数量', 1, 100)
    price_version = _int(body.get('price_version'), '商品版本')
    digest = hashlib.sha256(json.dumps(dict(offer_id=offer_id, quantity=quantity,
                                           price_version=price_version), sort_keys=True).encode()).hexdigest()
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute('SELECT pg_advisory_xact_lock_shared(%s)', (CATALOG_LOCK,))
            cursor.execute('SELECT user_id,is_tourist FROM users WHERE user_id=%s FOR UPDATE', (user_id,))
            user = cursor.fetchone()
            if user is None:
                raise InventoryError('请先登录后再购买', 401)
            if user['is_tourist'] is not False:
                raise InventoryError('游客无法购买物品', 403)
            cursor.execute('SELECT request_hash,result FROM shop_orders WHERE user_id=%s AND request_id=%s',
                           (user_id, request_id))
            existing = cursor.fetchone()
            if existing:
                if existing['request_hash'] != digest:
                    raise InventoryError('此购买编号已用于其他商品或数量', 409)
                result = existing['result']
            else:
                cursor.execute('SELECT * FROM shop_offers WHERE offer_id=%s FOR SHARE', (offer_id,))
                offer = cursor.fetchone()
                if not offer or not offer['enabled']:
                    raise InventoryError('商品已下架，请重新打开商店', 409)
                if offer['price_version'] != price_version:
                    raise InventoryError('商品价格已更新，请重新打开商店后购买', 409)
                if quantity > offer['max_purchase_quantity']:
                    raise InventoryError('超过此商品单次可购买数量')
                item = _definition(cursor, offer['item_id'])
                if item['is_default'] or not item['grant_enabled'] or not item['use_enabled']:
                    raise InventoryError('此物品暂不可购买', 409)
                before = _quantity(cursor, user_id, item['item_id'])
                if offer['prevent_duplicate_purchase'] and (before or quantity != 1):
                    raise InventoryError('已拥有此物品或数量无效，不能重复购买', 409)
                if item['category'] != 'item' and (quantity != 1 or before):
                    raise InventoryError('已拥有此永久物品或购买数量无效', 409)
                if before + quantity > item['max_quantity']:
                    raise InventoryError('超过物品持有上限', 409)
                total = offer['unit_price_minor'] * quantity
                order_id = str(uuid.uuid4())
                _wallet_change(cursor, user_id, -total, 'purchase', request_id,
                               metadata={'offer_id': offer_id, 'item_id': item['item_id'],
                                         'quantity': quantity, 'order_id': order_id})
                cursor.execute('''INSERT INTO inventory_operations
                    (operation_id,user_id,request_id,request_hash,action,reason)
                    VALUES(%s,%s,%s,%s,'purchase','商店购买')''',
                    (order_id, user_id, 'shop_' + order_id.replace('-', ''), digest))
                changes = [_change(cursor, order_id, user_id, item, quantity)]
                from .reward_receipts import make_reward_receipt
                receipt = make_reward_receipt(cursor, order_id, user_id, 'purchase', order_id, changes)
                result = dict(order_id=order_id, message='购买成功，物品已放入背包',
                              total_minor=total, reward_receipt=receipt)
                cursor.execute('UPDATE inventory_operations SET result=%s WHERE operation_id=%s', (Json(result), order_id))
                cursor.execute('''INSERT INTO shop_orders
                    (order_id,user_id,request_id,request_hash,offer_id,item_id,quantity,unit_price_minor,total_minor,result,price_version,item_snapshot)
                    VALUES(%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)''',
                    (order_id, user_id, request_id, digest, offer_id, item['item_id'], quantity,
                     offer['unit_price_minor'], total, Json(result), offer['price_version'], Json(_public(item))))
            response = dict(result, wallet_state=wallet_state(cursor, user_id, registered=True),
                            inventory_state=_state(cursor, user_id), shop_offers=_offers(cursor, user_id),
                            wallet_entries=_ledger(cursor, user_id))
        conn.commit()
        return response
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
