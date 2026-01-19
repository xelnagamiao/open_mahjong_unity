from typing import Dict
import random
from time import time
import logging

logger = logging.getLogger(__name__)

class PlayerTiles:
    def __init__(self, tiles_list, combination_list,complete_step):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step # +3 +3 +3 +3 +2 = 14
    
    def __deepcopy__(self, memo):
        return PlayerTiles(self.hand_tiles[:],
                         self.combination_list[:], 
                         self.complete_step)

class Chinese_Tingpai_Check:
    yaojiu = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47}
    zipai = {41, 42, 43, 44, 45, 46, 47}
    
    def __init__(self, debug=False):
        self.waiting_tiles = set()
        self.temp_waiting_tiles = set()
        self.debug = debug  # 添加debug标志
    
    def debug_print(self, *args, **kwargs):
        """只在debug模式下打印"""
        if self.debug:
            logger.debug(*args, **kwargs)

    def check_waiting_tiles(self, player_tiles: PlayerTiles):
        # 清空之前的结果
        self.waiting_tiles.clear()
        self.temp_waiting_tiles.clear()
        
        # 13张牌检查特殊牌型
        if len(player_tiles.hand_tiles) == 13:
            self.GS_check(player_tiles.hand_tiles)  # 国士无双检查
            self.QD_check(player_tiles.hand_tiles)  # 七对子检查

        if self.QBK_check(player_tiles):  # 全不靠检查
            return self.waiting_tiles
            
        self.normal_check(player_tiles) # 一般型检查
        
        self.debug_print(self.waiting_tiles)

        return self.waiting_tiles

    def GS_check(self, hand_tiles):
        # 检查国士
        GS_step_set = set()
        GS_allowed = True
        # 如果牌存在于幺九集合,则加入手牌幺九集合
        for tile_id in hand_tiles:
            if tile_id in self.yaojiu:
                GS_step_set.add(tile_id)
            else:
                GS_allowed = False
        # 如果牌都是幺九 并且满足12种或者13种 前者添加第十三种 后者添加全部十三种
        if GS_allowed:
            if len(GS_step_set) == 12:
                for i in self.yaojiu:
                    if i not in hand_tiles:
                        self.waiting_tiles.add(i)
            elif len(GS_step_set) == 13:
                for i in self.yaojiu:
                    self.waiting_tiles.add(i)
        
    def QD_check(self, hand_tiles):
        # 检查七对子
        tile_counts = {}
        
        # 统计每种牌的数量
        for tile_id in hand_tiles:
            if tile_id in tile_counts:
                tile_counts[tile_id] += 1
            else:
                tile_counts[tile_id] = 1
        
        single = 0
        waiting_tile = None
        
        for tile_id, count in tile_counts.items():
            if count == 1 or count == 3:
                single += 1
                waiting_tile = tile_id
            # 如果有超过2张相同的牌，则不可能是七对子
            elif single >= 2:
                return False
        
        if single == 1:
            self.waiting_tiles.add(waiting_tile)

    def QBK_check(self, player_tiles: PlayerTiles):
        hand_kind_set = len(set(player_tiles.hand_tiles))
        # 如果手牌种类大于13种,则可能全不靠听牌,如果手牌种类大于10种,则可能组合龙
        if hand_kind_set >= 13:
            QBK_case_list =[{11,14,17,22,25,28,33,36,39,41,42,43,44,45,46,47}, {11,14,17,32,35,38,23,26,29,41,42,43,44,45,46,47}, {21,24,27,12,15,18,33,36,39,41,42,43,44,45,46,47}, 
                            {21,24,27,32,35,38,13,16,19,41,42,43,44,45,46,47}, {31,34,37,22,25,28,13,16,19,41,42,43,44,45,46,47}, {31,34,37,12,15,18,23,26,29,41,42,43,44,45,46,47}]
            # 遍历手牌如果在对应的全不靠组合当中则加入全不靠集合
            for case in QBK_case_list:
                QBK_set = set()
                for i in player_tiles.hand_tiles:
                    if i in case:
                        QBK_set.add(i)
                # 如果全不靠集合满足,就将手牌中不在全不靠组合中的牌加入等待牌,并返回True
                if len(QBK_set) == 13:
                    need_tile_list = []
                    for i in case:
                        if i not in player_tiles.hand_tiles:
                            need_tile_list.append(i)
                    for i in need_tile_list:
                        self.waiting_tiles.add(i)
                    return True

        elif hand_kind_set >= 8:
            ZHL_case_list = [{11,14,17,22,25,28,33,36,39}, {11,14,17,32,35,38,23,26,29}, {21,24,27,12,15,18,33,36,39}, 
                            {21,24,27,32,35,38,13,16,19}, {31,34,37,22,25,28,13,16,19}, {31,34,37,12,15,18,23,26,29}]
            for case in ZHL_case_list:
                ZHL_set = set()
                for i in player_tiles.hand_tiles:
                    if i in case:
                        ZHL_set.add(i)
                # 如果组合龙集合 = 9或者8 则在一向听的前提下 如果的确听牌 和牌必然包含组合龙 直接移除后进入一般型检测
                # 完整情况的组合龙正常删除就行 当做一个正常的顺子和刻子来处理
                if len(ZHL_set) == 9:
                    player_tiles.complete_step += 9
                    player_tiles.combination_list.append(f"z{case}")
                    for i in case:
                        player_tiles.hand_tiles.remove(i)
                    return False
                # 不完整情况的组合龙将缺张牌加入temp_waiting_tiles 如果在一般型检测中和牌步数达到14 代表缺的那张就是组合龙的缺张，将缺张牌加入waiting_tiles
                elif len(ZHL_set) == 8:
                    player_tiles.complete_step += 9
                    player_tiles.combination_list.append(f"z{case}")
                    for i in case:
                        if i in player_tiles.hand_tiles:
                            player_tiles.hand_tiles.remove(i)
                        else:
                            self.temp_waiting_tiles.add(i)
                    return False
        else:
            return False

    def normal_check(self, player_tiles: PlayerTiles):
        # 为节约性能 如果卡牌有不相邻的七组卡牌 说明无法和牌 直接返回False
        if not self.normal_check_block(player_tiles):
            return False
        # 获取所有的雀头可能以及没有雀头的情况
        
        self.debug_print(player_tiles.hand_tiles)
        
        all_list = self.normal_check_traverse_quetou(player_tiles)
        end_list = []
        self.debug_print([i.hand_tiles for i in all_list])
        # 345567
        count_count = 0
        while all_list:
            count_count += 1
            temp_list = all_list.pop()
            # 使用temp_list而不是player_tiles
            self.normal_check_traverse_kezi(temp_list, all_list)
            self.normal_check_traverse_dazi(temp_list, all_list)
            if temp_list.complete_step >= 11:
                end_list.append(temp_list)
        self.debug_print("计算次数：",count_count)
        for i in end_list:
            self.debug_print("手牌",i.hand_tiles, "胡牌步数",i.complete_step, "胡牌组合",i.combination_list)

        # 只保留complete_step大于等于11的列表
        end_list = [i for i in end_list if i.complete_step >= 11]
        self.debug_print("处理后的列表:", [i.hand_tiles for i in end_list])
        self.debug_print("列表长度:", len(end_list))

        # 剩余的手牌有五种组成方式 
        # 1.单吊听牌型(无雀头型)[n] 2.有雀头剩余对子型(对碰)[n,n] 3.剩余两面型[n,n+1] 4.剩余坎张型[n,n+2] 5.无效型[n,m] 特殊情况:组合龙型 complete_step == 14 [temp_waiting_tiles]
        if end_list:
            waiting_tiles = []
            for i in end_list:
                self.debug_print(i.hand_tiles, i.complete_step,i.combination_list)
                # 如果听牌步数是14 则代表缺的那张牌就是组合龙的缺张 直接返回缺张牌
                if i.complete_step == 14:
                    self.debug_print("组合龙型")
                    self.waiting_tiles = self.temp_waiting_tiles
                    return

                if len(i.hand_tiles) == 1:
                    waiting_tiles.append(i.hand_tiles[0]) # 单吊型
                elif len(i.hand_tiles) == 2:
                    if i.hand_tiles[0] == i.hand_tiles[1]:
                        waiting_tiles.append(i.hand_tiles[0]) # 对碰型
                    elif i.hand_tiles[0] == i.hand_tiles[1] - 1:
                        waiting_tiles.append(i.hand_tiles[0] - 1) 
                        waiting_tiles.append(i.hand_tiles[0] + 2) # 两面型
                    elif i.hand_tiles[0] == i.hand_tiles[1] - 2:
                        waiting_tiles.append(i.hand_tiles[0] + 1) # 坎张型
            # 去重
            if waiting_tiles:
                for i in waiting_tiles:
                    self.waiting_tiles.add(i)
        
    def normal_check_block(self,player_tiles: PlayerTiles):
        block_count = len(player_tiles.combination_list)
        tile_id_pointer = player_tiles.hand_tiles[0]
        for tile_id in player_tiles.hand_tiles:
            if tile_id == tile_id_pointer or tile_id == tile_id_pointer + 1:
                pass
            else:
                block_count += 1
            tile_id_pointer = tile_id
        if block_count > 6:
            return False
        else:
            return True
        
    def normal_check_traverse_quetou(self,player_tiles: PlayerTiles):
        all_list = []
        quetou_id_pointer = 0
        for tile_id in player_tiles.hand_tiles:
            if player_tiles.hand_tiles.count(tile_id) >= 2 and tile_id != quetou_id_pointer:
                temp_list = player_tiles.__deepcopy__(None)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.complete_step += 2
                temp_list.combination_list.append(f"q{tile_id}")
                all_list.append(temp_list)
                quetou_id_pointer = tile_id
        temp_list = player_tiles.__deepcopy__(None)
        all_list.append(temp_list)
        return all_list

    def normal_check_traverse_kezi(self, player_tiles: PlayerTiles, all_list):
        same_tile_id = 0
        for tile_id in player_tiles.hand_tiles:
            if player_tiles.hand_tiles.count(tile_id) >= 3 and tile_id != same_tile_id:
                temp_list = player_tiles.__deepcopy__(None)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.complete_step += 3
                temp_list.combination_list.append(f"k{tile_id}")
                all_list.append(temp_list)
                same_tile_id = tile_id

    def normal_check_traverse_dazi(self, player_tiles: PlayerTiles, all_list):
        same_tile_id = 0
        for tile_id in player_tiles.hand_tiles:
            if tile_id <= 40:
                if tile_id+1 in player_tiles.hand_tiles and tile_id+2 in player_tiles.hand_tiles and tile_id != same_tile_id:
                    temp_list = player_tiles.__deepcopy__(None)
                    temp_list.hand_tiles.remove(tile_id)
                    temp_list.hand_tiles.remove(tile_id+1)
                    temp_list.hand_tiles.remove(tile_id+2)
                    temp_list.complete_step += 3
                    temp_list.combination_list.append(f"s{tile_id+1}")
                    all_list.append(temp_list)
                    same_tile_id = tile_id
    # 外部调用时传参手牌、组合 返回听牌集合
    def tingpai_check(self,hand_tile_list,combination_list):
        test_tiles = PlayerTiles(hand_tile_list,combination_list,len(combination_list)*3)
        self.check_waiting_tiles(test_tiles)
        # 排除 10 20 30 40这四种集合成员
        self.waiting_tiles = {i for i in self.waiting_tiles if i not in {10,20,30,40}}
        return self.waiting_tiles.copy()  # 返回set的副本，避免引用问题

