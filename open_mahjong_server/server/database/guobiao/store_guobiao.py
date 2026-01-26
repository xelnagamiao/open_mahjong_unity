"""
国标麻将游戏记录和统计数据存储方法
"""
import json
import logging
from psycopg2 import Error
from typing import Optional

logger = logging.getLogger(__name__)

# 番种定义映射（用于统计）
FAN_NAME_TO_FIELD = {
    "大四喜": "dasixi",
    "大三元": "dasanyuan",
    "绿一色": "lvyise",
    "九莲宝灯": "jiulianbaodeng",
    "四杠": "sigang",
    "三杠": "sangang",
    "连七对": "lianqidui",
    "十三幺": "shisanyao",
    "清幺九": "qingyaojiu",
    "小四喜": "xiaosixi",
    "小三元": "xiaosanyuan",
    "字一色": "ziyise",
    "四暗刻": "sianke",
    "一色双龙会": "yiseshuanglonghui",
    "一色四同顺": "yisesitongshun",
    "一色四节高": "yisesijiegao",
    "一色四步高": "yisesibugao",
    "混幺九": "hunyaojiu",
    "七对子": "qiduizi",
    "七星不靠": "qixingbukao",
    "全双刻": "quanshuangke",
    "清一色": "qingyise",
    "一色三同顺": "yisesantongshun",
    "一色三节高": "yisesanjiegao",
    "全大": "quanda",
    "全中": "quanzhong",
    "全小": "quanxiao",
    "清龙": "qinglong",
    "三色双龙会": "sanseshuanglonghui",
    "一色三步高": "yisesanbugao",
    "全带五": "quandaiwu",
    "三同刻": "santongke",
    "三暗刻": "sananke",
    "全不靠": "quanbukao",
    "组合龙": "zuhelong",
    "大于五": "dayuwu",
    "小于五": "xiaoyuwu",
    "三风刻": "sanfengke",
    "花龙": "hualong",
    "推不倒": "tuibudao",
    "三色三同顺": "sansesantongshun",
    "三色三节高": "sansesanjiegao",
    "无番和": "wufanhe",
    "妙手回春": "miaoshouhuichun",
    "海底捞月": "haidilaoyue",
    "杠上开花": "gangshangkaihua",
    "抢杠和": "qiangganghe",
    "碰碰和": "pengpenghe",
    "混一色": "hunyise",
    "三色三步高": "sansesanbugao",
    "五门齐": "wumenqi",
    "全求人": "quanqiuren",
    "双暗杠": "shuangangang",
    "双箭刻": "shuangjianke",
    "全带幺": "quandaiyao",
    "不求人": "buqiuren",
    "双明杠": "shuangminggang",
    "和绝张": "hejuezhang",
    "箭刻": "jianke",
    "圈风刻": "quanfengke",
    "门风刻": "menfengke",
    "门前清": "menqianqing",
    "平和": "pinghe",
    "四归一": "siguiyi",
    "双同刻": "shuangtongke",
    "双暗刻": "shuanganke",
    "暗杠": "angang",
    "断幺": "duanyao",
    "一般高": "yibangao",
    "喜相逢": "xixiangfeng",
    "连六": "lianliu",
    "老少副": "laoshaofu",
    "幺九刻": "yaojiuke",
    "明杠": "minggang",
    "缺一门": "queyimen",
    "无字": "wuzi",
    "边张": "bianzhang",
    "嵌张": "qianzhang",
    "单钓将": "dandiaojiang",
    "自摸": "zimo",
    "花牌": "huapai",
    "明暗杠": "mingangang"
}

STACKABLE_FANS = ["花牌", "四归一", "双同刻", "一般高", "喜相逢", "幺九刻", "连六"]
FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))

# 存储国标麻将游戏牌谱记录和玩家对局记录
def store_guobiao_game_record(db_manager, game_record: dict, player_list: list, room_type: str):
    """
    存储国标麻将游戏牌谱记录和玩家对局记录
    
    Args:
        db_manager: DatabaseManager 实例
        game_record: 游戏牌谱记录字典
        player_list: 玩家列表，每个玩家包含 record_counter 属性
        room_type: 房间规则类型（如 "guobiao"）
    
    Returns:
        成功返回 game_id，失败返回 None
    """
    conn = None
    try:
        conn = db_manager._get_connection()
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
        rule = room_type  # 使用传入的 room_type 作为规则
        
        # 获取玩家排名（rank_result 是 1-4）
        saved_count = 0
        for player in player_list:
            # 跳过游客账户（user_id < 10000000）
            if player.user_id < 10000000:
                continue
                
            rank = player.record_counter.rank_result  # 1-4
            # 从玩家对象获取使用的设置信息（对局时的设置）
            title_used = getattr(player, 'title_used', None)
            character_used = getattr(player, 'character_used', None)
            profile_used = getattr(player, 'profile_used', None)
            voice_used = getattr(player, 'voice_used', None)
            
            try:
                cursor.execute("""
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, rule, title_used, character_used, profile_used, voice_used
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                """, (
                    game_id,
                    player.user_id,
                    player.username,
                    player.score,
                    rank,
                    rule,
                    title_used,
                    character_used,
                    profile_used,
                    voice_used
                ))
                saved_count += 1
            except Error as e:
                logger.warning(f'跳过玩家对局记录存储（用户不存在）: user_id={player.user_id}, username={player.username}, error={e}')
        logger.info(f'已为 {saved_count} 名玩家保存对局记录到 game_player_records 表')
        
        conn.commit()
        logger.info(f'游戏记录已保存，game_id: {game_id}')
        return game_id
        
    except Error as e:
        logger.error(f'存储游戏记录失败: {e}', exc_info=True)
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)

