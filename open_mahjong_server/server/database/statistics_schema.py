"""统计存储的常规结构初始化；不扫描牌谱、不修改业务数据或迁移完成标记。"""
from psycopg2 import sql

from .legacy_statistics import FAMILIES


def ensure_statistics_schema(cursor):
    cursor.execute("ALTER TABLE game_player_metrics ADD COLUMN IF NOT EXISTS fan_stats JSONB")
    # 复式子规则标识超过旧的 32 字符限制。
    for table in ("game_player_records", "game_player_metrics"):
        cursor.execute(sql.SQL("ALTER TABLE {} ALTER COLUMN sub_rule TYPE VARCHAR(96)").format(sql.Identifier(table)))
    cursor.execute("ALTER TABLE scene_daily_stats ADD COLUMN IF NOT EXISTS sub_rule VARCHAR(96)")
    cursor.execute("ALTER TABLE scene_tier_fan_daily ADD COLUMN IF NOT EXISTS sub_rule VARCHAR(96) NOT NULL DEFAULT ''")
    # 只替换不含子规则的旧结构约束；历史数据修复不属于启动建表职责。
    for table, kind in (("scene_daily_stats", "u"), ("scene_tier_fan_daily", "p")):
        cursor.execute("SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid=%s::regclass AND contype=%s", (table, kind))
        for name, definition in cursor.fetchall():
            if "sub_rule" not in definition:
                cursor.execute(sql.SQL("ALTER TABLE {} DROP CONSTRAINT {}").format(sql.Identifier(table), sql.Identifier(name)))
    cursor.execute("CREATE UNIQUE INDEX IF NOT EXISTS scene_daily_subrule_key ON scene_daily_stats(stat_date,room_type,match_tier,event_id,rule,sub_rule,game_type)")
    cursor.execute("CREATE UNIQUE INDEX IF NOT EXISTS scene_fan_subrule_key ON scene_tier_fan_daily(stat_date,match_tier,rule,sub_rule,fan_field)")
    cursor.execute("CREATE UNIQUE INDEX IF NOT EXISTS game_player_metrics_game_user_key ON game_player_metrics(game_id,user_id)")
    cursor.execute("CREATE INDEX IF NOT EXISTS game_player_metrics_subrule_user ON game_player_metrics(sub_rule,user_id,room_type)")
    cursor.execute("""CREATE TABLE IF NOT EXISTS app_meta (
        meta_key VARCHAR(100) PRIMARY KEY, meta_value TEXT,
        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP)""")
    tables = [f"{family}_{suffix}" for family in FAMILIES for suffix in ("history_stats", "fan_stats")]
    for table in [*tables, "player_recent_records"]:
        cursor.execute("SELECT to_regclass(%s)", (table,))
        if cursor.fetchone()[0] is not None:
            cursor.execute(sql.SQL("ALTER TABLE {} ALTER COLUMN rule TYPE VARCHAR(96)").format(sql.Identifier(table)))