# 测试
if __name__ == "__main__":
    """
    # 生成测试牌组可能
    # 标准牌堆
    sth_tiles_set = {
            11,12,13,14,15,16,17,18,19, # 万
            21,22,23,24,25,26,27,28,29, # 饼
            31,32,33,34,35,36,37,38,39, # 条
            41,42,43,44, # 东南西北
            45,46,47 # 中白发
        }
    # 花牌牌堆
    hua_tiles_set = {51,52,53,54,55,56,57,58} # 春夏秋冬 梅兰竹菊
    # 搭子牌堆
    dazi_set = {
            "s12","s13","s14","s15","s16","s17","s18", # 万
            "s22","s23","s24","s25","s26","s27","s28", # 饼
            "s32","s33","s34","s35","s36","s37","s38", # 条
        }
    # 刻子牌堆
    kezi_set = {
            "k11","k12","k13","k14","k15","k16","k17","k18","k19", # 万
            "k21","k22","k23","k24","k25","k26","k27","k28","k29", # 饼
            "k31","k32","k33","k34","k35","k36","k37","k38","k39", # 条
            "k41","k42","k43","k44", # 东南西北
            "k45","k46","k47" # 中白发
        }
    # 雀头牌堆
    quetou_set = {
            "q11","q12","q13","q14","q15","q16","q17","q18","q19", # 万
            "q21","q22","q23","q24","q25","q26","q27","q28","q29", # 饼
            "q31","q32","q33","q34","q35","q36","q37","q38","q39", # 条
            "q41","q42","q43","q44", # 东南西北
            "q45","q46","q47" # 中白发
        }
    # 暗杠牌堆
    angang_set = {
            "a11","a12","a13","a14","a15","a16","a17","a18","a19", # 万
            "a21","a22","a23","a24","a25","a26","a27","a28","a29", # 饼
            "a31","a32","a33","a34","a35","a36","a37","a38","a39", # 条
            "a41","a42","a43","a44", # 东南西北
            "a45","a46","a47" # 中白发
        }
    # 杠牌堆
    gang_set = {
            "g11","g12","g13","g14","g15","g16","g17","g18","g19", # 万
            "g21","g22","g23","g24","g25","g26","g27","g28","g29", # 饼
            "g31","g32","g33","g34","g35","g36","g37","g38","g39", # 条
            "g41","g42","g43","g44", # 东南西北
            "g45","g46","g47" # 中白发
        }
    # 牌堆 => 手牌 映射字典
    tiles_dict = {
        "s12": [11,12,13],"s13": [12,13,14],"s14": [13,14,15],"s15": [14,15,16],"s16": [15,16,17],"s17": [16,17,18],"s18": [17,18,19],
        "s22": [21,22,23],"s23": [22,23,24],"s24": [23,24,25],"s25": [24,25,26],"s26": [25,26,27],"s27": [26,27,28],"s28": [27,28,29],
        "s32": [31,32,33],"s33": [32,33,34],"s34": [33,34,35],"s35": [34,35,36],"s36": [35,36,37],"s37": [36,37,38],"s38": [37,38,39],
        "k11": [11,11,11],"k12": [12,12,12],"k13": [13,13,13],"k14": [14,14,14],"k15": [15,15,15],"k16": [16,16,16],"k17": [17,17,17],"k18": [18,18,18],"k19": [19,19,19],
        "k21": [21,21,21],"k22": [22,22,22],"k23": [23,23,23],"k24": [24,24,24],"k25": [25,25,25],"k26": [26,26,26],"k27": [27,27,27],"k28": [28,28,28],"k29": [29,29,29],
        "k31": [31,31,31],"k32": [32,32,32],"k33": [33,33,33],"k34": [34,34,34],"k35": [35,35,35],"k36": [36,36,36],"k37": [37,37,37],"k38": [38,38,38],"k39": [39,39,39],
        "k41": [41,41,41],"k42": [42,42,42],"k43": [43,43,43],"k44": [44,44,44],"k45": [45,45,45],"k46": [46,46,46],"k47": [47,47,47],
    }
    quetou_dict = {
        "q11": [11,11],"q12": [12,12],"q13": [13,13],"q14": [14,14],"q15": [15,15],"q16": [16,16],"q17": [17,17],"q18": [18,18],"q19": [19,19],
        "q21": [21,21],"q22": [22,22],"q23": [23,23],"q24": [24,24],"q25": [25,25],"q26": [26,26],"q27": [27,27],"q28": [28,28],"q29": [29,29], 
        "q31": [31,31],"q32": [32,32],"q33": [33,33],"q34": [34,34],"q35": [35,35],"q36": [36,36],"q37": [37,37],"q38": [38,38],"q39": [39,39],
        "q41": [41,41],"q42": [42,42],"q43": [43,43],"q44": [44,44],"q45": [45,45],"q46": [46,46],"q47": [47,47], 
    }
    # 随机抽取1个quetou和4个tiles_dict的值组合
    while True:
        tiles_list = []
        tiles_list.extend(random.choice(list(quetou_dict.values())))
        for i in range(4):
            tiles = random.choice(list(tiles_dict.values()))
            tiles_list.extend(tiles)
        if all(tiles_list.count(tile_id) <= 4 for tile_id in tiles_list):
            break

    Chinese_test_combination = Chinese_Tingpai_Check()
    # 随机删除一张
    tiles_list.pop(random.randint(0, len(tiles_list) - 1))
    tiles_list.sort()

    time_start = time()
    # 生成100次
    test_list = []
    test_list_copy = []
    test_combination = Chinese_Tingpai_Check()
    for _ in range(100):  # 使用_避免与内层循环的i冲突
        tiles_list = []
        tiles_list.extend(random.choice(list(quetou_dict.values())))
        for j in range(4):
            tiles = random.choice(list(tiles_dict.values()))
            tiles_list.extend(tiles)
        tiles_list.pop(random.randint(0, len(tiles_list) - 1))
        tiles_list.sort()
        test_list_copy.append(tiles_list.copy())


        test_tiles = PlayerTiles(tiles_list,[],0)
        result = test_combination.check_waiting_tiles(test_tiles)
        test_list.append(result.copy())  # 使用copy()创建新的字典
    time_end = time()
    logger.debug(time_end - time_start)
    logger.debug(test_list)
    logger.debug(test_list_copy)
    """
    
    test_save = [["k39"],[32,32,32,33,33,33,34,34,34,41]] 

    # 手动指定牌组测试
    Chinese_test_combination = Chinese_Tingpai_Check(debug=True)  # 启用debug模式
    print("手动指定牌组")
    time_start = time()
    tiles_list = test_save[1]
    tile_combination_list = test_save[0]
    return_set = Chinese_test_combination.tingpai_check(tiles_list,tile_combination_list)
    print("返回结果",return_set)

    time_end = time()
    # 时间使用0.001-0.005 按每次手牌都是一向听,并且最高时长0.005情况下每秒可以处理200次计算
    # 按100次计算进行估测 平均出牌时间3秒 相当于可以承担300桌玩家同时进行(1200人)那就先不改了
    print("本次测试用时",time_end - time_start)


    

    


        
        

