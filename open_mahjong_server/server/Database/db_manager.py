"""
数据库管理类
用于管理 PostgreSQL 数据库连接和操作
"""
import psycopg2
from psycopg2 import Error
from psycopg2.extras import RealDictCursor
from psycopg2.pool import ThreadedConnectionPool, SimpleConnectionPool
from typing import Optional, Dict, Any
import logging
import threading
import hashlib
import secrets

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
    
    def verify_password(self, password: str, stored_hash: str) -> bool:
        """
        验证密码是否匹配存储的哈希值
        
        Args:
            password: 待验证的明文密码
            stored_hash: 存储的密码哈希值（格式：salt:hash）
            
        Returns:
            密码匹配返回 True，否则返回 False
        """
        return self._verify_password_hash(password, stored_hash)
    
    def user_exists(self, username: str) -> bool:
        """
        检查用户是否存在
        
        Args:
            username: 用户名
            
        Returns:
            用户存在返回 True，否则返回 False
        """
        user = self.get_user_by_username(username)
        return user is not None
