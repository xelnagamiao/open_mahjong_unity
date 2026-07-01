"""
数据库管理类
用于管理 PostgreSQL 数据库连接和操作
"""
import psycopg2
from psycopg2 import Error
from psycopg2.extras import RealDictCursor
from psycopg2.pool import ThreadedConnectionPool, SimpleConnectionPool
from typing import Optional, Dict, Any, List
from datetime import datetime, timezone
import logging
import threading
import hashlib
import secrets
import json

# 禁止登录的封禁类型
LOGIN_BAN_TYPES = frozenset({'login', 'full'})
# 封禁解封联系 QQ
BAN_CONTACT_QQ = '1448826180'

logger = logging.getLogger(__name__)


class DatabaseManager:
    """PostgreSQL 数据库管理类"""

    LOGIN_IP_KEEP_LIMIT = 20
    
    def __init__(self, host: str, user: str, password: str, database: str, port: int = 5432, minconn: int = 2, maxconn: int = 10):
        self.config = {
            'host': host,
            'user': user,
            'password': password,
            'database': database,
            'port': port
        }
        # 连接池（延迟初始化，在 init_database 之后创建）
        self.pool: Optional[ThreadedConnectionPool] = None
        self.pool_lock = threading.Lock()
    
    def _get_pool(self) -> ThreadedConnectionPool:
        """获取或创建连接池（线程安全）"""
        if self.pool is None:
            with self.pool_lock:
                if self.pool is None:
                    self.pool = ThreadedConnectionPool(
                        minconn=2,
                        maxconn=10,
                        **self.config
                    )
        return self.pool
    
    def _get_connection(self):
        """从连接池获取连接"""
        pool = self._get_pool()
        return pool.getconn()
    
    def _put_connection(self, conn):
        """将连接归还到连接池"""
        if self.pool:
            self.pool.putconn(conn)
    
    def init_database(self):
        conn = None
        try:
            conn = psycopg2.connect(**self.config)
            cursor = conn.cursor()

            # 创建表users（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS users (
                    user_id BIGSERIAL PRIMARY KEY,
                    username VARCHAR(255) UNIQUE NOT NULL,
                    password VARCHAR(255) NOT NULL,
                    is_tourist BOOLEAN DEFAULT FALSE,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 创建表 game_records（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_records (
                    game_id VARCHAR(16) PRIMARY KEY,
                    record JSONB NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 创建表 game_player_records（如果不存在）；记录字段使用 match_type，与 get_record_list 一致
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_player_records (
                    game_id VARCHAR(16) NOT NULL REFERENCES game_records(game_id) ON DELETE CASCADE,
                    user_id BIGINT NOT NULL,
                    username VARCHAR(255) NOT NULL,
                    score INT NOT NULL,
                    rank INT NOT NULL CHECK (rank >= 1 AND rank <= 4),
                    rule VARCHAR(10) NOT NULL,
                    sub_rule VARCHAR(32) NULL,
                    match_type VARCHAR(24) NULL,
                    title_used INT NULL,
                    character_used INT NULL,
                    profile_used INT NULL,
                    voice_used INT NULL,
                    PRIMARY KEY (game_id, user_id)
                );
            """)
            # 迁移：若表已存在且缺少 match_type，则追加列（重复列时回滚到 savepoint 继续，避免事务被中止）
            cursor.execute("SAVEPOINT sp_add_match_type;")
            try:
                cursor.execute("ALTER TABLE game_player_records ADD COLUMN match_type VARCHAR(24) NULL;")
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_add_match_type;")
                else:
                    raise
            cursor.execute("SAVEPOINT sp_add_room_type;")
            try:
                cursor.execute("ALTER TABLE game_player_records ADD COLUMN room_type VARCHAR(16) NULL;")
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_add_room_type;")
                else:
                    raise
            cursor.execute("SAVEPOINT sp_add_original_player_index;")
            try:
                cursor.execute("ALTER TABLE game_player_records ADD COLUMN original_player_index INT NULL CHECK (original_player_index >= 0 AND original_player_index <= 3);")
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_add_original_player_index;")
                else:
                    raise
            # 从牌谱 JSON 回填 room_type
            cursor.execute("""
                UPDATE game_player_records gpr
                SET room_type = gr.record->'game_title'->>'room_type'
                FROM game_records gr
                WHERE gpr.game_id = gr.game_id
                  AND (gpr.room_type IS NULL OR gpr.room_type = '')
                  AND gr.record->'game_title'->>'room_type' IS NOT NULL;
            """)
            # 迁移：追加 match_tier（排位场次等级 beginner/intermediate/advanced/mcrpl）
            cursor.execute("SAVEPOINT sp_add_match_tier;")
            try:
                cursor.execute("ALTER TABLE game_player_records ADD COLUMN match_tier VARCHAR(24) NULL;")
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_add_match_tier;")
                else:
                    raise
            # 迁移：追加 event_id（比赛场 room_type='events' 的比赛唯一 id）
            cursor.execute("SAVEPOINT sp_add_event_id;")
            try:
                cursor.execute("ALTER TABLE game_player_records ADD COLUMN event_id VARCHAR(64) NULL;")
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_add_event_id;")
                else:
                    raise
            # 从牌谱 JSON 回填 match_tier / event_id
            cursor.execute("""
                UPDATE game_player_records gpr
                SET match_tier = gr.record->'game_title'->>'match_tier'
                FROM game_records gr
                WHERE gpr.game_id = gr.game_id
                  AND (gpr.match_tier IS NULL OR gpr.match_tier = '')
                  AND gr.record->'game_title'->>'match_tier' IS NOT NULL;
            """)
            # 天梯对局按「是否提示」补齐 match_tier：有提示=初级场(beginner)，无提示=中级场(intermediate)
            # 取消历史排位(legacy_match)设计——所有 match 对局都归类为具体天梯场次
            cursor.execute("""
                UPDATE game_player_records gpr
                SET match_tier = CASE WHEN (gr.record->'game_title'->>'tips')::boolean
                                      THEN 'beginner' ELSE 'intermediate' END
                FROM game_records gr
                WHERE gpr.game_id = gr.game_id
                  AND gpr.room_type = 'match'
                  AND (gpr.match_tier IS NULL OR gpr.match_tier = '')
                  AND gr.record->'game_title'->>'tips' IS NOT NULL;
            """)
            cursor.execute("""
                UPDATE game_player_records gpr
                SET event_id = gr.record->'game_title'->>'event_id'
                FROM game_records gr
                WHERE gpr.game_id = gr.game_id
                  AND (gpr.event_id IS NULL OR gpr.event_id = '')
                  AND gr.record->'game_title'->>'event_id' IS NOT NULL;
            """)
            # 迁移：若表中曾有 mode 列，将 mode 拷贝到 match_type 后丢弃 mode
            cursor.execute("""
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'game_player_records' AND column_name = 'mode';
            """)
            if cursor.fetchone():
                cursor.execute("SAVEPOINT sp_migrate_mode;")
                try:
                    cursor.execute("""
                        UPDATE game_player_records SET match_type = mode
                        WHERE match_type IS NULL AND mode IS NOT NULL;
                    """)
                    cursor.execute("ALTER TABLE game_player_records DROP COLUMN mode;")
                except Error:
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_migrate_mode;")

            # 创建表guobiao_history_stats（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS guobiao_history_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    total_games INT NOT NULL DEFAULT 0,
                    total_rounds INT NOT NULL DEFAULT 0,
                    win_count INT NOT NULL DEFAULT 0,
                    self_draw_count INT NOT NULL DEFAULT 0,
                    deal_in_count INT NOT NULL DEFAULT 0,
                    total_fan_score INT NOT NULL DEFAULT 0,
                    total_win_turn INT NOT NULL DEFAULT 0,
                    total_fangchong_score INT NOT NULL DEFAULT 0,
                    first_place_count INT NOT NULL DEFAULT 0,
                    second_place_count INT NOT NULL DEFAULT 0,
                    third_place_count INT NOT NULL DEFAULT 0,
                    fourth_place_count INT NOT NULL DEFAULT 0,
                    fulu_round_count INT NOT NULL DEFAULT 0,
                    cuohe_count INT NOT NULL DEFAULT 0,
                    total_round_score INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)
            
            # 创建表riichi_history_stats（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS riichi_history_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    total_games INT NOT NULL DEFAULT 0,
                    total_rounds INT NOT NULL DEFAULT 0,
                    win_count INT NOT NULL DEFAULT 0,
                    self_draw_count INT NOT NULL DEFAULT 0,
                    deal_in_count INT NOT NULL DEFAULT 0,
                    total_fan_score INT NOT NULL DEFAULT 0,
                    total_win_turn INT NOT NULL DEFAULT 0,
                    total_fangchong_score INT NOT NULL DEFAULT 0,
                    first_place_count INT NOT NULL DEFAULT 0,
                    second_place_count INT NOT NULL DEFAULT 0,
                    third_place_count INT NOT NULL DEFAULT 0,
                    fourth_place_count INT NOT NULL DEFAULT 0,
                    fulu_round_count INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)

            from .riichi.store_riichi import FAN_FIELDS as RIICHI_FAN_FIELDS
            riichi_fan_columns = ",\n                    ".join(
                f"{field} INT NOT NULL DEFAULT 0" for field in RIICHI_FAN_FIELDS
            )
            cursor.execute(f"""
                CREATE TABLE IF NOT EXISTS riichi_fan_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    {riichi_fan_columns},
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)
            
            # 创建表guobiao_fan_stats（国标麻将番种统计表）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS guobiao_fan_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    dasixi INT NOT NULL DEFAULT 0,
                    dasanyuan INT NOT NULL DEFAULT 0,
                    lvyise INT NOT NULL DEFAULT 0,
                    jiulianbaodeng INT NOT NULL DEFAULT 0,
                    sigang INT NOT NULL DEFAULT 0,
                    sangang INT NOT NULL DEFAULT 0,
                    lianqidui INT NOT NULL DEFAULT 0,
                    shisanyao INT NOT NULL DEFAULT 0,
                    qingyaojiu INT NOT NULL DEFAULT 0,
                    xiaosixi INT NOT NULL DEFAULT 0,
                    xiaosanyuan INT NOT NULL DEFAULT 0,
                    ziyise INT NOT NULL DEFAULT 0,
                    sianke INT NOT NULL DEFAULT 0,
                    yiseshuanglonghui INT NOT NULL DEFAULT 0,
                    yisesitongshun INT NOT NULL DEFAULT 0,
                    yisesijiegao INT NOT NULL DEFAULT 0,
                    yisesibugao INT NOT NULL DEFAULT 0,
                    hunyaojiu INT NOT NULL DEFAULT 0,
                    qiduizi INT NOT NULL DEFAULT 0,
                    qixingbukao INT NOT NULL DEFAULT 0,
                    quanshuangke INT NOT NULL DEFAULT 0,
                    qingyise INT NOT NULL DEFAULT 0,
                    yisesantongshun INT NOT NULL DEFAULT 0,
                    yisesanjiegao INT NOT NULL DEFAULT 0,
                    quanda INT NOT NULL DEFAULT 0,
                    quanzhong INT NOT NULL DEFAULT 0,
                    quanxiao INT NOT NULL DEFAULT 0,
                    qinglong INT NOT NULL DEFAULT 0,
                    sanseshuanglonghui INT NOT NULL DEFAULT 0,
                    yisesanbugao INT NOT NULL DEFAULT 0,
                    quandaiwu INT NOT NULL DEFAULT 0,
                    santongke INT NOT NULL DEFAULT 0,
                    sananke INT NOT NULL DEFAULT 0,
                    quanbukao INT NOT NULL DEFAULT 0,
                    zuhelong INT NOT NULL DEFAULT 0,
                    dayuwu INT NOT NULL DEFAULT 0,
                    xiaoyuwu INT NOT NULL DEFAULT 0,
                    sanfengke INT NOT NULL DEFAULT 0,
                    hualong INT NOT NULL DEFAULT 0,
                    tuibudao INT NOT NULL DEFAULT 0,
                    sansesantongshun INT NOT NULL DEFAULT 0,
                    sansesanjiegao INT NOT NULL DEFAULT 0,
                    wufanhe INT NOT NULL DEFAULT 0,
                    miaoshouhuichun INT NOT NULL DEFAULT 0,
                    haidilaoyue INT NOT NULL DEFAULT 0,
                    gangshangkaihua INT NOT NULL DEFAULT 0,
                    qiangganghe INT NOT NULL DEFAULT 0,
                    pengpenghe INT NOT NULL DEFAULT 0,
                    hunyise INT NOT NULL DEFAULT 0,
                    sansesanbugao INT NOT NULL DEFAULT 0,
                    wumenqi INT NOT NULL DEFAULT 0,
                    quanqiuren INT NOT NULL DEFAULT 0,
                    shuangangang INT NOT NULL DEFAULT 0,
                    shuangjianke INT NOT NULL DEFAULT 0,
                    quandaiyao INT NOT NULL DEFAULT 0,
                    buqiuren INT NOT NULL DEFAULT 0,
                    shuangminggang INT NOT NULL DEFAULT 0,
                    hejuezhang INT NOT NULL DEFAULT 0,
                    jianke INT NOT NULL DEFAULT 0,
                    quanfengke INT NOT NULL DEFAULT 0,
                    menfengke INT NOT NULL DEFAULT 0,
                    menqianqing INT NOT NULL DEFAULT 0,
                    pinghe INT NOT NULL DEFAULT 0,
                    siguiyi INT NOT NULL DEFAULT 0,
                    shuangtongke INT NOT NULL DEFAULT 0,
                    shuanganke INT NOT NULL DEFAULT 0,
                    angang INT NOT NULL DEFAULT 0,
                    duanyao INT NOT NULL DEFAULT 0,
                    yibangao INT NOT NULL DEFAULT 0,
                    xixiangfeng INT NOT NULL DEFAULT 0,
                    lianliu INT NOT NULL DEFAULT 0,
                    laoshaofu INT NOT NULL DEFAULT 0,
                    yaojiuke INT NOT NULL DEFAULT 0,
                    minggang INT NOT NULL DEFAULT 0,
                    queyimen INT NOT NULL DEFAULT 0,
                    wuzi INT NOT NULL DEFAULT 0,
                    bianzhang INT NOT NULL DEFAULT 0,
                    qianzhang INT NOT NULL DEFAULT 0,
                    dandiaojiang INT NOT NULL DEFAULT 0,
                    zimo INT NOT NULL DEFAULT 0,
                    huapai INT NOT NULL DEFAULT 0,
                    mingangang INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)

            # 创建表qingque_history_stats（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS qingque_history_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    total_games INT NOT NULL DEFAULT 0,
                    total_rounds INT NOT NULL DEFAULT 0,
                    win_count INT NOT NULL DEFAULT 0,
                    self_draw_count INT NOT NULL DEFAULT 0,
                    deal_in_count INT NOT NULL DEFAULT 0,
                    total_fan_score INT NOT NULL DEFAULT 0,
                    total_win_turn INT NOT NULL DEFAULT 0,
                    total_fangchong_score INT NOT NULL DEFAULT 0,
                    first_place_count INT NOT NULL DEFAULT 0,
                    second_place_count INT NOT NULL DEFAULT 0,
                    third_place_count INT NOT NULL DEFAULT 0,
                    fourth_place_count INT NOT NULL DEFAULT 0,
                    fulu_round_count INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)

            # 创建表qingque_fan_stats（青雀麻将番种统计表）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS qingque_fan_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    hepai INT NOT NULL DEFAULT 0,
                    tianhe INT NOT NULL DEFAULT 0,
                    dihe INT NOT NULL DEFAULT 0,
                    lingshangkaihua INT NOT NULL DEFAULT 0,
                    haidilaoyue INT NOT NULL DEFAULT 0,
                    hedilaoyue INT NOT NULL DEFAULT 0,
                    qianggang INT NOT NULL DEFAULT 0,
                    qidui INT NOT NULL DEFAULT 0,
                    menqianqing INT NOT NULL DEFAULT 0,
                    siangang INT NOT NULL DEFAULT 0,
                    sanangang INT NOT NULL DEFAULT 0,
                    shuangangang INT NOT NULL DEFAULT 0,
                    angang INT NOT NULL DEFAULT 0,
                    sigang INT NOT NULL DEFAULT 0,
                    sangang INT NOT NULL DEFAULT 0,
                    shuanggang INT NOT NULL DEFAULT 0,
                    sianke INT NOT NULL DEFAULT 0,
                    sananke INT NOT NULL DEFAULT 0,
                    duiduihe INT NOT NULL DEFAULT 0,
                    shiergui INT NOT NULL DEFAULT 0,
                    bagui INT NOT NULL DEFAULT 0,
                    sandiedui INT NOT NULL DEFAULT 0,
                    erdiedui INT NOT NULL DEFAULT 0,
                    diedui INT NOT NULL DEFAULT 0,
                    ziyise INT NOT NULL DEFAULT 0,
                    dasixi INT NOT NULL DEFAULT 0,
                    xiaosixi INT NOT NULL DEFAULT 0,
                    sixidui INT NOT NULL DEFAULT 0,
                    fengpaisanke INT NOT NULL DEFAULT 0,
                    fengpaiqidui INT NOT NULL DEFAULT 0,
                    fengpailiudui INT NOT NULL DEFAULT 0,
                    fengpaiwudui INT NOT NULL DEFAULT 0,
                    fengpaisidui INT NOT NULL DEFAULT 0,
                    dasanyuan INT NOT NULL DEFAULT 0,
                    xiaosanyuan INT NOT NULL DEFAULT 0,
                    sanyuanliudui INT NOT NULL DEFAULT 0,
                    sanyuandui INT NOT NULL DEFAULT 0,
                    fanpaisike INT NOT NULL DEFAULT 0,
                    fanpaisanke INT NOT NULL DEFAULT 0,
                    fanpaierke INT NOT NULL DEFAULT 0,
                    fanpaike INT NOT NULL DEFAULT 0,
                    fanpaiqidui INT NOT NULL DEFAULT 0,
                    fanpailiudui INT NOT NULL DEFAULT 0,
                    fanpaiwudui INT NOT NULL DEFAULT 0,
                    fanpaisifu INT NOT NULL DEFAULT 0,
                    fanpaisanfu INT NOT NULL DEFAULT 0,
                    fanpaierfu INT NOT NULL DEFAULT 0,
                    fanpai INT NOT NULL DEFAULT 0,
                    qingyaojiu INT NOT NULL DEFAULT 0,
                    hunyaojiu INT NOT NULL DEFAULT 0,
                    qingdaiyao INT NOT NULL DEFAULT 0,
                    hundaiyao INT NOT NULL DEFAULT 0,
                    jiulianbaodeng INT NOT NULL DEFAULT 0,
                    qingyise INT NOT NULL DEFAULT 0,
                    hunyise INT NOT NULL DEFAULT 0,
                    wumenqi INT NOT NULL DEFAULT 0,
                    hunyishu INT NOT NULL DEFAULT 0,
                    ershu INT NOT NULL DEFAULT 0,
                    erju INT NOT NULL DEFAULT 0,
                    sanju INT NOT NULL DEFAULT 0,
                    siju INT NOT NULL DEFAULT 0,
                    lianshu INT NOT NULL DEFAULT 0,
                    jianshu INT NOT NULL DEFAULT 0,
                    jingshu INT NOT NULL DEFAULT 0,
                    yingshu INT NOT NULL DEFAULT 0,
                    mantingfang INT NOT NULL DEFAULT 0,
                    sitongshun INT NOT NULL DEFAULT 0,
                    santongshun INT NOT NULL DEFAULT 0,
                    erbangao INT NOT NULL DEFAULT 0,
                    yibangao INT NOT NULL DEFAULT 0,
                    silianke INT NOT NULL DEFAULT 0,
                    sanlianke INT NOT NULL DEFAULT 0,
                    sibugao INT NOT NULL DEFAULT 0,
                    sanbugao INT NOT NULL DEFAULT 0,
                    silianhuan INT NOT NULL DEFAULT 0,
                    sanlianhuan INT NOT NULL DEFAULT 0,
                    yiqiguantong INT NOT NULL DEFAULT 0,
                    qiliandui INT NOT NULL DEFAULT 0,
                    liuliandui INT NOT NULL DEFAULT 0,
                    wuliandui INT NOT NULL DEFAULT 0,
                    siliandui INT NOT NULL DEFAULT 0,
                    sansetongke INT NOT NULL DEFAULT 0,
                    sansetongshun INT NOT NULL DEFAULT 0,
                    sanseedui INT NOT NULL DEFAULT 0,
                    sansetongdui INT NOT NULL DEFAULT 0,
                    sanselianke INT NOT NULL DEFAULT 0,
                    sanseguantong INT NOT NULL DEFAULT 0,
                    jingtong INT NOT NULL DEFAULT 0,
                    jingtongsandui INT NOT NULL DEFAULT 0,
                    jingtongerdui INT NOT NULL DEFAULT 0,
                    shuanglonghui INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)

            # 创建表 classical_history_stats（古典麻将基础统计表）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS classical_history_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    total_games INT NOT NULL DEFAULT 0,
                    total_rounds INT NOT NULL DEFAULT 0,
                    win_count INT NOT NULL DEFAULT 0,
                    self_draw_count INT NOT NULL DEFAULT 0,
                    deal_in_count INT NOT NULL DEFAULT 0,
                    total_fan_score INT NOT NULL DEFAULT 0,
                    total_win_turn INT NOT NULL DEFAULT 0,
                    total_fangchong_score INT NOT NULL DEFAULT 0,
                    first_place_count INT NOT NULL DEFAULT 0,
                    second_place_count INT NOT NULL DEFAULT 0,
                    third_place_count INT NOT NULL DEFAULT 0,
                    fourth_place_count INT NOT NULL DEFAULT 0,
                    fulu_round_count INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)

            # 创建表 classical_fan_stats（古典麻将番种统计表）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS classical_fan_stats (
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    rule VARCHAR(10) NOT NULL,
                    mode VARCHAR(20) NOT NULL,
                    zimo INT NOT NULL DEFAULT 0,
                    hunyise INT NOT NULL DEFAULT 0,
                    xiaosanyuan INT NOT NULL DEFAULT 0,
                    qingyise INT NOT NULL DEFAULT 0,
                    ziyise INT NOT NULL DEFAULT 0,
                    luanfengheming INT NOT NULL DEFAULT 0,
                    lingshangkaihua INT NOT NULL DEFAULT 0,
                    haidilaoyue INT NOT NULL DEFAULT 0,
                    jinjidoushi INT NOT NULL DEFAULT 0,
                    dasanyuan INT NOT NULL DEFAULT 0,
                    dasixi INT NOT NULL DEFAULT 0,
                    xiaosixi INT NOT NULL DEFAULT 0,
                    tianhe INT NOT NULL DEFAULT 0,
                    dihe INT NOT NULL DEFAULT 0,
                    jiulianbaodeng INT NOT NULL DEFAULT 0,
                    guoshiwushuang INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, rule, mode)
                );
            """)

            # users 表迁移：is_mcrpl_qualified、sponsor_expires_at（赞助到期时间，NULL 表示非赞助或已过期）
            for col_name, col_def in [
                ("is_mcrpl_qualified", "BOOLEAN NOT NULL DEFAULT FALSE"),
            ]:
                cursor.execute(f"SAVEPOINT sp_add_{col_name};")
                try:
                    cursor.execute(f"ALTER TABLE users ADD COLUMN {col_name} {col_def};")
                except Error as e:
                    if getattr(e, "pgcode", None) == "42701":
                        cursor.execute(f"ROLLBACK TO SAVEPOINT sp_add_{col_name};")
                    else:
                        raise

            cursor.execute("SAVEPOINT sp_add_sponsor_expires_at;")
            try:
                cursor.execute("ALTER TABLE users ADD COLUMN sponsor_expires_at TIMESTAMP NULL;")
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_add_sponsor_expires_at;")
                else:
                    raise

            # users 表迁移：账号封禁字段
            for col_name, col_def in [
                ("ban_expires_at", "TIMESTAMP NULL"),
                ("ban_type", "VARCHAR(32) NULL"),
                ("ban_reason", "TEXT NULL"),
            ]:
                cursor.execute(f"SAVEPOINT sp_add_{col_name};")
                try:
                    cursor.execute(f"ALTER TABLE users ADD COLUMN {col_name} {col_def};")
                except Error as e:
                    if getattr(e, "pgcode", None) == "42701":
                        cursor.execute(f"ROLLBACK TO SAVEPOINT sp_add_{col_name};")
                    else:
                        raise

            # 旧版 is_sponsor 布尔字段迁移为 sponsor_expires_at 后删除
            cursor.execute("""
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'is_sponsor';
            """)
            if cursor.fetchone():
                cursor.execute("""
                    UPDATE users
                    SET sponsor_expires_at = TIMESTAMP '2099-12-31 23:59:59'
                    WHERE is_sponsor = TRUE AND sponsor_expires_at IS NULL;
                """)
                cursor.execute("SAVEPOINT sp_drop_is_sponsor;")
                try:
                    cursor.execute("ALTER TABLE users DROP COLUMN is_sponsor;")
                except Error as e:
                    if getattr(e, "pgcode", None) != "42703":
                        cursor.execute("ROLLBACK TO SAVEPOINT sp_drop_is_sponsor;")
                        raise
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_drop_is_sponsor;")

            # 创建通用段位数据表 rank_data
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS rank_data (
                    user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
                    guobiao_rank VARCHAR(10) NOT NULL DEFAULT '10级',
                    guobiao_score DOUBLE PRECISION NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)
            # rank_data 表迁移：guobiao_score 改为浮点，支持小数 PT
            cursor.execute("SAVEPOINT sp_rank_score_float;")
            try:
                cursor.execute("""
                    ALTER TABLE rank_data
                    ALTER COLUMN guobiao_score TYPE DOUBLE PRECISION
                    USING guobiao_score::DOUBLE PRECISION;
                """)
                cursor.execute("""
                    ALTER TABLE rank_data
                    ALTER COLUMN guobiao_score SET DEFAULT 0;
                """)
            except Error:
                cursor.execute("ROLLBACK TO SAVEPOINT sp_rank_score_float;")

            # 创建表user_settings（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS user_settings (
                    user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
                    title_id INT DEFAULT 1,
                    profile_image_id INT DEFAULT 1,
                    character_id INT DEFAULT 1,
                    voice_id INT DEFAULT 1,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 创建表user_config（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS user_config (
                    user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
                    volume INT NOT NULL DEFAULT 100,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 关注关系表（单向关注，A 关注 B 不要求 B 关注 A）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS user_friends (
                    user_id        BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    friend_user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    created_at     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, friend_user_id)
                );
            """)
            cursor.execute("CREATE INDEX IF NOT EXISTS idx_user_friends_user ON user_friends(user_id);")

            # 双向好友关系表：user_id_small / user_id_large 组成唯一好友边
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS user_friendships (
                    user_id_small BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    user_id_large BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    created_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id_small, user_id_large),
                    CHECK (user_id_small < user_id_large)
                );
            """)
            cursor.execute("CREATE INDEX IF NOT EXISTS idx_user_friendships_small ON user_friendships(user_id_small);")
            cursor.execute("CREATE INDEX IF NOT EXISTS idx_user_friendships_large ON user_friendships(user_id_large);")

            # 好友申请表：同一发送者到接收者只保留一个待处理申请
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS user_friend_requests (
                    from_user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    to_user_id   BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    created_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (from_user_id, to_user_id),
                    CHECK (from_user_id <> to_user_id)
                );
            """)
            cursor.execute("CREATE INDEX IF NOT EXISTS idx_user_friend_requests_to ON user_friend_requests(to_user_id);")

            # 用户登录 IP 记录（每用户最多保留最近 20 条，由应用层裁剪）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS user_login_ips (
                    id         BIGSERIAL PRIMARY KEY,
                    user_id    BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    ip_address VARCHAR(45) NOT NULL,
                    logged_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)
            cursor.execute("""
                CREATE INDEX IF NOT EXISTS idx_user_login_ips_user_time
                ON user_login_ips(user_id, logged_at DESC);
            """)

            # IP 封禁表（每个 IP 一条记录，upsert 更新）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS ip_bans (
                    id             BIGSERIAL PRIMARY KEY,
                    ip_address     VARCHAR(45) NOT NULL UNIQUE,
                    ban_expires_at TIMESTAMP NULL,
                    ban_reason     TEXT NULL,
                    created_by     BIGINT NULL REFERENCES users(user_id) ON DELETE SET NULL,
                    created_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)


            # 创建游客用户序列（9000000-9900000）
            cursor.execute("CREATE SEQUENCE IF NOT EXISTS tourist_user_id_seq START 9000000 INCREMENT 1 MAXVALUE 9900000 CYCLE;")

            # 创建注册用户序列（10000001+）
            cursor.execute("CREATE SEQUENCE IF NOT EXISTS registered_user_id_seq START 10000001 INCREMENT 1;")

            # 设置用户表的默认序列为注册用户序列
            cursor.execute("ALTER TABLE users ALTER COLUMN user_id SET DEFAULT nextval('registered_user_id_seq');")

            logger.info('用户ID序列初始化完成')
            print('用户ID序列初始化完成')

            # 迁移：各规则 history_stats 增加副露局数字段
            for table_name in (
                "guobiao_history_stats",
                "qingque_history_stats",
                "riichi_history_stats",
                "classical_history_stats",
            ):
                cursor.execute(f"SAVEPOINT sp_fulu_{table_name};")
                try:
                    cursor.execute(
                        f"ALTER TABLE {table_name} "
                        f"ADD COLUMN fulu_round_count INT NOT NULL DEFAULT 0;"
                    )
                except Error as e:
                    if getattr(e, "pgcode", None) == "42701":
                        cursor.execute(f"ROLLBACK TO SAVEPOINT sp_fulu_{table_name};")
                    else:
                        raise

            # 迁移：国标 history_stats 增加错和次数字段
            cursor.execute("SAVEPOINT sp_cuohe_guobiao_history_stats;")
            try:
                cursor.execute(
                    "ALTER TABLE guobiao_history_stats "
                    "ADD COLUMN cuohe_count INT NOT NULL DEFAULT 0;"
                )
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_cuohe_guobiao_history_stats;")
                else:
                    raise

            # 迁移：国标 history_stats 增加累计小局净得分（局均点分子）
            cursor.execute("SAVEPOINT sp_round_score_guobiao_history_stats;")
            added_round_score_column = False
            try:
                cursor.execute(
                    "ALTER TABLE guobiao_history_stats "
                    "ADD COLUMN total_round_score INT NOT NULL DEFAULT 0;"
                )
                added_round_score_column = True
            except Error as e:
                if getattr(e, "pgcode", None) == "42701":
                    cursor.execute("ROLLBACK TO SAVEPOINT sp_round_score_guobiao_history_stats;")
                else:
                    raise

            # ===== 每日统计相关表 =====
            # 每玩家每局原始指标：供每天 4 点聚合，避免重解牌谱 JSON
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_player_metrics (
                    id BIGSERIAL PRIMARY KEY,
                    game_id VARCHAR(16) NOT NULL,
                    user_id BIGINT NOT NULL,
                    username VARCHAR(255) NOT NULL,
                    rule VARCHAR(10) NOT NULL,
                    sub_rule VARCHAR(32) NULL,
                    room_type VARCHAR(16) NULL,
                    match_tier VARCHAR(24) NULL,
                    event_id VARCHAR(64) NULL,
                    game_type VARCHAR(16) NULL,
                    match_type VARCHAR(24) NULL,
                    score INT NOT NULL DEFAULT 0,
                    rank INT NOT NULL,
                    total_rounds INT NOT NULL DEFAULT 0,
                    win_count INT NOT NULL DEFAULT 0,
                    self_draw_count INT NOT NULL DEFAULT 0,
                    deal_in_count INT NOT NULL DEFAULT 0,
                    total_fan_score INT NOT NULL DEFAULT 0,
                    total_win_turn INT NOT NULL DEFAULT 0,
                    total_fangchong_score INT NOT NULL DEFAULT 0,
                    first_place_count INT NOT NULL DEFAULT 0,
                    second_place_count INT NOT NULL DEFAULT 0,
                    third_place_count INT NOT NULL DEFAULT 0,
                    fourth_place_count INT NOT NULL DEFAULT 0,
                    fulu_round_count INT NOT NULL DEFAULT 0,
                    cuohe_count INT NOT NULL DEFAULT 0,
                    total_round_score INT NOT NULL DEFAULT 0,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
            """)
            cursor.execute(
                "CREATE INDEX IF NOT EXISTS idx_game_player_metrics_created_at "
                "ON game_player_metrics (created_at);"
            )
            cursor.execute(
                "CREATE INDEX IF NOT EXISTS idx_game_player_metrics_scene "
                "ON game_player_metrics (room_type, match_tier, event_id, game_type);"
            )

            # 每日全站统计：对局数 / 用户量 / 最大在线
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS daily_stats (
                    stat_date DATE PRIMARY KEY,
                    game_count INT NOT NULL DEFAULT 0,
                    active_users INT NOT NULL DEFAULT 0,
                    max_online INT NOT NULL DEFAULT 0,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 当日在线峰值缓存：每 60s 由采样器 UPSERT(GREATEST) 持久化，
            # 服务端在凌晨 3-4 点关闭、5 点重启后仍可据此正确重写当日 daily_stats。
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS daily_online_cache (
                    stat_date DATE PRIMARY KEY,
                    max_online INT NOT NULL DEFAULT 0,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 每日场次统计：与 guobiao_history_stats 相同的指标列，按 (日期, 场次, 规则, 局制) 聚合
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS scene_daily_stats (
                    id BIGSERIAL PRIMARY KEY,
                    stat_date DATE NOT NULL,
                    room_type VARCHAR(16) NULL,
                    match_tier VARCHAR(24) NULL,
                    event_id VARCHAR(64) NULL,
                    rule VARCHAR(10) NOT NULL,
                    game_type VARCHAR(16) NULL,
                    total_games INT NOT NULL DEFAULT 0,
                    total_rounds INT NOT NULL DEFAULT 0,
                    win_count INT NOT NULL DEFAULT 0,
                    self_draw_count INT NOT NULL DEFAULT 0,
                    deal_in_count INT NOT NULL DEFAULT 0,
                    total_fan_score INT NOT NULL DEFAULT 0,
                    total_win_turn INT NOT NULL DEFAULT 0,
                    total_fangchong_score INT NOT NULL DEFAULT 0,
                    first_place_count INT NOT NULL DEFAULT 0,
                    second_place_count INT NOT NULL DEFAULT 0,
                    third_place_count INT NOT NULL DEFAULT 0,
                    fourth_place_count INT NOT NULL DEFAULT 0,
                    fulu_round_count INT NOT NULL DEFAULT 0,
                    cuohe_count INT NOT NULL DEFAULT 0,
                    total_round_score INT NOT NULL DEFAULT 0,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE (stat_date, room_type, match_tier, event_id, rule, game_type)
                );
            """)

            conn.commit() # 提交
            logger.info('数据表初始化成功')
            print('数据表初始化成功')
            self._get_pool()

            # 清理远古数据：删除 room_type 不属于 match/custom/events 的对局记录
            # （早期数据可能 room_type 为 NULL 或非法值），重建统计前先清掉避免污染。
            self._cleanup_legacy_room_type_records()

            if added_round_score_column or self._guobiao_history_stats_needs_rebuild():
                from .guobiao.backfill_history_stats import backfill_guobiao_history_stats
                backfill_guobiao_history_stats(self)
                self._mark_guobiao_history_stats_rebuilt()

        except Exception as e:
            logger.error(f'数据表初始化失败: {e}', exc_info=True)
            print(f'数据表初始化失败: {e}')
            if conn:
                conn.rollback()
        finally:
            if conn:
                cursor.close()
                conn.close()

    def _guobiao_history_stats_needs_rebuild(self) -> bool:
        """是否需要重建 guobiao_history_stats + fan_stats（按 match_type 分开排位/自定义 + 局均点赋值）。

        v3：total_win_turn（和巡）改为由 action_ticks 推理 seat 流转重建，故独立 marker。
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                """CREATE TABLE IF NOT EXISTS app_meta (
                       meta_key VARCHAR(100) PRIMARY KEY,
                       meta_value TEXT,
                       updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                   )"""
            )
            cursor.execute(
                "SELECT meta_value FROM app_meta WHERE meta_key = %s",
                ("guobiao_history_stats_rebuilt_v3",),
            )
            row = cursor.fetchone()
            conn.commit()
            return row is None or row[0] != "1"
        except Exception:
            if conn:
                conn.rollback()
            return True
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def _cleanup_legacy_room_type_records(self) -> None:
        """清理远古数据：删除 room_type 不属于 match/custom/events 的对局。

        删除 game_records（级联到 game_player_records），并清理 game_player_metrics
        中对应 game_id 的残留。每次 init 执行一次，无遗留时无副作用。
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute("""
                SELECT DISTINCT game_id FROM game_player_records
                WHERE room_type IS NULL OR room_type NOT IN ('match','custom','events')
            """)
            legacy_game_ids = [r[0] for r in cursor.fetchall()]
            if not legacy_game_ids:
                return
            logger.info("清理远古 room_type 数据：game_id 数=%d", len(legacy_game_ids))
            # game_player_metrics 无 FK，需显式删
            cursor.execute(
                "DELETE FROM game_player_metrics WHERE game_id = ANY(%s::varchar[])",
                (legacy_game_ids,),
            )
            # 删 game_records 级联到 game_player_records
            cursor.execute(
                "DELETE FROM game_records WHERE game_id = ANY(%s::varchar[])",
                (legacy_game_ids,),
            )
            conn.commit()
        except Exception as e:
            logger.error(f"清理远古 room_type 数据失败: {e}", exc_info=True)
            if conn:
                conn.rollback()
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def _mark_guobiao_history_stats_rebuilt(self) -> None:
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                """CREATE TABLE IF NOT EXISTS app_meta (
                       meta_key VARCHAR(100) PRIMARY KEY,
                       meta_value TEXT,
                       updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                   )"""
            )
            cursor.execute(
                """
                INSERT INTO app_meta (meta_key, meta_value)
                VALUES (%s, %s)
                ON CONFLICT (meta_key) DO UPDATE SET meta_value = EXCLUDED.meta_value,
                                                     updated_at = CURRENT_TIMESTAMP
                """, ("guobiao_history_stats_rebuilt_v3", "1")
            )
            conn.commit()
        except Exception as e:
            logger.error(f"标记 guobiao_history_stats_rebuilt 失败: {e}", exc_info=True)
            if conn:
                conn.rollback()
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def _guobiao_round_score_needs_backfill(self) -> bool:
        """列已存在但尚未完成牌谱回溯时返回 True（仅执行一次）。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute("""
                SELECT 1
                FROM information_schema.columns
                WHERE table_name = 'guobiao_history_stats'
                  AND column_name = 'total_round_score'
                LIMIT 1
            """)
            if cursor.fetchone() is None:
                return False
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS app_meta (
                    meta_key VARCHAR(64) PRIMARY KEY,
                    meta_value TEXT NOT NULL,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            """)
            cursor.execute(
                "SELECT meta_value FROM app_meta WHERE meta_key = %s",
                ("guobiao_round_score_backfilled",),
            )
            row = cursor.fetchone()
            if row and row[0] == "1":
                return False
            cursor.execute("""
                SELECT EXISTS (
                    SELECT 1 FROM game_player_records
                    WHERE rule = 'guobiao' AND user_id > 10000000
                )
            """)
            return bool(cursor.fetchone()[0])
        except Exception as e:
            logger.warning("检测国标局均点回溯需求失败: %s", e)
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def _mark_guobiao_round_score_backfilled(self) -> None:
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS app_meta (
                    meta_key VARCHAR(64) PRIMARY KEY,
                    meta_value TEXT NOT NULL,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            """)
            cursor.execute("""
                INSERT INTO app_meta (meta_key, meta_value)
                VALUES (%s, %s)
                ON CONFLICT (meta_key) DO UPDATE SET
                    meta_value = EXCLUDED.meta_value,
                    updated_at = CURRENT_TIMESTAMP
            """, ("guobiao_round_score_backfilled", "1"))
            conn.commit()
        except Exception as e:
            logger.warning("标记国标局均点回溯完成失败: %s", e)
            if conn:
                conn.rollback()
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    # 获取用户名
    def get_user_by_username(self, username: str) -> Optional[Dict[str, Any]]:
        """
        根据用户名获取用户信息
        Args:
            username: 用户名
        Returns:
            用户信息字典，如果不存在则返回 None
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                "SELECT * FROM users WHERE username = %s",
                (username,)
            )
            user = cursor.fetchone()
            # 如果用户存在，返回用户信息
            if user:
                return dict[str, Any](user)
            return None
        except Error as e:
            logger.error(f'查询用户失败: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def get_user_by_user_id(self, user_id: int) -> Optional[Dict[str, Any]]:
        """
        根据用户ID获取用户信息
        Args:
            user_id: 用户ID
        Returns:
            用户信息字典，如果不存在则返回 None
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                "SELECT * FROM users WHERE user_id = %s",
                (user_id,)
            )
            user = cursor.fetchone()
            
            if user:
                return dict(user)
            return None
        except Error as e:
            logger.error(f'查询用户失败: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def record_user_login_ip(self, user_id: int, ip_address: str) -> bool:
        """记录一次成功登录的 IP，并裁剪为每用户最近 LOGIN_IP_KEEP_LIMIT 条。"""
        ip = (ip_address or "").strip()[:45] or "unknown"
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                "INSERT INTO user_login_ips (user_id, ip_address) VALUES (%s, %s)",
                (user_id, ip),
            )
            cursor.execute(
                """
                DELETE FROM user_login_ips
                WHERE user_id = %s
                  AND id NOT IN (
                    SELECT id FROM user_login_ips
                    WHERE user_id = %s
                    ORDER BY logged_at DESC, id DESC
                    LIMIT %s
                  )
                """,
                (user_id, user_id, self.LOGIN_IP_KEEP_LIMIT),
            )
            conn.commit()
            return True
        except Error as e:
            logger.error(f'记录登录 IP 失败 user_id={user_id}: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def get_recent_login_ips(self, user_id: int, limit: int = 20) -> List[Dict[str, Any]]:
        """获取用户最近若干次登录 IP。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                """
                SELECT ip_address, logged_at
                FROM user_login_ips
                WHERE user_id = %s
                ORDER BY logged_at DESC, id DESC
                LIMIT %s
                """,
                (user_id, limit),
            )
            rows = cursor.fetchall()
            return [
                {
                    "ip_address": row["ip_address"],
                    "logged_at": str(row["logged_at"]),
                }
                for row in rows
            ]
        except Error as e:
            logger.error(f'查询登录 IP 失败 user_id={user_id}: {e}')
            if conn:
                conn.rollback()
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def create_user(self, username: str, password: str, is_tourist: bool = False) -> Optional[int]:
        """
        创建新用户（密码会自动哈希存储）

        Args:
            username: 用户名
            password: 明文密码
            is_tourist: 是否为游客账户

        Returns:
            创建成功返回 user_id，失败返回 None
        """
        conn = None
        try:
            # 对密码进行哈希处理（游客账户密码为空）
            password_hash = self._hash_password(password) if not is_tourist else ""

            conn = self._get_connection()
            cursor = conn.cursor()

            if is_tourist:
                # 游客用户：使用游客序列手动设置ID
                cursor.execute("SELECT nextval('tourist_user_id_seq')")
                user_id = cursor.fetchone()[0]
                cursor.execute(
                    "INSERT INTO users (user_id, username, password, is_tourist) VALUES (%s, %s, %s, %s)",
                    (user_id, username, password_hash, is_tourist)
                )
            else:
                # 注册用户：使用自动递增序列
                cursor.execute(
                    "INSERT INTO users (username, password, is_tourist) VALUES (%s, %s, %s) RETURNING user_id",
                    (username, password_hash, is_tourist)
                )
                user_id = cursor.fetchone()[0]

            # 初始化用户的 user_settings 记录
            cursor.execute("""
                INSERT INTO user_settings (user_id, title_id, profile_image_id, character_id, voice_id)
                VALUES (%s, 1, 1, 1, 1)
            """, (user_id,))

            # 初始化用户的 user_config 记录
            cursor.execute("""
                INSERT INTO user_config (user_id, volume)
                VALUES (%s, 100)
            """, (user_id,))

            # 初始化用户的 rank_data 记录
            cursor.execute("""
                INSERT INTO rank_data (user_id, guobiao_rank, guobiao_score)
                VALUES (%s, '10级', 0)
            """, (user_id,))

            conn.commit()
            account_type = "游客账户" if is_tourist else "用户"
            logger.info(f'{account_type} {username} 创建成功，user_id: {user_id}')
            return user_id
        except Error as e:
            logger.error(f'创建用户失败: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def delete_tourist_user(self, user_id: int, username: str) -> bool:
        """
        删除游客账户及其相关数据（由于外键约束，会自动删除user_settings和user_config）
        只有游客账户（is_tourist = True）且用户名包含"游客"才允许删除

        Args:
            user_id: 用户ID
            username: 用户名

        Returns:
            删除成功返回 True，失败返回 False
        """
        # 检查用户名是否包含"游客"
        if "游客" not in username:
            logger.warning(f'拒绝删除：用户名不包含"游客" user_id={user_id}, username={username}')
            return False

        # 验证游客ID范围（9000000-9900000）
        if not (9000000 <= user_id <= 9900000):
            logger.warning(f'拒绝删除：用户ID不在游客ID范围内 user_id={user_id}, username={username}')
            return False

        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()

            # 检查用户是否存在且为游客账户
            cursor.execute("SELECT is_tourist, password FROM users WHERE user_id = %s", (user_id,))
            user = cursor.fetchone()

            # 检查用户是否存在
            if user is None:
                logger.warning(f'用户不存在 user_id={user_id}')
                return False

            # 检查用户是否游客
            is_tourist, stored_password = user
            if not is_tourist:  # is_tourist 为 False
                logger.warning(f'拒绝删除：用户不是游客账户 user_id={user_id}, username={username}')
                return False

            # 如果密码哈希为空字符串，则可以删除
            if stored_password and not self.verify_password("", stored_password):
                logger.warning(f'拒绝删除：游客账户密码不为空 user_id={user_id}, username={username}')
                return False
            
            # 检查是否有牌谱记录，如果有则阻止删除（保留 user_id 用于牌谱查询）
            cursor.execute("SELECT COUNT(*) FROM game_player_records WHERE user_id = %s", (user_id,))
            record_count = cursor.fetchone()[0]
            
            if record_count > 0:
                logger.info(f'用户 {user_id} 有 {record_count} 条牌谱记录，保留用户记录以维护牌谱完整性')
                return False
            
            # 执行删除
            cursor.execute("DELETE FROM users WHERE user_id = %s", (user_id,))
            conn.commit()
            deleted_count = cursor.rowcount

            if deleted_count > 0:
                logger.info(f'游客账户删除成功 user_id={user_id}, username={username}')
                return True
            else:
                logger.warning(f'游客账户删除失败 user_id={user_id}, username={username}')
                return False
        except Error as e:
            logger.error(f'删除游客账户失败: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def _hash_password(self, password: str) -> str:
        """
        使用 PBKDF2 + SHA256 和随机盐对密码进行哈希
        返回格式：salt_hex:hash_hex
        """
        salt = secrets.token_bytes(16)
        pwd_hash = hashlib.pbkdf2_hmac(
            'sha256',
            password.encode('utf-8'),
            salt,
            100_000
        )
        return f"{salt.hex()}:{pwd_hash.hex()}"
    
    @staticmethod
    def format_ban_expires_at(expires_at: Optional[datetime]) -> str:
        """将封禁到期时间格式化为展示用字符串。"""
        if expires_at is None:
            return '永久'
        if isinstance(expires_at, str):
            try:
                expires_at = datetime.fromisoformat(expires_at.replace('Z', '+00:00'))
            except ValueError:
                return expires_at
        if expires_at.year >= 2099:
            return '永久'
        if expires_at.tzinfo is not None:
            local = expires_at.astimezone()
        else:
            local = expires_at
        return local.strftime('%Y-%m-%d %H:%M:%S')

    @staticmethod
    def is_login_ban_active(user: Dict[str, Any], now: Optional[datetime] = None) -> bool:
        """判断用户当前是否处于禁止登录的封禁状态。"""
        ban_type = user.get('ban_type')
        if not ban_type or ban_type not in LOGIN_BAN_TYPES:
            return False
        ban_expires_at = user.get('ban_expires_at')
        if ban_expires_at is None:
            return True
        if now is None:
            now = datetime.now(timezone.utc) if getattr(ban_expires_at, 'tzinfo', None) else datetime.now()
        if isinstance(ban_expires_at, str):
            try:
                ban_expires_at = datetime.fromisoformat(ban_expires_at.replace('Z', '+00:00'))
            except ValueError:
                return True
        expires = ban_expires_at
        if getattr(expires, 'tzinfo', None) is not None and now.tzinfo is None:
            now = now.replace(tzinfo=timezone.utc)
        elif getattr(expires, 'tzinfo', None) is None and now.tzinfo is not None:
            expires = expires.replace(tzinfo=now.tzinfo)
        return expires > now

    @classmethod
    def build_login_ban_message(cls, user: Dict[str, Any]) -> str:
        """生成登录被拒时的封禁提示文案。"""
        ban_reason = (user.get('ban_reason') or '无').strip() or '无'
        expires_text = cls.format_ban_expires_at(user.get('ban_expires_at'))
        if expires_text == '永久':
            return (
                f'您的账户已被永久封禁 封禁原因：{ban_reason} '
                f'请联系qq{BAN_CONTACT_QQ} 解封'
            )
        return (
            f'您的账户已经被封禁至{expires_text} 封禁原因：{ban_reason} '
            f'请联系qq{BAN_CONTACT_QQ} 解封'
        )

    @staticmethod
    def is_ban_expired(ban_expires_at: Any, now: Optional[datetime] = None) -> bool:
        """封禁是否已过期（ban_expires_at 为 NULL 表示永久有效）。"""
        if ban_expires_at is None:
            return False
        if now is None:
            now = datetime.now(timezone.utc) if getattr(ban_expires_at, 'tzinfo', None) else datetime.now()
        if isinstance(ban_expires_at, str):
            try:
                ban_expires_at = datetime.fromisoformat(ban_expires_at.replace('Z', '+00:00'))
            except ValueError:
                return False
        expires = ban_expires_at
        if getattr(expires, 'tzinfo', None) is not None and now.tzinfo is None:
            now = now.replace(tzinfo=timezone.utc)
        elif getattr(expires, 'tzinfo', None) is None and now.tzinfo is not None:
            expires = expires.replace(tzinfo=now.tzinfo)
        return expires <= now

    @classmethod
    def build_ip_ban_message(cls, ban: Dict[str, Any]) -> str:
        """生成 IP 被封禁时的登录提示文案。"""
        ban_reason = (ban.get('ban_reason') or '无').strip() or '无'
        expires_text = cls.format_ban_expires_at(ban.get('ban_expires_at'))
        if expires_text == '永久':
            return (
                f'您的 IP 已被永久封禁 封禁原因：{ban_reason} '
                f'请联系qq{BAN_CONTACT_QQ} 解封'
            )
        return (
            f'您的 IP 已被封禁至{expires_text} 封禁原因：{ban_reason} '
            f'请联系qq{BAN_CONTACT_QQ} 解封'
        )

    def get_active_ip_ban(self, ip_address: str) -> Optional[Dict[str, Any]]:
        """查询 IP 当前生效的封禁记录。"""
        ip = (ip_address or "").strip()[:45]
        if not ip or ip == "unknown":
            return None
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                "SELECT * FROM ip_bans WHERE ip_address = %s",
                (ip,),
            )
            row = cursor.fetchone()
            if not row:
                return None
            ban = dict(row)
            if self.is_ban_expired(ban.get('ban_expires_at')):
                return None
            return ban
        except Error as e:
            logger.error(f'查询 IP 封禁失败 ip={ip}: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def upsert_ip_ban(
        self,
        ip_address: str,
        ban_expires_at: Optional[datetime],
        ban_reason: Optional[str],
        created_by: Optional[int] = None,
    ) -> Optional[Dict[str, Any]]:
        """新增或更新 IP 封禁。"""
        ip = (ip_address or "").strip()[:45]
        if not ip:
            return None
        reason = (ban_reason or "").strip() or None
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                """
                INSERT INTO ip_bans (ip_address, ban_expires_at, ban_reason, created_by, updated_at)
                VALUES (%s, %s, %s, %s, CURRENT_TIMESTAMP)
                ON CONFLICT (ip_address) DO UPDATE SET
                    ban_expires_at = EXCLUDED.ban_expires_at,
                    ban_reason = EXCLUDED.ban_reason,
                    created_by = EXCLUDED.created_by,
                    updated_at = CURRENT_TIMESTAMP
                RETURNING *
                """,
                (ip, ban_expires_at, reason, created_by),
            )
            row = cursor.fetchone()
            conn.commit()
            return dict(row) if row else None
        except Error as e:
            logger.error(f'写入 IP 封禁失败 ip={ip}: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def delete_ip_ban(self, ip_address: str) -> bool:
        """解除 IP 封禁。"""
        ip = (ip_address or "").strip()[:45]
        if not ip:
            return False
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute("DELETE FROM ip_bans WHERE ip_address = %s", (ip,))
            conn.commit()
            return cursor.rowcount > 0
        except Error as e:
            logger.error(f'删除 IP 封禁失败 ip={ip}: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def list_ip_bans(self, limit: int = 50, offset: int = 0) -> List[Dict[str, Any]]:
        """分页列出 IP 封禁记录。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                """
                SELECT id, ip_address, ban_expires_at, ban_reason, created_by, created_at, updated_at
                FROM ip_bans
                ORDER BY updated_at DESC, id DESC
                LIMIT %s OFFSET %s
                """,
                (limit, offset),
            )
            return [dict(row) for row in cursor.fetchall()]
        except Error as e:
            logger.error(f'列出 IP 封禁失败: {e}')
            if conn:
                conn.rollback()
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def verify_password(self, password: str, stored_hash: str) -> bool:
        """
        验证密码是否匹配存储的哈希值
        
        Args:
            password: 待验证的明文密码
            stored_hash: 存储的密码哈希值（格式：salt:hash）
            
        Returns:
            密码匹配返回 True，否则返回 False
        """
        if not stored_hash:
            return False
        # 获取 盐值:密码哈希
        try:
            salt_hex, stored_hash_hex = stored_hash.split(':', 1)
            salt = bytes.fromhex(salt_hex)
        except ValueError:
            logger.warning('存储的密码哈希格式不正确')
            return False
        # 计算密码哈希
        computed_hash = hashlib.pbkdf2_hmac(
            'sha256',
            password.encode('utf-8'),
            salt,
            100_000
        ).hex()

        # 比较计算哈希与存储哈希
        return secrets.compare_digest(computed_hash, stored_hash_hex)
    
    def get_record_list(self, user_id: int, limit: int = 20, offset: int = 0) -> List[Dict[str, Any]]:
        """
        获取指定用户的最近 N 局游戏记录元数据（不含完整牌谱），按 created_at 倒序分页。
        
        Args:
            user_id: 用户ID
            limit: 返回游戏数量限制，默认20
            offset: 跳过的局数，用于滚动加载更多
        
        Returns:
            游戏记录列表，每个记录包含：
            - game_id: 游戏ID（字符串）
            - rule: 规则类型
            - created_at: 创建时间
            - players: 该游戏的4个玩家信息列表（按排名排序）
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            
            cursor.execute("""
                SELECT gpr.game_id, gr.created_at
                FROM game_player_records gpr
                INNER JOIN game_records gr ON gpr.game_id = gr.game_id
                WHERE gpr.user_id = %s
                ORDER BY gr.created_at DESC, gpr.game_id DESC
                LIMIT %s OFFSET %s
            """, (user_id, limit, offset))
            
            id_rows = cursor.fetchall()
            if not id_rows:
                logger.info(f'用户 {user_id} 在 offset={offset} 处没有更多游戏记录')
                return []
            
            game_ids = [row['game_id'] for row in id_rows]
            
            placeholders = ','.join(['%s'] * len(game_ids))
            cursor.execute(f"""
                SELECT 
                    gpr.game_id,
                    gpr.user_id,
                    gpr.username,
                    gpr.score,
                    gpr.rank,
                    gpr.original_player_index,
                    gpr.rule,
                    gpr.sub_rule,
                    gpr.match_type,
                    gpr.room_type,
                    gpr.title_used,
                    gpr.character_used,
                    gpr.profile_used,
                    gpr.voice_used,
                    gr.created_at
                FROM game_player_records gpr
                INNER JOIN game_records gr ON gpr.game_id = gr.game_id
                WHERE gpr.game_id IN ({placeholders})
                ORDER BY gpr.rank, gpr.original_player_index NULLS LAST, gpr.score DESC
            """, game_ids)
            
            games_dict = {}
            
            for row in cursor.fetchall():
                game_id = row['game_id']
                
                if game_id not in games_dict:
                    games_dict[game_id] = {
                        'game_id': game_id,
                        'created_at': str(row['created_at']),
                        'rule': row['rule'],
                        'sub_rule': row.get('sub_rule'),
                        'match_type': row.get('match_type'),
                        'room_type': row.get('room_type'),
                        'players': []
                    }
                
                games_dict[game_id]['players'].append({
                    'user_id': row['user_id'],
                    'username': row['username'],
                    'score': row['score'],
                    'rank': row['rank'],
                    'original_player_index': row.get('original_player_index'),
                    'title_used': row.get('title_used'),
                    'character_used': row.get('character_used'),
                    'profile_used': row.get('profile_used'),
                    'voice_used': row.get('voice_used')
                })
            
            records = [games_dict[gid] for gid in game_ids if gid in games_dict]
            
            logger.info(f'获取用户 {user_id} 的 {len(records)} 局游戏记录元数据 (offset={offset}, limit={limit})')
            return records
            
        except Error as e:
            logger.error(f'获取游戏记录列表失败: {e}', exc_info=True)
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def get_record_by_id(self, game_id: str) -> Optional[Dict[str, Any]]:
        """
        根据 game_id 获取完整牌谱记录和玩家信息
        
        Args:
            game_id: 游戏ID（字符串）
        
        Returns:
            包含完整牌谱和玩家信息的字典，找不到返回 None
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            
            cursor.execute("""
                SELECT game_id, record, created_at
                FROM game_records
                WHERE game_id = %s
            """, (game_id.strip(),))
            
            game_row = cursor.fetchone()
            if not game_row:
                return None
            
            record_data = game_row['record']
            if isinstance(record_data, str):
                record_data = json.loads(record_data)

            game_title = record_data.get('game_title') or {}
            rule = game_title.get('rule')
            sub_rule = game_title.get('sub_rule')
            room_type = game_title.get('room_type')
            
            cursor.execute("""
                SELECT user_id, username, score, rank, original_player_index, rule, sub_rule, room_type, title_used, character_used, profile_used, voice_used
                FROM game_player_records
                WHERE game_id = %s
                ORDER BY rank, original_player_index NULLS LAST, score DESC
            """, (game_id.strip(),))
            
            players = []
            for row in cursor.fetchall():
                if rule is None:
                    rule = row['rule']
                if sub_rule is None:
                    sub_rule = row.get('sub_rule')
                if room_type is None:
                    room_type = row.get('room_type')
                players.append({
                    'user_id': row['user_id'],
                    'username': row['username'],
                    'score': row['score'],
                    'rank': row['rank'],
                    'original_player_index': row.get('original_player_index'),
                    'title_used': row.get('title_used'),
                    'character_used': row.get('character_used'),
                    'profile_used': row.get('profile_used'),
                    'voice_used': row.get('voice_used')
                })
            
            return {
                'game_id': game_row['game_id'],
                'rule': rule or '',
                'sub_rule': sub_rule,
                'room_type': room_type,
                'record': record_data,
                'created_at': str(game_row['created_at']),
                'players': players
            }
            
        except Error as e:
            logger.error(f'获取牌谱记录失败 (game_id={game_id}): {e}', exc_info=True)
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def get_player_stats(self, user_id: int) -> Dict[str, Any]:
        """
        获取指定用户的所有统计数据（包括各规则的 history_stats 和 fan_stats）
        
        Args:
            user_id: 用户ID
        
        Returns:
            包含所有统计数据的字典
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            
            result = {
                'guobiao_stats': [],
                'riichi_stats': [],
                'guobiao_fan_stats': [],
                'qingque_stats': [],
                'qingque_fan_stats': [],
            }
            
            # 查询 guobiao_history_stats
            cursor.execute("""
                SELECT * FROM guobiao_history_stats
                WHERE user_id = %s
                ORDER BY rule, mode
            """, (user_id,))
            
            for row in cursor.fetchall():
                stats_dict = dict(row)
                result['guobiao_stats'].append(stats_dict)
            
            # 查询 riichi_history_stats
            cursor.execute("""
                SELECT * FROM riichi_history_stats
                WHERE user_id = %s
                ORDER BY rule, mode
            """, (user_id,))
            
            for row in cursor.fetchall():
                stats_dict = dict(row)
                result['riichi_stats'].append(stats_dict)
            
            # 查询 guobiao_fan_stats
            cursor.execute("""
                SELECT * FROM guobiao_fan_stats
                WHERE user_id = %s
                ORDER BY rule, mode
            """, (user_id,))
            
            for row in cursor.fetchall():
                stats_dict = dict(row)
                result['guobiao_fan_stats'].append(stats_dict)
            
            # 查询 qingque_history_stats
            cursor.execute("""
                SELECT * FROM qingque_history_stats
                WHERE user_id = %s
                ORDER BY rule, mode
            """, (user_id,))
            
            for row in cursor.fetchall():
                stats_dict = dict(row)
                result['qingque_stats'].append(stats_dict)
            
            # 查询 qingque_fan_stats
            cursor.execute("""
                SELECT * FROM qingque_fan_stats
                WHERE user_id = %s
                ORDER BY rule, mode
            """, (user_id,))
            
            for row in cursor.fetchall():
                stats_dict = dict(row)
                result['qingque_fan_stats'].append(stats_dict)
            
            logger.info(f'获取用户 {user_id} 的统计数据：国标 {len(result["guobiao_stats"])} 条，立直 {len(result["riichi_stats"])} 条，国标番种 {len(result["guobiao_fan_stats"])} 条，青雀 {len(result["qingque_stats"])} 条，青雀番种 {len(result["qingque_fan_stats"])} 条')
            return result
            
        except Error as e:
            logger.error(f'获取玩家统计数据失败: {e}', exc_info=True)
            return {'guobiao_stats': [], 'riichi_stats': [], 'guobiao_fan_stats': [], 'qingque_stats': [], 'qingque_fan_stats': []}
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def get_user_settings(self, user_id: int) -> Optional[Dict[str, Any]]:
        """
        获取指定用户的设置信息（称号、头像、角色、音色），包含用户名
        
        Args:
            user_id: 用户ID
        
        Returns:
            用户设置信息字典（包含 username），如果不存在则返回 None
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute("""
                SELECT 
                    us.user_id,
                    us.title_id,
                    us.profile_image_id,
                    us.character_id,
                    us.voice_id,
                    u.username
                FROM user_settings us
                INNER JOIN users u ON us.user_id = u.user_id
                WHERE us.user_id = %s
            """, (user_id,))
            settings = cursor.fetchone()
            
            if settings:
                return dict(settings)
            return None
        except Error as e:
            logger.error(f'获取用户设置失败: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def get_user_config(self, user_id: int) -> Optional[Dict[str, Any]]:
        """
        获取指定用户的游戏配置信息（音量等）
        
        Args:
            user_id: 用户ID
        
        Returns:
            游戏配置信息字典，如果不存在则返回 None
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                "SELECT * FROM user_config WHERE user_id = %s",
                (user_id,)
            )
            config = cursor.fetchone()
            
            if config:
                return dict(config)
            return None
        except Error as e:
            logger.error(f'获取游戏配置失败: {e}')
            if conn:
                conn.rollback()
            return None
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    # ---------------- 好友 / 关注 ----------------

    FOLLOW_MAX = 10
    FRIEND_MAX = 20

    def count_friends(self, user_id: int) -> int:
        """返回 user_id 当前关注的人数，失败返回 -1。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute("SELECT COUNT(*) FROM user_friends WHERE user_id = %s", (user_id,))
            row = cursor.fetchone()
            return int(row[0]) if row else 0
        except Error as e:
            logger.error(f'count_friends 失败 user_id={user_id}: {e}')
            if conn:
                conn.rollback()
            return -1
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def add_friend(self, user_id: int, friend_user_id: int) -> Dict[str, Any]:
        """
        关注一个玩家。返回 {success: bool, message: str, code: str}
        code 取值：ok / self / not_found / full / duplicate / error
        """
        if user_id == friend_user_id:
            return {"success": False, "message": "不能关注自己", "code": "self"}

        # 校验目标用户存在
        target = self.get_user_by_user_id(friend_user_id)
        if not target:
            return {"success": False, "message": "目标玩家不存在", "code": "not_found"}

        cur_count = self.count_friends(user_id)
        if cur_count < 0:
            return {"success": False, "message": "服务器繁忙，请稍后再试", "code": "error"}
        if cur_count >= self.FOLLOW_MAX:
            return {
                "success": False,
                "message": "关注人数已满，请清理关注列表后再关注",
                "code": "full",
            }

        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                "INSERT INTO user_friends (user_id, friend_user_id) VALUES (%s, %s)",
                (user_id, friend_user_id),
            )
            conn.commit()
            return {"success": True, "message": "关注成功", "code": "ok"}
        except Error as e:
            if conn:
                conn.rollback()
            if getattr(e, "pgcode", None) == "23505":
                return {"success": False, "message": "已经关注过该玩家", "code": "duplicate"}
            logger.error(f'add_friend 失败: {e}')
            return {"success": False, "message": "服务器繁忙，请稍后再试", "code": "error"}
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def count_mutual_friends(self, user_id: int) -> int:
        """返回 user_id 当前双向好友人数，失败返回 -1。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT COUNT(*)
                FROM user_friendships
                WHERE user_id_small = %s OR user_id_large = %s
                """,
                (user_id, user_id),
            )
            row = cursor.fetchone()
            return int(row[0]) if row else 0
        except Error as e:
            logger.error(f'count_mutual_friends 失败 user_id={user_id}: {e}')
            if conn:
                conn.rollback()
            return -1
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def are_mutual_friends(self, user_id: int, friend_user_id: int) -> bool:
        """判断两名用户是否已经是双向好友。"""
        small, large = sorted((user_id, friend_user_id))
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT 1 FROM user_friendships
                WHERE user_id_small = %s AND user_id_large = %s
                """,
                (small, large),
            )
            return cursor.fetchone() is not None
        except Error as e:
            logger.error(f'are_mutual_friends 失败: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def send_friend_request(self, user_id: int, target_user_id: int) -> Dict[str, Any]:
        """发送好友申请。"""
        if user_id == target_user_id:
            return {"success": False, "message": "不能添加自己为好友", "code": "self"}
        if not self.get_user_by_user_id(target_user_id):
            return {"success": False, "message": "目标玩家不存在", "code": "not_found"}
        if self.are_mutual_friends(user_id, target_user_id):
            return {"success": False, "message": "你们已经是好友", "code": "duplicate"}
        cur_count = self.count_mutual_friends(user_id)
        target_count = self.count_mutual_friends(target_user_id)
        if cur_count < 0 or target_count < 0:
            return {"success": False, "message": "服务器繁忙，请稍后再试", "code": "error"}
        if cur_count >= self.FRIEND_MAX:
            return {"success": False, "message": "好友已满，请清理好友列表后再申请", "code": "full"}
        if target_count >= self.FRIEND_MAX:
            return {"success": False, "message": "对方好友已满", "code": "target_full"}

        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            # 对方已申请你时，直接建立好友关系。
            cursor.execute(
                """
                SELECT 1 FROM user_friend_requests
                WHERE from_user_id = %s AND to_user_id = %s
                """,
                (target_user_id, user_id),
            )
            if cursor.fetchone():
                small, large = sorted((user_id, target_user_id))
                cursor.execute(
                    """
                    INSERT INTO user_friendships (user_id_small, user_id_large)
                    VALUES (%s, %s)
                    ON CONFLICT DO NOTHING
                    """,
                    (small, large),
                )
                cursor.execute(
                    """
                    DELETE FROM user_friend_requests
                    WHERE (from_user_id = %s AND to_user_id = %s)
                       OR (from_user_id = %s AND to_user_id = %s)
                    """,
                    (user_id, target_user_id, target_user_id, user_id),
                )
                conn.commit()
                return {"success": True, "message": "已成为好友", "code": "accepted"}

            cursor.execute(
                """
                INSERT INTO user_friend_requests (from_user_id, to_user_id)
                VALUES (%s, %s)
                """,
                (user_id, target_user_id),
            )
            conn.commit()
            return {"success": True, "message": "好友申请已发送", "code": "ok"}
        except Error as e:
            if conn:
                conn.rollback()
            if getattr(e, "pgcode", None) == "23505":
                return {"success": False, "message": "已经发送过好友申请", "code": "duplicate"}
            logger.error(f'send_friend_request 失败: {e}')
            return {"success": False, "message": "服务器繁忙，请稍后再试", "code": "error"}
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def respond_friend_request(self, user_id: int, from_user_id: int, accept: bool) -> Dict[str, Any]:
        """处理发给 user_id 的好友申请。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                """
                DELETE FROM user_friend_requests
                WHERE from_user_id = %s AND to_user_id = %s
                """,
                (from_user_id, user_id),
            )
            if cursor.rowcount <= 0:
                conn.commit()
                return {"success": False, "message": "好友申请不存在或已处理", "code": "not_found"}
            if not accept:
                conn.commit()
                return {"success": True, "message": "已拒绝好友申请", "code": "declined"}
            cur_count = self.count_mutual_friends(user_id)
            from_count = self.count_mutual_friends(from_user_id)
            if cur_count < 0 or from_count < 0:
                conn.rollback()
                return {"success": False, "message": "服务器繁忙，请稍后再试", "code": "error"}
            if cur_count >= self.FRIEND_MAX:
                conn.rollback()
                return {"success": False, "message": "好友已满，请清理好友列表后再接受", "code": "full"}
            if from_count >= self.FRIEND_MAX:
                conn.rollback()
                return {"success": False, "message": "对方好友已满", "code": "target_full"}
            small, large = sorted((user_id, from_user_id))
            cursor.execute(
                """
                INSERT INTO user_friendships (user_id_small, user_id_large)
                VALUES (%s, %s)
                ON CONFLICT DO NOTHING
                """,
                (small, large),
            )
            conn.commit()
            return {"success": True, "message": "已成为好友", "code": "accepted"}
        except Error as e:
            logger.error(f'respond_friend_request 失败: {e}')
            if conn:
                conn.rollback()
            return {"success": False, "message": "服务器繁忙，请稍后再试", "code": "error"}
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def delete_mutual_friend(self, user_id: int, friend_user_id: int) -> bool:
        """双向删除好友关系。"""
        small, large = sorted((user_id, friend_user_id))
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                """
                DELETE FROM user_friendships
                WHERE user_id_small = %s AND user_id_large = %s
                """,
                (small, large),
            )
            conn.commit()
            return cursor.rowcount > 0
        except Error as e:
            logger.error(f'delete_mutual_friend 失败: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def list_mutual_friends(self, user_id: int) -> List[Dict[str, Any]]:
        """列出 user_id 的双向好友基础信息。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                """
                SELECT u.user_id, u.username, COALESCE(us.profile_image_id, 1) AS profile_image_id
                FROM user_friendships f
                INNER JOIN users u ON u.user_id = CASE
                    WHEN f.user_id_small = %s THEN f.user_id_large
                    ELSE f.user_id_small
                END
                LEFT JOIN user_settings us ON us.user_id = u.user_id
                WHERE f.user_id_small = %s OR f.user_id_large = %s
                ORDER BY f.created_at ASC
                """,
                (user_id, user_id, user_id),
            )
            return [dict(row) for row in cursor.fetchall()]
        except Error as e:
            logger.error(f'list_mutual_friends 失败: {e}')
            if conn:
                conn.rollback()
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def list_friend_requests(self, user_id: int) -> List[Dict[str, Any]]:
        """列出发给 user_id 的好友申请。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                """
                SELECT u.user_id, u.username, COALESCE(us.profile_image_id, 1) AS profile_image_id
                FROM user_friend_requests r
                INNER JOIN users u ON u.user_id = r.from_user_id
                LEFT JOIN user_settings us ON us.user_id = r.from_user_id
                WHERE r.to_user_id = %s
                ORDER BY r.created_at ASC
                """,
                (user_id,),
            )
            return [dict(row) for row in cursor.fetchall()]
        except Error as e:
            logger.error(f'list_friend_requests 失败: {e}')
            if conn:
                conn.rollback()
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def remove_friend(self, user_id: int, friend_user_id: int) -> bool:
        """取消关注。"""
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                "DELETE FROM user_friends WHERE user_id = %s AND friend_user_id = %s",
                (user_id, friend_user_id),
            )
            conn.commit()
            return cursor.rowcount > 0
        except Error as e:
            logger.error(f'remove_friend 失败: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)

    def list_friends(self, user_id: int) -> List[Dict[str, Any]]:
        """
        列出 user_id 关注的所有玩家基础信息（不含在线状态，由调用方拼装）：
        [{user_id, username, profile_image_id}, ...]
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                """
                SELECT u.user_id, u.username, COALESCE(us.profile_image_id, 1) AS profile_image_id
                FROM user_friends uf
                INNER JOIN users u ON u.user_id = uf.friend_user_id
                LEFT JOIN user_settings us ON us.user_id = uf.friend_user_id
                WHERE uf.user_id = %s
                ORDER BY uf.created_at ASC
                """,
                (user_id,),
            )
            return [dict(row) for row in cursor.fetchall()]
        except Error as e:
            logger.error(f'list_friends 失败: {e}')
            if conn:
                conn.rollback()
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)


