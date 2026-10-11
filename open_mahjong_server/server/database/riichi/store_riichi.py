"""
立直麻将游戏记录存储：与国标/古典相同，写入 game_records + game_player_records。
"""
import json
import logging
import string
import secrets
from typing import Optional
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits
GAME_ID_LENGTH = 10


def _generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))


def store_riichi_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str) -> Optional[str]:
    if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
        logger.info("立直对局包含机器人，跳过牌谱与对局记录保存")
        return None

    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)
        max_retries = 5
        game_id = None
        for _ in range(max_retries):
            candidate_id = _generate_game_id()
            try:
                cursor.execute(
                    "INSERT INTO game_records (game_id, record) VALUES (%s, %s)",
                    (candidate_id, game_record_json),
                )
                game_id = candidate_id
                break
            except Error:
                conn.rollback()
                continue
        if game_id is None:
            logger.error("立直牌谱多次生成 game_id 均失败")
            return None

        game_title = game_record.get("game_title") or {}
        rule = game_title["rule"]
        sub_rule = game_title["sub_rule"]
        match_tier = game_title.get("match_tier")
        event_id = game_title.get("event_id")
        from ..scene_stats import normalize_scene_fields
        room_type, match_tier, event_id = normalize_scene_fields(room_type, match_tier, event_id)
        saved_count = 0
        for player in player_list:
            rank = player.record_counter.rank_result
            try:
                cursor.execute("""
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, original_player_index, rule, sub_rule, match_type, room_type, match_tier, event_id,
                        title_used, character_used, profile_used, voice_used, avatar_frame_used
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                """, (
                    game_id,
                    player.user_id,
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
                    getattr(player, "title_used", None),
                    getattr(player, "character_used", None),
                    getattr(player, "profile_used", None),
                    getattr(player, "voice_used", None),
                    getattr(player, "avatar_frame_used", 0),
                ))
                saved_count += 1
            except Exception as e:
                logger.warning(f"跳过立直玩家对局记录: user_id={player.user_id}, error={e}")
                raise

        from ..player_recent_records import update_player_recent_records
        from .record_stats import write_stats
        for player in player_list:
            write_stats(cursor, game_id, player.user_id, game_record,
                        player.original_player_index, player.score, player.record_counter.rank_result)
        update_player_recent_records(cursor, game_id, game_record)
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
        logger.error(f"立直牌谱存储失败: {e}", exc_info=True)
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            db_manager._put_connection(conn)


def store_riichi_game_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int, total_rounds: int) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "riichi")


# 役种定义映射（与 FanTextDictionary.FanToDisplayRiichi / Inactive 一致，不含错和）
FAN_NAME_TO_FIELD = {
    "立直": "riichi",
    "门前清自摸和": "menzen_tsumo",
    "平和": "pinfu",
    "断幺九": "tanyao",
    "一杯口": "iipeikou",
    "役牌·白": "yakuhai_haku",
    "役牌·发": "yakuhai_hatsu",
    "役牌·中": "yakuhai_chun",
    "自风·东": "jikaze_ton",
    "自风·南": "jikaze_nan",
    "自风·西": "jikaze_sha",
    "自风·北": "jikaze_pe",
    "场风·东": "bakaze_ton",
    "场风·南": "bakaze_nan",
    "场风·西": "bakaze_sha",
    "场风·北": "bakaze_pe",
    "岭上开花": "rinshan",
    "枪杠": "chankan",
    "海底捞月": "haitei",
    "河底捞鱼": "houtei",
    "一发": "ippatsu",
    "宝牌": "dora",
    "赤宝牌": "akadora",
    "里宝牌": "uradora",
    "双立直": "daburi_riichi",
    "三色同刻": "sanshoku_doukou",
    "三杠子": "san_kantsu",
    "对对和": "toitoi",
    "三暗刻": "sanankou",
    "小三元": "shousangen",
    "混老头": "honroutou",
    "七对子": "chiitoitsu",
    "混全带幺九": "chanta",
    "一气通贯": "ittsu",
    "三色同顺": "sanshoku_doujun",
    "一气通贯（门清）": "ittsu_menzen",
    "一气通贯（食下）": "ittsu_shitachi",
    "三色同顺（门清）": "sanshoku_doujun_menzen",
    "三色同顺（食下）": "sanshoku_doujun_shitachi",
    "混全带幺九（门清）": "chanta_menzen",
    "混全带幺九（食下）": "chanta_shitachi",
    "纯全带幺九（门清）": "junchan_menzen",
    "纯全带幺九（食下）": "junchan_shitachi",
    "混一色（门清）": "honitsu_menzen",
    "混一色（食下）": "honitsu_shitachi",
    "清一色（门清）": "chinitsu_menzen",
    "清一色（食下）": "chinitsu_shitachi",
    "二杯口": "ryanpeikou",
    "纯全带幺九": "junchan",
    "混一色": "honitsu",
    "清一色": "chinitsu",
    "天和": "tenhou",
    "地和": "chiihou",
    "大三元": "daisangen",
    "四暗刻": "suuankou",
    "字一色": "tsuuiisou",
    "绿一色": "ryuuiisou",
    "清老头": "chinroutou",
    "国士无双": "kokushi",
    "小四喜": "shousuushii",
    "四杠子": "suukantsu",
    "九莲宝灯": "chuuren",
    "四暗刻单骑": "suuankou_tanki",
    "国士无双十三面": "kokushi_juusan",
    "纯正九莲宝灯": "chuuren_junsei",
    "大四喜": "daisuushii",
    "开立直": "open_riichi",
    "双倍开立直": "double_open_riichi",
    "人和": "renhou",
    "流局满贯": "nagashi_mangan",
    "大七星": "daichisei",
    "大车轮": "daisharin",
    "八连庄": "paarenchan",
    "包牌": "sashikomi",
}

STACKABLE_FANS = ["宝牌", "里宝牌", "赤宝牌"]
EXCLUDED_FAN_NAMES = frozenset({"错和"})
FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))


def _accumulate_yaku_stats(fan_increment: dict, yaku_names: list) -> None:
    for fan_name in yaku_names:
        if not isinstance(fan_name, str) or fan_name in EXCLUDED_FAN_NAMES:
            continue
        if "*" in fan_name:
            base_name, _, count_str = fan_name.partition("*")
            base_name = base_name.strip()
            if base_name in STACKABLE_FANS and base_name in FAN_NAME_TO_FIELD:
                try:
                    count_val = int(count_str.strip())
                except ValueError:
                    logger.warning("无法解析立直役种数量: %s", fan_name)
                    continue
                field = FAN_NAME_TO_FIELD[base_name]
                fan_increment[field] += count_val
        elif fan_name in FAN_NAME_TO_FIELD:
            field = FAN_NAME_TO_FIELD[fan_name]
            fan_increment[field] += 1


def store_riichi_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "riichi")
