from typing import Dict
import logging
from . import blood_battle
from .duplicate_rules import duplicate_rules_for
from .sanma import filter_tiles, round_wind, ron_action
from ..public.logic_common import get_index_relative_position, next_current_num
from ..public.hand_slot_utils import normalize_tile
from ..public.tactical_claim import add_tactical_force_pass_options

logger = logging.getLogger(__name__)

# 检查操作 返回 action_dict

# 切牌后检查 存储 吃chi_left chi_mid chi_right 碰peng 杠gang 胡hu 操作
def check_action_after_cut(self,cut_tile):
    temp_action_dict:Dict[int,list] = {i: [] for i in range(len(self.player_list))}
    duplicate_rules = duplicate_rules_for(self)

    if blood_battle.enabled(self):
        self.result_dict = {}

    # 如果牌堆内仍有牌则可以吃碰杠
    if (not duplicate_rules.is_last_discard(self.current_player_index) if duplicate_rules else self.tiles_list != []):
        # 如果切牌是万 饼 条 且下家有C+1和C-1 则可以吃
        next_player_index = (blood_battle.next_active_index(self, self.current_player_index)
                             if blood_battle.enabled(self) else next_current_num(self.current_player_index, len(self.player_list)))
        if cut_tile <= 40:
            # left 左侧吃牌 [a-2,a-1,a]
            if cut_tile-2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_left")
            # mid 中间吃牌 [a-1,a,a+1]
            if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
                if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                        temp_action_dict[next_player_index].append("chi_mid")
            # right 右侧吃牌 [a,a+1,a+2]
            if cut_tile+2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_right")

        # 如果任意一家有C=2，则可以碰
        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) >= 2:
                temp_action_dict[item.player_index].append("peng")
        
        # 检测杠牌：手牌中有3张相同的牌
        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) == 3:
                if duplicate_rules.can_draw(item.player_index) if duplicate_rules else self.tiles_list != []:
                        temp_action_dict[item.player_index].append("gang")

    # 切牌后刷新他家听牌再判荣和（避免开局未刷新 waiting_tiles 导致漏判）
    for item in self.player_list:
        if item.player_index == self.current_player_index:
            continue
        refresh_waiting_tiles(self, item.player_index)
        if cut_tile in item.waiting_tiles:
            check_hepai(self, temp_action_dict, cut_tile, item.player_index, "dianhe")

    # 如果玩家有操作则添加取消；战术鸣牌的放弃统一在返回前附带。
    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")
    
    # 不能吃碰杠胡自己的牌
    temp_action_dict[self.current_player_index] = []

    # 陪打玩家不能完成鸣牌操作
    for item in self.player_list:
        if "peida" in item.tag_list or (blood_battle.enabled(self) and getattr(item, "is_hu", False)):
            temp_action_dict[item.player_index] = []

    return add_tactical_force_pass_options(self, temp_action_dict)

# 加杠检查操作 存储 抢杠
def check_action_jiagang(self,jiagang_tile):
    if blood_battle.enabled(self):
        self.result_dict = {}
    # 如果该牌是任意家的等待牌，则可以抢杠和
    temp_action_dict:Dict[int,list] = {i: [] for i in range(len(self.player_list))}
    # 如果该牌是任意家的等待牌 且不是自己
    for item in self.player_list:
        if jiagang_tile in item.waiting_tiles and item.player_index != self.current_player_index:
            refresh_waiting_tiles(self, item.player_index)
            if jiagang_tile in item.waiting_tiles:
                check_hepai(self,temp_action_dict,jiagang_tile,item.player_index,"qianggang")
    
    # 如果玩家有操作 则添加pass
    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")

    # 陪打玩家不能抢杠操作
    for item in self.player_list:
        if "peida" in item.tag_list or (blood_battle.enabled(self) and getattr(item, "is_hu", False)):
            temp_action_dict[item.player_index] = []
    
    return add_tactical_force_pass_options(self, temp_action_dict)

