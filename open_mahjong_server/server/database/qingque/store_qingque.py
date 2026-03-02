"""
青雀麻将游戏记录和统计数据存储方法
"""
import json
import logging
import string
import secrets
from psycopg2 import Error
from typing import Optional

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits  # a-z A-Z 0-9
GAME_ID_LENGTH = 10

def generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))

# 番种定义映射（用于统计），基于 FanTextDictionary.cs 的 FanToDisplayQingque
FAN_NAME_TO_FIELD = {
    "和牌": "hepai",
    "天和": "tianhe",
    "地和": "dihe",
    "岭上开花": "lingshangkaihua",
    "海底捞月": "haidilaoyue",
    "河底捞鱼": "hedilaoyue",
    "抢杠": "qianggang",
    "七对": "qidui",
    "门前清": "menqianqing",
    "四暗杠": "siangang",
    "三暗杠": "sanangang",
    "双暗杠": "shuangangang",
    "暗杠": "angang",
    "四杠": "sigang",
    "三杠": "sangang",
    "双杠": "shuanggang",
    "四暗刻": "sianke",
    "三暗刻": "sananke",
    "对对和": "duiduihe",
    "十二归": "shiergui",
    "八归": "bagui",
    "三叠对": "sandiedui",
    "二叠对": "erdiedui",
    "叠对": "diedui",
    "字一色": "ziyise",
    "大四喜": "dasixi",
    "小四喜": "xiaosixi",
    "四喜对": "sixidui",
    "风牌三刻": "fengpaisanke",
    "风牌七对": "fengpaiqidui",
    "风牌六对": "fengpailiudui",
    "风牌五对": "fengpaiwudui",
    "风牌四对": "fengpaisidui",
    "大三元": "dasanyuan",
    "小三元": "xiaosanyuan",
    "三元六对": "sanyuanliudui",
    "三元对": "sanyuandui",
    "番牌四刻": "fanpaisike",
    "番牌三刻": "fanpaisanke",
    "番牌二刻": "fanpaierke",
    "番牌刻": "fanpaike",
    "番牌七对": "fanpaiqidui",
    "番牌六对": "fanpailiudui",
    "番牌五对": "fanpaiwudui",
    "番牌四副": "fanpaisifu",
    "番牌三副": "fanpaisanfu",
    "番牌二副": "fanpaierfu",
    "番牌": "fanpai",
    "清幺九": "qingyaojiu",
    "混幺九": "hunyaojiu",
    "清带幺": "qingdaiyao",
    "混带幺": "hundaiyao",
    "九莲宝灯": "jiulianbaodeng",
    "清一色": "qingyise",
    "混一色": "hunyise",
    "五门齐": "wumenqi",
    "混一数": "hunyishu",
    "二数": "ershu",
    "二聚": "erju",
    "三聚": "sanju",
    "四聚": "siju",
    "连数": "lianshu",
    "间数": "jianshu",
    "镜数": "jingshu",
    "映数": "yingshu",
    "满庭芳": "mantingfang",
    "四同顺": "sitongshun",
    "三同顺": "santongshun",
    "二般高": "erbangao",
    "一般高": "yibangao",
    "四连刻": "silianke",
    "三连刻": "sanlianke",
    "四步高": "sibugao",
    "三步高": "sanbugao",
    "四连环": "silianhuan",
    "三连环": "sanlianhuan",
    "一气贯通": "yiqiguantong",
    "七连对": "qiliandui",
    "六连对": "liuliandui",
    "五连对": "wuliandui",
    "四连对": "siliandui",
    "三色同刻": "sansetongke",
    "三色同顺": "sansetongshun",
    "三色二对": "sanseedui",
    "三色同对": "sansetongdui",
    "三色连刻": "sanselianke",
    "三色贯通": "sanseguantong",
    "镜同": "jingtong",
    "镜同三对": "jingtongsandui",
    "镜同二对": "jingtongerdui",
    "双龙会": "shuanglonghui",
}

FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))