# 存储国标麻将游戏基础统计数据到 guobiao_history_stats 表
def store_guobiao_game_stats(db_manager, game_id: int, player_list: list, room_type: str, game_round: int, total_rounds: int):
    """
    存储国标麻将游戏基础统计数据到 guobiao_history_stats 表
    
    Args:
        db_manager: DatabaseManager 实例
        game_id: 游戏ID（由 store_guobiao_game_record 返回）
        player_list: 玩家列表，每个玩家包含 record_counter 属性
        room_type: 房间规则类型（如 "guobiao"）
        game_round: 游戏局数（最大局数，如 4）
        total_rounds: 实际进行的局数
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        
        rule = room_type  # 使用传入的 room_type 作为规则
        mode = f"{game_round}/4"  # 使用传入的 game_round 构建 mode
        
        stats_columns = [
            "total_games",
            "total_rounds",
            "win_count",
            "self_draw_count",
            "deal_in_count",
            "total_fan_score",
            "total_win_turn",
            "total_fangchong_score",
            "first_place_count",
            "second_place_count",
            "third_place_count",
            "fourth_place_count"
        ]
        
        # 更新每个玩家的基础统计数据
        for player in player_list:
            # 跳过游客账户（user_id < 10000000）
            if player.user_id < 10000000:
                continue
                
            user_id = player.user_id
            counter = player.record_counter
            win_count = counter.zimo_times + counter.dianhe_times
            
            stats_increment = {
                "total_games": 1,
                "total_rounds": total_rounds,
                "win_count": win_count,
                "self_draw_count": counter.zimo_times,
                "deal_in_count": counter.fangchong_times,
                "total_fan_score": counter.win_score,
                "total_win_turn": counter.win_turn,
                "total_fangchong_score": counter.fangchong_score,
                "first_place_count": 1 if counter.rank_result == 1 else 0,
                "second_place_count": 1 if counter.rank_result == 2 else 0,
                "third_place_count": 1 if counter.rank_result == 3 else 0,
                "fourth_place_count": 1 if counter.rank_result == 4 else 0
            }
            
            insert_columns = ["user_id", "rule", "mode"] + stats_columns
            insert_values = [
                user_id,
                rule,
                mode,
                *[stats_increment.get(col, 0) for col in stats_columns]
            ]
            
            update_clauses = ", ".join(
                f"{col} = guobiao_history_stats.{col} + EXCLUDED.{col}"
                for col in stats_columns
            )
            
            cursor.execute(f"""
                INSERT INTO guobiao_history_stats (
                    {', '.join(insert_columns)}
                ) VALUES (
                    {', '.join(['%s'] * len(insert_columns))}
                )
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
            """, insert_values)
        
        conn.commit()
        logger.info(f'基础统计数据已保存，game_id: {game_id}')
        
    except Error as e:
        logger.error(f'存储基础统计数据失败: {e}', exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)

# 存储国标麻将游戏番种统计数据到 guobiao_fan_stats 表
def store_guobiao_fan_stats(db_manager, game_id: int, player_list: list, room_type: str, game_round: int):
    """
    存储国标麻将游戏番种统计数据到 guobiao_fan_stats 表
    
    Args:
        db_manager: DatabaseManager 实例
        game_id: 游戏ID（由 store_guobiao_game_record 返回）
        player_list: 玩家列表，每个玩家包含 record_counter 属性
        room_type: 房间规则类型（如 "guobiao"）
        game_round: 游戏局数（最大局数，如 4）
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        
        rule = room_type  # 使用传入的 room_type 作为规则
        mode = f"{game_round}/4"  # 使用传入的 game_round 构建 mode
        
        # 更新每个玩家的番种统计数据
        for player in player_list:
            # 跳过游客账户（user_id < 10000000）
            if player.user_id < 10000000:
                continue
                
            user_id = player.user_id
            counter = player.record_counter
            
            fan_increment = {field: 0 for field in FAN_FIELDS}
            recorded_fans = getattr(counter, "recorded_fans", [])
            for fan_entry in recorded_fans:
                if isinstance(fan_entry, list):
                    fan_iterable = fan_entry
                else:
                    fan_iterable = [fan_entry]
                
                for fan_name in fan_iterable:
                    if not isinstance(fan_name, str):
                        continue
                    if "*" in fan_name:
                        base_name, _, count_str = fan_name.partition("*")
                        base_name = base_name.strip()
                        if base_name in STACKABLE_FANS and base_name in FAN_NAME_TO_FIELD:
                            try:
                                count_val = int(count_str.strip())
                            except ValueError:
                                logger.warning(f"无法解析番种数量: {fan_name}")
                                continue
                            field = FAN_NAME_TO_FIELD[base_name]
                            fan_increment[field] += count_val
                    elif fan_name in FAN_NAME_TO_FIELD:
                        field = FAN_NAME_TO_FIELD[fan_name]
                        fan_increment[field] += 1
            
            insert_columns = ["user_id", "rule", "mode"] + FAN_FIELDS
            insert_values = [
                user_id,
                rule,
                mode,
                *[fan_increment.get(col, 0) for col in FAN_FIELDS]
            ]
            
            update_clauses = ", ".join(
                f"{col} = guobiao_fan_stats.{col} + EXCLUDED.{col}"
                for col in FAN_FIELDS
            )
            
            cursor.execute(f"""
                INSERT INTO guobiao_fan_stats (
                    {', '.join(insert_columns)}
                ) VALUES (
                    {', '.join(['%s'] * len(insert_columns))}
                )
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
            """, insert_values)
        
        conn.commit()
        logger.info(f'番种统计数据已保存，game_id: {game_id}')
        
    except Error as e:
        logger.error(f'存储番种统计数据失败: {e}', exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)