# 开局检查补花操作 存储 补花buhua
def check_action_buhua(self,player_index):
    temp_action_dict:Dict[int,list] = {i: [] for i in range(len(self.player_list))}
    duplicate_rules = duplicate_rules_for(self)
    if duplicate_rules and not duplicate_rules.can_draw(player_index):
        return temp_action_dict
    if any(carditem >= 50 for carditem in self.player_list[player_index].hand_tiles):
        temp_action_dict[player_index].append("buhua")
        temp_action_dict[player_index].append("pass")
    return temp_action_dict

# 摸牌后检查操作 补花buhua 和牌hu 暗杠angang 加杠jiagang 切牌cut
def check_action_hand_action(self,player_index,is_get_gang_tile=False,is_first_action=False):
    temp_action_dict:Dict[int,list] = {i: [] for i in range(len(self.player_list))}
    player_item = self.player_list[player_index]
    duplicate_rules = duplicate_rules_for(self)
    if blood_battle.enabled(self) and getattr(player_item, "is_hu", False):
        return temp_action_dict

    # 如果牌堆内仍有牌则可以补花暗杠加杠
    if duplicate_rules.can_draw(player_index) if duplicate_rules else self.tiles_list != []:
        # 如果手牌中有花牌 则可以补花
        if any(carditem >= 50 for carditem in player_item.hand_tiles):
            if self.tiles_list != []:
                temp_action_dict[player_index].append("buhua")

        # 如果手牌中有4张相同的牌 则可以暗杠
        processed_cards = set()
        for carditem in player_item.hand_tiles:
            if carditem not in processed_cards and player_item.hand_tiles.count(carditem) == 4:
                if self.tiles_list != []:
                    temp_action_dict[player_index].append("angang")
                    processed_cards.add(carditem)
                
        # 如果组合牌中有加杠 则可以加杠
        for combination_tile in player_item.combination_tiles:
            if combination_tile[0] == "k":
                jiagang_norm = normalize_tile(int(combination_tile[1:]))
                if any(normalize_tile(t) == jiagang_norm for t in player_item.hand_tiles):
                    if self.tiles_list != []:
                        temp_action_dict[player_index].append("jiagang")

    # 摸牌后可以切牌
    temp_action_dict[player_index].append("cut")

    # 如果手牌中有等待牌 则检测和牌
    if player_item.hand_tiles[-1] in player_item.waiting_tiles:
        check_hepai(self,temp_action_dict,player_item.hand_tiles[-1],player_index,"handgot",is_first_action,is_get_gang_tile)

    # 如果玩家陪打，只允许加杠、暗杠、补花和切牌
    if "peida" in player_item.tag_list:
        allowed_actions = {"jiagang", "angang", "buhua", "cut"}
        temp_action_dict[player_index] = [action for action in temp_action_dict[player_index] if action in allowed_actions]

    return temp_action_dict

# 检查吃碰后切牌操作 存储 吃碰后切牌cut
def check_only_cut(self,player_index):
    temp_action_dict:Dict[int,list] = {i: [] for i in range(len(self.player_list))}
    temp_action_dict[player_index].append("cut")
    return temp_action_dict

