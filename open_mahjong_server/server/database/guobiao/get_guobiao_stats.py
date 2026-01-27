"""
国标麻将统计数据获取模块
用于从数据库获取国标麻将相关的统计数据
"""
from typing import Dict, Any, List, Optional
from psycopg2.extras import RealDictCursor
import logging

logger = logging.getLogger(__name__)


def get_guobiao_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    """
    获取指定用户的国标历史统计数据（基础统计）
    
    Args:
        db_manager: 数据库管理器实例
        user_id: 用户ID
    
    Returns:
        国标历史统计数据列表，每个元素包含 rule, mode 和基础统计字段
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
            FROM guobiao_history_stats
            WHERE user_id = %s
            GROUP BY rule, mode
            ORDER BY rule, mode
        """, (user_id,))
        
        stats_list = []
        for row in cursor.fetchall():
            stats_dict = dict(row)
            stats_list.append(stats_dict)
        
        logger.info(f'获取用户 {user_id} 的国标历史统计数据：{len(stats_list)} 条')
        return stats_list
        
    except Exception as e:
        logger.error(f'获取国标历史统计数据失败: {e}', exc_info=True)
        return []
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def get_guobiao_fan_stats_total(db_manager, user_id: int) -> Dict[str, int]:
    """
    获取指定用户的国标番种统计数据汇总（所有模式和规则的番种总和）
    
    Args:
        db_manager: 数据库管理器实例
        user_id: 用户ID
    
    Returns:
        番种统计数据字典，格式：{fan_name: total_count}
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        
        # 查询所有番种字段并汇总
        # 从 guobiao_fan_stats 表结构获取所有番种字段
        cursor.execute("""
            SELECT 
                COALESCE(SUM(dasixi), 0) as dasixi,
                COALESCE(SUM(dasanyuan), 0) as dasanyuan,
                COALESCE(SUM(lvyise), 0) as lvyise,
                COALESCE(SUM(jiulianbaodeng), 0) as jiulianbaodeng,
                COALESCE(SUM(sigang), 0) as sigang,
                COALESCE(SUM(sangang), 0) as sangang,
                COALESCE(SUM(lianqidui), 0) as lianqidui,
                COALESCE(SUM(shisanyao), 0) as shisanyao,
                COALESCE(SUM(qingyaojiu), 0) as qingyaojiu,
                COALESCE(SUM(xiaosixi), 0) as xiaosixi,
                COALESCE(SUM(xiaosanyuan), 0) as xiaosanyuan,
                COALESCE(SUM(ziyise), 0) as ziyise,
                COALESCE(SUM(sianke), 0) as sianke,
                COALESCE(SUM(yiseshuanglonghui), 0) as yiseshuanglonghui,
                COALESCE(SUM(yisesitongshun), 0) as yisesitongshun,
                COALESCE(SUM(yisesijiegao), 0) as yisesijiegao,
                COALESCE(SUM(yisesibugao), 0) as yisesibugao,
                COALESCE(SUM(hunyaojiu), 0) as hunyaojiu,
                COALESCE(SUM(qiduizi), 0) as qiduizi,
                COALESCE(SUM(qixingbukao), 0) as qixingbukao,
                COALESCE(SUM(quanshuangke), 0) as quanshuangke,
                COALESCE(SUM(qingyise), 0) as qingyise,
                COALESCE(SUM(yisesantongshun), 0) as yisesantongshun,
                COALESCE(SUM(yisesanjiegao), 0) as yisesanjiegao,
                COALESCE(SUM(quanda), 0) as quanda,
                COALESCE(SUM(quanzhong), 0) as quanzhong,
                COALESCE(SUM(quanxiao), 0) as quanxiao,
                COALESCE(SUM(qinglong), 0) as qinglong,
                COALESCE(SUM(sanseshuanglonghui), 0) as sanseshuanglonghui,
                COALESCE(SUM(yisesanbugao), 0) as yisesanbugao,
                COALESCE(SUM(quandaiwu), 0) as quandaiwu,
                COALESCE(SUM(santongke), 0) as santongke,
                COALESCE(SUM(sananke), 0) as sananke,
                COALESCE(SUM(quanbukao), 0) as quanbukao,
                COALESCE(SUM(zuhelong), 0) as zuhelong,
                COALESCE(SUM(dayuwu), 0) as dayuwu,
                COALESCE(SUM(xiaoyuwu), 0) as xiaoyuwu,
                COALESCE(SUM(sanfengke), 0) as sanfengke,
                COALESCE(SUM(hualong), 0) as hualong,
                COALESCE(SUM(tuibudao), 0) as tuibudao,
                COALESCE(SUM(sansesantongshun), 0) as sansesantongshun,
                COALESCE(SUM(sansesanjiegao), 0) as sansesanjiegao,
                COALESCE(SUM(wufanhe), 0) as wufanhe,
                COALESCE(SUM(miaoshouhuichun), 0) as miaoshouhuichun,
                COALESCE(SUM(haidilaoyue), 0) as haidilaoyue,
                COALESCE(SUM(gangshangkaihua), 0) as gangshangkaihua,
                COALESCE(SUM(qiangganghe), 0) as qiangganghe,
                COALESCE(SUM(pengpenghe), 0) as pengpenghe,
                COALESCE(SUM(hunyise), 0) as hunyise,
                COALESCE(SUM(sansesanbugao), 0) as sansesanbugao,
                COALESCE(SUM(wumenqi), 0) as wumenqi,
                COALESCE(SUM(quanqiuren), 0) as quanqiuren,
                COALESCE(SUM(shuangangang), 0) as shuangangang,
                COALESCE(SUM(shuangjianke), 0) as shuangjianke,
                COALESCE(SUM(quandaiyao), 0) as quandaiyao,
                COALESCE(SUM(buqiuren), 0) as buqiuren,
                COALESCE(SUM(shuangminggang), 0) as shuangminggang,
                COALESCE(SUM(hejuezhang), 0) as hejuezhang,
                COALESCE(SUM(jianke), 0) as jianke,
                COALESCE(SUM(quanfengke), 0) as quanfengke,
                COALESCE(SUM(menfengke), 0) as menfengke,
                COALESCE(SUM(menqianqing), 0) as menqianqing,
                COALESCE(SUM(pinghe), 0) as pinghe,
                COALESCE(SUM(siguiyi), 0) as siguiyi,
                COALESCE(SUM(shuangtongke), 0) as shuangtongke,
                COALESCE(SUM(shuanganke), 0) as shuanganke,
                COALESCE(SUM(angang), 0) as angang,
                COALESCE(SUM(duanyao), 0) as duanyao,
                COALESCE(SUM(yibangao), 0) as yibangao,
                COALESCE(SUM(xixiangfeng), 0) as xixiangfeng,
                COALESCE(SUM(lianliu), 0) as lianliu,
                COALESCE(SUM(laoshaofu), 0) as laoshaofu,
                COALESCE(SUM(yaojiuke), 0) as yaojiuke,
                COALESCE(SUM(minggang), 0) as minggang,
                COALESCE(SUM(queyimen), 0) as queyimen,
                COALESCE(SUM(wuzi), 0) as wuzi,
                COALESCE(SUM(bianzhang), 0) as bianzhang,
                COALESCE(SUM(qianzhang), 0) as qianzhang,
                COALESCE(SUM(dandiaojiang), 0) as dandiaojiang,
                COALESCE(SUM(zimo), 0) as zimo,
                COALESCE(SUM(huapai), 0) as huapai,
                COALESCE(SUM(mingangang), 0) as mingangang
            FROM guobiao_fan_stats
            WHERE user_id = %s
        """, (user_id,))
        
        row = cursor.fetchone()
        if row:
            # 返回所有番种数据，包括值为0的（没和过也是一种数据）
            fan_stats = {k: v for k, v in dict(row).items() if v is not None}
            logger.info(f'获取用户 {user_id} 的国标番种统计数据汇总：{len(fan_stats)} 个番种')
            return fan_stats
        else:
            return {}
            
    except Exception as e:
        logger.error(f'获取国标番种统计数据汇总失败: {e}', exc_info=True)
        return {}
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)

