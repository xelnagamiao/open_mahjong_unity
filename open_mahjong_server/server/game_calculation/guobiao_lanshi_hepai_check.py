# -*- coding: utf-8 -*-
"""????MCR 1001-2025????????

????????????????????????????????
????????????????????????????????
"""

from __future__ import annotations

from collections import Counter
from itertools import combinations, permutations
from time import time
import logging
from typing import Dict, List, Sequence, Tuple

logger = logging.getLogger(__name__)

class PlayerTiles:
    def __init__(self, tiles_list, combination_list,complete_step):
        self.hand_tiles = sorted(tiles_list)
        # Copy: fan_count may mutate combination_list (e.g. 暗转明 G→g / K→k).
        # Sharing the caller's list poisons later hypothetical-fan / cache keys.
        self.combination_list = list(combination_list)
        self.initial_combination_count = len(combination_list)
        self.complete_step = complete_step # +3 +3 +3 +3 +2 = 14
        self.fan_list = []
        self.point_count_dict = {} # 存储和牌得分
        self.fan_count_list = [] # 存储和牌文本

    def __deepcopy__(self, memo):
        new_instance = PlayerTiles(self.hand_tiles[:],
                                 self.combination_list[:],
                                 self.complete_step)
        new_instance.initial_combination_count = self.initial_combination_count
        new_instance.fan_list = self.fan_list[:]
        return new_instance

