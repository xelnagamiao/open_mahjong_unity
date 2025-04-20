from typing import Dict,List
import random
from time import time

class PlayerTiles:
    def __init__(self, tiles_list, combination_list,complete_step):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step # +3 +3 +3 +3 +2 = 14
        self.fan_dict = set()
    
    def __deepcopy__(self, memo):
        return PlayerTiles(self.hand_tiles[:],
                         self.combination_list[:], 
                         self.complete_step)

class HePaiCheck:
    def __init__(self):
        # 存储排斥的番种
        self.repel_model_dict:Dict[int,list] ={
            "dasixi":["pengpenghe","quanfengke","mengfengke","yaojiuke"],"dasanyuan":["yaojiuke"], # 大四喜 大三元
            "lvyise":["hunyise"],"sigang":["pengpenghe","dandiaojiang"], # 绿一色 四杠子
            "jiulianbaodeng_dianhe":["qingyise","wuzi"],"jiulianbaodeng_zimo":["qingyise","wuzi","buqiuren"], # 九莲宝灯 点和/自摸
            "lianqidui_dianhe":["qidui","qingyise","wuzi","mengqianqing"],"lianqidui_zimo":["qidui","qingyise","wuzi","buqiuren"], # 连七对 点和/自摸
            "shisanyao_dianhe":["hunyaojiu","wumengqi","mengqianqing"],"shisanyao_zimo":["hunyaojiu","wumengqi","buqiuren"], # 十三幺 点和/自摸
            "qingyaojiu":["pengpenghe","quandaiyao","shuangtongke","yaojiuke","wuzi"],"xiaosixi":["sanfengke","yaojiuke"], # 清幺九 小四喜
            "xiaosanyuan":["shuangjianke","yaojiuke"],"ziyise":["pengpenghe","quandaiyao","yaojiuke"], # 小三元 字一色
            "sianke_dianhe":["pengpenghe","mengqianqing"],"sianke_zimo":["pengpenghe","buqiuren"], # 四暗刻 点和/自摸
            "yiseshuanglonghui":["qingyise","pinghe","wuzi"], "yisesitongshun":["siguiyi"], # 一色双龙会 一色四同顺
            "yisesijiegao":["pengpenghe"],"yisesibugao":[],"sangang":[], # 一色四节高 一色四步高 三杠
            "hunyaojiu":["pengpenghe","quandaiyao","yaojiuke"], # 混幺九
            "qidui_dianhe":["mengqianqing"],"qidui_zimo":["buqiuren"], # 七对 点和/自摸
            "qixingbukao_dianhe":["quanbukao","wumengqi","mengqianqing"],"qixingbukao_zimo":["quanbukao","wumengqi","buqiuren"], # 七星不靠 全不靠
            "quanshuangke":["pengpenghe","duanyao","wuzi"], # 全双刻
            "qingyise":["wuzi"],"yisesantongshun":[],"yisesanjiegao":[], # 一色四顺 一色四节高
            "quanda":["dayuwu","wuzi"],"quanzhong":["duanyao","wuzi"],"quanxiao":["xiaoyuwu","wuzi"], # 全大 全中 全小
            "qinglong":[],"sanseshuanglonghui":["pinghe","wuzi"],"yisesanbugao":[], # 清龙 三色双龙会 一色三步高
            "quandaiwu":["duanyao","wuzi"],"santongke":[],"sananke":[], # 全带五 三同刻 三暗刻
            "quanbukao_dianhe":["mengqianqing"],"quanbukao_zimo":["buqiuren"], # 全不靠 点和/自摸
            "zuhelong":[],"dayuwu":["wuzi"],"xiaoyuwu":["wuzi"],"sanfengke":[], # 组合龙 大于五 小于五 三风刻
            "hualong":[],"tuibudao":["queyimeng"],"sansesantongshun":[],"sansesanjiegao":[], # 花龙 推不倒 三色三同顺 三色三节高
            "wufanhe":[],"miaoshouhuicun":[],"haidilaoyue":[],"gangshangkaihua":["zimo"], # 无番和 妙手回春 海底捞月 杠上开花
            "qiangganghe":["hejuezhang"],"pengpenghe":[],"hunyise":[],"sansesanbugao":[], # 抢杠和 碰碰和 混一色 一色三步高
            "wumengqi":[],"quanqiuren":["dandiaojiang"],"shuangangang":["shuanganke"], # 五门齐 全求人 双暗杠
            "shuangjianke":["yaojiuke"],"quandaiyao":[],"buqiuren":["zimo"], # 双箭刻 全带幺 不求人
            "shuangminggang":[],"hejuezhang":[],"jianke":[],"quanfengke":[], # 双明杠 和绝张 箭刻 全风刻
            "mengfengke":[],"mengqianqing":[],"pinghe":["wuzi"],"siguiyi":[], # 门风刻 门前清 平和 四归一
            "shuangtongke":[],"shuanganke":[],"angang":[],"duanyao":["wuzi"], # 双同刻 双暗刻 暗杠 断幺
            "yibangao":[],"xixiangfeng":[],"lianliu":[],"laoshaofu":[],"yaojiuke":[], # 一般高 喜相逢 连六 老少副 幺九刻
            "minggang":[],"queyimeng":[],"wuzi":[],"bianzhang":[],"qianzhang":[], # 明杠 缺一门 无字 边张 嵌张
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
            "gangshangkaihua":8,"qiangganghe":8,"pengpenghe":6,"hunyise":6,"sansesanbugao":6,"wumengqi":6,"quanqiuren":6,"shuangangang":6,"shuangjianke":6,
            "quandaiyao":4,"buqiuren":4,"shuangminggang":4,"hejuezhang":4,"jianke":2,"quanfengke":2,"mengfengke":2,"mengqianqing":2,
            "pinghe":2,"siguiyi":2,"shuangtongke":2,"shuanganke":2,"angang":2,"duanyao":2,"yibangao":1,"xixiangfeng":1,
            "lianliu":1,"laoshaofu":1,"yaojiuke":1,"minggang":1,"queyimeng":1,"wuzi":1,"bianzhang":1,
            "qianzhang":1,"dandiaojiang":1,"zimo":1,"huapai":1,"mingangang":5,
            }
        # 存储组合 => 手牌的映射
        self.combination_to_tiles_dict:Dict[str,List[int]] = {
            "s12": [11,12,13],"s13": [12,13,14],"s14": [13,14,15],"s15": [14,15,16],"s16": [15,16,17],"s17": [16,17,18],"s18": [17,18,19],
            "s22": [21,22,23],"s23": [22,23,24],"s24": [23,24,25],"s25": [24,25,26],"s26": [25,26,27],"s27": [26,27,28],"s28": [27,28,29],
            "s32": [31,32,33],"s33": [32,33,34],"s34": [33,34,35],"s35": [34,35,36],"s36": [35,36,37],"s37": [36,37,38],"s38": [37,38,39], # 顺
            "k11": [11,11,11],"k12": [12,12,12],"k13": [13,13,13],"k14": [14,14,14],"k15": [15,15,15],"k16": [16,16,16],"k17": [17,17,17],"k18": [18,18,18],"k19": [19,19,19],
            "k21": [21,21,21],"k22": [22,22,22],"k23": [23,23,23],"k24": [24,24,24],"k25": [25,25,25],"k26": [26,26,26],"k27": [27,27,27],"k28": [28,28,28],"k29": [29,29,29],
            "k31": [31,31,31],"k32": [32,32,32],"k33": [33,33,33],"k34": [34,34,34],"k35": [35,35,35],"k36": [36,36,36],"k37": [37,37,37],"k38": [38,38,38],"k39": [39,39,39],
            "k41": [41,41,41],"k42": [42,42,42],"k43": [43,43,43],"k44": [44,44,44],"k45": [45,45,45],"k46": [46,46,46],"k47": [47,47,47], # 刻
            "q11": [11,11],"q12": [12,12],"q13": [13,13],"q14": [14,14],"q15": [15,15],"q16": [16,16],"q17": [17,17],"q18": [18,18],"q19": [19,19],
            "q21": [21,21],"q22": [22,22],"q23": [23,23],"q24": [24,24],"q25": [25,25],"q26": [26,26],"q27": [27,27],"q28": [28,28],"q29": [29,29], 
            "q31": [31,31],"q32": [32,32],"q33": [33,33],"q34": [34,34],"q35": [35,35],"q36": [36,36],"q37": [37,37],"q38": [38,38],"q39": [39,39],
            "q41": [41,41],"q42": [42,42],"q43": [43,43],"q44": [44,44],"q45": [45,45],"q46": [46,46],"q47": [47,47], # 雀头
            "g11": [11,11,11,11],"g12": [12,12,12,12],"g13": [13,13,13,13],"g14": [14,14,14,14],"g15": [15,15,15,15],"g16": [16,16,16,16],"g17": [17,17,17,17],"g18": [18,18,18,18],"g19": [19,19,19,19],
            "g21": [21,21,21,21],"g22": [22,22,22,22],"g23": [23,23,23,23],"g24": [24,24,24,24],"g25": [25,25,25,25],"g26": [26,26,26,26],"g27": [27,27,27,27],"g28": [28,28,28,28],"g29": [29,29,29,29],
            "g31": [31,31,31,31],"g32": [32,32,32,32],"g33": [33,33,33,33],"g34": [34,34,34,34],"g35": [35,35,35,35],"g36": [36,36,36,36],"g37": [37,37,37,37],"g38": [38,38,38,38],"g39": [39,39,39,39],
            "g41": [41,41,41,41],"g42": [42,42,42,42],"g43": [43,43,43,43],"g44": [44,44,44,44],
            "g45": [45,45,45,45],"g46": [46,46,46,46],"g47": [47,47,47,47], # 杠
            "a11": [11,11,11,11],"a12": [12,12,12,12],"a13": [13,13,13,13],"a14": [14,14,14,14],"a15": [15,15,15,15],"a16": [16,16,16,16],"a17": [17,17,17,17],"a18": [18,18,18,18],"a19": [19,19,19,19],
            "a21": [21,21,21,21],"a22": [22,22,22,22],"a23": [23,23,23,23],"a24": [24,24,24,24],"a25": [25,25,25,25],"a26": [26,26,26,26],"a27": [27,27,27,27],"a28": [28,28,28,28],"a29": [29,29,29,29],
            "a31": [31,31,31,31],"a32": [32,32,32,32],"a33": [33,33,33,33],"a34": [34,34,34,34],"a35": [35,35,35,35],"a36": [36,36,36,36],"a37": [37,37,37,37],"a38": [38,38,38,38],"a39": [39,39,39,39],
            "a41": [41,41,41,41],"a42": [42,42,42,42],"a43": [43,43,43,43],"a44": [44,44,44,44],
            "a45": [45,45,45,45],"a46": [46,46,46,46],"a47": [47,47,47,47], # 暗杠
        }




        #
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
            self.GS_check(player_tiles)  # 国士无双check
            if not player_tiles.fan_dict:
                self.QD_check(player_tiles)  # 七对子check
                if not player_tiles.fan_dict:
                    self.QBK_check(player_tiles)  # 全不靠check
                    if not player_tiles.fan_dict:
                        player_tiles_list = self.normal_check(player_tiles)
        else:
            player_tiles_list = self.normal_check(player_tiles)
        
        if player_tiles.fan_dict:
            player_tiles_list.append(player_tiles)

        if player_tiles_list:
            for i in player_tiles_list:
                self.fan_count(i,get_tile,way_to_hepai)


        print("胡牌番种：",player_tiles.fan_dict)

        

    def GS_check(self,player_tiles:PlayerTiles):
        allow_same_id = True
        same_tile_id = 0
        hepai_step = 0
        for tile_id in player_tiles.hand_tiles:
            if tile_id in self.yaojiu and (tile_id != same_tile_id or allow_same_id):
                if tile_id == same_tile_id:
                    allow_same_id = False
                same_tile_id = tile_id
                hepai_step += 1
            if hepai_step == 13:
                player_tiles.fan_dict.add("国士无双")
    
    def QD_check(self, player_tiles:PlayerTiles):
        # 统计每种牌的数量
        tile_counts = {}
        for tile_id in player_tiles.hand_tiles:
            if tile_id in tile_counts:
                tile_counts[tile_id] += 1
            else:
                tile_counts[tile_id] = 1
        # 如果存在不是2张的牌，则不符合七对子
        for tile_id, count in tile_counts.items():
            if count != 2:
                return False
        player_tiles.fan_dict.add("七对子")

    def QBK_check(self, player_tiles:PlayerTiles):
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

        player_tiles.fan_dict.add("全不靠")

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
                temp_list.combination_list.append(f"k{tile_id}")
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
                temp_list.combination_list.append(f"s{tile_id+1}")
                all_list.append(temp_list)
                same_tile_id = tile_id
    
    def fan_count(self, player_tiles: PlayerTiles,get_tile,way_to_hepai):
        pass

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
        # 暗杠
        "a11": [11,11,11,11],"a12": [12,12,12,12],"a13": [13,13,13,13],"a14": [14,14,14,14],"a15": [15,15,15,15],"a16": [16,16,16,16],"a17": [17,17,17,17],"a18": [18,18,18,18],"a19": [19,19,19,19],
        "a21": [21,21,21,21],"a22": [22,22,22,22],"a23": [23,23,23,23],"a24": [24,24,24,24],"a25": [25,25,25,25],"a26": [26,26,26,26],"a27": [27,27,27,27],"a28": [28,28,28,28],"a29": [29,29,29,29],
        "a31": [31,31,31,31],"a32": [32,32,32,32],"a33": [33,33,33,33],"a34": [34,34,34,34],"a35": [35,35,35,35],"a36": [36,36,36,36],"a37": [37,37,37,37],"a38": [38,38,38,38],"a39": [39,39,39,39],
        "a41": [41,41,41,41],"a42": [42,42,42,42],"a43": [43,43,43,43],"a44": [44,44,44,44],
        "a45": [45,45,45,45],"a46": [46,46,46,46],"a47": [47,47,47,47],
        # 明杠
        "g11": [11,11,11,11],"g12": [12,12,12,12],"g13": [13,13,13,13],"g14": [14,14,14,14],"g15": [15,15,15,15],"g16": [16,16,16,16],"g17": [17,17,17,17],"g18": [18,18,18,18],"g19": [19,19,19,19],
        "g21": [21,21,21,21],"g22": [22,22,22,22],"g23": [23,23,23,23],"g24": [24,24,24,24],"g25": [25,25,25,25],"g26": [26,26,26,26],"g27": [27,27,27,27],"g28": [28,28,28,28],"g29": [29,29,29,29],
        "g31": [31,31,31,31],"g32": [32,32,32,32],"g33": [33,33,33,33],"g34": [34,34,34,34],"g35": [35,35,35,35],"g36": [36,36,36,36],"g37": [37,37,37,37],"g38": [38,38,38,38],"g39": [39,39,39,39],
        "g41": [41,41,41,41],"g42": [42,42,42,42],"g43": [43,43,43,43],"g44": [44,44,44,44],
        "g45": [45,45,45,45],"g46": [46,46,46,46],"g47": [47,47,47,47]
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
    print("手动指定手牌：",tiles_list,"手动指定组合：",combination_list)

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
