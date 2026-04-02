# 牌效AI机器人（牌效罗伯特）
# 基于向听数 + 进张数的牌效率决策，支持吃碰推进和牌形
# 能和则和、能补花则补花、切牌/吃碰根据评分决策
import asyncio
import logging
from .get_action import get_ai_action
from .smart_bot_logic import (
    count_melds, count_visible_tiles, evaluate_hand,
    find_best_cut, find_best_cut_score, should_accept_hu,
)

logger = logging.getLogger(__name__)

# 牌效AI机器人，支持补花询问，手牌询问，其他玩家询问，抢杠询问
async def smart_bot_action(game_state, player_index: int, action_list: list, game_status: str):
    """
    牌效AI自动操作
    规则：能和必和 > 能补花必补花 > 牌效率评估切牌/吃碰杠

    Args:
        game_state: 游戏状态对象
        player_index: 玩家索引
        action_list: 可用操作列表
        game_status: 游戏状态
    """
    try:
        await asyncio.sleep(0.5)
        current_player = game_state.player_list[player_index]

        if game_status == "waiting_hand_action":
            # 摸牌后手牌操作：和牌 > 暗杠/加杠 > 切牌
            await _handle_hand_action(game_state, player_index, action_list, current_player)
            return

        elif game_status == "onlycut_after_action":
            # 吃碰后手牌操作：和牌 > 切牌（不可暗杠/加杠）
            await _handle_hand_action(game_state, player_index, action_list, current_player)
            return

        elif game_status == "waiting_action_after_cut":
            # 他家切牌后：和牌 > 碰/吃/明杠评估 > pass
            await _handle_after_cut(game_state, player_index, action_list, current_player)
            return

        elif game_status == "waiting_action_qianggang":
            # 他家加杠时的抢杠询问：能和就和，否则pass
            await _handle_qianggang(game_state, player_index, action_list, current_player)
            return

        elif game_status == "waiting_buhua_round":
            # 补花轮询问：能补花就补花，有国士和牌就和，否则pass
            await _handle_buhua_round(game_state, player_index, action_list, current_player)
            return

        else:
            logger.warning(f"牌效AI {player_index} 遇到未知游戏状态: {game_status}")

    except Exception as e:
        logger.error(f"牌效AI {player_index} 自动操作失败: {e}", exc_info=True)


async def _handle_hand_action(game_state, player_index, action_list, player):
    """摸牌后/吃碰后的手牌阶段：补花 > 和牌 > 暗杠/加杠 > 牌效切牌"""
    # 有花牌必须先补花（手牌含花牌时不能做其他操作）
    if "buhua" in action_list:
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 buhua（手牌补花）")
        await get_ai_action(game_state, player_index, "buhua", None, None, None, None)
        return

    # 能和且满足起和番则和（避免错和）
    if "hu_self" in action_list and should_accept_hu(game_state, player_index, "hu_self"):
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 hu_self")
        await get_ai_action(game_state, player_index, "hu_self", None, None, None, None)
        return

    hand = player.hand_tiles[:]
    combs = getattr(player, 'combination_tiles', [])
    meld_count = count_melds(combs)
    visible = count_visible_tiles(game_state)

    # 评估暗杠：暗杠后手牌不变差则执行
    if "angang" in action_list:
        for tile in set(hand):
            if hand.count(tile) >= 4:
                test_hand = [t for t in hand if t != tile]
                base_score = evaluate_hand(hand, meld_count, visible)
                gang_score = evaluate_hand(test_hand, meld_count + 1, visible)
                if gang_score >= base_score:
                    logger.info(f"牌效AI {player_index} ({player.username}) 选择 angang, tile={tile}")
                    await get_ai_action(game_state, player_index, "angang", None, None, None, tile)
                    return

    # 评估加杠：加杠后手牌不变差则执行
    if "jiagang" in action_list:
        for c in combs:
            if c.startswith("k"):
                try:
                    ktile = int(c[1:])
                except ValueError:
                    continue
                if ktile in hand:
                    test_hand = hand[:]
                    test_hand.remove(ktile)
                    base_score = evaluate_hand(hand, meld_count, visible)
                    jia_score = evaluate_hand(test_hand, meld_count, visible)
                    if jia_score >= base_score:
                        logger.info(f"牌效AI {player_index} ({player.username}) 选择 jiagang, tile={ktile}")
                        await get_ai_action(game_state, player_index, "jiagang", None, None, None, ktile)
                        return

    # 切牌：枚举每张手牌切出后的评分，选最优
    if "cut" in action_list and hand:
        tile_id, cut_index = find_best_cut(hand, meld_count, visible)
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 cut, tile_id={tile_id}")
        await get_ai_action(game_state, player_index, "cut", True, tile_id, cut_index, None)
        return