# 挂载国标麻将相关方法到 DatabaseManager 类
from .guobiao.store_guobiao import store_guobiao_game_record, store_guobiao_game_stats, store_guobiao_fan_stats

DatabaseManager.store_guobiao_game_record = store_guobiao_game_record
DatabaseManager.store_guobiao_game_stats = store_guobiao_game_stats
DatabaseManager.store_guobiao_fan_stats = store_guobiao_fan_stats

# 挂载青雀麻将相关方法到 DatabaseManager 类
from .qingque.store_qingque import store_qingque_game_record, store_qingque_game_stats, store_qingque_fan_stats

DatabaseManager.store_qingque_game_record = store_qingque_game_record
DatabaseManager.store_qingque_game_stats = store_qingque_game_stats
DatabaseManager.store_qingque_fan_stats = store_qingque_fan_stats

# 挂载古典麻将相关方法到 DatabaseManager 类
from .classical.store_classical import store_classical_game_record, store_classical_game_stats, store_classical_fan_stats

DatabaseManager.store_classical_game_record = store_classical_game_record
DatabaseManager.store_classical_game_stats = store_classical_game_stats
DatabaseManager.store_classical_fan_stats = store_classical_fan_stats

# 挂载四川麻将（血战到底）相关方法到 DatabaseManager 类（牌谱写通用表，无需专用统计表）
from .sichuan.store_sichuan import store_sichuan_game_record

DatabaseManager.store_sichuan_game_record = store_sichuan_game_record

from .riichi.store_riichi import store_riichi_game_record, store_riichi_game_stats, store_riichi_fan_stats
from .riichi.get_riichi_stats import get_riichi_stats

DatabaseManager.store_riichi_game_record = store_riichi_game_record
DatabaseManager.store_riichi_game_stats = store_riichi_game_stats
DatabaseManager.store_riichi_fan_stats = store_riichi_fan_stats
DatabaseManager.get_riichi_stats = get_riichi_stats

# 挂载段位数据 CRUD 方法到 DatabaseManager 类
from .guobiao.rank_data import get_rank_data, update_rank_data, get_user_sponsor_mcrpl
from .guobiao.get_leaderboard import get_guobiao_leaderboard
from .guobiao.get_rank_record_list import get_rank_record_list

DatabaseManager.get_rank_data = get_rank_data
DatabaseManager.update_rank_data = update_rank_data
DatabaseManager.get_user_sponsor_mcrpl = get_user_sponsor_mcrpl
DatabaseManager.get_guobiao_leaderboard = get_guobiao_leaderboard
DatabaseManager.get_rank_record_list = get_rank_record_list