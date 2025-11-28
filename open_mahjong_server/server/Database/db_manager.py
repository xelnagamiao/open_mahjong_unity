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

            # 创建表（如果不存在）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS users (
                    user_id BIGSERIAL PRIMARY KEY,
                    username VARCHAR(255) UNIQUE NOT NULL,
                    password VARCHAR(255) NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 创建对局记录表
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_records (
                    game_id BIGSERIAL PRIMARY KEY,
                    record JSONB NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 创建玩家对局记录表（使用复合主键 (game_id, user_id)）
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS game_player_records (
                    game_id BIGINT NOT NULL REFERENCES game_records(game_id) ON DELETE CASCADE,
                    user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
                    username VARCHAR(255) NOT NULL,
                    score INT NOT NULL,
                    rank INT NOT NULL CHECK (rank >= 1 AND rank <= 4),
                    rule VARCHAR(10) NOT NULL,
                    character_used VARCHAR(255) NULL,
                    PRIMARY KEY (game_id, user_id)
                );
            """)

            # 创建国标记录表（根据局数）
            for table_suffix in ['1_4_game', '2_4_game', '3_4_game', '4_4_game']:
                cursor.execute(f"""
                    CREATE TABLE IF NOT EXISTS gb_record_{table_suffix} (
                        user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
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
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    );
                """)

            # 创建番种统计表
            cursor.execute("""
                CREATE TABLE IF NOT EXISTS gb_record_fan_count (
                    user_id BIGINT PRIMARY KEY REFERENCES users(user_id) ON DELETE CASCADE,
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
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            """)

            # 检查表是否为空
            cursor.execute("SELECT COUNT(*) FROM users;")
            user_count = cursor.fetchone()[0]

            if user_count == 0:
                # 表为空：重置序列，使第一个 user_id = 10000001
                cursor.execute("SELECT setval('users_user_id_seq', 10000000, false);")
            else:
                print(f"用户表已有 {user_count} 个用户")

            conn.commit() # 提交
            print('数据表初始化成功')
            self._get_pool()

        except Exception as e:
            print(f'数据表初始化失败: {e}')
            logger.error(f'数据表初始化失败: {e}', exc_info=True)
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
    
    def create_user(self, username: str, password: str) -> Optional[int]:
        """
        创建新用户（密码会自动哈希存储）
        
        Args:
            username: 用户名
            password: 明文密码
            
        Returns:
            创建成功返回 user_id，失败返回 None
        """
        conn = None
        try:
            # 对密码进行哈希处理
            password_hash = self._hash_password(password)
            
            conn = self._get_connection()
            cursor = conn.cursor()
            cursor.execute(
                "INSERT INTO users (username, password) VALUES (%s, %s) RETURNING user_id",
                (username, password_hash)
            )
            user_id = cursor.fetchone()[0]
            conn.commit()
            logger.info(f'用户 {username} 创建成功，user_id: {user_id}')
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

    def store_game_record(self, game_record: dict, player_list: list):
        """
        存储游戏记录和更新玩家统计数据
        
        Args:
            game_record: 游戏牌谱记录字典
            player_list: 玩家列表，每个玩家包含 record_counter 属性
        """
        conn = None
        try:
            conn = self._get_connection()
            cursor = conn.cursor()
            
            # 1. 存储牌谱记录到 game_records 表
            game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)
            cursor.execute(
                "INSERT INTO game_records (record) VALUES (%s) RETURNING game_id",
                (game_record_json,)
            )
            game_id = cursor.fetchone()[0]
            logger.info(f'牌谱记录已保存到 game_records 表，game_id: {game_id}')
            
            # 2. 存储玩家对局记录到 game_player_records 表
            game_title = game_record.get("game_title", {})
            rule = game_title.get("rule", "GB")
            
            # 获取玩家排名（rank_result 是 1-4）
            for player in player_list:
                rank = player.record_counter.rank_result  # 1-4
                character_used = getattr(player, 'character_used', None)  # 可能不存在，默认为 None
                
                cursor.execute("""
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, rule, character_used
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s)
                """, (
                    game_id,
                    player.user_id,
                    player.username,
                    player.score,
                    rank,
                    rule,
                    character_used
                ))
            logger.info(f'已为 {len(player_list)} 名玩家保存对局记录到 game_player_records 表')
            
            # 3. 根据 max_round 确定存储到哪个表
            max_round = game_record.get("game_title", {}).get("max_round", 4)
            table_suffix_map = {
                1: "1_4_game",  # 东风战
                2: "2_4_game",  # 南风战
                3: "3_4_game",  # 西风战
                4: "4_4_game"   # 全庄
            }
            table_suffix = table_suffix_map.get(max_round, "4_4_game")
            record_table = f"gb_record_{table_suffix}"
            
            # 4. 计算总回合数（从 game_round 中获取）
            total_rounds = len(game_record.get("game_round", {}))
            
            # 5. 更新每个玩家的统计数据
            for player in player_list:
                user_id = player.user_id
                counter = player.record_counter
                
                # 计算和牌总次数
                win_count = counter.zimo_times + counter.dianhe_times
                
                # 根据排名更新排名统计（rank_result 是 1-4，转换为 0-3 用于索引）
                rank_index = counter.rank_result - 1  # 转换为 0-3
                rank_updates = {
                    "first_place_count": 1 if rank_index == 0 else 0,
                    "second_place_count": 1 if rank_index == 1 else 0,
                    "third_place_count": 1 if rank_index == 2 else 0,
                    "fourth_place_count": 1 if rank_index == 3 else 0
                }
                
                # 使用 INSERT ... ON CONFLICT 更新或插入记录
                cursor.execute(f"""
                    INSERT INTO {record_table} (
                        user_id, total_games, total_rounds, win_count, 
                        self_draw_count, deal_in_count, total_fan_score, total_win_turn, total_fangchong_score,
                        first_place_count, second_place_count, third_place_count, fourth_place_count
                    ) VALUES (
                        %s, 1, %s, %s, %s, %s, %s, %s, %s,
                        %s, %s, %s, %s
                    )
                    ON CONFLICT (user_id) DO UPDATE SET
                        total_games = {record_table}.total_games + 1,
                        total_rounds = {record_table}.total_rounds + %s,
                        win_count = {record_table}.win_count + %s,
                        self_draw_count = {record_table}.self_draw_count + %s,
                        deal_in_count = {record_table}.deal_in_count + %s,
                        total_fan_score = {record_table}.total_fan_score + %s,
                        total_win_turn = {record_table}.total_win_turn + %s,
                        total_fangchong_score = {record_table}.total_fangchong_score + %s,
                        first_place_count = {record_table}.first_place_count + %s,
                        second_place_count = {record_table}.second_place_count + %s,
                        third_place_count = {record_table}.third_place_count + %s,
                        fourth_place_count = {record_table}.fourth_place_count + %s,
                        updated_at = CURRENT_TIMESTAMP
                """, (
                    user_id, total_rounds, win_count, counter.zimo_times, counter.fangchong_times,
                    counter.win_score, counter.win_turn, counter.fangchong_score,
                    rank_updates["first_place_count"], rank_updates["second_place_count"],
                    rank_updates["third_place_count"], rank_updates["fourth_place_count"],
                    total_rounds, win_count, counter.zimo_times, counter.fangchong_times,
                    counter.win_score, counter.win_turn, counter.fangchong_score,
                    rank_updates["first_place_count"], rank_updates["second_place_count"],
                    rank_updates["third_place_count"], rank_updates["fourth_place_count"]
                ))
            
            # 6. 更新番种统计
            # 番种名称到数据库字段的映射
            fan_name_to_field = {
                "大四喜": "dasixi", "大三元": "dasanyuan", "绿一色": "lvyise",
                "九莲宝灯": "jiulianbaodeng", "四杠": "sigang", "三杠": "sangang",
                "连七对": "lianqidui", "十三幺": "shisanyao", "清幺九": "qingyaojiu",
                "小四喜": "xiaosixi", "小三元": "xiaosanyuan", "字一色": "ziyise",
                "四暗刻": "sianke", "一色双龙会": "yiseshuanglonghui", "一色四同顺": "yisesitongshun",
                "一色四节高": "yisesijiegao", "一色四步高": "yisesibugao", "混幺九": "hunyaojiu",
                "七对子": "qiduizi", "七星不靠": "qixingbukao", "全双刻": "quanshuangke",
                "清一色": "qingyise", "一色三同顺": "yisesantongshun", "一色三节高": "yisesanjiegao",
                "全大": "quanda", "全中": "quanzhong", "全小": "quanxiao",
                "清龙": "qinglong", "三色双龙会": "sanseshuanglonghui", "一色三步高": "yisesanbugao",
                "全带五": "quandaiwu", "三同刻": "santongke", "三暗刻": "sananke",
                "全不靠": "quanbukao", "组合龙": "zuhelong", "大于五": "dayuwu",
                "小于五": "xiaoyuwu", "三风刻": "sanfengke", "花龙": "hualong",
                "推不倒": "tuibudao", "三色三同顺": "sansesantongshun", "三色三节高": "sansesanjiegao",
                "无番和": "wufanhe", "妙手回春": "miaoshouhuichun", "海底捞月": "haidilaoyue",
                "杠上开花": "gangshangkaihua", "抢杠和": "qiangganghe", "碰碰和": "pengpenghe",
                "混一色": "hunyise", "三色三步高": "sansesanbugao", "五门齐": "wumenqi",
                "全求人": "quanqiuren", "双暗杠": "shuangangang", "双箭刻": "shuangjianke",
                "全带幺": "quandaiyao", "不求人": "buqiuren", "双明杠": "shuangminggang",
                "和绝张": "hejuezhang", "箭刻": "jianke", "圈风刻": "quanfengke",
                "门风刻": "menfengke", "门前清": "menqianqing", "平和": "pinghe",
                "四归一": "siguiyi", "双同刻": "shuangtongke", "双暗刻": "shuanganke",
                "暗杠": "angang", "断幺": "duanyao", "一般高": "yibangao",
                "喜相逢": "xixiangfeng", "连六": "lianliu", "老少副": "laoshaofu",
                "幺九刻": "yaojiuke", "明杠": "minggang", "缺一门": "queyimen",
                "无字": "wuzi", "边张": "bianzhang", "嵌张": "qianzhang",
                "单钓将": "dandiaojiang", "自摸": "zimo", "花牌": "huapai",
                "明暗杠": "mingangang"
            }
            
            # 可叠加番种列表（这些番种以"番种名*数量"的形式出现）
            stackable_fans = ["花牌", "四归一", "双同刻", "一般高", "喜相逢", "幺九刻", "连六"]
            
            # 统计每个玩家的番种
            fan_counts = {}  # {user_id: {fan_field: count}}
            for player in player_list:
                user_id = player.user_id
                if user_id not in fan_counts:
                    fan_counts[user_id] = {}
                
                # 遍历玩家的所有和牌番种
                for fan_list in player.record_counter.recorded_fans:
                    if isinstance(fan_list, list):
                        for fan_name in fan_list:
                            # 处理可叠加番种（格式：番种名*数量）
                            if "*" in fan_name:
                                parts = fan_name.split("*")
                                if len(parts) == 2:
                                    base_fan_name = parts[0].strip()
                                    try:
                                        count = int(parts[1].strip())
                                        if base_fan_name in stackable_fans and base_fan_name in fan_name_to_field:
                                            field = fan_name_to_field[base_fan_name]
                                            fan_counts[user_id][field] = fan_counts[user_id].get(field, 0) + count
                                    except ValueError:
                                        logger.warning(f'无法解析可叠加番种数量: {fan_name}')
                            # 处理普通番种
                            elif fan_name in fan_name_to_field:
                                field = fan_name_to_field[fan_name]
                                fan_counts[user_id][field] = fan_counts[user_id].get(field, 0) + 1
            
            # 更新番种统计表
            for user_id, fan_dict in fan_counts.items():
                if fan_dict:
                    # 构建更新语句
                    set_clauses = []
                    values = [user_id]  # 先添加 user_id 用于 INSERT
                    for field, count in fan_dict.items():
                        set_clauses.append(f"{field} = gb_record_fan_count.{field} + %s")
                        values.append(count)  # 添加字段值用于 UPDATE
                    
                    if set_clauses:
                        cursor.execute(f"""
                            INSERT INTO gb_record_fan_count (user_id) VALUES (%s)
                            ON CONFLICT (user_id) DO UPDATE SET
                                {', '.join(set_clauses)},
                                updated_at = CURRENT_TIMESTAMP
                        """, values)
            
            conn.commit()
            logger.info(f'游戏记录和统计数据已保存，game_id: {game_id}')
            
        except Error as e:
            logger.error(f'存储游戏记录失败: {e}', exc_info=True)
            if conn:
                conn.rollback()
        finally:
            if conn:
                cursor.close()
                self._put_connection(conn)