async def _handle_after_cut(game_state, player_index, action_list, player):
    """他家切牌后的响应阶段：和牌 > 碰/吃/明杠评估 > pass"""
    # 能和且满足起和番则和（避免错和）
    for hu_action in ("hu_first", "hu_second", "hu_third"):
        if hu_action in action_list and should_accept_hu(game_state, player_index, hu_action):
            logger.info(f"牌效AI {player_index} ({player.username}) 选择 {hu_action}")
            await get_ai_action(game_state, player_index, hu_action, None, None, None, None)
            return

    # 获取被切出的牌
    discard_tiles = game_state.player_list[game_state.current_player_index].discard_tiles
    cut_tile = discard_tiles[-1] if discard_tiles else None
    if cut_tile is None:
        if "pass" in action_list:
            await get_ai_action(game_state, player_index, "pass", None, None, None, None)
        return

    hand = player.hand_tiles[:]
    combs = getattr(player, 'combination_tiles', [])
    meld_count = count_melds(combs)
    visible = count_visible_tiles(game_state)

    # 当前手牌基准评分（不做任何操作时 pass 后的手牌价值）
    base_score = evaluate_hand(hand, meld_count, visible)
    best_action = "pass"
    best_action_score = base_score

    # 评估碰：碰后需要切一张，取切后最优评分
    if "peng" in action_list and hand.count(cut_tile) >= 2:
        test_hand = hand[:]
        test_hand.remove(cut_tile)
        test_hand.remove(cut_tile)
        if test_hand:
            peng_best = find_best_cut_score(test_hand, meld_count + 1, visible)
            if peng_best > best_action_score:
                best_action = "peng"
                best_action_score = peng_best

    # 评估吃：吃后需要切一张，取切后最优评分
    for chi_type in ("chi_left", "chi_mid", "chi_right"):
        if chi_type not in action_list:
            continue
        if chi_type == "chi_left":
            need = [cut_tile - 2, cut_tile - 1]
        elif chi_type == "chi_mid":
            need = [cut_tile - 1, cut_tile + 1]
        else:
            need = [cut_tile + 1, cut_tile + 2]
        test_hand = hand[:]
        valid = True
        for n in need:
            if n in test_hand:
                test_hand.remove(n)
            else:
                valid = False
                break
        if not valid:
            continue
        if test_hand:
            chi_best = find_best_cut_score(test_hand, meld_count + 1, visible)
            if chi_best > best_action_score:
                best_action = chi_type
                best_action_score = chi_best

    # 评估明杠：杠后不需要切牌，直接评估手牌
    if "gang" in action_list and hand.count(cut_tile) >= 3:
        test_hand = hand[:]
        for _ in range(3):
            test_hand.remove(cut_tile)
        gang_score = evaluate_hand(test_hand, meld_count + 1, visible)
        if gang_score > best_action_score:
            best_action = "gang"
            best_action_score = gang_score

    logger.info(f"牌效AI {player_index} ({player.username}) 选择 {best_action} (score={best_action_score})")
    await get_ai_action(game_state, player_index, best_action, None, None, None, None)


async def _handle_qianggang(game_state, player_index, action_list, player):
    """他家加杠时的抢杠询问：能和就和，否则pass"""
    # 抢杠和（满足起和番才和）
    for hu_action in ("hu_first", "hu_second", "hu_third"):
        if hu_action in action_list and should_accept_hu(game_state, player_index, hu_action):
            logger.info(f"牌效AI {player_index} ({player.username}) 选择 {hu_action}（抢杠和）")
            await get_ai_action(game_state, player_index, hu_action, None, None, None, None)
            return
    # 没有和牌选项则pass
    if "pass" in action_list:
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 pass（抢杠）")
        await get_ai_action(game_state, player_index, "pass", None, None, None, None)


async def _handle_buhua_round(game_state, player_index, action_list, player):
    """补花轮询问：能补花就补花，有和牌（国士/九老峰回）就和，否则pass"""
    # 补花必须直接补花
    if "buhua" in action_list:
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 buhua")
        await get_ai_action(game_state, player_index, "buhua", None, None, None, None)
        return
    # 国士无双等特殊和牌（满足起和番才和）
    if "hu_self" in action_list and should_accept_hu(game_state, player_index, "hu_self"):
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 hu_self（补花轮）")
        await get_ai_action(game_state, player_index, "hu_self", None, None, None, None)
        return
    # 九老峰回流局选择
    if "jiuzhongjiupai" in action_list:
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 pass（放弃九老峰回）")
        await get_ai_action(game_state, player_index, "pass", None, None, None, None)
        return
    # 默认pass
    if "pass" in action_list:
        logger.info(f"牌效AI {player_index} ({player.username}) 选择 pass（补花轮）")
        await get_ai_action(game_state, player_index, "pass", None, None, None, None)
