"""
数据库管理类
用于管理 PostgreSQL 数据库连接和操作
"""
import psycopg2
from psycopg2 import Error
from psycopg2.extras import RealDictCursor
from psycopg2.pool import ThreadedConnectionPool, SimpleConnectionPool
from typing import Optional, Dict, Any, List
import logging
import threading
import hashlib
import secrets
import json

logger = logging.getLogger(__name__)


class DatabaseManager:
    """PostgreSQL 数据库管理类"""
    
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

            # 创建表game_records（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_records (
                    game_id BIGSERIAL PRIMARY KEY,
                    record JSONB NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 创建表game_player_records（使用复合主键 (game_id, user_id)）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_player_records (
                    game_id BIGINT NOT NULL REFERENCES game_records(game_id) ON DELETE CASCADE,
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    username VARCHAR(255) NOT NULL,
                    score INT NOT NULL,
                    rank INT NOT NULL CHECK (rank >= 1 AND rank <= 4),
                    rule VARCHAR(10) NOT NULL,
                    title_used INT NULL,
                    character_used INT NULL,
                    profile_used INT NULL,
                    voice_used INT NULL,
                    PRIMARY KEY (game_id, user_id)
                );
            """)

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


            # 创建游客用户序列（9000000-9900000）
            cursor.execute("CREATE SEQUENCE IF NOT EXISTS tourist_user_id_seq START 9000000 INCREMENT 1 MAXVALUE 9900000 CYCLE;")

            # 创建注册用户序列（10000001+）
            cursor.execute("CREATE SEQUENCE IF NOT EXISTS registered_user_id_seq START 10000001 INCREMENT 1;")

            # 设置用户表的默认序列为注册用户序列
            cursor.execute("ALTER TABLE users ALTER COLUMN user_id SET DEFAULT nextval('registered_user_id_seq');")

            logger.info('用户ID序列初始化完成')
            print('用户ID序列初始化完成')

            conn.commit() # 提交
            logger.info('数据表初始化成功')
            print('数据表初始化成功')
            self._get_pool()

        except Exception as e:
            logger.error(f'数据表初始化失败: {e}', exc_info=True)
            print(f'数据表初始化失败: {e}')
            if conn:
                conn.rollback()
        finally:
            if conn:
                cursor.close()
                conn.close()
    
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
    
    def get_record_list(self, user_id: int, limit: int = 20) -> List[Dict[str, Any]]:
        """
        获取指定用户的最近N局游戏记录，按 game_id 分组打包
        
        注意：每个游戏在 game_player_records 表中有4条记录（对应4个玩家），
        需要按 game_id 分组，将同一游戏的4个玩家记录打包在一起。
        
        Args:
            user_id: 用户ID
            limit: 返回游戏数量限制，默认20（每个游戏包含4个玩家）
        
        Returns:
            游戏记录列表，每个记录包含：
            - game_id: 游戏ID
            - record: 完整的牌谱记录（JSONB）
            - created_at: 创建时间
            - players: 该游戏的4个玩家信息列表（按排名排序）
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            
            # 第一步：获取用户参与的最近N个游戏的 game_id（去重）
            cursor.execute("""
                SELECT DISTINCT game_id
                FROM game_player_records
                WHERE user_id = %s
                ORDER BY game_id DESC
                LIMIT %s
            """, (user_id, limit))
            
            game_ids = [row['game_id'] for row in cursor.fetchall()]
            
            if not game_ids:
                logger.info(f'用户 {user_id} 没有游戏记录')
                return []
            
            # 第二步：获取这些游戏的所有玩家记录和牌谱记录
            placeholders = ','.join(['%s'] * len(game_ids))
            cursor.execute(f"""
                SELECT 
                    gpr.game_id,
                    gpr.user_id,
                    gpr.username,
                    gpr.score,
                    gpr.rank,
                    gpr.rule,
                    gpr.title_used,
                    gpr.character_used,
                    gpr.profile_used,
                    gpr.voice_used,
                    gr.record,
                    gr.created_at
                FROM game_player_records gpr
                INNER JOIN game_records gr ON gpr.game_id = gr.game_id
                WHERE gpr.game_id IN ({placeholders})
                ORDER BY gpr.game_id DESC, gpr.rank
            """, game_ids)
            
            # 第三步：按 game_id 分组打包
            games_dict = {}  # {game_id: {game_info, players}}
            
            for row in cursor.fetchall():
                game_id = row['game_id']
                
                # 初始化游戏记录（如果还没有）
                if game_id not in games_dict:
                    record_data = row['record']
                    # 解析 JSONB record 字段
                    if isinstance(record_data, str):
                        record_data = json.loads(record_data)
                    
                    games_dict[game_id] = {
                        'game_id': game_id,
                        'record': record_data,
                        'created_at': str(row['created_at']),
                        'rule': row['rule'],
                        'players': []
                    }
                
                # 添加玩家信息到该游戏的玩家列表
                games_dict[game_id]['players'].append({
                    'user_id': row['user_id'],
                    'username': row['username'],
                    'score': row['score'],
                    'rank': row['rank'],
                    'title_used': row.get('title_used'),
                    'character_used': row.get('character_used'),
                    'profile_used': row.get('profile_used'),
                    'voice_used': row.get('voice_used')
                })
            
            # 转换为列表，按 game_id 降序排序（最新的在前）
            records = list(games_dict.values())
            records.sort(key=lambda x: x['game_id'], reverse=True)
            
            logger.info(f'获取用户 {user_id} 的 {len(records)} 局游戏记录（共 {sum(len(r["players"]) for r in records)} 条玩家记录）')
            return records
            
        except Error as e:
            logger.error(f'获取游戏记录列表失败: {e}', exc_info=True)
            return []
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)
    
    def get_player_stats(self, user_id: int) -> Dict[str, Any]:
        """
        获取指定用户的所有统计数据（包括 guobiao_history_stats、riichi_history_stats 和 guobiao_fan_stats）
        
        Args:
            user_id: 用户ID
        
        Returns:
            包含所有统计数据的字典，格式：
            {
                'guobiao_stats': [{'rule': 'guobiao', 'mode': '4/4', ...}, ...],
                'riichi_stats': [{'rule': 'riichi', 'mode': '1_4_game', ...}, ...],
                'guobiao_fan_stats': [{'rule': 'guobiao', 'mode': '4/4', ...}, ...]  # 番种统计数据
            }
            如果某个规则没有数据，则返回空列表
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            
            result = {
                'guobiao_stats': [],
                'riichi_stats': [],
                'guobiao_fan_stats': []
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
            
            logger.info(f'获取用户 {user_id} 的统计数据：国标 {len(result["guobiao_stats"])} 条，立直 {len(result["riichi_stats"])} 条，番种 {len(result["guobiao_fan_stats"])} 条')
            return result
            
        except Error as e:
            logger.error(f'获取玩家统计数据失败: {e}', exc_info=True)
            return {'guobiao_stats': [], 'riichi_stats': [], 'guobiao_fan_stats': []}
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


# 挂载国标麻将相关方法到 DatabaseManager 类
from .guobiao.store_guobiao import store_guobiao_game_record, store_guobiao_game_stats, store_guobiao_fan_stats

DatabaseManager.store_guobiao_game_record = store_guobiao_game_record
DatabaseManager.store_guobiao_game_stats = store_guobiao_game_stats
DatabaseManager.store_guobiao_fan_stats = store_guobiao_fan_stats