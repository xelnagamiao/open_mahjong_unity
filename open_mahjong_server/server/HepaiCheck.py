from typing import Dict,List
import random
from time import time

class PlayerTiles:
    def __init__(self, tiles_list, combination_list,complete_step):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step # +3 +3 +3 +3 +2 = 14
        self.fan_list = []
    
    def __deepcopy__(self, memo):
        return PlayerTiles(self.hand_tiles[:],
                         self.combination_list[:], 
                         self.complete_step)

class HePaiCheck:
    def __init__(self):
        # 存储排斥的番种 # 仅 64. 四归一、65. 双同刻、69. 一般高、70. 喜相逢、71. 连六、73. 幺九刻、81. 花牌七个番种允许复计。 先计算减计列表，然后取最大值向下覆盖
        self.repel_model_dict:Dict[int,list] ={
            "dasixi":["pengpenghe","quanfengke","menfengke"]+["yaojiuke"]*4,"dasanyuan":["yaojiuke"]*3, # 大四喜 大三元
            "lvyise":["hunyise"],"sigang":["pengpenghe","dandiaojiang"], # 绿一色 四杠子
            "jiulianbaodeng_dianhe":["qingyise","wuzi","yaojiuke","menqianqing"],"jiulianbaodeng_zimo":["qingyise","wuzi","buqiuren","yaojiuke"], # 九莲宝灯 点和/自摸
            "lianqidui_dianhe":["qidui","qingyise","wuzi","menqianqing"],"lianqidui_zimo":["qidui","qingyise","wuzi","buqiuren"], # 连七对 点和/自摸
            "shisanyao_dianhe":["hunyaojiu","wumenqi","menqianqing"],"shisanyao_zimo":["hunyaojiu","wumenqi","buqiuren"], # 十三幺 点和/自摸
            "qingyaojiu":["pengpenghe","quandaiyao","shuangtongke","yaojiuke","wuzi"],"xiaosixi":["sanfengke"] + ["yaojiuke"]*3, # 清幺九 小四喜
            "xiaosanyuan":["shuangjianke"] + ["yaojiuke"]*2 ,"ziyise":["pengpenghe","quandaiyao"] + ["yaojiuke"]*4, # 小三元 字一色
            "sianke_dianhe":["pengpenghe","menqianqing"],"sianke_zimo":["pengpenghe","buqiuren"], # 四暗刻 点和/自摸

            "yiseshuanglonghui":["qingyise","pinghe","wuzi"], "yisesitongshun":["siguiyi"], # 一色双龙会 一色四同顺
            "yisesijiegao":["pengpenghe"],"yisesibugao":[],"sangang":[], # 一色四节高 一色四步高 三杠
            "hunyaojiu":["pengpenghe","quandaiyao","yaojiuke"], # 混幺九
            "qidui_dianhe":["menqianqing"],"qidui_zimo":["buqiuren"], # 七对 点和/自摸
            "qixingbukao_dianhe":["quanbukao","wumenqi","menqianqing"],"qixingbukao_zimo":["quanbukao","wumenqi","buqiuren"], # 七星不靠 全不靠
            "quanshuangke":["pengpenghe","duanyao","wuzi"], # 全双刻
            "qingyise":["wuzi"],"yisesantongshun":[],"yisesanjiegao":[], # 一色四顺 一色四节高
            "quanda":["dayuwu","wuzi"],"quanzhong":["duanyao","wuzi"],"quanxiao":["xiaoyuwu","wuzi"], # 全大 全中 全小
            "qinglong":[],"sanseshuanglonghui":["pinghe","wuzi"],"yisesanbugao":[], # 清龙 三色双龙会 一色三步高
            "quandaiwu":["duanyao","wuzi"],"santongke":[],"sananke":[], # 全带五 三同刻 三暗刻
            "quanbukao_dianhe":["menqianqing"],"quanbukao_zimo":["buqiuren"], # 全不靠 点和/自摸
            "zuhelong":[],"dayuwu":["wuzi"],"xiaoyuwu":["wuzi"],"sanfengke":[], # 组合龙 大于五 小于五 三风刻
            "hualong":[],"tuibudao":["queyimen"],"sansesantongshun":[],"sansesanjiegao":[], # 花龙 推不倒 三色三同顺 三色三节高
            "wufanhe":[],"miaoshouhuicun":[],"haidilaoyue":[],"gangshangkaihua":["zimo"], # 无番和 妙手回春 海底捞月 杠上开花
            "qiangganghe":["hejuezhang"],"pengpenghe":[],"hunyise":[],"sansesanbugao":[], # 抢杠和 碰碰和 混一色 一色三步高
            "wumenqi":[],"quanqiuren":["dandiaojiang"],"shuangangang":["shuanganke"], # 五门齐 全求人 双暗杠
            "shuangjianke":["yaojiuke"],"quandaiyao":[],"buqiuren":["zimo"], # 双箭刻 全带幺 不求人
            "shuangminggang":[],"hejuezhang":[],"jianke":[],"quanfengke":[], # 双明杠 和绝张 箭刻 全风刻
            "menfengke":[],"menqianqing":[],"pinghe":["wuzi"],"siguiyi":[], # 门风刻 门前清 平和 四归一
            "shuangtongke":[],"shuanganke":[],"angang":[],"duanyao":["wuzi"], # 双同刻 双暗刻 暗杠 断幺
            "yibangao":[],"xixiangfeng":[],"lianliu":[],"laoshaofu":[],"yaojiuke":[], # 一般高 喜相逢 连六 老少副 幺九刻
            "minggang":[],"queyimen":[],"wuzi":[],"bianzhang":[],"qianzhang":[], # 明杠 缺一门 无字 边张 嵌张
            "dandiaojiang":[],"zimo":[],"huapai":[],"mingangang":[], # 单钓将 自摸 花牌 明暗杠
            }
        # 存储番种的番数
        self.count_model_dict:Dict[str,int] = {
            "dasixi":88,"dasanyuan":88,"lvyise":88,"jiulianbaodeng_dianhe":88,"jiulianbaodeng_zimo":88,"sigang":88,
            "lianqidui_dianhe":88,"lianqidui_zimo":88,"shisanyao_dianhe":88,"shisanyao_zimo":88,
            "qingyaojiu":64,"xiaosixi":64,"xiaosanyuan":64,"ziyise":64,"sianke_dianhe":64,"sianke_zimo":64,"yiseshuanglonghui":64,
            "yisesitongshun":48,"yisesijiegao":48,"yisesibugao":32,"sangang":32,"hunyaojiu":32,
            "qidui_dianhe":24,"qidui_zimo":24,"qixingbukao_dianhe":24,"qixingbukao_zimo":24,"quanshuangke":24,
            "qingyise":24,"yisesantongshun":24,"yisesanjiegao":24,"quanda":24,"quanzhong":24,"quanxiao":24,
            "qinglong":16,"sanseshuanglonghui":16,"yisesanbugao":16,"quandaiwu":16,"santongke":16,"sananke":16,"quanbukao_dianhe":12,
            "quanbukao_zimo":12,"zuhelong":12,"dayuwu":12,"xiaoyuwu":12,"sanfengke":12,
            "hualong":8,"tuibudao":8,"sansesantongshun":8,"sansesanjiegao":8,"wufanhe":8,"miaoshouhuicun":8,"haidilaoyue":8,
            "gangshangkaihua":8,"qiangganghe":8,"pengpenghe":6,"hunyise":6,"sansesanbugao":6,"wumenqi":6,"quanqiuren":6,"shuangangang":6,"shuangjianke":6,
            "quandaiyao":4,"buqiuren":4,"shuangminggang":4,"hejuezhang":4,"jianke":2,"quanfengke":2,"menfengke":2,"menqianqing":2,
            "pinghe":2,"siguiyi":2,"shuangtongke":2,"shuanganke":2,"angang":2,"duanyao":2,"yibangao":1,"xixiangfeng":1,
            "lianliu":1,"laoshaofu":1,"yaojiuke":1,"minggang":1,"queyimen":1,"wuzi":1,"bianzhang":1,
            "qianzhang":1,"dandiaojiang":1,"zimo":1,"huapai":1,"mingangang":5,
            }
        # 存储组合 => 手牌的映射
        self.combination_to_tiles_dict:Dict[str,List[int]] = {
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
        }


        self.yaojiu = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47}
        self.zipai = {41, 42, 43, 44, 45, 46, 47}

    def hepai_check(self,hand_list:list,tiles_combination,way_to_hepai,get_tile):
        hand_list.append(get_tile)
        tiles_combination = tiles_combination
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list,tiles_combination,complete_step)


        print("手牌：",player_tiles.hand_tiles,"组合：",player_tiles.combination_list)
        
        player_tiles_list = []
        if len(player_tiles.hand_tiles) == 14:
            self.GS_check(player_tiles,player_tiles_list)  # 国士无双check
            if not player_tiles_list:
                self.QBK_check(player_tiles,player_tiles_list)  # 全不靠check
                if not player_tiles_list:
                    player_tiles_list = self.normal_check(player_tiles)
                    self.QD_check(player_tiles,player_tiles_list)  # 七对子可能复合二杯口
        else:
            player_tiles_list = self.normal_check(player_tiles)

        if player_tiles_list:
            for i in player_tiles_list:
                self.fan_count(i,get_tile,way_to_hepai)


        print("胡牌番种：",player_tiles.fan_list.append)

        

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
    
    def QD_check(self, player_tiles:PlayerTiles,player_tiles_list):
        temp_player_tiles = player_tiles.__deepcopy__(None)
        # 统计每种牌的数量
        tile_counts = {}
        for tile_id in temp_player_tiles.hand_tiles:
            if tile_id in tile_counts:
                tile_counts[tile_id] += 1
            else:
                tile_counts[tile_id] = 1
        # 如果存在不是2张的牌，则不符合七对子
        for tile_id, count in tile_counts.items():
            if count == 2:
                pass
            else:
                return False
            
        tile_pointer = temp_player_tiles.hand_tiles[0]
        for i in temp_player_tiles.hand_tiles:
            if i == tile_pointer or i + 1 == tile_pointer:
                tile_pointer = i
            else:
                break
        else:
            temp_player_tiles.fan_list.append("lianqidui")

        temp_player_tiles.complete_step = 14
        temp_player_tiles.fan_list.append("qiduizi") # 七对子
        player_tiles_list.append(temp_player_tiles)

    def QBK_check(self, player_tiles:PlayerTiles,player_tiles_list):
        # 全不靠check可以查六种可能性的表，也可以进行切片计算操作，这里采用切片计算操作
        first_suit = []
        second_suit = []
        third_suit = []
        null_suit = []
        # 将手牌按花色分类
        for tile_id in player_tiles.hand_tiles:
            if tile_id < 40:
                rank = tile_id % 10   # 点数
                if rank in [1, 4, 7]:
                    first_suit.append(tile_id)
                elif rank in [2, 5, 8]:
                    second_suit.append(tile_id)
                elif rank in [3, 6, 9]:
                    third_suit.append(tile_id)
            else:
                null_suit.append(tile_id)

        # 如果花色手牌总数不等于9,则不符合全不靠
        if len(first_suit) + len(second_suit) + len(third_suit) != 9:
            return False
            
        # 检查每个花色内部是否有重复的牌,有重复则不符合全不靠
        for suit_list in [first_suit, second_suit, third_suit]:
            suits_count_list = []
            for tile_id in suit_list:
                suit = tile_id // 10
                suits_count_list.append(suit)
            if suit_list and suits_count_list.count(suits_count_list[0]) != len(suit_list):
                return False

        # 检查null_suit是否有重复字牌，有重复则不符合全不靠
        zipai_seen = set()
        for tile_id in null_suit:
            if tile_id in self.zipai:
                if tile_id in zipai_seen:
                    return False
                zipai_seen.add(tile_id)

        temp_player_tiles = player_tiles.__deepcopy__(None)
        temp_player_tiles.complete_step = 14
        temp_player_tiles.fan_list.append("quanbukao") # 全不靠
        player_tiles_list.append(temp_player_tiles)

    def normal_check(self, player_tiles: PlayerTiles):
        # 为节约性能 如果卡牌有不相邻的七组卡牌 说明无法和牌 直接返回False
        if not self.normal_check_block(player_tiles):
            return False
        # 获取所有的雀头可能以及没有雀头的情况
        all_list = self.normal_check_traverse_quetou(player_tiles)
        end_list = []
        print("所有雀头可能",[i.hand_tiles for i in all_list])
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
        
        print("计算次数：",count_count)
        combination_class = None
        temp_list = []
        for i in end_list:
            i.combination_list.sort()
            if i.combination_list != combination_class:
                combination_class = i.combination_list
                temp_list.append(i)
        end_list = temp_list

        print("和牌类型的数量:", len(end_list))
        for i in end_list:
            print("手牌",i.hand_tiles, "胡牌步数",i.complete_step, "胡牌组合",i.combination_list)
        
        return end_list

    def normal_check_block(self,player_tiles: PlayerTiles):
        block_count = len(player_tiles.combination_list)
        tile_id_pointer = player_tiles.hand_tiles[0]
        for tile_id in player_tiles.hand_tiles:
            if tile_id == tile_id_pointer or tile_id == tile_id_pointer + 1:
                continue
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
                temp_list.combination_list.append(f"K{tile_id}")
                all_list.append(temp_list)
                same_tile_id = tile_id

    def normal_check_traverse_dazi(self, player_tiles: PlayerTiles, all_list):
        same_tile_id = 0
        for tile_id in player_tiles.hand_tiles:
            if tile_id+1 in player_tiles.hand_tiles and tile_id+2 in player_tiles.hand_tiles and tile_id != same_tile_id:
                temp_list = player_tiles.__deepcopy__(None)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id+1)
                temp_list.hand_tiles.remove(tile_id+2)
                temp_list.complete_step += 3
                temp_list.combination_list.append(f"S{tile_id+1}")
                all_list.append(temp_list)
                same_tile_id = tile_id
    
    def fan_count(self, player_tiles: PlayerTiles,get_tile,way_to_hepai):
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
        jiulianbaodeng_list = [1,1,1,2,3,4,5,6,7,8,9,9,9]
        yiseshuanglonghui_list = [1,1,2,2,3,3,5,5,7.7,8,8,9,9]
        quandaiwu_set = {"s14","s15","s16","s24","s25","s26","s34","s35","s36","k15","K15","g15","G15","k25","K25","g25","G25","k35","K35","g35","G35"
                         "S14","S15","S16","S24","S25","S26","S34","S35","S36"} # 全带五
        fengke_set = {"k41","k42","k43","k44","K41","K42","K43","K44","g41","G41","g42","G42","g43","G43","g44","G44"} # 风刻
        jianke_set = {"k45","k46","k47","K45","K46","K47","g45","G45","g46","G46","g47","G47"} # 箭刻
        fengke_quetou_set = {"q41","q42","q43","q44"} # 风刻雀头
        jianke_quetou_set = {"q45","q46","q47"} # 箭刻雀头
        quandaiyao_set = {"s12","s18","s22","s28","s32","s38","k11","K11","g11","G11","k19","K19","g19","G19","k21","K21","g21","G21","k29","K29","g29","G29",
                          "k31","K31","g31","G31","k39","K39","g39","G39","k41","K41","g41","G41","k42","K42","g42","G42","k43","K43","g43","G43","k44","K44","g44","G44",
                          "k45","K45","g45","G45","k46","K46","g46","G46","k47","K47","g47","G47"} # 全带幺
        yaojiuke_set = {"k11","K11","k19","K19","k21","K21","k29","K29","k31","K31","k39","K39",
                        "k41","K41","k42","K42","k43","K43","k44","K44","K45","K45","K46","K46","K47","K47"} # 幺九刻
        special_set = {"shisanyao","qiduizi","quanbukao"}
        
        # 如果番数内有值说明是特殊型 特殊型传参时包含手牌列表 不需要获取手牌映射
        if player_tiles.fan_list:
            print(player_tiles.fan_list)
        else:
            # 获取手牌映射
            hand_tiles_list = []
            for i in player_tiles.combination_list:
                hand_tiles_list.extend(self.combination_to_tiles_dict[i])
            hand_tiles_list = sorted(hand_tiles_list)
        print("手牌映射：",hand_tiles_list)

        # 对手牌映射查表 
        # 负责判断 断幺  字一色 混一色 绿一色 清一色 大三元 混幺九 全大 全中 全小 大于五 小于五 缺一门 无字 四归一 九莲宝灯 一色双龙会

        if all(i in duanyao_set for i in hand_tiles_list):
            player_tiles.fan_list.append("duanyao") # 断幺
            if all(i in quanzhong_set for i in hand_tiles_list):
                player_tiles.fan_list.append("quanzhong") # 全中
        else:
            if all(i in zipai_set for i in hand_tiles_list):
                player_tiles.fan_list.append("ziyise") # 字一色

        if all(i in wan_set|zipai_set for i in hand_tiles_list) or all(i in bing_set|zipai_set for i in hand_tiles_list) or all(i in tiao_set|zipai_set for i in hand_tiles_list):
            player_tiles.fan_list.append("hunyise") # 混一色
            if all(i in lvyise_set for i in hand_tiles_list):
                player_tiles.fan_list.append("lvyise") # 绿一色
            if all(i in wan_set for i in hand_tiles_list) or all(i in bing_set for i in hand_tiles_list) or all(i in tiao_set for i in hand_tiles_list):
                player_tiles.fan_list.append("qingyise") # 清一色
                temp_tiles_list = hand_tiles_list
                temp_tiles_list.remove(get_tile)
                save_list = []
                for i in temp_tiles_list:
                    rank = i % 10
                    save_list.append(rank)
                if save_list == jiulianbaodeng_list:
                    player_tiles.fan_list.append("jiulianbaodeng") # 九莲宝灯
                if save_list == yiseshuanglonghui_list:
                    player_tiles.fan_list.append("yiseshuanglonghui") # 一色双龙会

        if all(i in hunyaojiu_set for i in hand_tiles_list):
            player_tiles.fan_list.append("hunyaojiu") # 混幺九
            if all(i in qingyaojiu_set for i in hand_tiles_list):
                player_tiles.fan_list.append("qingyaojiu") # 清幺九

        if all(i in quanda_set for i in hand_tiles_list):
            player_tiles.fan_list.append("quanda") # 全大
            if all(i in dayuwu_set for i in hand_tiles_list):
                player_tiles.fan_list.append("dayuwu") # 大于五

        if all(i in quanxiao_set for i in hand_tiles_list):
            player_tiles.fan_list.append("quanxiao") # 全小
            if all(i in xiaoyuwu_set for i in hand_tiles_list):
                player_tiles.fan_list.append("xiaoyuwu") # 小于五

        if all(i not in wan_set for i in hand_tiles_list) and all(i not in bing_set for i in hand_tiles_list) and all(i not in tiao_set for i in hand_tiles_list and all(i not in zipai_set for i in hand_tiles_list)):
            player_tiles.fan_list.append("queyimen") # 缺一门
        
        if all (i not in zipai_set for i in hand_tiles_list):
            player_tiles.fan_list.append("wuzi") # 无字

        if all (i in tuibudao_set for i in hand_tiles_list):
            player_tiles.fan_list.append("tuibudao") # 推不倒

        for i in hand_tiles_list:
            if hand_tiles_list.count(i) == 4:
                if not {f"g{i}",f"G{i}"} in player_tiles.combination_list:
                    player_tiles.fan_list.append("siguiyi") # 四归一

        if any(i in zhongbaifa_set for i in hand_tiles_list):
            if any(i in feng_set for i in hand_tiles_list):
                if any(i in wan_set for i in hand_tiles_list):
                    if any(i in bing_set for i in hand_tiles_list):
                        if any(i in tiao_set for i in hand_tiles_list):
                            player_tiles.fan_list.append("wumenqi") # 五门齐
        
            

        # 没完成判断的役种 七星不靠 三色双龙会 组合龙 全求人 不求人 圈风刻 门风刻 门前清 平和 边张 嵌张 单吊将 自摸 花牌


        # 对组合单元本身查表
        # 负责判断全带五 全带幺 箭刻 双箭刻 大四喜 小四喜
        if all(i in quandaiwu_set for i in player_tiles.combination_list):
            player_tiles.fan_list.append("quandaiwu") # 全带五

        if all(i in quandaiyao_set for i in player_tiles.combination_list):
            player_tiles.fan_list.append("quandaiyao") # 全带幺

        jianke_count = 0
        jianke_quetou = False
        for i in player_tiles.combination_list:
            if i in jianke_set:
                jianke_count += 1
            if i in jianke_quetou_set:
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
            if i in fengke_set:
                fengke_count += 1
            if i in fengke_quetou_set:
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
            if i in yaojiuke_set:
                yaojiuke_count += 1
                player_tiles.fan_list.append("yaojiuke") # 幺九刻
        

        # 获取组合映射
        combination_str = ""
        for i in player_tiles.combination_list:
            combination_str += i
        print("组合映射：",combination_str)

        # 对组合映射查表
        angang_count = combination_str.count("G")
        minggang_count = combination_str.count("g")
        anke_count = combination_str.count("K")
        kezi_count = combination_str.count("k")

        if angang_count + minggang_count >= 1:
            pass


        
        if combination_str.count("G") + combination_str.count("g") == 4:
            player_tiles.fan_list.append("sigang") # 四杠
        elif combination_str.count("G") + combination_str.count("g") == 3:
            player_tiles.fan_list.append("sanzhang") # 三杠
        
        if combination_str.count("G") + combination_str.count("K") == 4:
            player_tiles.fan_list.append("sianke") # 四暗刻

        if combination_str.count("G") + combination_str.count("K") == 3:
            player_tiles.fan_list.append("sanzhang") # 三暗刻

        if combination_str.count("G") + combination_str.count("g") + combination_str.count("K") + combination_str.count("k") == 4:
            player_tiles.fan_list.append("pengpenghe") # 碰碰和

        if combination_str.count("G") == 2:
            player_tiles.fan_list.append("shuangangang") # 双暗杠

        if combination_str.count("G") == 1:
            player_tiles.fan_list.append("angang") # 暗杠

        if combination_str.count("K") == 2:
            player_tiles.fan_list.append("shuanganke") # 双暗刻

        if combination_str.count("g") == 2:
            player_tiles.fan_list.append("shuangminggang") # 双明杠
        
        if combination_str.count("g") == 1:
            player_tiles.fan_list.append("minggang") # 明杠

        if combination_str.count("g") == 1 and combination_str.count("G") == 1:
            player_tiles.fan_list.append("mingangang") # 明暗杠


            
            

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

        print("搭子：",save_dazi_sign)
        print("刻子：",save_kezi_sign)

        if any(save_dazi_sign.count(i) == 4 for i in save_dazi_sign):
            player_tiles.fan_list.append("yisesitongshun") # 一色四同顺

        
        if len(save_dazi_sign) >= 3:
            # 检测一色四步高 以1为步长
            sign_pointer = int(save_dazi_sign[0])
            sign_count = 1
            for sign in save_dazi_sign:
                if int(sign) == sign_pointer + 1:
                    sign_count += 1
                    sign_pointer = int(sign)
            if sign_count == 4:
                player_tiles.fan_list.append("yisesibugao") # 一色四步高
            # 检测一色四步高 以2为步长
            sign_pointer = int(save_dazi_sign[0])
            sign_count = 1
            for sign in save_dazi_sign:
                if int(sign) == sign_pointer + 2:
                    sign_count += 1
                    sign_pointer = int(sign)
            if sign_count == 4:
                player_tiles.fan_list.append("yisesibugao") # 一色四步高
            elif sign_count == 3:
                player_tiles.fan_list.append("yisesisantongshun") # 一色三步高

            already_count = 0
            for i in save_dazi_sign:
                if save_dazi_sign.count(i) != already_count:
                    if save_dazi_sign.count(i) == 2:
                        player_tiles.fan_list.append("xixiangfeng") # 喜相逢
                    elif save_dazi_sign.count(i) == 3:
                        player_tiles.fan_list.append("yisesantongshun") # 一色三同顺
                    elif save_dazi_sign.count(i) == 4:
                        player_tiles.fan_list.append("yisesitongshun") # 一色四同顺
                    already_count = save_dazi_sign.count(i)

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

            for rank_list in [wan_list,bing_list,tiao_list]:
                if len(rank_list) == 3:
                    if rank_list[0] == "2":
                        if rank_list[1] == "5":
                            if rank_list[2] == "8":
                                player_tiles.fan_list.append("qinglong") # 清龙

            hualong_set = {"2": None, "5": None, "8": None}  # 初始化字典
            for rank_list in [wan_list,bing_list,tiao_list]:
                if "2" in rank_list:
                    hualong_set["2"] = rank_list
                if "5" in rank_list:
                    hualong_set["5"] = rank_list
                if "8" in rank_list:
                    hualong_set["8"] = rank_list
            if all(hualong_set[key] is not None for key in ["2", "5", "8"]):
                # 确保三个花色互不相同
                suits = [hualong_set["2"], hualong_set["5"], hualong_set["8"]]
                if len(set(suits)) == 3:
                    player_tiles.fan_list.append("hualong")  # 花龙
            
            for i in wan_list:
                if i in bing_list:
                    if i in tiao_list:
                        player_tiles.fan_list.append("sansesantongshun") # 三色三同顺
            
            for i in wan_list:
                if int(i)-1 in bing_list:
                    if int(i)-2 in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三步高
                if int(i)-2 in bing_list:
                    if int(i)-4 in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三步高
                if int(i)+1 in bing_list:
                    if int(i)+2 in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三步高
                if int(i)+2 in bing_list:
                    if int(i)+4 in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三步高

            for i in [wan_list,bing_list,tiao_list]:
                if len(i) >= 2:
                    if int(i[0])+3 == int(i[1]):
                        player_tiles.fan_list.append("lianliu") # 连六
                    if int(i[0]) == 2:
                        if "8" in i:
                            player_tiles.fan_list.append("laoshoufu") # 老少副
                    
                    
        
        if len(save_kezi_sign) >= 3:
            sign_pointer = int(save_kezi_sign[0])
            sign_count = 1
            for sign in save_kezi_sign:
                if int(sign) == sign_pointer + 1:
                    sign_count += 1
                    sign_pointer = int(sign)
            
            if sign_count == 3:
                player_tiles.fan_list.append("yisesanjiegao") # 一色三节高
            elif sign_count == 4:
                player_tiles.fan_list.append("yisesijiegao") # 一色四节高
            
            if all(sign[1] in [2,4,6,8] for sign in save_kezi_sign):
                if save_quetou_sign[0][1] in [2,4,6,8]:
                    player_tiles.fan_list.append("quanshuangke") # 全双刻
            
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

            already_count = 0
            for rank in all_list:
                if all_list.count(rank) == 2 and all_list.count(rank) != already_count:
                    already_count = rank
                    if all_list.count(rank) == 3:
                        player_tiles.fan_list.append("santongke") # 三同刻
                    else:
                        player_tiles.fan_list.append("shuangtongke") # 双同刻

            
            for i in wan_list:
                if int(i)+1 in bing_list:
                    if int(i)+2 in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                if int(i)-1 in bing_list:
                    if int(i)-2 in tiao_list:
                        player_tiles.fan_list.append("sansesanjiegao") # 三色三节高
                        
        





        if not player_tiles.fan_list:
            player_tiles.fan_list.append("wufanhe") # 无番和

        # 妙手回春 岭上开花 抢杠和 和绝张
        if way_to_hepai == "妙手回春":
            player_tiles.fan_list.append("miaoshouhuichun") # 妙手回春
        elif way_to_hepai == "岭上开花":
            player_tiles.fan_list.append("lingshangkaifang") # 岭上开花
        elif way_to_hepai == "抢杠和":
            player_tiles.fan_list.append("qiangganghe") # 抢杠和
        elif way_to_hepai == "和绝张":
            player_tiles.fan_list.append("hejuezhang") # 和绝张
        
        
        


