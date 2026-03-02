"""
青雀麻将统计数据获取模块
用于从数据库获取青雀麻将相关的统计数据
"""
from typing import Dict, Any, List
from psycopg2.extras import RealDictCursor
import logging
from .store_qingque import FAN_FIELDS

logger = logging.getLogger(__name__)


def get_qingque_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    """
    获取指定用户的青雀历史统计数据（基础统计）
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        
        cursor.execute("""
            SELECT 
                rule,
                mode,
                COALESCE(SUM(total_games), 0) as total_games,
                COALESCE(SUM(total_rounds), 0) as total_rounds,
                COALESCE(SUM(win_count), 0) as win_count,
                COALESCE(SUM(self_draw_count), 0) as self_draw_count,
                COALESCE(SUM(deal_in_count), 0) as deal_in_count,
                COALESCE(SUM(total_fan_score), 0) as total_fan_score,
                COALESCE(SUM(total_win_turn), 0) as total_win_turn,
                COALESCE(SUM(total_fangchong_score), 0) as total_fangchong_score,
                COALESCE(SUM(first_place_count), 0) as first_place_count,
                COALESCE(SUM(second_place_count), 0) as second_place_count,
                COALESCE(SUM(third_place_count), 0) as third_place_count,
                COALESCE(SUM(fourth_place_count), 0) as fourth_place_count
            FROM qingque_history_stats
            WHERE user_id = %s
            GROUP BY rule, mode
            ORDER BY rule, mode
        """, (user_id,))
        
        stats_list = []
        for row in cursor.fetchall():
            stats_dict = dict(row)
            stats_list.append(stats_dict)
        
        logger.info(f'获取用户 {user_id} 的青雀历史统计数据：{len(stats_list)} 条')
        return stats_list
        
    except Exception as e:
        logger.error(f'获取青雀历史统计数据失败: {e}', exc_info=True)
        return []
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def get_qingque_fan_stats_total(db_manager, user_id: int) -> Dict[str, int]:
    """
    获取指定用户的青雀番种统计数据汇总（所有模式和规则的番种总和）
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        
        # 动态构建 SELECT 子句，基于 FAN_FIELDS
        fan_columns = ", ".join(f"COALESCE(SUM({f}), 0) as {f}" for f in FAN_FIELDS)
        
        cursor.execute(f"""
            SELECT {fan_columns}
            FROM qingque_fan_stats
            WHERE user_id = %s
        """, (user_id,))
        
        row = cursor.fetchone()
        if row:
            fan_stats = {k: v for k, v in dict(row).items() if v is not None}
            logger.info(f'获取用户 {user_id} 的青雀番种统计数据汇总：{len(fan_stats)} 个番种')
            return fan_stats
        else:
            return {}
            
    except Exception as e:
        logger.error(f'获取青雀番种统计数据汇总失败: {e}', exc_info=True)
        return {}
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
