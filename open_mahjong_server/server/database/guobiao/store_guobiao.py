"""
国标麻将游戏记录和统计数据存储方法
"""
import json
import logging
import string
import secrets
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits  # a-z A-Z 0-9
GAME_ID_LENGTH = 10

def generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))

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
    "七对": "qiduizi",
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
def store_guobiao_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    """
    存储国标麻将游戏牌谱记录和玩家对局记录
    
    Args:
        db_manager: DatabaseManager 实例
        game_record: 游戏牌谱记录字典
        player_list: 玩家列表，每个玩家包含 record_counter 属性
        room_type: 房间类型（custom/match，保留参数以兼容调用方）
        match_type: 局数/模式，由外部显式传入（如 "4/4"、"1/4_rank"），保存到 match_type 字段
    
    Returns:
        成功返回 game_id，失败返回 None
    """
    conn = None
    try:
        # 对局包含机器人时，不保存牌谱/对局记录
        if not (game_record.get("game_title") or {}).get("duplicate_key") and any(getattr(p, "user_id", 0) <= 10 for p in player_list):
            logger.info("对局包含机器人，跳过牌谱与对局记录保存")
            return None

        conn = db_manager._get_connection()
        cursor = conn.cursor()
        
        # 1. 存储牌谱记录到 game_records 表（生成 base62 game_id，碰撞时重试）
        game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)
        max_retries = 5
        game_id = None
        for _ in range(max_retries):
            candidate_id = (game_record.get("game_title") or {}).get("duplicate_game_id") or generate_game_id()
            try:
                cursor.execute(
                    "INSERT INTO game_records (game_id, record) VALUES (%s, %s)",
                    (candidate_id, game_record_json)
                )
                game_id = candidate_id
                break
            except Exception as dup_err:
                conn.rollback()
                logger.warning(f'game_id 碰撞: {candidate_id}, 重试...')
                continue
        if game_id is None:
            logger.error('多次生成 game_id 均碰撞，存储失败')
            return None
        logger.info(f'牌谱记录已保存到 game_records 表，game_id: {game_id}')
        
        # 2. 存储玩家对局记录到 game_player_records 表（rule=游戏规则，match_type=局数类型）
        game_title = game_record.get("game_title") or {}
        rule = game_title["rule"]
        sub_rule = game_title["sub_rule"]
        match_tier = game_title.get("match_tier")
        event_id = game_title.get("event_id")
        from ..scene_stats import normalize_scene_fields
        room_type, match_tier, event_id = normalize_scene_fields(room_type, match_tier, event_id)
        saved_count = 0
        for player in player_list:
            rank = player.record_counter.rank_result  # 1-4
            title_used = getattr(player, 'title_used', None)
            character_used = getattr(player, 'character_used', None)
            profile_used = getattr(player, 'profile_used', None)
            voice_used = getattr(player, 'voice_used', None)
            avatar_frame_used = getattr(player, "avatar_frame_used", 0)
            actual_user_id = player.user_id
            # 使用结算广播的 PT（含并列名次均摊），升降段后的余额差并非本场 PT。
            pt_change = getattr(player, "pt", None) if room_type == "match" else None

            try:
                cursor.execute("""
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, original_player_index, rule, sub_rule, match_type, room_type, match_tier, event_id, title_used, character_used, profile_used, voice_used, avatar_frame_used, pt_change
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                """, (
                    game_id,
                    actual_user_id,
                    player.username,
                    player.score,
                    rank,
                    player.original_player_index,
                    rule,
                    sub_rule,
                    match_type,
                    room_type,
                    match_tier,
                    event_id,
                    title_used,
                    character_used,
                    profile_used,
                    voice_used,
                    avatar_frame_used,
                    pt_change
                ))
                saved_count += 1
            except Exception as e:
                logger.warning(f'跳过玩家对局记录存储: user_id={player.user_id}, username={player.username}, error={e}')
                raise
        logger.info(f'已为 {saved_count} 名玩家保存对局记录到 game_player_records 表')
        
        from ..player_recent_records import update_player_recent_records
        if not (game_record.get("game_title") or {}).get("duplicate_key"):
            update_player_recent_records(cursor, game_id, game_record)
        from ..duplicate_walls import link_duplicate_record
        link_duplicate_record(cursor, game_id, game_record)
        # 牌谱、参与者和统计同事务发布，成功保存不依赖后续回填。
        from ..scene_stats import record_game_metrics
        record_game_metrics(db_manager, game_id, game_record, player_list, {
            "rule": rule, "sub_rule": sub_rule, "room_type": room_type,
            "match_tier": match_tier, "event_id": event_id, "match_type": match_type,
        }, cursor=cursor)
        conn.commit()
        logger.info("牌谱及子规则统计已保存，game_id: %s", game_id)
        return game_id
        
    except Exception as e:
        logger.error(f'存储游戏记录失败: {e}', exc_info=True)
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)

# 存储国标麻将游戏基础统计数据到 guobiao_history_stats 表
def store_guobiao_game_stats(db_manager, game_id: str, player_list: list, room_type: str, match_type: str, total_rounds: int):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "guobiao")

# 存储国标麻将游戏番种统计数据到 guobiao_fan_stats 表
def store_guobiao_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, match_type: str):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "guobiao")