# 检查等待牌操作 用来在玩家手牌发生改变时检测监听的卡牌
def refresh_waiting_tiles(self,player_index,is_first_action=False):
    if blood_battle.enabled(self) and getattr(self.player_list[player_index], "is_hu", False):
        self.player_list[player_index].waiting_tiles = set()
        return
    # 获取GuobiaoPlayer
    player_item = self.player_list[player_index]
    # 获取手牌
    current_player_hand_tiles = player_item.hand_tiles
    if is_first_action:
        current_player_hand_tiles = player_item.hand_tiles[:-1] # 第一轮行动时只计算前13张牌
    # 获取组合牌
    current_player_combination_tiles = player_item.combination_tiles
    # 调用听牌检查（使用计算服务类）
    waiting_check = (self.calculation_service.GB_lanshi_tingpai_check
                     if getattr(self, "sub_rule", "") == "guobiao/lanshi"
                     else self.calculation_service.GB_tingpai_check)
    current_player_waiting_tiles = waiting_check(
        current_player_hand_tiles,
        current_player_combination_tiles
    )
    current_player_waiting_tiles = set(filter_tiles(current_player_waiting_tiles, getattr(self, "sub_rule", "")))
    # 更新等待牌
    if current_player_waiting_tiles != self.player_list[player_index].waiting_tiles:
        self.player_list[player_index].waiting_tiles = current_player_waiting_tiles
        logger.info(f"玩家{player_index}的等待牌更新为{current_player_waiting_tiles}")

def opening_win_fan(self, player_index, hepai_type, is_get_gang_tile=False):
    """可选天地人和；补花不打断，任何吃碰杠（包括暗杠）均打断。"""
    if (not getattr(self, "tian_di_ren_he", False)
            or getattr(self, "sub_rule", "guobiao/standard") not in ("guobiao/standard", "guobiao/sanma", "guobiao/blood_battle")
            or is_get_gang_tile
            or any(player.combination_tiles for player in self.player_list)):
        return None
    discard_counts = [len(player.discard_tiles) for player in self.player_list]
    dealer = self.dealer_index
    if hepai_type == "handgot" and self.current_player_index == player_index:
        if player_index == dealer and sum(discard_counts) == 0:
            return "天和"
        if player_index != dealer and discard_counts[player_index] == 0:
            return "人和"
    if (hepai_type == "dianhe" and player_index != dealer
            and self.current_player_index == dealer
            and discard_counts[dealer] == 1 and sum(discard_counts) == 1):
        return "地和"
    return None


