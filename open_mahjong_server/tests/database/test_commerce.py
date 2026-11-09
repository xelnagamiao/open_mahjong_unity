"""Real PostgreSQL accounting, concurrency and authenticated transport."""
import asyncio
from concurrent.futures import ThreadPoolExecutor
import os
from types import SimpleNamespace
import uuid

import pytest

from server.database import test_inventory as inventory_fixtures
from server.database import commerce
from server.database.commerce_router import handle_commerce_message
from server.database.inventory import InventoryError, get_inventory, save_definition
from test_subrule_statistics import db
from test_round_progress import record


@pytest.fixture
def shop():
    if not os.environ.get('INVENTORY_TEST_DATABASE_URL'):
        pytest.skip('requires local PostgreSQL test connection')
    fixture = inventory_fixtures.InventoryDatabaseTests()
    fixture.setUp()
    fixture.sql('''INSERT INTO users(user_id,is_tourist) VALUES(104,FALSE);
        CREATE TABLE game_player_records(game_id text,user_id bigint);
        CREATE TABLE game_records(game_id text PRIMARY KEY,record jsonb);''')
    try:
        yield fixture
    finally:
        fixture.tearDown()


def fund(shop, amount=200, uid=101):
    conn = shop.pool.getconn()
    try:
        with conn.cursor() as cur:
            cur.execute('SELECT user_id FROM users WHERE user_id=%s FOR UPDATE', (uid,))
            commerce._wallet_change(cur,uid,amount,'test_seed',uuid.uuid4().hex)
        conn.commit()
    finally:
        shop.pool.putconn(conn)


def purchase_body(offer='grass_tile', quantity=1):
    return dict(request_id=uuid.uuid4().hex,offer_id=offer,quantity=quantity,price_version=1)


def test_actual_repeats_reward_registered_players_and_guests_only_complete_the_roster(shop):
    replay = record(hands=11)
    replay['game_title']['room_type'] = 'match'
    for i in range(4):
        replay['game_title'][f'p{i}_uid'] = 101+i
    replay['game_round']['round_index_12'] = dict(round_index=12,current_round=8,action_ticks=[['d',11]])
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,104)', ('repeat-game',))
    conn = shop.pool.getconn()
    try:
        with conn.cursor() as cur:
            first = commerce.settle_game_reward(cur,'repeat-game',replay)
            second = commerce.settle_game_reward(cur,'repeat-game',replay)
        conn.commit()
    finally:
        shop.pool.putconn(conn)
    assert first == dict(credited_players=3,credited_minor=165)
    assert second == dict(credited_players=0,credited_minor=0)
    assert commerce.get_wallet(shop.db,101)['wallet_state']['balance_minor'] == 55
    assert commerce.get_wallet(shop.db,102)['wallet_state']['balance_minor'] == 0
    assert shop.sql('SELECT DISTINCT completed_hands FROM wallet_ledger') == [(11,)]


def test_purchase_atomicity_and_retry_survive_price_change_and_delisting(shop):
    fund(shop)
    body = purchase_body(quantity=3)
    first = commerce.purchase(shop.db,101,body)
    assert first['wallet_state']['balance_minor'] == 170
    assert next(i['quantity'] for i in first['inventory_state']['owned'] if i['item_id']==3011) == 3
    shop.sql("UPDATE shop_offers SET enabled=FALSE,price_version=2,unit_price_minor=20 WHERE offer_id='grass_tile'")
    second = commerce.purchase(shop.db,101,body)
    assert first['order_id'] == second['order_id']
    assert second['wallet_state']['balance_minor'] == 170
    assert second['reward_receipt'] == first['reward_receipt']
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(1,)]
    assert shop.sql("SELECT COUNT(*) FROM wallet_ledger WHERE source='purchase'") == [(1,)]
    with pytest.raises(InventoryError,match='购买编号'):
        commerce.purchase(shop.db,101,dict(body,quantity=2))


def test_unknown_registration_flags_are_not_treated_as_registered_accounts(shop):
    shop.sql('UPDATE users SET is_tourist=NULL WHERE user_id=103')
    replay=record(hands=1)
    replay['game_title']['room_type']='match'
    for i in range(4): replay['game_title'][f'p{i}_uid']=101+i
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,104)',('unknown-account',))
    conn=shop.pool.getconn()
    try:
        with conn.cursor() as cur: outcome=commerce.settle_game_reward(cur,'unknown-account',replay)
        conn.commit()
    finally:shop.pool.putconn(conn)
    assert outcome['credited_players']==2
    assert not commerce.get_wallet(shop.db,103)['wallet_state']['registered']
    with pytest.raises(InventoryError,match='游客'):
        commerce.purchase(shop.db,103,purchase_body())