def store_qingque_game_record(db_manager, game_record: dict, player_list: list, room_type: str):
    """
    存储青雀麻将游戏牌谱记录和玩家对局记录
    
    Args:
        db_manager: DatabaseManager 实例
        game_record: 游戏牌谱记录字典
        player_list: 玩家列表，每个玩家包含 record_counter 属性
        room_type: 房间规则类型（如 "qingque"）
    
    Returns:
        成功返回 game_id，失败返回 None
    """
    conn = None
    try:
        if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
            logger.info("对局包含机器人，跳过牌谱与对局记录保存")
            return None

        conn = db_manager._get_connection()
        cursor = conn.cursor()
        
        game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)
        max_retries = 5
        game_id = None
        for _ in range(max_retries):
            candidate_id = generate_game_id()
            try:
                cursor.execute(
                    "INSERT INTO game_records (game_id, record) VALUES (%s, %s)",
                    (candidate_id, game_record_json)
                )
                game_id = candidate_id
                break
            except Error:
                conn.rollback()
                logger.warning(f'game_id 碰撞: {candidate_id}, 重试...')
                continue
        if game_id is None:
            logger.error('多次生成 game_id 均碰撞，存储失败')
            return None
        logger.info(f'牌谱记录已保存到 game_records 表，game_id: {game_id}')
        
        rule = room_type
        game_title = game_record.get("game_title") or {}
        sub_rule = game_title.get("sub_rule") or "qingque/standard"
        saved_count = 0
        for player in player_list:
            rank = player.record_counter.rank_result
            title_used = getattr(player, 'title_used', None)
            character_used = getattr(player, 'character_used', None)
            profile_used = getattr(player, 'profile_used', None)
            voice_used = getattr(player, 'voice_used', None)

            try:
                cursor.execute("""
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, rule, sub_rule, title_used, character_used, profile_used, voice_used
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                """, (
                    game_id, player.user_id, player.username, player.score, rank, rule, sub_rule,
                    title_used, character_used, profile_used, voice_used
                ))
                saved_count += 1
            except Error as e:
                logger.warning(f'跳过玩家对局记录存储: user_id={player.user_id}, username={player.username}, error={e}')
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


def store_qingque_game_stats(db_manager, game_id: str, player_list: list, room_type: str, game_round: int, total_rounds: int):
    """
    存储青雀麻将游戏基础统计数据到 qingque_history_stats 表
    """
    conn = None
    try:
        if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
            logger.info("对局包含机器人，跳过基础统计保存")
            return

        conn = db_manager._get_connection()
        cursor = conn.cursor()
        
        rule = room_type
        mode = f"{game_round}/4"
        
        stats_columns = [
            "total_games", "total_rounds", "win_count", "self_draw_count",
            "deal_in_count", "total_fan_score", "total_win_turn",
            "total_fangchong_score", "first_place_count", "second_place_count",
            "third_place_count", "fourth_place_count"
        ]
        
        for player in player_list:
            user_id = player.user_id
            if user_id <= 10000000:
                continue
            
            cursor.execute("SELECT 1 FROM users WHERE user_id = %s", (user_id,))
            if cursor.fetchone() is None:
                continue
            
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
            insert_values = [user_id, rule, mode] + [stats_increment.get(col, 0) for col in stats_columns]
            
            update_clauses = ", ".join(
                f"{col} = qingque_history_stats.{col} + EXCLUDED.{col}"
                for col in stats_columns
            )
            
            cursor.execute(f"""
                INSERT INTO qingque_history_stats (
                    {', '.join(insert_columns)}
                ) VALUES (
                    {', '.join(['%s'] * len(insert_columns))}
                )
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
            """, insert_values)
        
        conn.commit()
        logger.info(f'青雀基础统计数据已保存，game_id: {game_id}')
        
    except Error as e:
        logger.error(f'存储青雀基础统计数据失败: {e}', exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def store_qingque_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, game_round: int):
    """
    存储青雀麻将游戏番种统计数据到 qingque_fan_stats 表
    """
    conn = None
    try:
        if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
            logger.info("对局包含机器人，跳过番种统计保存")
            return

        conn = db_manager._get_connection()
        cursor = conn.cursor()
        
        rule = room_type
        mode = f"{game_round}/4"
        
        for player in player_list:
            user_id = player.user_id
            if user_id <= 10000000:
                continue
            
            cursor.execute("SELECT 1 FROM users WHERE user_id = %s", (user_id,))
            if cursor.fetchone() is None:
                continue
            
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
                    if fan_name in FAN_NAME_TO_FIELD:
                        field = FAN_NAME_TO_FIELD[fan_name]
                        fan_increment[field] += 1
            
            insert_columns = ["user_id", "rule", "mode"] + FAN_FIELDS
            insert_values = [user_id, rule, mode] + [fan_increment.get(col, 0) for col in FAN_FIELDS]
            
            update_clauses = ", ".join(
                f"{col} = qingque_fan_stats.{col} + EXCLUDED.{col}"
                for col in FAN_FIELDS
            )
            
            cursor.execute(f"""
                INSERT INTO qingque_fan_stats (
                    {', '.join(insert_columns)}
                ) VALUES (
                    {', '.join(['%s'] * len(insert_columns))}
                )
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
            """, insert_values)
        
        conn.commit()
        logger.info(f'青雀番种统计数据已保存，game_id: {game_id}')
        
    except Error as e:
        logger.error(f'存储青雀番种统计数据失败: {e}', exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
