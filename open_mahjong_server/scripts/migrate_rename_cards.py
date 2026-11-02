"""Convert all legacy rename credits to inventory cards, one credit per card.

Read-only preview by default; --apply commits one atomic migration with ledger
entries and an audit marker. Stop old Node/Python writers before applying.
Connection: RENAME_CARDS_DATABASE_URL or local_config/test_config.Config.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import psycopg2
from psycopg2.extensions import TRANSACTION_STATUS_IDLE
from psycopg2.extras import Json, RealDictCursor

from server.database.inventory import CATALOG_LOCK

MIGRATION_KEY = '20261028_rename_cards_v1'
AUDIT_ACTION = 'inventory.rename_credit_migration'
CARD_ID = 3001


def migrate_database(conn, *, apply=False, expected_fingerprint=None):
    if conn.get_transaction_status() != TRANSACTION_STATUS_IDLE:
        raise ValueError('迁移需要没有未提交事务的独立数据库连接')
    conn.set_session(readonly=not apply, isolation_level='READ COMMITTED' if apply else 'REPEATABLE READ')
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            if apply:
                cursor.execute("SET LOCAL lock_timeout = '10s'")
                # Same lock order as inventory mutations and account rename.
                cursor.execute('SELECT pg_advisory_xact_lock(%s)', (CATALOG_LOCK,))
                cursor.execute('LOCK TABLE users IN SHARE ROW EXCLUSIVE MODE')
                cursor.execute('LOCK TABLE user_inventory IN SHARE ROW EXCLUSIVE MODE')
            cursor.execute("SELECT * FROM item_definitions WHERE item_id=%s OR code='rename_coupon'", (CARD_ID,))
            definitions = cursor.fetchall()
            if (len(definitions) != 1 or definitions[0]['item_id'] != CARD_ID
                    or definitions[0]['code'] != 'rename_coupon'
                    or definitions[0]['asset_key'] != 'item.rename_credit'
                    or definitions[0]['category'] != 'item' or definitions[0]['is_default']):
                raise ValueError('内置改名卡目录不一致；请先初始化并核对物品 3001 / rename_coupon')
            card = definitions[0]
            cursor.execute('''SELECT u.user_id, u.rename_count AS legacy_count, u.is_tourist,
                    COALESCE(i.quantity,0) AS quantity_before
                FROM users u LEFT JOIN user_inventory i ON i.user_id=u.user_id AND i.item_id=%s
                WHERE u.rename_count IS NULL OR u.rename_count<>0 ORDER BY u.user_id''', (CARD_ID,))
            balances = [dict(row) for row in cursor.fetchall()]
            for row in balances:
                if type(row['legacy_count']) is not int or row['legacy_count'] < 0:
                    raise ValueError(f"用户 {row['user_id']} 的历史改名次数无效；未修改任何余额")
                row['quantity_after'] = row['quantity_before'] + row['legacy_count']
                if row['quantity_after'] > min(card['max_quantity'], 1000000):
                    raise ValueError(f"用户 {row['user_id']} 折算后超过改名卡持有上限；未截断或修改余额")
            cursor.execute('''SELECT payload FROM admin_audit_log
                WHERE action=%s AND target_id=%s ORDER BY id DESC LIMIT 1''', (AUDIT_ACTION, MIGRATION_KEY))
            completed = cursor.fetchone()
            if completed and balances:
                raise ValueError('迁移已完成但又出现历史改名次数；请先停止旧版服务并核对流水，禁止重复折算')
            cursor.execute('SELECT COALESCE(SUM(quantity),0) AS total FROM user_inventory WHERE item_id=%s', (CARD_ID,))
            total_before = int(cursor.fetchone()['total'])
            fingerprint = hashlib.sha256(json.dumps(
                dict(card_id=CARD_ID, max_quantity=card['max_quantity'], balances=balances),
                ensure_ascii=False, sort_keys=True).encode('utf-8')).hexdigest()
            report = dict(migration=MIGRATION_KEY, status='already_complete' if completed else 'ready', applied=False,
                          affected_users=len(balances), cards_to_add=sum(row['legacy_count'] for row in balances),
                          tourist_users=sum(bool(row['is_tourist']) for row in balances),
                          card_id=CARD_ID, card_use_enabled=card['use_enabled'],
                          fingerprint=fingerprint, balances=balances)
            if completed:
                conn.rollback()
                return report
            if expected_fingerprint and fingerprint != expected_fingerprint:
                raise ValueError('数据库余额与预览指纹不一致；请重新预览，未修改任何余额')
            if not apply:
                conn.rollback()
                return report
            for row in balances:
                uid, amount = row['user_id'], row['legacy_count']
                operation_id = str(uuid.uuid5(uuid.NAMESPACE_URL, f'{MIGRATION_KEY}/{uid}'))
                result = dict(message='历史改名次数按 1:1 折算为改名卡',
                              legacy_rename_count_before=amount, quantity_before=row['quantity_before'],
                              changes=[dict(item_id=CARD_ID, delta=amount, quantity_after=row['quantity_after'])])
                digest = hashlib.sha256(json.dumps(result, sort_keys=True, ensure_ascii=False).encode('utf-8')).hexdigest()
                cursor.execute('''INSERT INTO inventory_operations
                    (operation_id,user_id,request_id,request_hash,action,reason,result)
                    VALUES (%s,%s,%s,%s,'migrate_rename_cards','历史改名次数 1:1 折算',%s)''',
                    (operation_id,uid,MIGRATION_KEY,digest,Json(result)))
                cursor.execute('''INSERT INTO user_inventory(user_id,item_id,quantity) VALUES (%s,%s,%s)
                    ON CONFLICT (user_id,item_id) DO UPDATE
                    SET quantity=EXCLUDED.quantity,updated_at=CURRENT_TIMESTAMP''', (uid,CARD_ID,row['quantity_after']))
                cursor.execute('''INSERT INTO inventory_ledger(operation_id,user_id,item_id,delta,quantity_after)
                    VALUES (%s,%s,%s,%s,%s)''', (operation_id,uid,CARD_ID,amount,row['quantity_after']))
                cursor.execute('UPDATE users SET rename_count=0 WHERE user_id=%s AND rename_count=%s', (uid,amount))
                if cursor.rowcount != 1:
                    raise ValueError('迁移期间历史余额发生变化；整批迁移已回滚')
            cursor.execute('SELECT COUNT(*) AS remaining FROM users WHERE rename_count IS NULL OR rename_count<>0')
            if cursor.fetchone()['remaining']:
                raise ValueError('仍有未折算的历史余额；整批迁移已回滚')
            cursor.execute('SELECT COALESCE(SUM(quantity),0) AS total FROM user_inventory WHERE item_id=%s', (CARD_ID,))
            if int(cursor.fetchone()['total']) != total_before + report['cards_to_add']:
                raise ValueError('改名卡总量校验失败；整批迁移已回滚')
            report.update(status='complete', applied=True)
            cursor.execute('''INSERT INTO admin_audit_log(admin_user_id,action,target_type,target_id,payload,reason)
                VALUES (0,%s,'inventory',%s,%s,'一次性将所有历史改名次数按 1:1 折算为改名卡')''',
                (AUDIT_ACTION,MIGRATION_KEY,Json(report)))
        conn.commit()
        return report
    except BaseException:
        conn.rollback()
        raise


def connect_database():
    if os.environ.get('RENAME_CARDS_DATABASE_URL'):
        return psycopg2.connect(os.environ['RENAME_CARDS_DATABASE_URL'])
    try:
        from server.local_config import Config
    except ModuleNotFoundError as exc:
        if exc.name != 'server.local_config':
            raise
        from server.test_config import Config
    return psycopg2.connect(**{key: getattr(Config, key) for key in ('host', 'port', 'user', 'password', 'database')})


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--apply', action='store_true', help='提交折算、库存流水和审计标记')
    mode.add_argument('--dry-run', action='store_true', help='只读预览（默认）')
    parser.add_argument('--expect-fingerprint', help='仅允许折算与指定预览 SHA-256 一致的余额')
    parser.add_argument('--report', type=Path, help='保存 JSON 报告；本机报告请放 .om_workspace/')
    args = parser.parse_args(argv)
    # Validate the report destination before any database write; never overwrite.
    output = args.report.open('x', encoding='utf-8') if args.report else None
    conn = None
    try:
        conn = connect_database()
        report = migrate_database(conn, apply=args.apply, expected_fingerprint=args.expect_fingerprint)
    except (ValueError, psycopg2.Error) as exc:
        error = str(exc) if isinstance(exc, ValueError) else f'数据库操作失败（{exc.pgcode or type(exc).__name__}）；请检查连接、表结构或锁占用'
        report = dict(migration=MIGRATION_KEY,status='blocked',applied=False,error=error)
    finally:
        if conn is not None:
            conn.close()
    text = json.dumps(report, ensure_ascii=False, indent=2)
    try:
        if output:
            output.write(text + '\n')
        # Per-user details belong in the optional report and database audit.
        print(json.dumps({key:value for key,value in report.items() if key != 'balances'}, ensure_ascii=False, indent=2))
    finally:
        if output:
            output.close()
    return 2 if report['status'] == 'blocked' else 0


if __name__ == '__main__':
    raise SystemExit(main())
