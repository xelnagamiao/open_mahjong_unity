"""Real PostgreSQL shop policy, administration and immutable purchase history."""
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace

from fastapi import FastAPI, HTTPException
from fastapi.testclient import TestClient
import pytest

from server.database import commerce
from server.database.commerce_admin import get_admin_shop, get_shop_orders, save_offer
from server.database.commerce_router import register_commerce_routes
from server.database.inventory import InventoryError, get_catalog, mutate_inventory, save_definition
from test_commerce import shop, fund, purchase_body


def body(shop, offer='grass_tile', **changes):
    row = next(o for o in get_admin_shop(shop.db)['offers'] if o['offer_id'] == offer)
    return dict(row, expected_version=row['price_version'], reason='shop admin test') | changes


def test_admin_offer_create_update_delist_and_audit_are_preserved_by_bootstrap(shop):
    created = save_offer(shop.db, 999, None, dict(offer_id='new_grass', item_id=3011,
        unit_price_minor=25, max_purchase_quantity=10, prevent_duplicate_purchase=True,
        enabled=False, sort_order=-20, reason='new item test'))
    assert created['max_purchase_quantity'] == 1 and created['price_version'] == 1
    assert not any(o['offer_id'] == 'new_grass' for o in commerce.get_shop(shop.db, 101)['shop_offers'])
    updated = save_offer(shop.db, 999, 'new_grass', body(shop, 'new_grass', enabled=True, unit_price_minor=40))
    assert updated['price_version'] == 2
    public = next(o for o in commerce.get_shop(shop.db, 101)['shop_offers'] if o['offer_id'] == 'new_grass')
    assert public['prevent_duplicate_purchase'] and public['max_purchase_quantity'] == 1
    shop.ensure_schema()
    assert next(o for o in get_admin_shop(shop.db)['offers'] if o['offer_id'] == 'new_grass') == updated
    assert shop.sql("SELECT admin_user_id,action FROM admin_audit_log ORDER BY id") == [
        (999, 'shop.offer_create'), (999, 'shop.offer_update')]
    payload = shop.sql("SELECT payload FROM admin_audit_log ORDER BY id DESC LIMIT 1")[0][0]
    assert payload['before']['unit_price_minor'] == 25 and payload['after']['unit_price_minor'] == 40


def test_catalog_saves_reject_stale_admins_invalid_prices_and_default_items(shop):
    original = body(shop)
    save_offer(shop.db, 999, 'grass_tile', dict(original, unit_price_minor=15))
    with pytest.raises(InventoryError, match='其他管理员'):
        save_offer(shop.db, 999, 'grass_tile', original)
    for changes in (dict(unit_price_minor=0), dict(unit_price_minor=True),
                    dict(prevent_duplicate_purchase='true'), dict(item_id=3012), dict(reason='')):
        with pytest.raises(InventoryError):
            save_offer(shop.db, 999, 'grass_tile', body(shop, **changes))
    with pytest.raises(InventoryError, match='默认物品'):
        save_offer(shop.db, 999, None, dict(offer_id='default', item_id=2301, unit_price_minor=5, reason='test'))
    with pytest.raises(InventoryError, match='已存在'):
        save_offer(shop.db, 999, None, dict(offer_id='grass_tile', item_id=3011, unit_price_minor=5, reason='test'))
    unchanged = get_admin_shop(shop.db)
    save_offer(shop.db, 999, 'grass_tile', body(shop))
    assert get_admin_shop(shop.db) == unchanged


def test_prevent_duplicate_honors_manual_grants_and_caps_new_purchases(shop):
    fund(shop)
    current = save_offer(shop.db, 999, 'grass_tile', body(shop, prevent_duplicate_purchase=True))
    with pytest.raises(InventoryError):
        commerce.purchase(shop.db, 101, dict(purchase_body(quantity=2), price_version=current['price_version']))
    mutate_inventory(shop.db, 101, 'grant', dict(item_id=3011, quantity=1,
        request_id='manual_grass_01', reason='test granted ownership'), 999)
    with pytest.raises(InventoryError, match='不能重复购买'):
        commerce.purchase(shop.db, 101, dict(purchase_body(), price_version=current['price_version']))
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 200
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(0,)]
    mutate_inventory(shop.db, 101, 'revoke', dict(item_id=3011, quantity=1,
        request_id='manual_revoke_01', reason='test empty inventory'), 999)
    commerce.purchase(shop.db, 101, dict(purchase_body(), price_version=current['price_version']))
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 190


def test_prevent_duplicate_is_atomic_for_distinct_orders_but_retry_returns_original(shop):
    fund(shop)
    current = save_offer(shop.db, 999, 'grass_tile', body(shop, prevent_duplicate_purchase=True))
    request = dict(purchase_body(), price_version=current['price_version'])
    def buy(index):
        try:
            return commerce.purchase(shop.db, 101, request if index == 0 else dict(purchase_body(), price_version=current['price_version']))
        except InventoryError:
            return None
    with ThreadPoolExecutor(max_workers=6) as executor:
        results = list(executor.map(buy, range(6)))
    succeeded = [r for r in results if r]
    assert len(succeeded) == 1
    row = shop.sql('SELECT request_id FROM shop_orders')[0][0]
    replay = dict(request, request_id=row)
    retry = commerce.purchase(shop.db, 101, replay)
    assert retry['order_id'] == succeeded[0]['order_id']
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(1,)]
    assert shop.sql("SELECT COUNT(*) FROM wallet_ledger WHERE source='purchase'") == [(1,)]
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 190