# 检查和牌操作
def check_hepai(self,temp_action_dict,hepai_tile,player_index,hepai_type,is_first_action=False,is_get_gang_tile=False):
    if blood_battle.enabled(self) and getattr(self.player_list[player_index], "is_hu", False):
        return
    # 和牌操作
    tiles_list = self.player_list[player_index].hand_tiles + [hepai_tile]
    combination_tiles = self.player_list[player_index].combination_tiles
    way_to_hepai = ["花牌"] * len(self.player_list[player_index].huapai_list)
    duplicate_rules = duplicate_rules_for(self)

    # 抢杠和
    if hepai_type == "qianggang":
        way_to_hepai.append("抢杠和")
        way_to_hepai.append("点和")

    # 荣和
    elif hepai_type == "dianhe":
        way_to_hepai.append("点和")
        if duplicate_rules.is_last_discard(self.current_player_index) if duplicate_rules else len(self.tiles_list) == 0:
            way_to_hepai.append("last_cut")  # 牌墙空荣和 → 海底捞月

    # 自摸 / 杠上开花（杠上开花需同时传自摸，计番侧据此判不求人）
    elif hepai_type == "handgot":
        tiles_list = tiles_list[:-1] # 删除最后一张牌
        way_to_hepai.append("自摸")
        if is_get_gang_tile:
            way_to_hepai.append("杠上开花")
        if duplicate_rules.is_last_draw(player_index) if duplicate_rules else len(self.tiles_list) == 0:
            way_to_hepai.append("last_deal")  # 牌墙空自摸 → 妙手回春

    # 每圈按实际人数轮庄。
    way_to_hepai.append("场风" + round_wind(self.current_round, len(self.player_list)))
    # 自风检查
    if self.player_list[player_index].player_index == 0:
        way_to_hepai.append("自风东")
    elif self.player_list[player_index].player_index == 1:
        way_to_hepai.append("自风南")
    elif self.player_list[player_index].player_index == 2:
        way_to_hepai.append("自风西")
    elif self.player_list[player_index].player_index == 3:
        way_to_hepai.append("自风北")
    # 和单张检查
    if len(self.player_list[player_index].waiting_tiles) == 1:
        way_to_hepai.append("和单张")

    opening_fan = opening_win_fan(self, player_index, hepai_type, is_get_gang_tile)
    if opening_fan:
        way_to_hepai.append(opening_fan)

    # 蓝十改天和/地和。以“尚无任何鸣牌或杠”和各家首次出牌状态判定。
    if getattr(self, "sub_rule", "") == "guobiao/lanshi":
        no_calls_or_kongs = all(not player.combination_tiles for player in self.player_list)
        discard_counts = [len(player.discard_tiles) for player in self.player_list]
        if hepai_type == "handgot" and no_calls_or_kongs and discard_counts[player_index] == 0:
            if player_index == self.dealer_index and sum(discard_counts) == 0:
                way_to_hepai.extend(("天和", "庄家起手"))
            elif player_index != self.dealer_index:
                way_to_hepai.append("天和")
        elif (hepai_type == "dianhe" and no_calls_or_kongs and player_index != self.dealer_index
              and self.current_player_index == self.dealer_index
              and discard_counts[self.dealer_index] == 1 and sum(discard_counts) == 1):
            way_to_hepai.append("地和")

    # 和绝张检查 弃牌+1 有顺子+1 有刻+2
    show_tiles_count = (getattr(self, "blood_public_win_tiles", []).count(hepai_tile)
                        if blood_battle.enabled(self) else 0)
    now_combinations = []
    for i in self.player_list:
        show_tiles_count += i.discard_tiles.count(hepai_tile)
        now_combinations.extend(i.combination_tiles)
    for i in now_combinations:
        if f"k{hepai_tile}" in i:
            show_tiles_count += 3
        if f"s{hepai_tile-1}" in i:
            show_tiles_count += 1
        if f"s{hepai_tile}" in i:
            show_tiles_count += 1
        if f"s{hepai_tile+1}" in i:
            show_tiles_count += 1
    if show_tiles_count == 4:
        way_to_hepai.append("和绝张")
    elif show_tiles_count == 3:
        if "自摸" in way_to_hepai:
            way_to_hepai.append("和绝张")

    # 第一轮行动时移除独听番种
    if is_first_action:
        if "和单张" in way_to_hepai:
            way_to_hepai.remove("和单张")
        elif "和绝张" in way_to_hepai:
            way_to_hepai.remove("和绝张")

    # 使用计算服务类检查和牌（根据子规则选择检查方法）
    if hasattr(self, 'sub_rule') and self.sub_rule == "guobiao/xiaolin":
        result = self.calculation_service.GB_xiaolin_hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tile)
    elif hasattr(self, 'sub_rule') and self.sub_rule == "guobiao/kshen":
        result = self.calculation_service.GB_kshen_hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tile)
    elif hasattr(self, 'sub_rule') and self.sub_rule == "guobiao/lanshi":
        result = self.calculation_service.GB_lanshi_hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tile)
    else:
        result = self.calculation_service.GB_hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tile)

    if result[0] == 0 and not result[1]:
        # 蓝十没有国标的无番和：合法结构也可能为0分。仅允许已由本规则
        # 原生听牌确认的和张进入错和分支；不能把任意未成和手牌放行。
        zero_point_lanshi = (getattr(self, "sub_rule", "") == "guobiao/lanshi"
                             and self.open_cuohe
                             and hepai_tile in self.player_list[player_index].waiting_tiles)
        if not zero_point_lanshi:
            return

    # 花牌不计起和门槛；错和模式仍允许合法但不足番的牌型报和。
    hepai_limit = getattr(self, 'hepai_limit', 8)
    huapai_count = way_to_hepai.count("花牌")
    if result[0] - huapai_count >= hepai_limit or self.open_cuohe:
        action = ron_action(player_index, self.current_player_index, len(self.player_list))
        temp_action_dict[player_index].append(action)
        self.result_dict[action] = result