def test_item_write_failure_rolls_back_money_ledger_order_and_inventory(shop,monkeypatch):
    fund(shop)
    def fail(*args,**kwargs):
        raise RuntimeError('simulate item storage failure')
    monkeypatch.setattr(commerce,'_change',fail)
    with pytest.raises(RuntimeError):
        commerce.purchase(shop.db,101,purchase_body())
    wallet = commerce.get_wallet(shop.db,101)['wallet_state']
    assert wallet['balance_minor'] == 200 and wallet['spent_minor'] == 0
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(0,)]
    assert shop.sql("SELECT COUNT(*) FROM wallet_ledger WHERE source='purchase'") == [(0,)]
    assert not any(i['item_id']==3011 for i in get_inventory(shop.db,101)['owned'])


def test_parallel_retry_of_one_order_charges_once(shop):
    fund(shop)
    body=purchase_body()
    with ThreadPoolExecutor(max_workers=6) as executor:
        results=list(executor.map(lambda _:commerce.purchase(shop.db,101,body),range(6)))
    assert len({r['order_id'] for r in results}) == 1
    assert commerce.get_wallet(shop.db,101)['wallet_state']['balance_minor'] == 190
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(1,)]


def test_parallel_different_orders_cannot_overspend(shop):
    fund(shop,20)
    def attempt(_):
        try:
            commerce.purchase(shop.db,101,purchase_body())
            return True
        except InventoryError as exc:
            assert exc.status == 409
            return False
    with ThreadPoolExecutor(max_workers=6) as executor:
        results=list(executor.map(attempt,range(6)))
    assert sum(results) == 2
    assert commerce.get_wallet(shop.db,101)['wallet_state']['balance_minor'] == 0
    assert shop.sql('SELECT quantity FROM user_inventory WHERE user_id=101 AND item_id=3011') == [(2,)]


def test_guest_and_stale_price_requests_do_not_spend_currency(shop):
    fund(shop)
    with pytest.raises(InventoryError,match='游客'):
        commerce.purchase(shop.db,102,purchase_body())
    body=purchase_body()
    shop.sql("UPDATE shop_offers SET price_version=2 WHERE offer_id='grass_tile'")
    with pytest.raises(InventoryError,match='价格已更新'):
        commerce.purchase(shop.db,101,body)
    assert commerce.get_wallet(shop.db,101)['wallet_state']['balance_minor'] == 200
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(0,)]


def test_permanent_ownership_is_checked_before_debit(shop):
    fund(shop)
    item=save_definition(shop.db,999,None,dict(code='shop_frame',name='商店测试框',description='',
        asset_key='frame.a1',category='cosmetic',max_quantity=1,grant_enabled=True,use_enabled=True,
        sort_order=0,config={},reason='test permanent purchase'))['item']
    shop.sql("INSERT INTO shop_offers(offer_id,item_id,unit_price_minor) VALUES('test_frame',%s,10)",(item['item_id'],))
    commerce.purchase(shop.db,101,purchase_body('test_frame'))
    with pytest.raises(InventoryError,match='已拥有'):
        commerce.purchase(shop.db,101,purchase_body('test_frame'))
    assert commerce.get_wallet(shop.db,101)['wallet_state']['balance_minor'] == 190
    assert shop.sql('SELECT COUNT(*) FROM shop_orders') == [(1,)]


def test_socket_ignores_client_user_identity(shop):
    fund(shop,200,101)
    fund(shop,500,103)
    messages=[]
    class Socket:
        async def send_json(self,payload): messages.append(payload)
    server=SimpleNamespace(players={'connection':SimpleNamespace(user_id=101)},db_manager=shop.db)
    asyncio.run(handle_commerce_message(server,'connection',dict(type='wallet/get',user_id=103),Socket()))
    assert messages[0]['success'] and messages[0]['wallet_state']['user_id'] == 101
    assert messages[0]['wallet_state']['balance_minor'] == 200


def test_shared_record_transaction_rolls_back_all_rewards_then_retries_same_id(db,monkeypatch):
    from server.database.guobiao.store_guobiao import store_guobiao_game_record
    from test_subrule_realtime import prepare,players,replay
    from server.database.statistics_catalog import BY_SUB_RULE
    prepare(db)
    definition=BY_SUB_RULE['guobiao/standard']
    data=replay(definition)
    original=commerce._wallet_change
    def fail_second(cursor,uid,*args,**kwargs):
        if uid==10000102: raise RuntimeError('second player reward fails')
        return original(cursor,uid,*args,**kwargs)
    monkeypatch.setattr(commerce,'_wallet_change',fail_second)
    data['game_title']['room_type']='match'
    assert store_guobiao_game_record(db,data,players(definition),'match','1/4_rank') is None
    assert 'currency_reward' not in data
    for table in ('game_records','game_player_records','game_player_metrics','wallet_ledger','user_wallets'):
        assert db.sql('SELECT COUNT(*) FROM '+table) == [(0,)]
    reserved=data['game_title']['cloud_game_id']
    monkeypatch.setattr(commerce,'_wallet_change',original)
    gid=store_guobiao_game_record(db,data,players(definition),'match','1/4_rank')
    assert gid == reserved
    assert store_guobiao_game_record(db,data,players(definition),'match','1/4_rank') == gid
    assert db.sql('SELECT COUNT(*) FROM game_records') == [(1,)]
    assert db.sql('SELECT COUNT(*) FROM wallet_ledger') == [(4,)]