# 测试
if __name__ == "__main__":

    tiles_dict = {
        # 搭子
        "s12": [11,12,13],"s13": [12,13,14],"s14": [13,14,15],"s15": [14,15,16],"s16": [15,16,17],"s17": [16,17,18],"s18": [17,18,19],
        "s22": [21,22,23],"s23": [22,23,24],"s24": [23,24,25],"s25": [24,25,26],"s26": [25,26,27],"s27": [26,27,28],"s28": [27,28,29],
        "s32": [31,32,33],"s33": [32,33,34],"s34": [33,34,35],"s35": [34,35,36],"s36": [35,36,37],"s37": [36,37,38],"s38": [37,38,39],
        # 刻子
        "k11": [11,11,11],"k12": [12,12,12],"k13": [13,13,13],"k14": [14,14,14],"k15": [15,15,15],"k16": [16,16,16],"k17": [17,17,17],"k18": [18,18,18],"k19": [19,19,19],
        "k21": [21,21,21],"k22": [22,22,22],"k23": [23,23,23],"k24": [24,24,24],"k25": [25,25,25],"k26": [26,26,26],"k27": [27,27,27],"k28": [28,28,28],"k29": [29,29,29],
        "k31": [31,31,31],"k32": [32,32,32],"k33": [33,33,33],"k34": [34,34,34],"k35": [35,35,35],"k36": [36,36,36],"k37": [37,37,37],"k38": [38,38,38],"k39": [39,39,39],
        "k41": [41,41,41],"k42": [42,42,42],"k43": [43,43,43],"k44": [44,44,44],
        "k45": [45,45,45],"k46": [46,46,46],"k47": [47,47,47],
    }

    combination_dict = {
            "s12": [11,12,13],"s13": [12,13,14],"s14": [13,14,15],"s15": [14,15,16],"s16": [15,16,17],"s17": [16,17,18],"s18": [17,18,19],
            "s22": [21,22,23],"s23": [22,23,24],"s24": [23,24,25],"s25": [24,25,26],"s26": [25,26,27],"s27": [26,27,28],"s28": [27,28,29],
            "s32": [31,32,33],"s33": [32,33,34],"s34": [33,34,35],"s35": [34,35,36],"s36": [35,36,37],"s37": [36,37,38],"s38": [37,38,39], # 顺
            "k11": [11,11,11],"k12": [12,12,12],"k13": [13,13,13],"k14": [14,14,14],"k15": [15,15,15],"k16": [16,16,16],"k17": [17,17,17],"k18": [18,18,18],"k19": [19,19,19],
            "k21": [21,21,21],"k22": [22,22,22],"k23": [23,23,23],"k24": [24,24,24],"k25": [25,25,25],"k26": [26,26,26],"k27": [27,27,27],"k28": [28,28,28],"k29": [29,29,29],
            "k31": [31,31,31],"k32": [32,32,32],"k33": [33,33,33],"k34": [34,34,34],"k35": [35,35,35],"k36": [36,36,36],"k37": [37,37,37],"k38": [38,38,38],"k39": [39,39,39],
            "k41": [41,41,41],"k42": [42,42,42],"k43": [43,43,43],"k44": [44,44,44],"k45": [45,45,45],"k46": [46,46,46],"k47": [47,47,47], # 刻
            "g11": [11,11,11,11],"g12": [12,12,12,12],"g13": [13,13,13,13],"g14": [14,14,14,14],"g15": [15,15,15,15],"g16": [16,16,16,16],"g17": [17,17,17,17],"g18": [18,18,18,18],"g19": [19,19,19,19],
            "g21": [21,21,21,21],"g22": [22,22,22,22],"g23": [23,23,23,23],"g24": [24,24,24,24],"g25": [25,25,25,25],"g26": [26,26,26,26],"g27": [27,27,27,27],"g28": [28,28,28,28],"g29": [29,29,29,29],
            "g31": [31,31,31,31],"g32": [32,32,32,32],"g33": [33,33,33,33],"g34": [34,34,34,34],"g35": [35,35,35,35],"g36": [36,36,36,36],"g37": [37,37,37,37],"g38": [38,38,38,38],"g39": [39,39,39,39],
            "g41": [41,41,41,41],"g42": [42,42,42,42],"g43": [43,43,43,43],"g44": [44,44,44,44],
            "g45": [45,45,45,45],"g46": [46,46,46,46],"g47": [47,47,47,47], # 杠
            "G11": [11,11,11,11],"G12": [12,12,12,12],"G13": [13,13,13,13],"G14": [14,14,14,14],"G15": [15,15,15,15],"G16": [16,16,16,16],"G17": [17,17,17,17],"G18": [18,18,18,18],"G19": [19,19,19,19],
            "G21": [21,21,21,21],"G22": [22,22,22,22],"G23": [23,23,23,23],"G24": [24,24,24,24],"G25": [25,25,25,25],"G26": [26,26,26,26],"G27": [27,27,27,27],"G28": [28,28,28,28],"G29": [29,29,29,29],
            "G31": [31,31,31,31],"G32": [32,32,32,32],"G33": [33,33,33,33],"G34": [34,34,34,34],"G35": [35,35,35,35],"G36": [36,36,36,36],"G37": [37,37,37,37],"G38": [38,38,38,38],"G39": [39,39,39,39],
            "G41": [41,41,41,41],"G42": [42,42,42,42],"G43": [43,43,43,43],"G44": [44,44,44,44],
            "G45": [45,45,45,45],"G46": [46,46,46,46],"G47": [47,47,47,47], # 暗杠
            "K11": [11,11,11,11],"K12": [12,12,12,12],"K13": [13,13,13,13],"K14": [14,14,14,14],"K15": [15,15,15,15],"K16": [16,16,16,16],"K17": [17,17,17,17],"K18": [18,18,18,18],"K19": [19,19,19,19],
            "K21": [21,21,21,21],"K22": [22,22,22,22],"K23": [23,23,23,23],"K24": [24,24,24,24],"K25": [25,25,25,25],"K26": [26,26,26,26],"K27": [27,27,27,27],"K28": [28,28,28,28],"K29": [29,29,29,29],
            "K31": [31,31,31,31],"K32": [32,32,32,32],"K33": [33,33,33,33],"K34": [34,34,34,34],"K35": [35,35,35,35],"K36": [36,36,36,36],"K37": [37,37,37,37],"K38": [38,38,38,38],"K39": [39,39,39,39],
            "K41": [41,41,41,41],"K42": [42,42,42,42],"K43": [43,43,43,43],"K44": [44,44,44,44],
            "K45": [45,45,45,45],"K46": [46,46,46,46],"K47": [47,47,47,47], # 暗刻
            
    }

    quetou_dict = {
        "q11": [11,11],"q12": [12,12],"q13": [13,13],"q14": [14,14],"q15": [15,15],"q16": [16,16],"q17": [17,17],"q18": [18,18],"q19": [19,19],
        "q21": [21,21],"q22": [22,22],"q23": [23,23],"q24": [24,24],"q25": [25,25],"q26": [26,26],"q27": [27,27],"q28": [28,28],"q29": [29,29], 
        "q31": [31,31],"q32": [32,32],"q33": [33,33],"q34": [34,34],"q35": [35,35],"q36": [36,36],"q37": [37,37],"q38": [38,38],"q39": [39,39],
        "q41": [41,41],"q42": [42,42],"q43": [43,43],"q44": [44,44],"q45": [45,45],"q46": [46,46],"q47": [47,47], 
    }
    
    # 随机抽取1个quetou和4个tiles_dict的值组合 并且有概率生成已经组合的牌型
    tiles_list = []
    combination_list = []
    while True:
        temp_tiles_list = []
        tiles_list.extend(random.choice(list(quetou_dict.values())))
        for i in range(4):
            if random.randint(0,100) < 50:
                tiles = random.choice(list(tiles_dict.values()))
                tiles_list.extend(tiles)
                temp_tiles_list.extend(tiles)
            else:
                combination = random.choice(list(combination_dict.keys()))
                combination_list.append(combination)
                temp_tiles_list.extend(combination_dict[combination])
        if all(temp_tiles_list.count(tile_id) <= 4 for tile_id in temp_tiles_list):
            break
        else:
            tiles_list = []
            combination_list = []
        
    print("随机生成手牌：",tiles_list,"随机生成组合：",combination_list)

    # 手动指定
    # tiles_list = [11,11,11,12,12,12,13,13,13,14,14,14,15,15]
    # combination_list = []
    # print("手动指定手牌：",tiles_list,"手动指定组合：",combination_list)

    # 排序,然后随机删除一张用作和牌张
    tiles_list.sort()
    hepai_tiles = tiles_list.pop(random.randint(0, len(tiles_list) - 1))
    
    # 选择一种和牌方式
    way_to_hepai = random.choice(["荣和","自摸","抢杠和","妙手回春","海底捞月","岭上开花","和绝张"])

    # 开始测试
    test_check = HePaiCheck()
    time_start = time()
    test_check.hepai_check(tiles_list,combination_list,way_to_hepai,hepai_tiles)
    time_end = time()