class Lanshi_Hepai_Check:
    """??????????????"""

    duanyao_set = {12,13,14,15,16,17,18,22,23,24,25,26,27,28,32,33,34,35,36,37,38} # 断幺

    zipai_set = {41,42,43,44,45,46,47} # 字牌

    wan_set = {11,12,13,14,15,16,17,18,19} # 万

    bing_set = {21,22,23,24,25,26,27,28,29} # 饼

    tiao_set = {31,32,33,34,35,36,37,38,39} # 条

    feng_set = {41,42,43,44} # 风

    zhongbaifa_set = {45,46,47} # 中白发

    lvyise_set = {32,33,34,36,38,47} # 绿一色

    hunyaojiu_set = {11,19,21,29,31,39,41,42,43,44,45,46,47} # 混幺九

    qingyaojiu_set = {11,19,21,29,31,39} # 清幺九

    quanda_set = {17,18,19,27,28,29,37,38,39} # 全大

    quanzhong_set = {14,15,16,24,25,26,34,35,36} # 全中

    quanxiao_set = {11,12,13,21,22,23,31,32,33} # 全小

    dayuwu_set = {16,17,18,19,26,27,28,29,36,37,38,39} # 大于五

    xiaoyuwu_set = {11,12,13,14,21,22,23,24,31,32,33,34} # 小于五

    tuibudao_set = {21,22,23,24,25,28,29,46,32,34,35,36,38,39} # 推不倒

    jiulianbaodeng_list = [1,1,1,2,3,4,5,6,7,8,9,9,9] # 九莲宝灯

    yiseshuanglonghui_list = [1,1,2,2,3,3,5,5,7,7,8,8,9,9] # 一色双龙会

    quandaiwu_set = {"s14","s15","s16","s24","s25","s26","s34","s35","s36",
                     "S14","S15","S16","S24","S25","S26","S34","S35","S36",
                     "k15","K15","g15","G15","k25","K25","g25","G25","k35","K35","g35","G35",
                     "q15","q25","q35"} # 全带五

    fengke_set = {"k41","k42","k43","k44","K41","K42","K43","K44","g41","G41","g42","G42","g43","G43","g44","G44"} # 风刻

    jianke_set = {"k45","k46","k47","K45","K46","K47","g45","G45","g46","G46","g47","G47"} # 箭刻

    fengke_quetou_set = {"q41","q42","q43","q44"} # 风刻雀头

    jianke_quetou_set = {"q45","q46","q47"} # 箭刻雀头

    quandaiyao_set = {"s12","s18","s22","s28","s32","s38",
                          "S12","S18","S22","S28","S32","S38",
                          "k11","k19","k21","k29","k31","k39","k41","k42","k43","k44","k45","k46","k47",
                          "K11","K19","K21","K29","K31","K39","K41","K42","K43","K44","K45","K46","K47",
                          "g11","g19","g21","g29","g31","g39","g41","g42","g43","g44","g45","g46","g47",
                          "G11","G19","G21","G29","G31","G39","G41","G42","G43","G44","G45","G46","G47",
                          "q11","q19","q21","q29","q31","q39","q41","q42","q43","q44","q45","q46","q47"} # 全带幺

    yaojiuke_set = {"k11","K11","k19","K19","k21","K21","k29","K29","k31","K31","k39","K39",
                        "k41","K41","k42","K42","k43","K43","k44","K44","k45","K45","k46","K46","k47","K47",
                        "g11","G11","g19","G19","g21","G21","g29","G29","g31","G31","g39","G39",
                        "g41","G41","g42","G42","g43","G43","g44","G44","g45","G45","g46","G46","g47","G47"} # 幺九刻

    combination_to_tiles_dict:Dict[str,List[int]] = {
        "s12": [11,12,13],"s13": [12,13,14],"s14": [13,14,15],"s15": [14,15,16],"s16": [15,16,17],"s17": [16,17,18],"s18": [17,18,19],
        "s22": [21,22,23],"s23": [22,23,24],"s24": [23,24,25],"s25": [24,25,26],"s26": [25,26,27],"s27": [26,27,28],"s28": [27,28,29],
        "s32": [31,32,33],"s33": [32,33,34],"s34": [33,34,35],"s35": [34,35,36],"s36": [35,36,37],"s37": [36,37,38],"s38": [37,38,39], # 顺
        "S12": [11,12,13],"S13": [12,13,14],"S14": [13,14,15],"S15": [14,15,16],"S16": [15,16,17],"S17": [16,17,18],"S18": [17,18,19],
        "S22": [21,22,23],"S23": [22,23,24],"S24": [23,24,25],"S25": [24,25,26],"S26": [25,26,27],"S27": [26,27,28],"S28": [27,28,29],
        "S32": [31,32,33],"S33": [32,33,34],"S34": [33,34,35],"S35": [34,35,36],"S36": [35,36,37],"S37": [36,37,38],"S38": [37,38,39], # 暗顺
        "k11": [11,11,11],"k12": [12,12,12],"k13": [13,13,13],"k14": [14,14,14],"k15": [15,15,15],"k16": [16,16,16],"k17": [17,17,17],"k18": [18,18,18],"k19": [19,19,19],
        "k21": [21,21,21],"k22": [22,22,22],"k23": [23,23,23],"k24": [24,24,24],"k25": [25,25,25],"k26": [26,26,26],"k27": [27,27,27],"k28": [28,28,28],"k29": [29,29,29],
        "k31": [31,31,31],"k32": [32,32,32],"k33": [33,33,33],"k34": [34,34,34],"k35": [35,35,35],"k36": [36,36,36],"k37": [37,37,37],"k38": [38,38,38],"k39": [39,39,39],
        "k41": [41,41,41],"k42": [42,42,42],"k43": [43,43,43],"k44": [44,44,44],"k45": [45,45,45],"k46": [46,46,46],"k47": [47,47,47], # 刻
        "K11": [11,11,11],"K12": [12,12,12],"K13": [13,13,13],"K14": [14,14,14],"K15": [15,15,15],"K16": [16,16,16],"K17": [17,17,17],"K18": [18,18,18],"K19": [19,19,19],
        "K21": [21,21,21],"K22": [22,22,22],"K23": [23,23,23],"K24": [24,24,24],"K25": [25,25,25],"K26": [26,26,26],"K27": [27,27,27],"K28": [28,28,28],"K29": [29,29,29],
        "K31": [31,31,31],"K32": [32,32,32],"K33": [33,33,33],"K34": [34,34,34],"K35": [35,35,35],"K36": [36,36,36],"K37": [37,37,37],"K38": [38,38,38],"K39": [39,39,39],
        "K41": [41,41,41],"K42": [42,42,42],"K43": [43,43,43],"K44": [44,44,44],"K45": [45,45,45],"K46": [46,46,46],"K47": [47,47,47], # 暗刻
        "q11": [11,11],"q12": [12,12],"q13": [13,13],"q14": [14,14],"q15": [15,15],"q16": [16,16],"q17": [17,17],"q18": [18,18],"q19": [19,19],
        "q21": [21,21],"q22": [22,22],"q23": [23,23],"q24": [24,24],"q25": [25,25],"q26": [26,26],"q27": [27,27],"q28": [28,28],"q29": [29,29],
        "q31": [31,31],"q32": [32,32],"q33": [33,33],"q34": [34,34],"q35": [35,35],"q36": [36,36],"q37": [37,37],"q38": [38,38],"q39": [39,39],
        "q41": [41,41],"q42": [42,42],"q43": [43,43],"q44": [44,44],"q45": [45,45],"q46": [46,46],"q47": [47,47], # 雀头
        "g11": [11,11,11],"g12": [12,12,12],"g13": [13,13,13],"g14": [14,14,14],"g15": [15,15,15],"g16": [16,16,16],"g17": [17,17,17],"g18": [18,18,18],"g19": [19,19,19],
        "g21": [21,21,21],"g22": [22,22,22],"g23": [23,23,23],"g24": [24,24,24],"g25": [25,25,25],"g26": [26,26,26],"g27": [27,27,27],"g28": [28,28,28],"g29": [29,29,29],
        "g31": [31,31,31],"g32": [32,32,32],"g33": [33,33,33],"g34": [34,34,34],"g35": [35,35,35],"g36": [36,36,36],"g37": [37,37,37],"g38": [38,38,38],"g39": [39,39,39],
        "g41": [41,41,41],"g42": [42,42,42],"g43": [43,43,43],"g44": [44,44,44],
        "g45": [45,45,45],"g46": [46,46,46],"g47": [47,47,47], # 杠
        "G11": [11,11,11],"G12": [12,12,12],"G13": [13,13,13],"G14": [14,14,14],"G15": [15,15,15],"G16": [16,16,16],"G17": [17,17,17],"G18": [18,18,18],"G19": [19,19,19],
        "G21": [21,21,21],"G22": [22,22,22],"G23": [23,23,23],"G24": [24,24,24],"G25": [25,25,25],"G26": [26,26,26],"G27": [27,27,27],"G28": [28,28,28],"G29": [29,29,29],
        "G31": [31,31,31],"G32": [32,32,32],"G33": [33,33,33],"G34": [34,34,34],"G35": [35,35,35],"G36": [36,36,36],"G37": [37,37,37],"G38": [38,38,38],"G39": [39,39,39],
        "G41": [41,41,41],"G42": [42,42,42],"G43": [43,43,43],"G44": [44,44,44],
        "G45": [45,45,45],"G46": [46,46,46],"G47": [47,47,47], # 暗杠
        "z0":[11,14,17,22,25,28,33,36,39], "z1":[11,14,17,32,35,38,23,26,29],"z2":[21,24,27,12,15,18,33,36,39],
        "z3":[21,24,27,32,35,38,13,16,19], "z4":[31,34,37,22,25,28,13,16,19], "z5":[31,34,37,12,15,18,23,26,29] # 组合龙
    }

    yaojiu = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47}

    zipai = {41, 42, 43, 44, 45, 46, 47}

    count_model_dict: Dict[str, int] = {
        "qixingdui": 100, "sitongshun": 100, "jiulianbaodeng": 100, "sigang": 100,
        "dasixi": 72, "qingyaojiu": 72,
        "sianke": 48, "shisanyao": 48,
        "ziyise": 40, "silianshun": 40, "silianke": 40,
        "xiaosixi": 32, "sangang": 32,
        "dasanyuan": 24, "shunwang": 24, "santongshun": 24, "shunlian": 24,
        "hunyaojiu": 16, "quanda": 16, "quanzhong": 16, "quanxiao": 16,
        "quandaiwu": 16, "santongke": 16, "xiaosanyuan": 16, "quanbukao": 16,
        "sanfengke": 12, "sananke": 12, "sanlianke": 12, "qingyise": 12,
        "sanlianshun": 12,
        "sanselianke": 8, "qingquandaiyao": 8, "shuanggang": 8, "dayuwu": 8,
        "xiaoyuwu": 8, "qiduizi": 8, "shunhuan": 8,
        "shuangjianke": 6, "qinglong": 6,
        "miaoshouhuichun": 5, "haidilaoyue": 5, "gangshangkaihua": 5,
        "qiangganghe": 5, "tianhe": 5, "dihe": 5,
        "hualong": 4, "sansetongshun": 4,
        "pengpenghe": 3, "hunquandaiyao": 3, "hunyise": 3, "sanselianshun": 3,
        "angang": 2, "shuanganke": 2, "wumenqi": 2, "shuangtongke": 2,
        "quanqiuren": 2, "siguiyi": 2, "yibangao": 2, "hejuezhang": 2,
        "jianke": 2, "quanfengke": 2, "menfengke": 2,
        "menqianqing": 1, "minggang": 1, "duanyao": 1, "xixiangfeng": 1,
        "lianliu": 1, "laoshaofu": 1, "yaojiuke": 1, "zimo": 1,
    }

    eng_to_chinese_dict = {
        "qixingdui": "七星对", "sitongshun": "四同顺", "jiulianbaodeng": "九莲宝灯",
        "sigang": "四杠", "dasixi": "大四喜", "qingyaojiu": "清幺九", "sianke": "四暗刻",
        "shisanyao": "十三幺", "ziyise": "字一色", "silianshun": "四连顺",
        "silianke": "四连刻", "xiaosixi": "小四喜", "sangang": "三杠",
        "dasanyuan": "大三元", "shunwang": "顺网", "santongshun": "三同顺",
        "shunlian": "顺链", "hunyaojiu": "混幺九", "quanda": "全大",
        "quanzhong": "全中", "quanxiao": "全小", "quandaiwu": "全带五",
        "santongke": "三同刻", "xiaosanyuan": "小三元", "quanbukao": "全不靠",
        "sanfengke": "三风刻", "sananke": "三暗刻", "sanlianke": "三连刻",
        "qingyise": "清一色", "sanlianshun": "三连顺", "sanselianke": "三色连刻",
        "qingquandaiyao": "清全带幺", "shuanggang": "双杠", "dayuwu": "大于五",
        "xiaoyuwu": "小于五", "qiduizi": "七对", "shunhuan": "顺环",
        "shuangjianke": "双箭刻", "qinglong": "清龙", "miaoshouhuichun": "妙手回春",
        "haidilaoyue": "海底捞月", "gangshangkaihua": "杠上开花", "qiangganghe": "抢杠和",
        "tianhe": "天和", "dihe": "地和", "hualong": "花龙",
        "sansetongshun": "三色同顺", "pengpenghe": "碰碰和",
        "hunquandaiyao": "混全带幺", "hunyise": "混一色",
        "sanselianshun": "三色连顺", "angang": "暗杠", "shuanganke": "双暗刻",
        "wumenqi": "五门齐", "shuangtongke": "双同刻", "quanqiuren": "全求人",
        "siguiyi": "四归一", "yibangao": "一般高", "hejuezhang": "和绝张",
        "jianke": "箭刻", "quanfengke": "圈风刻", "menfengke": "门风刻",
        "menqianqing": "门前清", "minggang": "明杠", "duanyao": "断幺",
        "xixiangfeng": "喜相逢", "lianliu": "连六", "laoshaofu": "老少副",
        "yaojiuke": "幺九刻", "zimo": "自摸",
    }

    _table_order = tuple(count_model_dict)

    _repeatable = {"siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke"}

    _occasional = (
        "miaoshouhuichun", "haidilaoyue", "gangshangkaihua",
        "qiangganghe", "tianhe", "dihe",
    )

    _unrelated_cases = (
        {11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47},
        {11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47},
        {21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47},
        {21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47},
        {31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47},
        {31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47},
    )

    # 基础识别阶段会产生结构事实；只有蓝十番表中的原生番名直接进入计分，
    # 其余差异番在 _collect_lanshi_fans 中由最终组合统一派生。
    _native_fan_names = {
        "dasixi", "dasanyuan", "jiulianbaodeng", "sigang", "shisanyao",
        "qingyaojiu", "xiaosixi", "xiaosanyuan", "ziyise", "sianke", "sangang", "shuangjianke",
        "hunyaojiu", "qiduizi", "qingyise", "quanda", "quanzhong", "quanxiao",
        "quandaiwu", "santongke", "sananke", "quanbukao", "dayuwu", "xiaoyuwu",
        "sanfengke", "miaoshouhuichun", "haidilaoyue", "gangshangkaihua",
        "qiangganghe", "pengpenghe", "hunyise", "wumenqi", "quanqiuren",
        "hejuezhang", "jianke", "quanfengke", "menfengke", "menqianqing", "siguiyi",
        "shuangtongke", "shuanganke", "angang", "duanyao", "yaojiuke", "minggang", "zimo",
        "qixingdui",
    }

    def __init__(self, debug=False):
        self.debug = debug  # 添加debug标志

    def debug_print(self, *args, **kwargs):
        """只在debug模式下打印"""
        if self.debug:
            logger.debug(*args, **kwargs)
            print(*args, **kwargs)

    def hepai_check(self,hand_list:list,tiles_combination,way_to_hepai,get_tile):
        tiles_combination = tiles_combination
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list,tiles_combination,complete_step)


        self.debug_print("传参手牌：",player_tiles.hand_tiles,"传参组合：",player_tiles.combination_list,"传参和牌方式：",way_to_hepai,"传参和牌张：",get_tile)

        player_tiles_list = []
        if len(player_tiles.hand_tiles) == 14:
            # 如果手牌等于14张,则进行国士无双、全不靠、七对子的计算
            # 如果国士无双成立,将player_tiles返回player_tiles_list
            # 如果全不靠成立,将player_tiles返回player_tiles_list(与组合龙在一个方法内)
            # 如果组合龙成立,并且全不靠不成立,将成立组合龙的player_tiles返回player_tiles_list
            # 如果七对子成立,将player_tiles返回player_tiles_list
            if not player_tiles_list:
                self.GS_check(player_tiles,player_tiles_list)  # 国士无双检查
            if not player_tiles_list:
                self.QBK_check(player_tiles,player_tiles_list)  # 全不靠检查
            if not player_tiles_list:
                self.QD_check(player_tiles,player_tiles_list)  # 七对子检查
        # 如果手牌不等于14张,如果组合龙成立,有可能复合一般型,复制一份player_tiles进入player_tiles_list
        else:
            self.QBK_check(player_tiles,player_tiles_list)
        player_tiles_list.append(player_tiles)
        check_done_list = []
        for player_tiles_item in player_tiles_list:
            self.normal_check(player_tiles_item,check_done_list)



        fancount_time_start = time()
        # 计算番种
        allow_list = []
        if check_done_list:
            for i in check_done_list:
                # 每拆解拷贝 way：fan_count 会就地 append 暗转明/门风圈风相同，
                # 共享 list 会污染后续拆解的取 max。
                allow_list.append(self.fan_count(i, get_tile, list(way_to_hepai)))

        fancount_time_end = time()
        logger.debug(f"番种计算耗时：{fancount_time_end - fancount_time_start}秒")

        # 对比返回元组的第一个元素，只返回第一个元素最大的元组
        allow_list = sorted(allow_list,key=lambda x:x[0],reverse=True)
        logger.debug(f"允许的番种：{allow_list}")
        if not allow_list:
            return 0, []
        return allow_list[0]

    def GS_check(self,player_tiles:PlayerTiles,player_tiles_list):
        temp_player_tiles = player_tiles.__deepcopy__(None)
        allow_same_id = True
        same_tile_id = 0
        hepai_step = 0
        for tile_id in temp_player_tiles.hand_tiles:
            if tile_id in self.yaojiu and (tile_id != same_tile_id or allow_same_id):
                if tile_id == same_tile_id:
                    allow_same_id = False
                same_tile_id = tile_id
                hepai_step += 1
            if hepai_step == 14:
                temp_player_tiles.complete_step = 14
                temp_player_tiles.fan_list.append("shisanyao")
                player_tiles_list.append(temp_player_tiles)

    def normal_check(self, player_tiles: PlayerTiles,check_done_list:list[PlayerTiles]):
        self.debug_print("player_tiles:",player_tiles.hand_tiles,player_tiles.complete_step,player_tiles.combination_list)
        # 如果牌型已经和牌,说明有国士无双、七对子、全不靠、七星不靠、不进行一般型检测
        if player_tiles.complete_step == 14:
            check_done_list.append(player_tiles)
            return
        # 如果牌型没有组合,为节约性能 如果卡牌有不相邻的七组卡牌 说明无法和牌 直接返回False
        elif player_tiles.complete_step == 0:
            if not self.normal_check_block(player_tiles):
                return

        # 获取所有的雀头可能以及没有雀头的情况
        all_list = self.normal_check_traverse_quetou(player_tiles)
        end_list = []
        self.debug_print("所有雀头可能",[i.hand_tiles for i in all_list])
        # 345567
        count_count = 0
        while all_list:
            count_count += 1
            temp_list = all_list.pop()
            # 使用temp_list而不是player_tiles
            self.normal_check_traverse_kezi(temp_list, all_list)
            self.normal_check_traverse_dazi(temp_list, all_list)
            if temp_list.complete_step == 14:
                end_list.append(temp_list)

        self.debug_print("计算次数：",count_count)
        combination_class = None
        temp_list = []
        for i in end_list:
            i.combination_list.sort()
            if i.combination_list != combination_class:
                combination_class = i.combination_list
                temp_list.append(i)
        end_list = temp_list

        self.debug_print("和牌类型的数量:", len(end_list))
        for i in end_list:
            self.debug_print("手牌",i.hand_tiles, "胡牌步数",i.complete_step, "胡牌组合",i.combination_list)

        check_done_list.extend(end_list)

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
            player_tiles.hand_tiles.count(tile_id)
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
                temp_list.combination_list.append(f"K{tile_id}")
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
                    temp_list.combination_list.append(f"S{tile_id+1}")
                    all_list.append(temp_list)
                    same_tile_id = tile_id

    def fan_count_hand_check(self,player_tiles:PlayerTiles,hand_tiles_list,get_tile):
        self.debug_print("手牌",hand_tiles_list)
        if hand_tiles_list == []:
            return
        # 对手牌映射查表
        if all(i in self.duanyao_set for i in hand_tiles_list):
            player_tiles.fan_list.append("duanyao") # 断幺
            if all(i in self.quanzhong_set for i in hand_tiles_list):
                player_tiles.fan_list.append("quanzhong") # 全中

        if all(i in self.wan_set|self.zipai_set for i in hand_tiles_list) or all(i in self.bing_set|self.zipai_set for i in hand_tiles_list) or all(i in self.tiao_set|self.zipai_set for i in hand_tiles_list):
            if all(i in self.wan_set for i in hand_tiles_list) or all(i in self.bing_set for i in hand_tiles_list) or all(i in self.tiao_set for i in hand_tiles_list):
                temp_tiles_list = hand_tiles_list.copy()
                self.debug_print("temp_tiles_list",temp_tiles_list)
                temp_tiles_list.remove(get_tile)
                save_list = []
                for i in temp_tiles_list:
                    rank = i % 10
                    save_list.append(rank)
                self.debug_print(save_list)
                if player_tiles.initial_combination_count == 0 and save_list == self.jiulianbaodeng_list:
                    player_tiles.fan_list.append("jiulianbaodeng") # 九莲宝灯
                else:
                    player_tiles.fan_list.append("qingyise") # 清一色
            if all(i in self.lvyise_set for i in hand_tiles_list):
                player_tiles.fan_list.append("lvyise") # 绿一色
            else:
                if all(i in self.zipai_set for i in hand_tiles_list):
                    player_tiles.fan_list.append("ziyise") # 字一色
                elif any(i in self.zipai_set for i in hand_tiles_list):
                    player_tiles.fan_list.append("hunyise") # 混一色

        if "ziyise" not in player_tiles.fan_list:
            if all(i in self.hunyaojiu_set for i in hand_tiles_list):
                if all(i in self.qingyaojiu_set for i in hand_tiles_list):
                    player_tiles.fan_list.append("qingyaojiu") # 清幺九
                else:
                    player_tiles.fan_list.append("hunyaojiu") # 混幺九

        if all(i in self.dayuwu_set for i in hand_tiles_list):
            if all(i in self.quanda_set for i in hand_tiles_list):
                player_tiles.fan_list.append("quanda") # 全大
            else:
                player_tiles.fan_list.append("dayuwu") # 大于五
        elif all(i in self.xiaoyuwu_set for i in hand_tiles_list):
            if all(i in self.quanxiao_set for i in hand_tiles_list):
                player_tiles.fan_list.append("quanxiao") # 全小
            else:
                player_tiles.fan_list.append("xiaoyuwu") # 小于五

        # 和牌中只包含两种花色 则缺一门
        suit_count = 0
        for suit_set in [self.wan_set,self.bing_set,self.tiao_set]:
            if any(i in suit_set for i in hand_tiles_list):
                suit_count += 1
        if suit_count == 2:
            player_tiles.fan_list.append("queyimen") # 缺一门

        if all (i not in self.zipai_set for i in hand_tiles_list):
            player_tiles.fan_list.append("wuzi") # 无字

        if all (i in self.tuibudao_set for i in hand_tiles_list):
            player_tiles.fan_list.append("tuibudao") # 推不倒

        count_pointer = 0
        for i in hand_tiles_list:
            if hand_tiles_list.count(i) == 4:
                if not {f"g{i}",f"G{i}"} in player_tiles.combination_list and count_pointer != i:
                    count_pointer = i
                    player_tiles.fan_list.append("siguiyi") # 四归一

        if any(i in self.zhongbaifa_set for i in hand_tiles_list):
            if any(i in self.feng_set for i in hand_tiles_list):
                if any(i in self.wan_set for i in hand_tiles_list):
                    if any(i in self.bing_set for i in hand_tiles_list):
                        if any(i in self.tiao_set for i in hand_tiles_list):
                            player_tiles.fan_list.append("wumenqi") # 五门齐

    def fan_count_combination_check(self,player_tiles:PlayerTiles):
        if player_tiles.combination_list == []:
            return
        # 对组合单元本身查表
        # 负责判断全带五 全带幺 箭刻 双箭刻 大四喜 小四喜
        if all(i in self.quandaiwu_set for i in player_tiles.combination_list):
            player_tiles.fan_list.append("quandaiwu") # 全带五

        if all(i in self.quandaiyao_set for i in player_tiles.combination_list):
            player_tiles.fan_list.append("quandaiyao") # 全带幺

        jianke_count = 0
        jianke_quetou = False
        for i in player_tiles.combination_list:
            if i in self.jianke_set:
                jianke_count += 1
            if i in self.jianke_quetou_set:
                jianke_quetou = True
        if jianke_count == 1:
            player_tiles.fan_list.append("jianke") # 箭刻
        if jianke_count == 2:
            if jianke_quetou:
                player_tiles.fan_list.append("xiaosanyuan") # 小三元
            else:
                player_tiles.fan_list.append("shuangjianke") # 双箭刻
        if jianke_count == 3:
            player_tiles.fan_list.append("dasanyuan") # 大三元

        fengke_count = 0
        fengke_quetou = False
        for i in player_tiles.combination_list:
            if i in self.fengke_set:
                fengke_count += 1
            if i in self.fengke_quetou_set:
                fengke_quetou = True
        if fengke_count == 3:
            if fengke_quetou:
                player_tiles.fan_list.append("xiaosixi") # 小四喜
            else:
                player_tiles.fan_list.append("sanfengke") # 三风刻
        elif fengke_count == 4:
            player_tiles.fan_list.append("dasixi") # 大四喜


        yaojiuke_count = 0
        for i in player_tiles.combination_list:
            if i in self.yaojiuke_set:
                yaojiuke_count += 1
                player_tiles.fan_list.append("yaojiuke") # 幺九刻

    def fan_count_combination_str_check(self,player_tiles:PlayerTiles,combination_str,hand_tiles_list):
        if combination_str == "":
            return
        # 对组合映射查表
        # 如果有全不靠加一个顺子 或者四个顺子 同时所有手牌是数牌 满足平和
        if ("z" in combination_str and combination_str.count("s") + combination_str.count("S") == 1) or (combination_str.count("s") + combination_str.count("S") == 4):
            if all(i <= 40 for i in hand_tiles_list):
                player_tiles.fan_list.append("pinghe") # 平和

        if combination_str.count("G") + combination_str.count("g") == 4:
            player_tiles.fan_list.append("sigang") # 四杠
        elif combination_str.count("G") + combination_str.count("g") == 3:
            player_tiles.fan_list.append("sangang") # 三杠
        elif combination_str.count("G") == 2:
            player_tiles.fan_list.append("shuangangang") # 双暗杠
        elif combination_str.count("g") == 2:
            player_tiles.fan_list.append("shuangminggang") # 双明杠
        elif combination_str.count("g") == 1 and combination_str.count("G") == 1:
            player_tiles.fan_list.append("mingangang") # 明暗杠
        elif combination_str.count("G") == 1:
            player_tiles.fan_list.append("angang") # 暗杠
        elif combination_str.count("g") == 1:
            player_tiles.fan_list.append("minggang") # 明杠

        if combination_str.count("G") + combination_str.count("K") == 4:
            player_tiles.fan_list.append("sianke") # 四暗刻
        elif combination_str.count("G") + combination_str.count("K") == 3:
            player_tiles.fan_list.append("sananke") # 三暗刻
        elif combination_str.count("G") + combination_str.count("K") == 2:
            player_tiles.fan_list.append("shuanganke") # 双暗刻

        if combination_str.count("G") + combination_str.count("g") + combination_str.count("K") + combination_str.count("k") == 4:
            player_tiles.fan_list.append("pengpenghe") # 碰碰和

    def fan_count_combination_sign_check(self,player_tiles:PlayerTiles,combination_str,way_to_hepai):
        if combination_str == "":
            return
        save_dazi_sign = []
        save_kezi_sign = []
        save_quetou_sign = []
        for index,tile_id in enumerate(combination_str):
            if tile_id == "s" or tile_id == "S":
                save_dazi_sign.append(combination_str[index+1] + combination_str[index+2])
            elif tile_id == "k" or tile_id == "K" or tile_id == "g" or tile_id == "G":
                save_kezi_sign.append(combination_str[index+1] + combination_str[index+2])
            elif tile_id == "q":
                save_quetou_sign.append(combination_str[index+1] + combination_str[index+2])

        save_dazi_sign.sort()
        save_kezi_sign.sort()
        self.debug_print("搭子标记：",save_dazi_sign)
        self.debug_print("刻子标记：",save_kezi_sign)

        # 顺子关系判断 包含一色三步高 一色四步高 一色三同顺 一色四同顺 三色三步高 三色三同顺 清龙 花龙 喜相逢 连六 老少副
        # 根据顺子标记的步进判断同色内顺子的连续性 检测一色三步高和一色四步高 以1为步长
        if len(save_dazi_sign) >= 2:
            sign_pointer = int(save_dazi_sign[0])
            sign_count = 1
            for sign in save_dazi_sign:
                if int(sign) == sign_pointer + 1:
                    sign_count += 1
                    sign_pointer = int(sign)
                elif int(sign) == sign_pointer:
                    pass
                else: # 如果顺子标记的步进不连续 则重新开始计数
                    if sign_count <= 2:
                        sign_count = 1
                        sign_pointer = int(sign)
            if sign_count == 3:
                player_tiles.fan_list.append("yisesanbugao") # 一色三步高

            elif sign_count == 4:
                player_tiles.fan_list.append("yisesibugao") # 一色四步高

            # 根据顺子标记的步进判断同色内顺子的连续性 检测一色三步高和一色四步高 以2为步长
            sign_pointer = int(save_dazi_sign[0])
            sign_count = 1
            for sign in save_dazi_sign:
                if int(sign) == sign_pointer + 2:
                    sign_count += 1
                    sign_pointer = int(sign)
                elif int(sign) == sign_pointer:
                    pass
                else:
                    if sign_count <= 2:
                        sign_count = 1
                        sign_pointer = int(sign)
            if sign_count == 3:
                player_tiles.fan_list.append("yisesanbugao") # 一色三步高
            elif sign_count == 4:
                player_tiles.fan_list.append("yisesibugao") # 一色四步高

            # 根据顺子标记的相同值 检测一般高、一色三同顺和一色四同顺
            already_count = 0
            for i in save_dazi_sign:
                if i != already_count:
                    if save_dazi_sign.count(i) == 2:
                        player_tiles.fan_list.append("yibangao") # 一般高
                    elif save_dazi_sign.count(i) == 3:
                        player_tiles.fan_list.append("yisesantongshun") # 一色三同顺
                    elif save_dazi_sign.count(i) == 4:
                        player_tiles.fan_list.append("yisesitongshun") # 一色四同顺
                    already_count = i

            # 根据顺子与雀头标记的值查表 检测三色双龙会
            sanseshuanglonghui_list = [{"12","18","22","28","q35"},{"12","18","32","38","q25"},{"32","38","22","28","q15"}]
            for set in sanseshuanglonghui_list:
                # 分离顺子标记和雀头标记
                shunzi_in_set = [i for i in set if not i.startswith("q")]
                quetou_in_set = [i for i in set if i.startswith("q")]
                # 检查顺子标记是否都在 save_dazi_sign 中
                if all(i in save_dazi_sign for i in shunzi_in_set):
                    # 检查雀头标记是否匹配
                    if quetou_in_set and f"q{save_quetou_sign[0]}" in quetou_in_set:
                        player_tiles.fan_list.append("sanseshuanglonghui") # 三色双龙会
                        break

            # 根据顺子标记尾部的值 检测清龙
            wan_list = []
            bing_list = []
            tiao_list = []
            all_list = []
            for sign in save_dazi_sign:
                if sign[0] == "1":
                    wan_list.append(sign[1])
                    all_list.append(sign[1])
                elif sign[0] == "2":
                    bing_list.append(sign[1])
                    all_list.append(sign[1])
                elif sign[0] == "3":
                    tiao_list.append(sign[1])
                    all_list.append(sign[1])

            # 如果同组顺子有3个 且顺子尾部的值为2 5 8 则清龙
            suit_list = [wan_list,bing_list,tiao_list]
            for rank_list in suit_list:
                if len(rank_list) >= 3:
                    if any(i == "2" for i in rank_list):
                        if any(i == "5" for i in rank_list):
                            if any(i == "8" for i in rank_list):
                                player_tiles.fan_list.append("qinglong") # 清龙
                                break

            # 如果有三种顺子 且顺子尾部的值各包含以下六种排列的其中一种 则花龙
            hualong_form_list = [["2","5","8"],["2","8","5"],["5","2","8"],["5","8","2"],["8","2","5"],["8","5","2"]]
            for form in hualong_form_list:
                if form[0] in wan_list:
                    if form[1] in bing_list:
                        if form[2] in tiao_list:
                            player_tiles.fan_list.append("hualong") # 花龙
                            break

            # 判断 喜相逢 三色三同顺 三色三步高
            order_kind_list = [0,1,2]
            counted_pointer_list = []

            for order in order_kind_list:
                if order == 0:
                    # 三色三同顺判断
                    for i in suit_list[0]:
                        if i in suit_list[1]:
                            if i in suit_list[2]:
                                player_tiles.fan_list.append("sansesantongshun") # 三色三同顺
                                break
                    # 三色三步高判断
                    for i in suit_list[0]:
                        i = int(i)
                        self.debug_print(i)
                        # 如果[i,i+1,i+2 或者 i,i+1,i-1] 则三色三步高
                        if str(i+1) in suit_list[1]:
                            if str(i+2) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i-1) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                        # 如果[i,i-1,i-2 或者 i,i-1,i+1] 则三色三步高
                        if str(i-1) in suit_list[1]:
                            if str(i-2) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i+1) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                        # 如果[i,i+1,i+2 或者 i,i+1,i-1] 则三色三步高
                        if str(i+1) in suit_list[2]:
                            if str(i+2) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i-1) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                        # 如果[i,i-1,i-2 或者 i,i-1,i+1] 则三色三步高
                        if str(i-1) in suit_list[2]:
                            if str(i-2) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i+1) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break

                # 喜相逢判断
                    for i in suit_list[0]:
                        if (i in suit_list[1] or i in suit_list[2]) and i not in counted_pointer_list:
                            counted_pointer_list.append(i)
                            player_tiles.fan_list.append("xixiangfeng") # 喜相逢
                elif order == 1:
                    for i in suit_list[1]:
                        if (i in suit_list[0] or i in suit_list[2]) and i not in counted_pointer_list:
                            counted_pointer_list.append(i)
                            player_tiles.fan_list.append("xixiangfeng") # 喜相逢
                elif order == 2:
                    for i in suit_list[2]:
                        if (i in suit_list[0] or i in suit_list[1]) and i not in counted_pointer_list:
                            counted_pointer_list.append(i)
                            player_tiles.fan_list.append("xixiangfeng") # 喜相逢

            # 根据同色手牌标记的距离判断 连六 老少副
            # 连六按顺子对计数：仅当两侧起始点各有多余顺子时才复计（如 123123456456 计 2 次，123123456 只计 1 次）
            for list in [wan_list,bing_list,tiao_list]:
                if len(list) >= 2:
                    for rank in range(1, 7):
                        pair_count = min(list.count(str(rank)), list.count(str(rank + 3)))
                        for _ in range(pair_count):
                            player_tiles.fan_list.append("lianliu") # 连六
                min_count = min(list.count("2"),list.count("8"))
                if min_count != 0:
                    if min_count == 2 and "qingyise" in player_tiles.fan_list and int(save_quetou_sign[0]) % 10 == 5:
                        player_tiles.fan_list.append("yiseshuanglonghui") # 一色双龙会
                    else:
                        for i in range(min_count):
                            player_tiles.fan_list.append("laoshaofu") # 老少副

        # 刻子关系判断 包含一色三节高 一色四节高 全双刻 三同刻 双同刻 三色三节高
        if len(save_kezi_sign) >= 2:

            # 根据刻子标记的步进判断 一色三节高 一色四节高
            sign_pointer = int(save_kezi_sign[0])
            sign_count = 1
            for sign in save_kezi_sign:
                sign_val = int(sign)
                if sign_val == sign_pointer + 1 and sign_val <= 40:
                    sign_count += 1
                    sign_pointer = sign_val
                elif sign_val == sign_pointer:
                    pass
                else: # 步进不连续则重新开始计数
                    if sign_count <= 2:
                        sign_count = 1
                        sign_pointer = sign_val
            if sign_count >= 4:
                player_tiles.fan_list.append("yisesijiegao") # 一色四节高
            elif sign_count >= 3:
                player_tiles.fan_list.append("yisesanjiegao") # 一色三节高

            # 根据刻子标记的值的尾数切片判断 全双刻 三同刻 双同刻 三色三节高
            wan_list = []
            bing_list = []
            tiao_list = []
            all_list = []
            for sign in save_kezi_sign:
                if sign[0] == "1":
                    wan_list.append(sign[1])
                    all_list.append(sign[1])
                elif sign[0] == "2":
                    bing_list.append(sign[1])
                    all_list.append(sign[1])
                elif sign[0] == "3":
                    tiao_list.append(sign[1])
                    all_list.append(sign[1])

            if len(all_list) == 4:
                if all(i in ("2","4","6","8") for i in all_list):
                    if save_quetou_sign:
                        quetou_id = int(save_quetou_sign[0])
                        if quetou_id < 40 and quetou_id % 10 in (2, 4, 6, 8):
                            player_tiles.fan_list.append("quanshuangke") # 全双刻

            already_count_list = []
            self.debug_print(all_list)
            for rank in all_list:
                if all_list.count(rank) >= 2 and rank not in already_count_list:
                    already_count_list.append(rank)
                    if all_list.count(rank) == 3:
                        player_tiles.fan_list.append("santongke") # 三同刻
                    elif all_list.count(rank) == 2:
                        player_tiles.fan_list.append("shuangtongke") # 双同刻
            self.debug_print(wan_list,bing_list,tiao_list)
            for i in wan_list:
                if str(int(i)+1) in bing_list:
                    if str(int(i)+2) in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                    if str(int(i)-1) in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                if str(int(i)-1) in bing_list:
                    if str(int(i)-2) in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                    if str(int(i)+1) in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                if str(int(i)+1) in tiao_list:
                    if str(int(i)+2) in bing_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                    if str(int(i)-1) in bing_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                if str(int(i)-1) in tiao_list:
                    if str(int(i)-2) in bing_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break
                    if str(int(i)+1) in bing_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        break

        # 根据传参和字牌的关系判断 门风刻 圈风刻
        menfeng = "None"
        if "自风东" in way_to_hepai:
            menfeng = "41"
        elif "自风南" in way_to_hepai:
            menfeng = "42"
        elif "自风西" in way_to_hepai:
            menfeng = "43"
        elif "自风北" in way_to_hepai:
            menfeng = "44"
        changfeng = "null"
        if "场风东" in way_to_hepai:
            changfeng = "41"
        elif "场风南" in way_to_hepai:
            changfeng = "42"
        elif "场风西" in way_to_hepai:
            changfeng = "43"
        elif "场风北" in way_to_hepai:
            changfeng = "44"
        if menfeng in save_kezi_sign:
            player_tiles.fan_list.append("menfengke") # 门风刻
        if changfeng in save_kezi_sign:
            player_tiles.fan_list.append("quanfengke") # 圈风刻
        if menfeng == changfeng:
            way_to_hepai.append("门风圈风相同")

    def fan_count_hepai_relationship_check(self,player_tiles:PlayerTiles,combination_str,get_tile,way_to_hepai):

        # 判断 边张 嵌张 单吊将 妙手回春 杠上开花 抢杠和 和绝张 花牌 海底捞月 全求人 门前清 不求人 自摸
        for i in way_to_hepai:
            match i:

                # 开始判断关于和牌关系的番种 包括 边张、嵌张、单吊将
                case "和单张":
                    # 边张的位置如果有顺子则可判边张
                    if get_tile % 10 == 3:
                        if f"S{get_tile-1}" in player_tiles.combination_list:
                            player_tiles.fan_list.append("bianzhang") # 边张
                            continue
                    elif get_tile % 10 == 7:
                        if f"S{get_tile+1}" in player_tiles.combination_list:
                            player_tiles.fan_list.append("bianzhang") # 边张
                            continue
                    # 在和单张的情况下如果有所在位置的顺子则可判嵌张
                    if f"S{get_tile}" in player_tiles.combination_list:
                        player_tiles.fan_list.append("qianzhang") # 嵌张
                        continue
                    # 在和单张的情况下如果有所在位置的雀头则可判单吊将
                    if f"q{get_tile}" in player_tiles.combination_list:
                        player_tiles.fan_list.append("dandiaojiang") # 单吊将
                        continue

                # 开始判断传参番种 包括 last_deal 杠上开花 抢杠和 和绝张 花牌 last_cut 全求人 门前清 不求人 自摸
                case "last_deal" | "妙手回春":
                    player_tiles.fan_list.append("miaoshouhuichun") # 妙手回春（牌墙空自摸）
                case "杠上开花":
                    player_tiles.fan_list.append("gangshangkaihua") # 杠上开花
                case "抢杠和":
                    player_tiles.fan_list.append("qiangganghe") # 抢杠和
                case "和绝张":
                    player_tiles.fan_list.append("hejuezhang") # 和绝张
                case "花牌":
                    player_tiles.fan_list.append("huapai") # 花牌
                case "last_cut" | "海底捞月":
                    player_tiles.fan_list.append("haidilaoyue") # 海底捞月（牌墙空荣和）
                case "点和":
                    self.debug_print(player_tiles.combination_list)
                    if combination_str != "" and all(i not in ["S","K","G","z"] for i in combination_str) and "和单张" in way_to_hepai:
                        player_tiles.fan_list.append("quanqiuren") # 全求人
                    elif combination_str.count("s") + combination_str.count("k") + combination_str.count("g") == 0:
                        player_tiles.fan_list.append("menqianqing") # 门前清
                    elif "暗转明" in way_to_hepai:
                        if combination_str.count("s") + combination_str.count("k") + combination_str.count("g") == 1:
                            player_tiles.fan_list.append("menqianqing") # 门前清
                case "自摸":
                    if all(i not in ["s","k","g"] for i in combination_str):
                        # 由于七对子，连七对，九莲宝灯的自摸不计不求人，但是不求人又不计自摸，所以如果不求人和自摸都存在，就会被同时剔除
                        # 添加额外验证避免阻挡番嵌套 暂时是粗糙的方法 也可以使用阻挡牌优先级依次消除阻挡
                        if any(i in {"qiduizi","jiulianbaodeng","lianqidui","shisanyao","sianke","qixingbukao","quanbukao"} for i in player_tiles.fan_list):
                            player_tiles.fan_list.append("zimo") # 自摸
                        else:
                            player_tiles.fan_list.append("buqiuren") # 不求人
                    else:
                        player_tiles.fan_list.append("zimo") # 自摸

    def fan_count(self, player_tiles: PlayerTiles,get_tile,way_to_hepai):

        # 判断前处理 处理get_tile
        zimo_or_not = False
        if any(i in ["last_deal", "妙手回春", "自摸", "杠上开花"] for i in way_to_hepai):
            zimo_or_not = True
        if zimo_or_not == False:
            # 如果和牌张来自外部 暗杠转为明杠 暗刻转为明刻 暗顺明顺仅用于标识是否副露 不用转换 标记暗转明用于后续计算门前清
            for i in player_tiles.combination_list:
                if i == f"G{get_tile}":
                    # 如果和牌张所在位置在有暗刻的同时拥有暗顺侧的其他组合,应当看做手牌被阻挡,原因在于将和牌张-
                    # 看做顺子能够保留暗刻权益,而暗刻权益在任何情况下总是更高
                    # 全不靠情况下任何情况只能保留一个暗刻,不会构成影响和牌权益的和牌构成
                    if any(i in player_tiles.combination_list for i in [f"S{get_tile}",f"S{get_tile+1}",f"S{get_tile-1}"]):
                        pass
                    else:
                        player_tiles.combination_list.remove(i)
                        player_tiles.combination_list.append(f"g{i[1]}{i[2]}")
                        way_to_hepai.append("暗转明")
                        break
                elif i == f"K{get_tile}":
                    if any(i in player_tiles.combination_list for i in [f"S{get_tile}",f"S{get_tile+1}",f"S{get_tile-1}"]):
                        pass
                    else:
                        player_tiles.combination_list.remove(i)
                        player_tiles.combination_list.append(f"k{i[1]}{i[2]}")
                        way_to_hepai.append("暗转明")
                        break

        # 判断前处理 建立手牌映射和组合映射
        hand_tiles_list = []
        combination_str = ""
        # 七对子情况下手牌直接等于传参的手牌,因为在QDcheck中没有对手牌进行移除,也没有添加组合
        if any(i in player_tiles.fan_list for i in ["qiduizi","lianqidui"]):
            hand_tiles_list = player_tiles.hand_tiles
        # 全不靠情况下手牌进行清空,因为全不靠和七星不靠不复合其他手牌映射的番种
        elif any(i in player_tiles.fan_list for i in ["quanbukao","qixingbukao"]):
            hand_tiles_list = []
        # 正常型和组合龙正常建立手牌映射
        else:
            for i in player_tiles.combination_list:
                if i in self.combination_to_tiles_dict:
                    hand_tiles_list.extend(self.combination_to_tiles_dict[i])
                hand_tiles_list.sort()
        # 七对子没有组合映射,全不靠和七星不靠没有手牌映射,正常型和组合龙正常建立组合映射和手牌映射
        for i in player_tiles.combination_list:
            combination_str += i
        self.debug_print("组合映射：",combination_str)
        self.debug_print("手牌映射：",hand_tiles_list)



        # 外部传参番值 [十三幺 组合龙 七对子 连七对 全不靠 七星不靠]

        # 通过生成手牌映射查表计算 [清一色 混一色 字一色 断幺 混幺九 清幺九 全中 全大 全小 大于五 小于五 缺一门 推不倒 四归一 五门齐]
        # [绿一色 无字 九莲宝灯]
        self.fan_count_hand_check(player_tiles,hand_tiles_list,get_tile)

        # 通过遍历组合列表计算 [全带五 全带幺 箭刻 双箭刻 大四喜 小四喜 三风刻 小三元 大三元 幺九刻]
        self.fan_count_combination_check(player_tiles)

        # 通过组合映射计算 [平和 四杠 三杠 四暗刻 三暗刻 双暗刻 碰碰和 暗杠 双暗杠 双明杠 明杠 明暗杠]
        self.fan_count_combination_str_check(player_tiles,combination_str,hand_tiles_list)

        # 通过组合映射标记计算 [一色三步高 一色四步高 一色三同顺 一色四同顺 三色三步高 三色三同顺 三色双龙会 清龙 花龙 喜相逢 连六 老少副 一色双龙会]
        # [一色三节高 一色四节高 全双刻 三同刻 双同刻 三色三节高 门风刻 圈风刻 ]
        self.fan_count_combination_sign_check(player_tiles,combination_str,way_to_hepai)

        # 通过和牌关系计算 [嵌张 单吊将 边张 妙手回春 杠上开花 抢杠和 和绝张 花牌 海底捞月 全求人 门前清 不求人 自摸]
        self.fan_count_hepai_relationship_check(player_tiles,combination_str,get_tile,way_to_hepai)

        self.debug_print("现在存在的组合",player_tiles.combination_list)
        # 通过番种列表清理阻挡番种 输出文本和得分
        result = self.fan_count_output(player_tiles, combination_str, zimo_or_not, way_to_hepai)
        return result # 元组(int,list[str])

    def hepai_decompose(self, hand_list: list, tiles_combination: list, way_to_hepai: list, get_tile: int) -> list:
        """
        和牌拆解：返回所有有效的和牌拆解形态及其番种与分数。

        Returns:
            按番数从高到低排序的列表，每个元素为：
            {
                "score": int,
                "fan_list": List[str],     中文番种名
                "fan_keys": List[str],     英文番种 key（剔除 0 番后）
                "combinations": List[str], 例如 ["s12","k15","K33","q41"]
            }
            非和牌返回空列表。
        """
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list, tiles_combination, complete_step)

        player_tiles_list = []
        if len(player_tiles.hand_tiles) == 14:
            if not player_tiles_list:
                self.GS_check(player_tiles, player_tiles_list)
            if not player_tiles_list:
                self.QBK_check(player_tiles, player_tiles_list)
            if not player_tiles_list:
                self.QD_check(player_tiles, player_tiles_list)
        else:
            self.QBK_check(player_tiles, player_tiles_list)
        player_tiles_list.append(player_tiles)

        check_done_list = []
        for player_tiles_item in player_tiles_list:
            self.normal_check(player_tiles_item, check_done_list)

        chinese_to_eng = {v: k for k, v in self.eng_to_chinese_dict.items()}

        results = []
        for pt in check_done_list:
            local_way = list(way_to_hepai) if way_to_hepai else []
            score, fan_list_cn = self.fan_count(pt, get_tile, local_way)
            # fan_count 后 combination_list 已反映最终拆解（含暗转明等修正）
            fan_keys = []
            for name in fan_list_cn:
                if "*" in name:
                    base, _, count_str = name.partition("*")
                    base = base.strip()
                    if base in chinese_to_eng:
                        fan_keys.append(f"{chinese_to_eng[base]}*{count_str.strip()}")
                elif name in chinese_to_eng:
                    fan_keys.append(chinese_to_eng[name])
            results.append({
                "score": score,
                "fan_list": list(fan_list_cn),
                "fan_keys": fan_keys,
                "combinations": list(pt.combination_list),
            })

        # 同一拆分可能由不同递归路径抵达；对外只暴露一次，避免调用方重复展示。
        unique_results = {}
        for result in results:
            key = (
                tuple(sorted(result["combinations"])),
                tuple(result["fan_keys"]),
                result["score"],
            )
            unique_results.setdefault(key, result)
        ordered_results = list(unique_results.values())
        ordered_results.sort(key=lambda item: item["score"], reverse=True)
        return ordered_results

    def QD_check(self, player_tiles: PlayerTiles, player_tiles_list: List[PlayerTiles]):
        """七对必须是七种不同对子，四张同牌不能拆成两个对子。"""
        counts = Counter(player_tiles.hand_tiles)
        if len(counts) != 7 or any(count != 2 for count in counts.values()):
            return False
        candidate = player_tiles.__deepcopy__(None)
        candidate.complete_step = 14
        candidate.fan_list.append("qixingdui" if set(counts) == self.zipai_set else "qiduizi")
        player_tiles_list.append(candidate)
        return False

    def QBK_check(self, player_tiles: PlayerTiles, player_tiles_list: List[PlayerTiles]):
        """蓝十没有组合龙和七星不靠，只保留规则表定义的全不靠。"""
        hand = player_tiles.hand_tiles
        if len(hand) == 14 and len(set(hand)) == 14 and any(set(hand) <= case for case in self._unrelated_cases):
            candidate = player_tiles.__deepcopy__(None)
            candidate.complete_step = 14
            candidate.fan_list.append("quanbukao")
            player_tiles_list.append(candidate)
        return False

    @staticmethod
    def _sequence(token: str) -> Tuple[int, int]:
        tile = int(token[1:])
        return tile // 10, tile % 10 - 1

    @staticmethod
    def _triplet(token: str) -> Tuple[int, int]:
        tile = int(token[1:])
        return tile // 10, tile % 10

    @staticmethod
    def _low_relation(a: Tuple[int, int], b: Tuple[int, int]) -> str | None:
        (suit_a, rank_a), (suit_b, rank_b) = a, b
        if a == b:
            return "yibangao"
        if rank_a == rank_b and suit_a != suit_b:
            return "xixiangfeng"
        if suit_a == suit_b and abs(rank_a - rank_b) == 3:
            return "lianliu"
        if suit_a == suit_b and {rank_a, rank_b} == {1, 7}:
            return "laoshaofu"
        return None

    @classmethod
    def _best_low_sequence_fans(
        cls,
        seqs: Sequence[Tuple[int, int]],
        occupied: frozenset[int] = frozenset(),
    ) -> List[str]:
        """按国标不循环组合原则，取双顺番的最高分无环组合。"""
        edges: List[Tuple[int, int, str]] = []
        for left, right in combinations(range(len(seqs)), 2):
            if left in occupied and right in occupied:
                continue
            relation = cls._low_relation(seqs[left], seqs[right])
            if relation:
                edges.append((left, right, relation))

        best: Tuple[int, Tuple[int, ...], List[str]] = (0, (), [])
        for size in range(len(edges) + 1):
            for selected in combinations(range(len(edges)), size):
                parent = list(range(len(seqs)))

                def root(node: int) -> int:
                    while parent[node] != node:
                        parent[node] = parent[parent[node]]
                        node = parent[node]
                    return node

                valid = True
                names: List[str] = []
                for edge_index in selected:
                    left, right, name = edges[edge_index]
                    left_root, right_root = root(left), root(right)
                    if left_root == right_root:
                        valid = False
                        break
                    parent[left_root] = right_root
                    names.append(name)
                if not valid:
                    continue
                score = sum(cls.count_model_dict[name] for name in names)
                tie = tuple(-cls._table_order.index(name) for name in names)
                if (score, tie) > (best[0], best[1]):
                    best = (score, tie, names)
        return best[2]

    @classmethod
    def _sequence_fans(cls, tokens: Sequence[str]) -> List[str]:
        """计算蓝十顺系列番，并记录高番实际占用的面子。"""
        seqs = [cls._sequence(token) for token in tokens if token and token[0] in "sS"]
        if not seqs:
            return []

        candidates: List[Tuple[int, int, str, frozenset[int]]] = []

        def add(name: str, indices: Sequence[int]) -> None:
            used = frozenset(indices)
            candidates.append((cls.count_model_dict[name], len(used), name, used))

        for value, indices in Counter(seqs).items():
            positions = [index for index, sequence in enumerate(seqs) if sequence == value]
            if len(positions) >= 4:
                add("sitongshun", positions[:4])
            elif len(positions) >= 3:
                add("santongshun", positions[:3])

        for suit in (1, 2, 3):
            for start in range(1, 5):
                indices = [next((i for i, value in enumerate(seqs) if value == (suit, start + step)), -1) for step in range(4)]
                if min(indices) >= 0:
                    add("silianshun", indices)
            for start in range(1, 6):
                indices = [next((i for i, value in enumerate(seqs) if value == (suit, start + step)), -1) for step in range(3)]
                if min(indices) >= 0:
                    add("sanlianshun", indices)
            indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank)), -1) for rank in (1, 3, 5, 7)]
            if min(indices) >= 0:
                add("shunlian", indices)
            indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank)), -1) for rank in (1, 4, 7)]
            if min(indices) >= 0:
                add("qinglong", indices)

        if len(seqs) == 4:
            relations = []
            for left, right in ((0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3)):
                relation = cls._low_relation(seqs[left], seqs[right])
                if relation in {"xixiangfeng", "lianliu", "laoshaofu"}:
                    relations.append((left, right, relation, frozenset((seqs[left], seqs[right]))))
            if any(first[2:] == second[2:] and {first[0], first[1]}.isdisjoint({second[0], second[1]})
                   for first, second in combinations(relations, 2)):
                add("shunwang", range(4))

            suits = {suit for suit, _rank in seqs}
            if len(suits) == 2:
                grouped = [sorted(rank for current, rank in seqs if current == suit) for suit in suits]
                if len(grouped[0]) == len(grouped[1]) == 2 and grouped[0] == grouped[1] and (
                    abs(grouped[0][0] - grouped[0][1]) == 3 or set(grouped[0]) == {1, 7}
                ):
                    add("shunhuan", range(4))

        for rank in range(1, 8):
            indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank)), -1) for suit in (1, 2, 3)]
            if min(indices) >= 0:
                add("sansetongshun", indices)
        for suit_order in permutations((1, 2, 3)):
            dragon = [next((i for i, value in enumerate(seqs) if value == pair), -1)
                      for pair in zip(suit_order, (1, 4, 7))]
            if min(dragon) >= 0:
                add("hualong", dragon)
            for rank in range(1, 6):
                indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank + offset)), -1)
                           for suit, offset in zip(suit_order, (0, 1, 2))]
                if min(indices) >= 0:
                    add("sanselianshun", indices)

        # 同一组面子只采用一个最高的三/四顺主体番；第四副顺子仍可与主体中的
        # 一副组成一个合法双顺番。主体内部的固有低番不再显示。
        if candidates:
            _score, _size, name, occupied = max(candidates, key=lambda item: (item[0], item[1], -cls._table_order.index(item[2])))
            if len(occupied) == 4:
                return [name]
            low = cls._best_low_sequence_fans(seqs, occupied)
            return [name] + low[:1]
        return cls._best_low_sequence_fans(seqs)

    @classmethod
    def _extra_triplet_fans(cls, tokens: Sequence[str]) -> List[str]:
        trips = [cls._triplet(token) for token in tokens if token and token[0] in "kKgG"]
        fans: List[str] = []
        for suit in (1, 2, 3):
            ranks = {rank for current_suit, rank in trips if current_suit == suit}
            if any(all(start + step in ranks for step in range(4)) for start in range(1, 7)):
                fans.append("silianke")
            elif any(all(start + step in ranks for step in range(3)) for start in range(1, 8)):
                fans.append("sanlianke")
        if not any(name in fans for name in ("silianke", "sanlianke")):
            for suit_order in permutations((1, 2, 3)):
                if any(all((suit, rank + offset) in trips for suit, offset in zip(suit_order, (0, 1, 2)))
                       for rank in range(1, 8)):
                    fans.append("sanselianke")
                    break
        return fans

    def _collect_lanshi_fans(self, player_tiles: PlayerTiles, way: Sequence[str]) -> List[str]:
        detected = player_tiles.fan_list
        fans = [name for name in detected if name in self._native_fan_names]
        if "buqiuren" in detected:
            fans.extend(("menqianqing", "zimo"))

        tokens = [token for token in player_tiles.combination_list if token and token[0] in "sSkKgGq"]
        fans.extend(self._sequence_fans(tokens))
        fans.extend(self._extra_triplet_fans(tokens))

        if "quandaiyao" in detected:
            fans.append("hunquandaiyao" if any(int(token[1:]) >= 40 for token in tokens) else "qingquandaiyao")
        if any(name in detected for name in ("shuangangang", "shuangminggang", "mingangang")):
            fans.append("shuanggang")
        if "天和" in way:
            fans.append("tianhe")
        if "地和" in way:
            fans.append("dihe")

        # 蓝十 A.12.1 要求先按常规方式计分，再判断是否以 5 分偶然番
        # 替代常规番。这些伴随番是和牌事实的必然结果，不能依赖上游
        # way 是否额外传入了“和绝张”或“自摸”。
        if "qiangganghe" in fans and "hejuezhang" not in fans:
            fans.append("hejuezhang")
        if any(name in fans for name in ("miaoshouhuichun", "gangshangkaihua", "tianhe")) and "zimo" not in fans:
            fans.append("zimo")

        # 箭牌系列以最终采用的拆分统一重建，避免基础识别顺序影响
        # 大三元、小三元、双箭刻和箭刻之间的互斥层级。
        dragon_family = {"dasanyuan", "xiaosanyuan", "shuangjianke", "jianke"}
        fans = [name for name in fans if name not in dragon_family]
        dragon_trips = {
            int(token[1:]) for token in tokens
            if token[0] in "kKgG" and token[1:].isdigit() and 45 <= int(token[1:]) <= 47
        }
        dragon_pairs = {
            int(token[1:]) for token in tokens
            if token[0] == "q" and token[1:].isdigit() and 45 <= int(token[1:]) <= 47
        }
        if len(dragon_trips) == 3:
            fans.append("dasanyuan")
        elif len(dragon_trips) == 2 and dragon_pairs - dragon_trips:
            fans.append("xiaosanyuan")
        elif len(dragon_trips) == 2:
            fans.append("shuangjianke")
        elif len(dragon_trips) == 1:
            fans.append("jianke")
        return fans

    @staticmethod
    def _remove_once(fans: List[str], names: Sequence[str]) -> None:
        for name in names:
            if name in fans:
                fans.remove(name)

    def _apply_exclusions(self, fans: List[str], way: Sequence[str]) -> List[str]:
        # 满贯番不加计任何其他番种。
        hundred = next((name for name in self._table_order if name in fans and self.count_model_dict[name] == 100), None)
        if hundred:
            return [hundred]

        rules = {
            "dasixi": ["xiaosixi", "sanfengke", "pengpenghe", "quanfengke", "menfengke"] + ["yaojiuke"] * 4,
            "qingyaojiu": ["pengpenghe"] + ["yaojiuke"] * 4,
            "sianke": ["sananke", "shuanganke", "pengpenghe", "menqianqing"],
            "shisanyao": ["hunyaojiu", "wumenqi", "menqianqing"],
            "ziyise": ["pengpenghe"] + ["yaojiuke"] * 4,
            "silianke": ["sanlianke", "santongshun", "pengpenghe"],
            "xiaosixi": ["sanfengke"] + ["yaojiuke"] * 3,
            "sangang": ["shuanggang", "angang", "minggang"],
            "dasanyuan": ["xiaosanyuan", "shuangjianke", "jianke"] + ["yaojiuke"] * 3,
            "shunwang": ["qiduizi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu"],
            "santongshun": ["sanlianke", "yibangao"],
            "hunyaojiu": ["qiduizi", "pengpenghe"] + ["yaojiuke"] * 4,
            "quanda": ["dayuwu"], "quanzhong": ["duanyao"], "quanxiao": ["xiaoyuwu"],
            "quandaiwu": ["duanyao"], "santongke": ["shuangtongke"],
            "xiaosanyuan": ["shuangjianke", "jianke"] + ["yaojiuke"] * 2,
            "quanbukao": ["wumenqi", "menqianqing"],
            "sanfengke": ["yaojiuke"] * 3, "sananke": ["shuanganke"],
            "sanlianke": ["santongshun"], "shuanggang": ["angang", "minggang"],
            "qiduizi": ["menqianqing"],
            "shuangjianke": ["jianke"] + ["yaojiuke"] * 2,
            "jianke": ["yaojiuke"],
        }
        result = list(fans)
        for fan in list(fans):
            self._remove_once(result, rules.get(fan, ()))

        wind_removals = int("quanfengke" in fans) + int("menfengke" in fans)
        if wind_removals == 2 and "门风圈风相同" in way:
            wind_removals = 1
        self._remove_once(result, ["yaojiuke"] * wind_removals)

        # 5 分偶然番：常规番不足 5 分时仅计偶然番；达到 5 分时反而不计偶然番。
        occasional = next((name for name in self._occasional if name in result), None)
        if occasional:
            regular = [name for name in result if name not in self._occasional]
            regular_score = sum(self.count_model_dict[name] for name in regular)
            return regular if regular_score >= 5 else [occasional]
        return result

    def _score(self, fans: Sequence[str]) -> Tuple[int, List[str]]:
        ordered = sorted(fans, key=lambda name: self._table_order.index(name))
        score = 0
        output: List[str] = []
        for name in self._table_order:
            count = ordered.count(name)
            if not count:
                continue
            if name in self._repeatable:
                score += count * self.count_model_dict[name]
                output.append(f"{self.eng_to_chinese_dict[name]}*{count}")
            else:
                score += self.count_model_dict[name]
                output.append(self.eng_to_chinese_dict[name])
        return min(score, 100), output

    def fan_count_output(self, player_tiles: PlayerTiles, combination_str, zimo_or_not, way_to_hepai):
        """汇总蓝十番种、执行不计规则并输出最终分数。"""
        fans = self._collect_lanshi_fans(player_tiles, way_to_hepai)
        return self._score(self._apply_exclusions(fans, way_to_hepai))

    def filter_zero_value_fans(self, fan_score: int, fan_count_list: List[str]) -> Tuple[int, List[str]]:
        # 蓝十番表没有 0 分番；保留接口兼容，但绝不返回 0 分占位。
        return min(fan_score, 100), [name for name in fan_count_list if not name.endswith("*0")]
