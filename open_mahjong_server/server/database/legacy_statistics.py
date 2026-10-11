"""旧累计表保留兼容结构，rule 列改存完整子规则；由逐局指标覆盖刷新，禁止累加。"""
from psycopg2 import sql
from .riichi.record_stats import BASE_KEYS

FAMILIES = {"guobiao": "guobiao", "riichi": "riichi", "qingque": "qingque",
            "classical": "classical", "changsha": "changsha", "jiandan": "zhongyong"}


def _refresh_legacy_statistics(cursor, family, game_id):
    """只刷新当前对局涉及玩家的兼容累计数据，不提供全表重建入口。"""
    if family not in FAMILIES or not game_id:
        raise ValueError("兼容统计刷新必须指定有效规则和当前对局")
    for prefix, rule in FAMILIES.items():
        if prefix != family:
            continue
        for suffix in ("history_stats", "fan_stats"):
            table = f"{prefix}_{suffix}"
            cursor.execute("SELECT to_regclass(%s)", (table,))
            if cursor.fetchone()[0] is None:
                continue
            cursor.execute("SELECT pg_advisory_xact_lock(hashtext(%s))", (table,))
            cursor.execute("SELECT attname FROM pg_attribute WHERE attrelid=%s::regclass AND attnum>0 AND NOT attisdropped", (table,))
            columns = [row[0] for row in cursor.fetchall()]
            if suffix == "history_stats":
                fields = [key for key in BASE_KEYS if key in columns]
                expressions = ["COUNT(*)" if key == "total_games" else f'SUM("{key}")' for key in fields]
            else:
                fields = [key for key in columns if key not in ("user_id", "rule", "mode", "created_at", "updated_at", "id")]
                expressions = [sql.SQL("SUM(COALESCE((fan_stats->>{})::int,0))").format(sql.Literal(key)).as_string(cursor) for key in fields]
            scope = " AND user_id IN (SELECT user_id FROM game_player_metrics WHERE game_id=%s)"
            params = (game_id,)
            cursor.execute(sql.SQL("DELETE FROM {} WHERE TRUE" + scope).format(sql.Identifier(table)), params)
            cursor.execute(sql.SQL("INSERT INTO {} (user_id,rule,mode,{}) SELECT user_id,sub_rule,match_type,{} FROM game_player_metrics WHERE rule=%s{}{} GROUP BY user_id,sub_rule,match_type").format(
                sql.Identifier(table), sql.SQL(',').join(map(sql.Identifier, fields)), sql.SQL(','.join(expressions)),
                sql.SQL(" AND fan_stats IS NOT NULL" if suffix == "fan_stats" else ""), sql.SQL(scope)), (rule, *params))


def refresh_legacy_statistics(db, game_id, family):
    conn = db._get_connection()
    try:
        with conn.cursor() as cur:
            _refresh_legacy_statistics(cur, family, game_id)
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
