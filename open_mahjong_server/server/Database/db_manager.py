"""
数据库管理类
用于管理 PostgreSQL 数据库连接和操作
"""
import psycopg2
from psycopg2 import Error
from psycopg2.extras import RealDictCursor
from typing import Optional, Dict, Any
import logging

logger = logging.getLogger(__name__)


class DatabaseManager:
    """PostgreSQL 数据库管理类"""
    
    def __init__(self, host: str, user: str, password: str, database: str, port: int = 5432):
        self.config = {
            'host': host,
            'user': user,
            'password': password,
            'database': database,
            'port': port
        }
    
    def init_database(self):
        # 初始化数据库表，如果表不存在则创建
        conn = None
        try:
            conn = psycopg2.connect(**self.config)
            cursor = conn.cursor()
            
            # 创建用户表 users
            create_table_sql = """
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
                username VARCHAR(255) UNIQUE NOT NULL,
                password VARCHAR(255) NOT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )
            """
            cursor.execute(create_table_sql)
            conn.commit()
            print('数据表创建成功')
            logger.info('数据表创建成功')
        except Error as e:
            print(f'数据表创建失败: {e}')
            logger.error(f'数据表创建失败: {e}')
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
            conn = psycopg2.connect(**self.config)
            cursor = conn.cursor(cursor_factory=RealDictCursor)
            cursor.execute(
                "SELECT * FROM users WHERE username = %s",
                (username,)
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
                conn.close()
    
    def create_user(self, username: str, password: str) -> bool:
        """
        创建新用户
        
        Args:
            username: 用户名
            password: 密码
            
        Returns:
            创建成功返回 True，失败返回 False
        """
        conn = None
        try:
            conn = psycopg2.connect(**self.config)
            cursor = conn.cursor()
            cursor.execute(
                "INSERT INTO users (username, password) VALUES (%s, %s)",
                (username, password)
            )
            conn.commit()
            logger.info(f'用户 {username} 创建成功')
            return True
        except Error as e:
            logger.error(f'创建用户失败: {e}')
            if conn:
                conn.rollback()
            return False
        finally:
            if conn:
                cursor.close()
                conn.close()
    
    def verify_password(self, username: str, password: str) -> bool:
        """
        验证用户密码
        
        Args:
            username: 用户名
            password: 密码
            
        Returns:
            密码正确返回 True，否则返回 False
        """
        user = self.get_user_by_username(username)
        if user and user.get('password') == password:
            return True
        return False
    
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