def test_policy_can_be_disabled_and_existing_purchase_is_not_invalidated(shop):
    fund(shop)
    first_request = purchase_body()
    first = commerce.purchase(shop.db, 101, first_request)
    enabled = save_offer(shop.db, 999, 'grass_tile', body(shop, prevent_duplicate_purchase=True))
    assert commerce.purchase(shop.db, 101, first_request)['order_id'] == first['order_id']
    with pytest.raises(InventoryError, match='价格已更新'):
        commerce.purchase(shop.db, 101, purchase_body())
    assert enabled['price_version'] == 2
    disabled = save_offer(shop.db, 999, 'grass_tile', body(shop, prevent_duplicate_purchase=False, max_purchase_quantity=10))
    commerce.purchase(shop.db, 101, dict(purchase_body(quantity=2), price_version=disabled['price_version']))
    assert shop.sql('SELECT quantity FROM user_inventory WHERE user_id=101 AND item_id=3011') == [(3,)]


def test_orders_keep_historical_price_name_and_balance_with_paging_filters(shop):
    fund(shop)
    first = commerce.purchase(shop.db, 101, purchase_body())
    current = next(item for item in get_catalog(shop.db) if item['item_id'] == 3011)
    save_definition(shop.db, 999, 3011, dict(current, name='后来改名的草牌', reason='rename test'))
    changed = save_offer(shop.db, 999, 'grass_tile', body(shop, unit_price_minor=25))
    second = commerce.purchase(shop.db, 101, dict(purchase_body(), price_version=changed['price_version']))
    found = get_shop_orders(shop.db, user_id=101, offer_id='grass_tile', order_id=first['order_id'])
    assert found['total'] == 1
    order = found['items'][0]
    assert (order['item_name'], order['unit_price_minor'], order['total_minor'], order['balance_after'], order['price_version']) == ('草牌', 10, 10, 190, 1)
    assert order['item_snapshot']['name'] == '草牌'
    assert get_shop_orders(shop.db, user_id=103)['total'] == 0
    assert get_shop_orders(shop.db, limit=1, page=1)['items'][0]['order_id'] == second['order_id']
    assert get_shop_orders(shop.db, limit=1, page=2)['items'][0]['order_id'] == first['order_id']
    assert get_shop_orders(shop.db, from_time='2100-01-01T00:00:00Z')['total'] == 0
    with pytest.raises(InventoryError, match='时区'):
        get_shop_orders(shop.db, from_time='2026-01-01T00:00:00')
    with pytest.raises(InventoryError):
        get_shop_orders(shop.db, user_id=True)


def test_admin_audit_failure_rolls_back_offer_change(shop):
    before = get_admin_shop(shop.db)
    shop.sql("ALTER TABLE admin_audit_log ADD CONSTRAINT reject_shop_audit CHECK(reason <> 'fail')")
    with pytest.raises(Exception, match='reject_shop_audit'):
        save_offer(shop.db, 999, 'grass_tile', body(shop, unit_price_minor=50, reason='fail'))
    assert get_admin_shop(shop.db) == before


def test_schema_upgrade_keeps_legacy_orders_without_inventing_a_price_version(shop):
    fund(shop)
    order = commerce.purchase(shop.db, 101, purchase_body())
    shop.sql('''ALTER TABLE shop_orders DROP COLUMN price_version;
        ALTER TABLE shop_orders DROP COLUMN item_snapshot;
        ALTER TABLE shop_offers DROP COLUMN prevent_duplicate_purchase;''')
    shop.ensure_schema()
    saved = get_shop_orders(shop.db, order_id=order['order_id'])['items'][0]
    assert saved['unit_price_minor'] == 10 and saved['total_minor'] == 10
    assert saved['price_version'] is None and saved['item_snapshot'] == {}
    assert saved['item_name'] == '草牌'
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 190


def test_http_admin_boundary_uses_verified_actor_and_preserves_transaction_errors(shop, monkeypatch):
    import server.database.inventory_router as auth
    tokens = []
    def verify(token):
        if token == 'Bearer rejected':
            raise HTTPException(status_code=403, detail='not an admin')
        tokens.append(token)
        return 999
    monkeypatch.setattr(auth, '_verify_admin', verify)
    app = FastAPI()
    register_commerce_routes(app, SimpleNamespace(db_manager=shop.db))
    client = TestClient(app)
    assert client.get('/admin/shop/offers').status_code == 401
    assert client.get('/admin/shop/offers', headers={'Authorization':'Bearer rejected'}).status_code == 403
    headers = {'Authorization':'Bearer valid-admin'}
    assert client.get('/admin/shop/offers', headers=headers).json()['data']['offers']
    payload = body(shop, unit_price_minor=20)
    payload['actor_id'] = 123
    saved = client.patch('/admin/shop/offers/grass_tile', headers=headers, json=payload)
    assert saved.status_code == 200 and saved.json()['data']['unit_price_minor'] == 20
    assert shop.sql('SELECT admin_user_id FROM admin_audit_log') == [(999,)]
    assert client.patch('/admin/shop/offers/grass_tile', headers=headers, json=payload).status_code == 409
    assert client.get('/admin/shop/orders?user_id=not-a-number', headers=headers).status_code == 400
    assert client.get('/admin/shop/orders?user_id=101&page=1&limit=10', headers=headers).json()['data']['total'] == 0
    assert all(token == 'Bearer valid-admin' for token in tokens)
