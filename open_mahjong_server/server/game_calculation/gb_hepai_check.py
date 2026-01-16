from typing import Dict,List
from time import time
import logging

logger = logging.getLogger(__name__)

class PlayerTiles:
    def __init__(self, tiles_list, combination_list,complete_step):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step # +3 +3 +3 +3 +2 = 14
        self.fan_list = []
        self.point_count_dict = {} # 存储和牌得分
        self.fan_count_list = [] # 存储和牌文本

    def __deepcopy__(self, memo):
        new_instance = PlayerTiles(self.hand_tiles[:],
                                 self.combination_list[:],
                                 self.complete_step)
        new_instance.fan_list = self.fan_list[:]
        return new_instance

class Chinese_Hepai_Check:
    # hand_check 手牌检查所用的集合
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
    # combination_check 组合检查所用的集合
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
    # 存储排斥的番种
    repel_model_dict:Dict[int,list] ={
        "dasixi":["pengpenghe","quanfengke","menfengke"]+["yaojiuke"]*4,"dasanyuan":["yaojiuke"]*3, # 大四喜 大三元
        "lvyise":["hunyise"],"sigang":["pengpenghe","dandiaojiang"], # 绿一色 四杠子
        "jiulianbaodeng_dianhe":["qingyise","wuzi","yaojiuke","menqianqing"],"jiulianbaodeng_zimo":["qingyise","wuzi","buqiuren","yaojiuke"], # 九莲宝灯 点和/自摸
        "lianqidui_dianhe":["qidui","qingyise","wuzi","menqianqing"],"lianqidui_zimo":["qidui","qingyise","wuzi","buqiuren"], # 连七对 点和/自摸
        "shisanyao_dianhe":["hunyaojiu","wumenqi","menqianqing"],"shisanyao_zimo":["hunyaojiu","wumenqi","buqiuren"], # 十三幺 点和/自摸
        "qingyaojiu":["pengpenghe","quandaiyao","shuangtongke","shuangtongke","wuzi"] + ["yaojiuke"]*4,"xiaosixi":["sanfengke"] + ["yaojiuke"]*3, # 清幺九 小四喜
        "xiaosanyuan":["shuangjianke"] + ["yaojiuke"]*2 ,"ziyise":["pengpenghe","quandaiyao"] + ["yaojiuke"]*4, # 小三元 字一色
        "sianke_dianhe":["pengpenghe","menqianqing"],"sianke_zimo":["pengpenghe","buqiuren"], # 四暗刻 点和/自摸
        "yiseshuanglonghui":["qingyise","pinghe","wuzi","yibangao","yibangao"], "yisesitongshun":["siguiyi"]*4, # 一色双龙会 一色四同顺
        "yisesijiegao":["pengpenghe"],"yisesibugao":[],"sangang":[], # 一色四节高 一色四步高 三杠
        "hunyaojiu":["pengpenghe","quandaiyao",]+["yaojiuke"]*4, # 混幺九
        "qiduizi_dianhe":["menqianqing"],"qiduizi_zimo":["buqiuren"], # 七对 点和/自摸
        "qixingbukao_dianhe":["quanbukao","wumenqi","menqianqing"],"qixingbukao_zimo":["quanbukao","wumenqi","buqiuren"], # 七星不靠 全不靠
        "quanshuangke":["pengpenghe","duanyao","wuzi"], # 全双刻
        "qingyise":["wuzi"],"yisesantongshun":[],"yisesanjiegao":[], # 一色三同顺 一色四节高
        "quanda":["dayuwu","wuzi"],"quanzhong":["duanyao","wuzi"],"quanxiao":["xiaoyuwu","wuzi"], # 全大 全中 全小
        "qinglong":[],"sanseshuanglonghui":["pinghe","wuzi"],"yisesanbugao":[], # 清龙 三色双龙会 一色三步高
        "quandaiwu":["duanyao","wuzi"],"santongke":[],"sananke":[], # 全带五 三同刻 三暗刻
        "quanbukao_dianhe":["menqianqing"],"quanbukao_zimo":["buqiuren"], # 全不靠 点和/自摸
        "zuhelong":[],"dayuwu":["wuzi"],"xiaoyuwu":["wuzi"],"sanfengke":[], # 组合龙 大于五 小于五 三风刻
        "hualong":[],"tuibudao":["queyimen"],"sansesantongshun":[],"sansesanjiegao":[], # 花龙 推不倒 三色三同顺 三色三节高
        "wufanhe":[],"miaoshouhuichun":[],"haidilaoyue":[],"gangshangkaihua":["zimo"], # 无番和 妙手回春 海底捞月 杠上开花
        "qiangganghe":["hejuezhang"],"pengpenghe":[],"hunyise":[],"sansesanbugao":[], # 抢杠和 碰碰和 混一色 一色三步高
        "wumenqi":[],"quanqiuren":["dandiaojiang"],"shuangangang":["shuanganke"], # 五门齐 全求人 双暗杠
        "shuangjianke":["yaojiuke"]*2,"quandaiyao":[],"buqiuren":["zimo"], # 双箭刻 全带幺 不求人
        "shuangminggang":[],"hejuezhang":[],"jianke":["yaojiuke"]*1,"quanfengke":[], # 双明杠 和绝张 箭刻 全风刻
        "menfengke":[],"menqianqing":[],"pinghe":["wuzi"],"siguiyi":[], # 门风刻 门前清 平和 四归一
        "shuangtongke":[],"shuanganke":[],"angang":[],"duanyao":["wuzi"], # 双同刻 双暗刻 暗杠 断幺
        "yibangao":[],"xixiangfeng":[],"lianliu":[],"laoshaofu":[],"yaojiuke":[], # 一般高 喜相逢 连六 老少副 幺九刻
        "minggang":[],"queyimen":[],"wuzi":[],"bianzhang":[],"qianzhang":[], # 明杠 缺一门 无字 边张 嵌张
        "dandiaojiang":[],"zimo":[],"huapai":[],"mingangang":[], # 单钓将 自摸 花牌 明暗杠
        }
    # 存储番种的番数
    count_model_dict:Dict[str,int] = {
        "dasixi":88,"dasanyuan":88,"lvyise":88,"jiulianbaodeng":88,"sigang":88,
        "lianqidui":88,"shisanyao":88,
        "qingyaojiu":64,"xiaosixi":64,"xiaosanyuan":64,"ziyise":64,"sianke":64,"yiseshuanglonghui":64,
        "yisesitongshun":48,"yisesijiegao":48,"yisesibugao":32,"sangang":32,"hunyaojiu":32,
        "qiduizi":24,"qixingbukao":24,"quanshuangke":24,
        "qingyise":24,"yisesantongshun":24,"yisesanjiegao":24,"quanda":24,"quanzhong":24,"quanxiao":24,
        "qinglong":16,"sanseshuanglonghui":16,"yisesanbugao":16,"quandaiwu":16,"santongke":16,"sananke":16,
        "quanbukao":12,"zuhelong":12,"dayuwu":12,"xiaoyuwu":12,"sanfengke":12,
        "hualong":8,"tuibudao":8,"sansesantongshun":8,"sansesanjiegao":8,"wufanhe":8,"miaoshouhuichun":8,"haidilaoyue":8,
        "gangshangkaihua":8,"qiangganghe":8,"pengpenghe":6,"hunyise":6,"sansesanbugao":6,"wumenqi":6,"quanqiuren":6,"shuangangang":6,"shuangjianke":6,
        "quandaiyao":4,"buqiuren":4,"shuangminggang":4,"hejuezhang":4,"jianke":2,"quanfengke":2,"menfengke":2,"menqianqing":2,
        "pinghe":2,"siguiyi":2,"shuangtongke":2,"shuanganke":2,"angang":2,"duanyao":2,"yibangao":1,"xixiangfeng":1,
        "lianliu":1,"laoshaofu":1,"yaojiuke":1,"minggang":1,"queyimen":1,"wuzi":1,"bianzhang":1,
        "qianzhang":1,"dandiaojiang":1,"zimo":1,"huapai":1,"mingangang":5,
        }
    eng_to_chinese_dict = {
        "dasixi":"大四喜",
        "dasanyuan":"大三元",
        "lvyise":"绿一色",
        "jiulianbaodeng":"九莲宝灯",
        "sigang":"四杠",
        "sangang":"三杠",
        "lianqidui":"连七对",
        "shisanyao":"十三幺",
        "qingyaojiu":"清幺九",
        "xiaosixi":"小四喜",
        "xiaosanyuan":"小三元",
        "ziyise":"字一色",
        "sianke":"四暗刻",
        "yiseshuanglonghui":"一色双龙会",
        "yisesitongshun":"一色四同顺",
        "yisesijiegao":"一色四节高",
        "yisesibugao":"一色四步高",
        "hunyaojiu":"混幺九",
        "qiduizi":"七对子",
        "qixingbukao":"七星不靠",
        "quanshuangke":"全双刻",
        "qingyise":"清一色",
        "yisesantongshun":"一色三同顺",
        "yisesanjiegao":"一色三节高",
        "quanda":"全大",
        "quanzhong":"全中",
        "quanxiao":"全小",
        "qinglong":"清龙",
        "sanseshuanglonghui":"三色双龙会",
        "yisesanbugao":"一色三步高",
        "quandaiwu":"全带五",
        "santongke":"三同刻",
        "sananke":"三暗刻",
        "quanbukao":"全不靠",
        "zuhelong":"组合龙",
        "dayuwu":"大于五",
        "xiaoyuwu":"小于五",
        "sanfengke":"三风刻",
        "hualong":"花龙",
        "tuibudao":"推不倒",
        "sansesantongshun":"三色三同顺",
        "sansesanjiegao":"三色三节高",
        "wufanhe":"无番和",
        "miaoshouhuichun":"妙手回春",
        "haidilaoyue":"海底捞月",
        "gangshangkaihua":"杠上开花",
        "qiangganghe":"抢杠和",
        "pengpenghe":"碰碰和",
        "hunyise":"混一色",
        "sansesanbugao":"三色三步高",
        "wumenqi":"五门齐",
        "quanqiuren":"全求人",
        "shuangangang":"双暗杠",
        "shuangjianke":"双箭刻",
        "quandaiyao":"全带幺",
        "buqiuren":"不求人",
        "shuangminggang":"双明杠",
        "hejuezhang":"和绝张",
        "jianke":"箭刻",
        "quanfengke":"圈风刻",
        "menfengke":"门风刻",
        "menqianqing":"门前清",
        "pinghe":"平和",
        "siguiyi":"四归一",
        "shuangtongke":"双同刻",
        "shuanganke":"双暗刻",
        "angang":"暗杠",
        "duanyao":"断幺",
        "yibangao":"一般高",
        "xixiangfeng":"喜相逢",
        "lianliu":"连六",
        "laoshaofu":"老少副",
        "yaojiuke":"幺九刻",
        "minggang":"明杠",
        "queyimen":"缺一门",
        "wuzi":"无字",
        "bianzhang":"边张",
        "qianzhang":"嵌张",
        "dandiaojiang":"单钓将",
        "zimo":"自摸",
        "huapai":"花牌",
        "mingangang":"明暗杠"
    }
    def __init__(self, debug=False):
        self.debug = debug  # 添加debug标志
    
    def debug_print(self, *args, **kwargs):
        """只在debug模式下打印"""
        if self.debug:
            logger.debug(*args, **kwargs)
            print(*args, **kwargs)

    # 存储组合 => 手牌的映射
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
    # GS_check QBK_check 十三幺和全不靠检查使用的集合
    yaojiu = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47}
    zipai = {41, 42, 43, 44, 45, 46, 47}

    def hepai_check(self,hand_list:list,tiles_combination,way_to_hepai,get_tile):
        tiles_combination = tiles_combination
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list,tiles_combination,complete_step)


        print("传参手牌：",player_tiles.hand_tiles,"传参组合：",player_tiles.combination_list,"传参和牌方式：",way_to_hepai,"传参和牌张：",get_tile)
        
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
                allow_list.append(self.fan_count(i,get_tile,way_to_hepai))

        fancount_time_end = time()
        logger.debug(f"番种计算耗时：{fancount_time_end - fancount_time_start}秒")
        
        # 对比返回元组的第一个元素，只返回第一个元素最大的元组
        allow_list = sorted(allow_list,key=lambda x:x[0],reverse=True)
        logger.debug(f"允许的番种：{allow_list}")
        return allow_list[0]
    # 允许的番种：[(115, ['四暗刻', '一色四节高', '缺一门', '无字', '幺九刻*1']), (115, ['四暗刻', '一色四节高', '缺一门', '无字', '幺九刻*1']), (29, ['一色三同顺', '门前清', '缺一门', '无字', '幺九刻*1']), (29, ['一色三同顺', '门前清', '缺一门', '无字', '幺九刻*1']), (28, ['一色三同顺', '门前清
    #', '缺一门', '无字'])] 这里有重复判断 以后进行优化

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
        double_pair = False
        for tile_id, count in tile_counts.items():
            if count == 2:
                pass
            elif count == 4:
                double_pair = True
            else:
                return False
            
        tile_pointer = temp_player_tiles.hand_tiles[0]
        for i in temp_player_tiles.hand_tiles:
            if (tile_pointer == i or tile_pointer + 1 == i) and i <= 40:
                tile_pointer = i
            else:
                break
        else: # 如果遍历完手牌没有break 并且没有双对 则连七对
            if double_pair == False:
                temp_player_tiles.fan_list.append("lianqidui") # 连七对
                temp_player_tiles.complete_step = 14
                player_tiles_list.append(temp_player_tiles)
                return False
            
        temp_player_tiles.complete_step = 14
        temp_player_tiles.fan_list.append("qiduizi") # 七对子
        player_tiles_list.append(temp_player_tiles)
        return False

    def QBK_check(self, player_tiles:PlayerTiles,player_tiles_list):
        hand_kind_set = len(set(player_tiles.hand_tiles))
        # 如果手牌种类为14种 则可能全不靠
        if hand_kind_set == 14:
            QBK_case_list =[{11,14,17,22,25,28,33,36,39,41,42,43,44,45,46,47}, {11,14,17,32,35,38,23,26,29,41,42,43,44,45,46,47}, {21,24,27,12,15,18,33,36,39,41,42,43,44,45,46,47}, 
                            {21,24,27,32,35,38,13,16,19,41,42,43,44,45,46,47}, {31,34,37,22,25,28,13,16,19,41,42,43,44,45,46,47}, {31,34,37,12,15,18,23,26,29,41,42,43,44,45,46,47}]
            for case in QBK_case_list:
                QBK_set = set()
                for i in player_tiles.hand_tiles:
                    if i in case:
                        QBK_set.add(i)
                if len(QBK_set) == 14:
                    temp_player_tiles = player_tiles.__deepcopy__(None)
                    temp_player_tiles.complete_step += 14
                    temp_player_tiles.combination_list.append(f"z{case}")
                    zipai_count = 0
                    for i in QBK_set:
                        if i in self.zipai:
                            zipai_count += 1
                    if zipai_count == 7:
                        temp_player_tiles.fan_list.append("qixingbukao") # 七星不靠
                        player_tiles_list.append(temp_player_tiles)
                    elif zipai_count == 5: # 如果字牌数量 == 5 说明数牌侧有九种组成组合龙的手牌
                        temp_player_tiles.fan_list.append("quanbukao") # 全不靠
                        temp_player_tiles.fan_list.append("zuhelong") # 组合龙
                        player_tiles_list.append(temp_player_tiles)
                    else:
                        temp_player_tiles.fan_list.append("quanbukao") # 全不靠
                        player_tiles_list.append(temp_player_tiles)
                    return False
                
        # 如果手牌种类为9种 则可能组合龙
        elif hand_kind_set >= 9:
            ZHL_case_list = [{11,14,17,22,25,28,33,36,39}, {11,14,17,32,35,38,23,26,29}, {21,24,27,12,15,18,33,36,39}, 
                            {21,24,27,32,35,38,13,16,19}, {31,34,37,22,25,28,13,16,19}, {31,34,37,12,15,18,23,26,29}]
            for index,case in enumerate(ZHL_case_list):
                ZHL_set = set()
                for i in player_tiles.hand_tiles:
                    if i in case:
                        ZHL_set.add(i)
                # 如果组合龙集合 = 9或者8 则在一向听的前提下 如果的确听牌 和牌必然包含组合龙 直接移除后进入一般型检测
                if len(ZHL_set) == 9:
                    temp_player_tiles = player_tiles.__deepcopy__(None)
                    temp_player_tiles.complete_step += 9
                    temp_player_tiles.combination_list.append(f"z{index}")
                    temp_player_tiles.fan_list.append("zuhelong") # 组合龙
                    for i in case:
                        temp_player_tiles.hand_tiles.remove(i)
                    player_tiles_list.append(temp_player_tiles)
                    return False
        else:
            return False

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
                if save_list == self.jiulianbaodeng_list:
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
            for list in [wan_list,bing_list,tiao_list]:
                if len(list) >= 2:
                    for i in list:
                        i = int(i)
                        if str(i + 3) in list:
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
            sign_count = 0
            for sign in save_kezi_sign:
                if int(sign) == sign_pointer and int(sign) <= 40:
                    sign_count += 1
                    sign_pointer += 1
            if sign_count == 3:
                player_tiles.fan_list.append("yisesanjiegao") # 一色三节高
            elif sign_count == 4:
                player_tiles.fan_list.append("yisesijiegao") # 一色四节高
            
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
                if all(i in ["2","4","6","8"] for i in all_list):
                    if save_quetou_sign[0][1] in ["2","4","6","8"]:
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

                # 开始判断传参番种 包括 妙手回春 杠上开花 抢杠和 和绝张 花牌 海底捞月 全求人 门前清 不求人 自摸
                case "妙手回春":
                    player_tiles.fan_list.append("miaoshouhuichun") # 妙手回春
                case "杠上开花":
                    player_tiles.fan_list.append("gangshangkaihua") # 杠上开花
                case "抢杠和":
                    player_tiles.fan_list.append("qiangganghe") # 抢杠和
                case "和绝张":
                    player_tiles.fan_list.append("hejuezhang") # 和绝张
                case "花牌":
                    player_tiles.fan_list.append("huapai") # 花牌
                case "海底捞月":
                    player_tiles.fan_list.append("haidilaoyue") # 海底捞月
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
                        # 由于七对子，连七对，九莲宝灯的自摸不计不求人，但是不求人又不计自摸，所以如果不求人和自摸都存在，就会被同事剔除
                        # 添加额外验证避免阻挡番嵌套 是粗糙的方法 也可以使用阻挡牌优先级依次消除阻挡
                        if any(i in {"qiduizi","jiulianbaodeng","lianqidui","shisanyao","sianke","qixingbukao","quanbukao"} for i in player_tiles.fan_list):
                            player_tiles.fan_list.append("zimo") # 自摸
                        else:
                            player_tiles.fan_list.append("buqiuren") # 不求人
                    else:
                        player_tiles.fan_list.append("zimo") # 自摸

    def fan_count_output(self,player_tiles:PlayerTiles,combination_str,zimo_or_not):
        if not player_tiles.fan_list:
            player_tiles.fan_list.append("wufanhe") # 无番和



        # 根据规定原则排除阻挡番种：
        # 通过字典repel_model_dict存储番种的阻挡关系,通过自摸或点和找到字典的对应阻挡列表项
        need_to_remove = []
        max_yaojiuke_count = 0
        for fan in player_tiles.fan_list:
            if fan in self.repel_model_dict:
                for i in self.repel_model_dict[fan]:
                    if i != "yaojiuke":
                        need_to_remove.append(i)
                    elif i == "yaojiuke":
                        yaojiuke_count = self.repel_model_dict[fan].count("yaojiuke")
                        if yaojiuke_count > max_yaojiuke_count:
                            max_yaojiuke_count = yaojiuke_count
            else:
                if zimo_or_not: # yes
                    for i in self.repel_model_dict[f"{fan}_zimo"]:
                        if i != "yaojiuke":
                            need_to_remove.append(i)
                        elif i == "yaojiuke":
                            yaojiuke_count = self.repel_model_dict[f"{fan}_zimo"].count("yaojiuke")
                            if yaojiuke_count > max_yaojiuke_count:
                                max_yaojiuke_count = yaojiuke_count
                else: # no
                    for i in self.repel_model_dict[f"{fan}_dianhe"]:
                        if i != "yaojiuke":
                            need_to_remove.append(i)
                        elif i == "yaojiuke":
                            yaojiuke_count = self.repel_model_dict[f"{fan}_dianhe"].count("yaojiuke")
                            if yaojiuke_count > max_yaojiuke_count:
                                max_yaojiuke_count = yaojiuke_count
        
        # 如果三风刻,三风刻不计幺九刻,如果没有三风刻,根据门风圈风是否存在,阻挡幺九刻
        if "sanfengke" in player_tiles.fan_list:
            need_to_remove.append("yaojiuke")
            need_to_remove.append("yaojiuke")
            need_to_remove.append("yaojiuke")
        else:
            if "quanfengke" in player_tiles.fan_list:
                need_to_remove.append("yaojiuke")
            if "menfengke" in player_tiles.fan_list:
                if "门风圈风相同" in way_to_hepai:
                    pass
                else:
                    need_to_remove.append("yaojiuke")

        self.debug_print("全部被添加的番种",player_tiles.fan_list)
        # 按番大小排列
        player_tiles.fan_list.sort(key=lambda x: self.count_model_dict[x], reverse=True)

        self.debug_print("需要被阻挡的番种",need_to_remove)

        for i in need_to_remove:
            if i in player_tiles.fan_list:
                player_tiles.fan_list.remove(i)
        self.debug_print("需要移除的幺九刻数量",max_yaojiuke_count)
        for i in range(max_yaojiuke_count):
            if "yaojiuke" in player_tiles.fan_list:
                player_tiles.fan_list.remove("yaojiuke")




        # 根据顺子的组合原则对牌型进行处理：
        repeatable_fan_list = []
        origin_fan_list = []
        for i in player_tiles.fan_list:
            if i in {"yibangao","xixiangfeng","lianliu","laoshaofu"}:
                repeatable_fan_list.append(i)
            else:
                origin_fan_list.append(i)
        
        self.debug_print("重复番种",repeatable_fan_list)

        if len(repeatable_fan_list) > 0:
            # A.四顺子番种成立时，不得加计任何双顺子番种（一般高、喜相逢、连六、老少副）以下方法在拥有四顺子番种时剔除所有双顺番种即可
            # 如果有四顺子番种，则不添加全部二顺番种
            if any(i in player_tiles.fan_list for i in {"yiseshuanglonghui","yisesitongshun","yisesibugao","sanseshuanglonghui"}):
                player_tiles.fan_list = origin_fan_list
            # B.三顺子番种成立时，仅当第 4 个面子为顺子，且该顺子与组成三顺子番种的 3 副顺子中的 1 副满足某个双顺子番种的定义时，
            # 方可加计至多 1 番符合相应定义的双顺子番种；让我们细数各种三顺子番种的可能 
            # 1.一色三同顺成立则现有算法一般高不成立必然,保留任意唯一二顺番即可
            elif "yisesantongshun" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            # 2.三色三步高本身什么都不复合,直接使用第一个成立的二顺番即可
            elif "sansesanbugao" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            # 3.三色三同顺在复合喜相逢的情况下,复合一般高必然,剔除全部喜相逢保留任意唯一二顺番即可
            elif "sansesantongshun" in player_tiles.fan_list:
                for i in repeatable_fan_list:
                    if i in ["yibangao","lianliu","laoshaofu"]: # 没有喜相逢
                        origin_fan_list.append(i)
                        break
            # 4.一色三步高本身什么都不复合,直接使用第一个成立的二顺番即可
            elif "yisesanbugao" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            # 5.清龙可能复合连六和老少副,但是清龙复合连六或老少副说明一般高成立,去除全部连六和老少副,保留任意唯一二顺番即可
            elif "qinglong" in player_tiles.fan_list:
                for i in repeatable_fan_list:
                    if i in ["yibangao","xixiangfeng"]: # 没有连六、老少副
                        origin_fan_list.append(i)
                        break
            # 6.花龙本身什么都不复合,直接使用第一个成立的二顺番即可
            elif "hualong" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            # C.任何四顺子番种、三顺子番种均不成立时，双顺子番种在 4 副顺子之间至多计 3 番,3 副顺子之间至多计 2 番，即能够保留的二顺番数量始终是顺子数量-1
            # 但是x副顺子之间至多计x番并不等于只要计的二顺番比顺子数量-1少就可以了,例如12s12s17s计一般高、一般高显然就是不行的
            # 前面的算法确保了喜相逢和连六成立的位置是唯一的,而老少副和一般高成立的位置是可重复的,所以可能得以下关系
            # s12 s12 s15 s15 一般高 一般高 喜相逢 喜相逢 此时拿掉喜相逢牌型仍然是正确的
            # s12 s15 s18 s22 连六连六老少副喜相逢 此时清龙成立的情况下只计一般高和喜相逢 仍然是正确的
            else:
                max_fan_count = combination_str.count("s")+combination_str.count("S") - 1
                if len(repeatable_fan_list) <= max_fan_count: # 重复番种数量小于等于允许数量则通过
                    origin_fan_list = origin_fan_list + repeatable_fan_list
                else: # 重复番种数量大于允许数量则只添加头部分
                    for i in range(max_fan_count):
                        origin_fan_list.append(repeatable_fan_list[i])

        player_tiles.fan_list = origin_fan_list
        self.debug_print("最终番种",player_tiles.fan_list)



        # 结算得分和展示文本
        # 四归一、双同刻、一般高、喜相逢、连六、幺九刻、花牌七个番种允许复计。 先计算减计列表，然后取最大值向下覆盖
        fuji_set = {"siguiyi","shuangtongke","yibangao","xixiangfeng","lianliu","yaojiuke","huapai"}
        fuji_list = ["siguiyi","shuangtongke","yibangao","xixiangfeng","lianliu","yaojiuke","huapai"]
        fan_count = 0
        temp_fan_count_list = []
        for i in player_tiles.fan_list:
            if i not in fuji_set:
                fan_count += self.count_model_dict[i]
                self.debug_print(f"添加番数{i},{self.count_model_dict[i]}")
                temp_fan_count_list.append(f"{self.eng_to_chinese_dict[i]}")
        for i in fuji_list:
            if i in player_tiles.fan_list:
                fan_count += player_tiles.fan_list.count(i) * self.count_model_dict[i]
                self.debug_print(f"添加番数{i},{player_tiles.fan_list.count(i) * self.count_model_dict[i]}")
                temp_fan_count_list.append(f"{self.eng_to_chinese_dict[i]}*{player_tiles.fan_list.count(i)}")

        player_tiles.fan_count_list = temp_fan_count_list
        self.debug_print("和牌文本",player_tiles.fan_count_list)
        self.debug_print("和牌得分",fan_count)
        return fan_count,player_tiles.fan_count_list # 返回和牌得分 展示文本 int/list[str]

    def fan_count(self, player_tiles: PlayerTiles,get_tile,way_to_hepai):

        # 判断前处理 处理get_tile
        zimo_or_not = False
        if any(i in ["妙手回春","自摸","杠上开花"] for i in way_to_hepai):
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
        result = self.fan_count_output(player_tiles,combination_str,zimo_or_not)
        return result # 元组(int,list[str])

# 测试
if __name__ == "__main__":
    # 生成刻顺=>手牌
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
    # 生成雀头=>手牌
    quetou_dict = {
        "q11": [11,11],"q12": [12,12],"q13": [13,13],"q14": [14,14],"q15": [15,15],"q16": [16,16],"q17": [17,17],"q18": [18,18],"q19": [19,19],
        "q21": [21,21],"q22": [22,22],"q23": [23,23],"q24": [24,24],"q25": [25,25],"q26": [26,26],"q27": [27,27],"q28": [28,28],"q29": [29,29], 
        "q31": [31,31],"q32": [32,32],"q33": [33,33],"q34": [34,34],"q35": [35,35],"q36": [36,36],"q37": [37,37],"q38": [38,38],"q39": [39,39],
        "q41": [41,41],"q42": [42,42],"q43": [43,43],"q44": [44,44],"q45": [45,45],"q46": [46,46],"q47": [47,47], 
    }
    # 生成组合
    combination_dict = {
            "s12": [11,12,13],"s13": [12,13,14],"s14": [13,14,15],"s15": [14,15,16],"s16": [15,16,17],"s17": [16,17,18],"s18": [17,18,19],
            "s22": [21,22,23],"s23": [22,23,24],"s24": [23,24,25],"s25": [24,25,26],"s26": [25,26,27],"s27": [26,27,28],"s28": [27,28,29],
            "s32": [31,32,33],"s33": [32,33,34],"s34": [33,34,35],"s35": [34,35,36],"s36": [35,36,37],"s37": [36,37,38],"s38": [37,38,39], # 顺
            "k11": [11,11,11],"k12": [12,12,12],"k13": [13,13,13],"k14": [14,14,14],"k15": [15,15,15],"k16": [16,16,16],"k17": [17,17,17],"k18": [18,18,18],"k19": [19,19,19],
            "k21": [21,21,21],"k22": [22,22,22],"k23": [23,23,23],"k24": [24,24,24],"k25": [25,25,25],"k26": [26,26,26],"k27": [27,27,27],"k28": [28,28,28],"k29": [29,29,29],
            "k31": [31,31,31],"k32": [32,32,32],"k33": [33,33,33],"k34": [34,34,34],"k35": [35,35,35],"k36": [36,36,36],"k37": [37,37,37],"k38": [38,38,38],"k39": [39,39,39],
            "k41": [41,41,41],"k42": [42,42,42],"k43": [43,43,43],"k44": [44,44,44],"k45": [45,45,45],"k46": [46,46,46],"k47": [47,47,47], # 刻
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

    """
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
        
    logger.debug("随机生成手牌：",tiles_list,"随机生成组合：",combination_list)

    # 手动指定
    # tiles_list = [11,11,11,12,12,12,13,13,13,14,14,14,15,15]
    # combination_list = []
    # print("手动指定手牌：",tiles_list,"手动指定组合：",combination_list)

    # 排序,然后随机删除一张用作和牌张
    tiles_list.sort()
    hepai_tiles = tiles_list.(random.randint(0, len(tiles_list) - 1))
    
    # 选择一种和牌方式
    way_to_hepai = []
    way_to_hepai.append(random.choice(["荣和","自摸","抢杠和","妙手回春","海底捞月","岭上开花"])) # 1级传参 和牌方式
    way_to_hepai.append(random.choice(["场风东","场风南","场风西","场风北"])) # 2级传参 自身位置
    way_to_hepai.append(random.choice(["自风东","自风南","自风西","自风北"]))
    if random.randint(0,100) < 50:
        way_to_hepai.append(random.choice(["和绝张"])) # 3级传参 条件番
    if random.randint(0,100) < 50:
        way_to_hepai.append(random.choice(["和单张"]))

    for i in range(random.randint(0,3)):
        if random.randint(0,100) < 50:
            way_to_hepai.append("花牌") # 4级传参 奖励番

    logger.debug("随机生成和牌方式：",way_to_hepai)
    """
    # 手动指定和牌方式

    test_save = [["k47","g45"],[15,15,31,31,31,46,46,46],46,["自摸"]]

    # 测试数据 摘自新编MCR番种定义
    # 大四喜
    # 1 test_save = [["k41","G44","k42"],[19,19,43,43,43],19,["自摸","和单张"]] 132
    # 2 test_save = [["k44","g43"],[28,28,41,41,41,42,42,42],42,["自摸"]] 98
    # 3 test_save = [["k42","k43"],[34,34,41,41,41,42,42,42],42,["点和"]] 94
    # 4 test_save = [["k43","k42","k44"],[41,41,41,46,46],41,["点和"]] 152
    # 大三元
    # 1 test_save = [["k47","g45"],[15,15,31,31,31,46,46,46],46,["自摸"]] 100
    # 2 test_save = [["k47","k46"],[11,11,21,22,23,45,45,45],11,["点和","和单张"]] 94
    # 3 test_save = [["k45"],[26,26,26,27,28,46,46,46,47,47,47],47,["点和"]] 94 
    # 4 test_save = [["k42","G46","k47"],[43,43,45,45,45],45,["点和","自风南"]] # 156
    # 绿一色
    # 1 test_save = [["s33"],[32,32,32,33,33,33,34,34,34,36,36],33,["点和"]] 164
    # 2 test_save = [["k33","G38","k34"],[32,32,32,36,36],36,["自摸","和单张"]] 150
    # 3 test_save = [["g47","g38","k34"],[32,32,33,33,33],32,["点和"]] 100
    # 4 test_save = [["s33","k36"],[32,33,34,38,38,38,47,47],34,["点和"]] 89
    # 5 test_save = [[],[32,32,33,33,34,34,36,36,38,38,47,47,47,47],47,["自摸","和单张"]] 115
    # 九莲宝灯
    # 1 test_save = [[],[11,11,11,12,13,14,15,16,17,18,19,19,19,18],18,["点和"]] 92
    # 2 test_save = [[],[21,21,21,22,23,24,25,26,27,28,29,29,29,23],23,["自摸"]] 90
    # 3 test_save = [[],[31,31,31,32,33,34,35,36,37,38,39,39,39,36],36,["点和"]] 89
    # 4 test_save = [[],[21,21,21,22,23,24,25,26,27,28,29,29,29,25],25,["自摸"]] 92
    # 四杠
    # 1 test_save = [["G16","G28","G32","G44"],[29,29],29,["点和"]] 153
    # 2 test_save = [["G36","g31","G38","G12"],[35,35],35,["自摸"]] 108
    # 3 test_save = [["G41","g19","g17","G11"],[45,45],45,["自摸"]] 100
    # 4 test_save = [["g36","G15","g33","g22"],[42,42],42,["点和"]] 88
    # 5 test_save = [["g43","g25","g13","g21"],[14,14],14,["点和","自风西","场风西","和单张"]] # 100
    # 连七对
    # 1 test_save = [[],[12,12,13,13,14,14,15,15,16,16,17,17,18,18],18,["点和"]] 90
    # 2 test_save = [[],[23,23,24,24,25,25,26,26,27,27,28,28,29,29],25,["自摸"]] 89
    # 3 test_save = [[],[31,31,32,32,33,33,34,34,35,35,36,36,37,37],33,["点和"]] 88
    # 十三幺
    # test_save = [[],[11,19,21,29,31,39,41,42,43,44,44,45,46,47],19,["自摸"]] # 89

    # 清幺九
    # 1 test_save = [["k29","g19","k11"],[31,31,21,21,21],31,["点和","和单张"]] 66
    # 2 test_save = [["G39","k21"],[11,11,11,19,19,19,29,29],29,["自摸","和单张"]] 84
    # 3 test_save = [["k19","g29"],[11,11,11,31,31,39,39,39],39,["自摸"]] 84
    # 4 test_save = [["k31","k39","k19"],[11,11,11,29,29],11,["点和"]] 64
    # 5 test_save = [[],[11,11,19,19,31,31,31,31,39,39,21,21,29,29],19,["点和"]] 90




    # 小四喜
    # 1 test_save = [["k41","k42"],[43,43,43,44,44,47,47,47],47,["点和"]] 130
    # 2 test_save = [["k29","k44"],[41,41,41,42,42,43,43,43],43,["自摸","场风西","自风东"]] 109
    # 3 test_save = [["k43","k14","g44"],[41,41,42,42,42],41,["点和","和单张","场风东","自风西"]] 80
    # 4 test_save = [["k41","k43"],[35,36,37,42,42,44,44,44],44,["点和","场风南","自风南"]] 70
    # 小三元
    # 1 test_save = [["k46","g39"],[11,12,13,45,45,45,47,47],47,["点和","和单张"]] 72
    # 2 test_save = [["g47","k27","G46"],[22,22,22,45,45],22,["自摸"]] 84
    # 3 test_save = [["k42","k47","k45"],[43,43,43,46,46],43,["点和"]] 128
    # 4 test_save = [[],[43,43,43,44,44,44,45,45,45,47,47,47,46,46],46,["自摸","场风南","自风北","和单张"]] 196
    # 字一色
    # 1 test_save = [["k44","k46","g41"],[43,43,43,45,45],43,["自摸","场风东","自风北"]] 84
    # 2 test_save = [["k47","k44","k42"],[41,41,45,45,45],45,["点和","场风西","自风西"]] 70
    # 3 test_save = [[],[41,41,42,42,43,43,44,44,45,45,46,46,47,47],42,["点和","和单张"]] 88
    # 四暗刻
    # 1 test_save = [["G12","G23"],[15,15,15,36,36,36,21,21],15,["自摸"]] 72
    # 2 test_save = [["G18"],[34,34,34,26,26,26,44,44,44,47,47],47,["自摸","场风北","自风西","和单张"]] 76
    # 3 test_save = [[],[11,11,13,13,13,37,37,37,25,25,25,41,41,41],11,["点和","场风南","自风南"]] 65
    # 一色双龙会
    # 1 test_save = [[],[11,11,12,12,13,13,15,15,17,17,18,18,19,19],13,["自摸","和单张"]] 69
    # 2 test_save = [["s38","s32"],[31,32,33,35,35,37,38,39],39,["点和"]] 64
    # 一色四同顺
    # 1 test_save = [["s13","s13"],[12,12,13,13,14,14,18,18],13,["自摸","和单张"]] 78
    # 2 test_save = [["s27","s27","s27"],[11,11,26,27,28],28,["点和","和绝张"]] 55
    # 3 test_save = [["s32"],[31,31,31,32,32,32,33,33,33,44,44],33,["自摸"]] 59
    # 一色四节高
    # 1 test_save = [["k25","G24"],[22,22,22,23,23,23,28,28],23,["自摸"]] 101
    # 2 test_save = [["k16","k17"],[15,15,15,18,18,18,31,31],18,["点和"]] 50
    # 3 test_save = [["k38","k39","g36"],[37,37,37,42,42],37,["点和"]] 56
    # 一色四步高
    # 1 test_save = [["s34"],[32,33,34,34,35,35,36,36,37,26,26],37,["自摸"]] 38
    # 2 test_save = [["s18","s12"],[13,14,15,15,16,17,45,45],13,["点和"]] 38
    # 三杠
    # 1 test_save = [["G31","G44","G12"],[23,23,25,25,25],23,["点和","场风北","自风东"]] 99
    # 2 test_save = [["G21","g14","G43"],[13,13,13,47,47],13,["自摸","场风东","自风南"]] 58
    # 3 test_save = [["g45","s26","G46","G36"],[42,42],42,["点和","和单张"]] 42
    # 4 test_save = [["g34","g22","G15"],[16,17,18,41,41],16,["点和"]] 32
    # 混幺九
    # 1 test_save = [["g11","g46","k21"],[31,31,43,43,43],43,["点和","场风东","自风西"]] 48
    # 2 test_save = [["k45","k21","G44"],[29,29,47,47,47],47,["自摸","场风北","自风南"]] 51
    # 3 test_save = [["k41","k39"],[19,19,21,21,21,42,42,42],21,["点和","场风西","自风北"]] 32
    # 4 test_save = [[],[11,11,19,19,31,31,29,29,41,41,44,44,46,46],19,["自摸"]] 63
    # 七对
    # 1 test_save = [[],[13,13,15,15,15,15,31,31,39,39,22,22,27,27],22,["自摸"]] 28
    # 2 test_save = [[],[14,14,16,16,18,18,32,32,36,36,22,22,28,28],18,["点和"]] 26
    # 3 test_save = [[],[17,17,19,19,33,33,39,39,24,24,25,25,45,45],39,["点和"]] 24
    # 七星不靠
    # 1 test_save = [[],[11,14,17,32,35,23,29,41,42,43,44,45,46,47],42,["自摸"]] 25
    # 2 test_save = [[],[12,15,18,23,26,29,34,41,42,43,44,45,46,47],34,["点和"]] 24
    # 全双刻
    # 1 test_save = [["G16","k18","k38"],[26,26,26,28,28],26,["点和"]] 42
    # 2 test_save = [["k26","g24"],[32,32,34,34,34,32,22,22],12,["自摸"]] 31
    # 3 test_save = [["k36","k14"],[12,12,22,22,28,28,28,12],12,["点和"]] 24
    # 清一色
    # 1 test_save = [[],[11,11,11,12,13,14,15,16,16,17,18,19,19,19],12,["点和"]] 28
    # 2 test_save = [["k38"],[32,32,33,33,33,34,34,34,35,36,36],35,["自摸"]] 28
    # 3 test_save = [["s33","k37","s32"],[38,38,38,39,39],38,["点和"]] 24
    # 4 test_save = [[],[21,21,22,22,23,23,24,24,25,25,28,28,29,29],22,["自摸"]] 57
    # 一色三同顺
    # 1 test_save = [["s12","s12"],[11,12,13,17,18,19,42,42],11,["点和"]] 35
    # 2 test_save = [[],[15,15,15,16,17,25,25,25,26,26,26,27,27,27],17,["自摸"]] 48
    # 一色三节高
    # 1 test_save = [["k39"],[32,32,32,33,33,33,34,34,34,41,41],33,["点和"]] 39
    # 2 test_save = [["k14","k13"],[15,15,15,31,31,22,23,24],24,["点和"]] 25
    # 3 test_save = [["k29","g27","k28"],[17,18,19,19,19],19,["自摸"]] 52
    # 全大
    # 1 test_save = [["k19","g27"],[18,18,37,37,38,38,39,39],18,["点和","和单张"]] 28
    # 2 test_save = [[],[17,17,18,18,19,19,38,38,39,39,28,28,29,29],39,["自摸","和单张"]] 49
    # 全中
    # 1 test_save = [[],[14,14,15,15,16,16,35,35,24,24,25,25,26,26],25,["自摸","和单张"]] 50
    # 2 test_save = [["k34","k36"],[14,14,14,16,16,24,24,24],14,["点和"]] 64
    # 3 test_save = [["s15","k34"],[35,35,36,36,36,25,25,25],36,["点和"]] 24
    # 4 test_save = [[],[16,16,34,34,35,35,36,36,24,24,25,25,26,26],16,["点和"]] 48
    # 全小
    # 1 test_save = [["s32"],[11,11,12,12,13,13,31,31,21,22,23],13,["点和","和单张"]] 40
    # 2 test_save = [["k12","g13"],[32,32,32,22,22,23,23,23],23,["自摸"]] 40
    # 3 test_save = [[],[31,31,31,31,32,32,33,33,21,21,22,22,23,23],31,["自摸"]] 52
    # 清龙
    # 1 test_save = [[],[11,11,11,11,12,13,14,15,16,17,18,19,19,19],11,["点和"]] 106
    # 2 test_save = [["s12","s35","s32","s38"],[31,31],31,["点和","和单张"]] 26
    # 3 test_save = [["s18"],[11,12,13,14,15,15,16,16,17,28,28],16,["自摸"]] 20
    # 4 test_save = [["g44","s28"],[21,22,23,24,25,26,45,45],25,["自摸","和单张","场风北","自风东"]] 27
    # 5 test_save = [[],[18,18,31,32,33,34,35,36,37,38,39,28,28,28],36,["点和"]] 19
    # 6 test_save = [["s22"],[33,33,33,24,25,26,27,28,29,41,41],29,["点和"]] 17
    # 三色双龙会
    # test_save = [[],[11,12,13,17,18,19,35,35,21,22,23,27,28,29],22,["点和","和单张"]] # 19
    # test_save = [["s32","s38","s28","s22"],[15,15],15,["自摸","和单张"]] # 18
    # test_save = [["s18","s38"],[11,12,13,31,32,33,25,25],31,["点和"]] # 16


    
    # 一色三步高
    # 1 test_save = [[],[11,12,13,13,14,15,15,16,17,18,18,18,19,19],15,["自摸"]] 44
    # 2 test_save = [["s35"],[14,14,32,33,34,35,36,36,37,37,38],38,["点和"]] 22
    # 3 test_save = [["g46","s23"],[21,22,23,23,24,25,29,29],23,["点和"]] 33
    # 全带五
    # 1 test_save = [["s14","s35"],[15,15,23,24,25,25,26,27],15,["点和","和单张"]] 26
    # 2 test_save = [["G25"],[15,15,15,15,16,17,33,34,35,35,35],34,["自摸"]] 28
    # 3 test_save = [["s25","s16"],[13,14,15,35,35,35,25,25],13,["点和"]] 16
    # 三同刻
    # 1 test_save = [["k14","k22"],[12,12,12,32,32,32,34,34],34,["自摸"]] 55
    # 2 test_save = [["g31","k11"],[39,39,21,21,21,28,28,28],28,["点和"]] 27
    # 3 test_save = [["k27","k37"],[17,17,17,22,23,24,46,46],17,["点和"]] 16



    # 三暗刻
    # 1 test_save = [["G28","g17","G31","G39"],[16,16],16,["点和","和单张"]]  # 115
    # 2 test_save = [["G42","G33","G12"],[29,29,45,45,45],45,["点和","场风东","自风东"]]  65
    # 3 test_save = [["G43"],[13,13,19,19,19,34,35,35,35,35,36],35,["点和","场风北","自风西"]]  26
    # 4 test_save = [["s17"],[33,33,33,26,26,26,27,27,27,47,47],26,["自摸"]]  17
    # 5 test_save = [["k36"],[11,11,11,38,38,38,41,41,41,47,47],47,["点和","和单张","场风南","自风西"]] 26
    # 6 test_save = [[],[14,14,14,32,32,32,23,23,23,24,25,44,44,44],24,["自摸","场风北","自风南"]] 22
    # 全不靠
    # 1 test_save = [[],[12,15,18,33,36,39,21,24,27,41,42,44,45,47],12,["自摸"]] 25
    # 2 test_save = [[],[11,14,17,32,38,23,26,29,42,43,44,45,46,47],32,["点和"]] 12
    # 组合龙
    # 1 test_save = [[],[13,16,19,31,34,37,22,25,28,41,42,43,44,46],46,["点和"]] 24
    # 2 test_save = [[],[11,14,17,32,35,38,21,21,22,23,23,24,26,29],17,["自摸"]] 18
    # 3 test_save = [["s25"],[12,15,18,33,36,39,21,24,27,47,47],33,["点和"]] 12
    # 大于五
    # 1 test_save = [["s17","s28"],[19,19,26,26,27,27,28,28],28,["自摸"]] 18
    # 2 test_save = [["s38","k36"],[16,17,18,19,19,27,27,27],19,["点和"]] 12
    # 3 test_save = [[],[16,16,17,17,18,18,37,37,37,37,39,39,29,29],37,["点和"]] 38
    # 小于五
    # 1 test_save = [["k31","g13","s33"],[21,21,22,22,22],22,["自摸"]] 23
    # 2 test_save = [["k12"],[11,11,34,34,34,21,22,22,23,23,24],34,["点和"]] 12
    # 3 test_save = [[],[12,12,14,14,32,32,33,33,22,22,23,23,24,24],14,["点和"]] 38
    # 三风刻
    # 1 test_save = [["g41","k44","G42"],[31,31,31,32,32],31,["点和","场风北","自风东"]] 34
    # 2 test_save = [["k41","k43"],[11,11,27,28,29,44,44,44],11,["自摸","和单张","场风南","自风西"]] 21
    # 3 test_save = [["g46","k42"],[19,19,41,41,41,43,43,43],41,["自摸","场风南","自风北"]] 58
    # 4 test_save = [["k42"],[12,13,14,35,35,43,43,43,44,44,44],44,["点和","场风东","自风东"]] 13
    # 花龙
    # 1 test_save = [["k26"],[14,15,16,17,17,31,32,33,27,28,29],27,["自摸","和单张"]] 11
    # 2 test_save = [["s13","s18"],[34,35,36,21,22,23,29,29],36,["点和"]] 10
    # 3 test_save = [["s18"],[11,12,13,15,15,31,32,33,24,25,26],11,["自摸"]] 12
    # 推不倒
    # 1 test_save = [["s22","s24"],[22,23,24,28,28,28,29,29],29,["点和"]] 48
    # 2 test_save = [["s35","g39"],[32,32,32,38,38,46,46,46],46,["点和"]] # 18
    # 3 test_save = [["s35"],[34,36,35,22,22,23,23,24,24,29,29],35,["自摸","和单张"]] # 新编MCR中缺少自摸 14
    # 4 test_save = [["k38","G28","k35"],[23,23,23,25,25],25,["自摸"]] # 23
    # 5 test_save = [["s35","s22"],[38,38,38,22,23,24,46,46],38,["点和"]] # 8
    # 6 test_save = [[],[34,34,36,36,21,21,23,23,24,24,25,25,29,29],24,["自摸"]] # 34
    # 三色三同顺
    # 1 test_save = [["g45","s25"],[14,15,16,34,35,36,41,41],16,["自摸"]] 18
    # 2 test_save = [[],[12,13,14,15,16,17,18,18,35,36,37,25,26,27],27,["点和"]] 15
    # 3 test_save = [["s34"],[12,13,13,14,14,15,23,24,25,47,47],27,["点和"]] 8
    # 三色三节高
    # 1 test_save = [["k47","g39","k27"],[18,18,18,43,43],18,["自摸"]] 25
    # 2 test_save = [["k26","k17","k38"],[21,21,27,28,29],21,["点和","和单张"]] 10
    # 3 test_save = [["k24","k16"],[31,32,33,35,35,35,41,41],41,["点和"]] 8
    # 无番和
    # 1 test_save = [["k28","s13"],[13,14,15,35,36,37,42,42],15,["点和"]] 8
    # 2 test_save = [["s22","s16","s27"],[32,33,34,44,44],32,["点和"]] 8
    # 3 test_save = [["k37"],[12,12,12,17,18,19,23,23,23,45,45],23,["点和"]] 8
    # 妙手回春 海底捞月 杠上开花 抢杠和 花牌
    # 1 test_save = [["k37"],[12,12,12,17,18,19,23,23,23,45,45],23,["妙手回春","海底捞月","杠上开花","抢杠和","花牌","花牌"]]
    # 碰碰和
    # 1 test_save = [["k32","g46"],[13,13,13,25,25,25,45,45],25,["点和"]] 9
    # 2 test_save = [["k31","k23","k28"],[11,11,16,16,16],16,["自摸"]] 9
    # 混一色
    # 1 test_save = [["k41","k43"],[12,12,14,15,16,17,17,17],12,["点和","场风南","自风北"]] 8
    # 2 test_save = [["k38"],[32,32,32,33,33,33,34,34,31,47,47],31,["自摸"]] 8
    # 3 test_save = [[],[21,21,23,23,24,24,29,29,42,42,44,44,45,45],44,["点和"]] 30
    # 三色三步高
    # 1 test_save = [["s22","s14"],[17,18,19,32,33,34,36,36],32,["点和"]] 8
    # 2 test_save = [["s18","k42"],[14,14,35,36,37,26,27,28],26,["点和","场风南","自风东"]] 8
    # 3 test_save = [["s34"],[14,16,15,17,17,22,23,24,44,44,44],15,["自摸","和单张","场风东","自风南"]] 9
    # 五门齐
    # 1 test_save = [[],[12,15,18,31,34,37,23,26,29,41,41,47,47,47],15,["点和"]] 22
    # 2 test_save = [["k43","g45","k24"],[17,17,34,34,34],34,["自摸","场风西","自风东"]] 20
    # 3 test_save = [["k42","k32"],[17,18,19,26,27,28,46,46],26,["自摸","场风东","自风东"]] 8
    # 4 test_save = [[],[14,14,31,31,39,39,28,28,41,41,44,44,46,46],44,["点和"]] 30
    # 全求人
    # 1 test_save = [["s27","k47","s15","k38"],[11,11],11,["点和","和单张"]] 8
    # 2 test_save = [["g44","k42","k12","g37"],[21,21],21,["点和","和单张","场风西","自风北"]] 19
    # 双暗杠
    # 1 test_save = [["G22","k41","G17"],[33,33,34,35,36],36,["自摸","场风东","自风南"]] 9
    # 2 test_save = [["G13","G25"],[16,17,18,19,19,19,38,38,38],19,["点和"]] 25
    # 双箭刻
    # 1 test_save = [["k45","k44"],[18,18,37,38,39,47,47,47],39,["点和","场风南","自风西"]] 8
    # 2 test_save = [["g47","k46"],[15,16,17,32,32,24,25,26],17,["自摸"]] 8
    # 3 test_save = [["k28","k14"],[37,37,45,45,45,46,46,46],46,["点和"]] 12
    # 全带幺
    # 1 test_save = [["s12","s32"],[37,38,39,42,42,47,47,47],47,["点和"]] 9
    # 2 test_save = [["g43","G41","s28"],[11,12,13,39,39],13,["自摸","场风西","自风南","和单张"]] 14
    # 3 test_save = [["k31","k11"],[21,21,21,22,23,27,28,29],22,["点和"]] 10
    # 不求人
    # 1 test_save = [[],[16,17,18,24,24,26,27,28,31,32,33,34,35,36],31,["自摸"]] 8
    # 2 test_save = [["G37"],[11,11,11,12,12,13,14,15,17,18,19],12,["自摸"]] 11
    # 双明杠
    # 1 test_save = [["g42","g22"],[17,17,17,26,27,28,45,45],45,["点和","和单张","场风南","自风西"]] 8
    # 2 test_save = [["g19","g44"],[33,33,33,34,35,36,41,41],36,["自摸","场风东","自风东"]] 8
    # 3 test_save = [["g15","g23","k38"],[11,11,25,25,25],25,["点和"]] 13
    # 和绝张
    # 1 test_save = [["g43","k16"],[14,15,16,29,29,32,32,32],16,["点和","和绝张"]] 8
    # 箭刻
    # 1 test_save = [["k45"],[12,13,14,37,37,37,24,25,26,44,44],26,["点和"]] 8
    # 2 test_save = [["s26"],[13,14,15,34,35,36,46,46,46,47,47],46,["点和"]] 8
    # 3 test_save = [["g47","s38"],[11,12,13,31,32,33,34,34],33,["自摸","和单张"]] 8
    # 圈风刻
    # 1 test_save = [["G42"],[12,13,14,27,28,29,33,33,36,37,38],14,["点和","场风南","自风南"]] 8
    # 2 test_save = [["g43","s23","k44"],[31,31,25,26,27],26,["自摸","和单张","场风西","自风东"]] 8
    # 门风刻
    # 1 test_save = [["s36","k44","s18"],[21,21,26,27,28],28,["点和","场风西","自风北"]] 8
    # 2 test_save = [["g29"],[14,14,15,15,16,16,41,41,41,46,46],46,["自摸","和单张","场风北","自风东"]] 8
    # 门前清
    # 1 test_save = [[],[16,17,18,22,22,22,27,28,29,35,36,37,42,42],35,["点和"]] 8 
    # 2 test_save = [[],[11,12,13,14,15,16,34,34,24,25,26,27,28,29],13,["点和","和单张"]] 8
    # 3 test_save = [[],[12,13,14,15,16,17,19,19,19,22,23,24,28,28],23,["点和","和单张"]] 8
    # 4 test_save = [["G34"],[18,18,37,38,39,24,24,24,45,45,45],24,["点和"]] 10
    # 平和
    # 1 test_save = [["s25"],[12,15,18,33,36,39,21,24,27,28,28],15,["点和"]] 14
    # 2 test_save = [["s18"],[11,12,13,15,15,34,35,36,37,38,39],37,["自摸","和单张"]] 8
    # 四归一
    # 1 test_save = [["s32","k32"],[33,33,33,34,35,35,35,35],35,["点和"]] 30
    # 2 test_save = [[],[14,14,35,35,21,21,21,21,28,28,44,44,44,44],21,["自摸"]] 29
    # 3 test_save = [[],[13,15,16,16,16,16,17,19,22,25,28,31,34,37],15,["自摸","和单张"]] 20
    # 4 test_save = [["k19","s28","g31"],[17,18,19,34,34],17,["点和","和单张"]] 8
    # 双同刻
    # 1 test_save = [["k32","k38","k22"],[15,15,18,18,18],18,["自摸"]] 13
    # 2 test_save = [["k21","s24"],[11,11,11,14,14,23,24,25],24,["点和","和单张"]] 8
    # 3 test_save = [["k37","g17"],[11,12,13,31,32,33,36,36],36,["自摸","和单张"]] 8
    # 双暗刻
    # 1 test_save = [["k13"],[17,17,34,34,34,38,38,38,24,24,24],24,["点和"]] 12
    # 2 test_save = [["k19","G42"],[12,12,12,26,27,28,28,28],12,["自摸","场风西","自风南"]] 9
    # 3 test_save = [["g31"],[32,32,23,23,23,25,26,27,29,29,29],32,["点和","和单张"]] 8
    # 暗杠
    # 1 test_save = [["G47","s23"],[11,11,11,32,33,34,35,35],35,["点和"]] 8
    # 2 test_save = [["G39","s22"],[34,35,36,38,38,24,25,26],38,["点和","和单张"]] 8
    # 3 test_save = [["k29","G17"],[11,12,13,14,15,16,24,24],13,["自摸","和单张"]] 8
    # 断幺
    # 1 test_save = [["s25","s14","k27"],[12,12,32,33,34],34,["点和"]] 8
    # 2 test_save = [[],[15,16,16,17,17,18,32,33,34,35,36,37,28,28],16,["点和"]] 8
    # 3 test_save = [["s17","g26"],[13,14,15,22,22,23,24,25],24,["自摸","和单张"]] 8
    # 一般高
    # 1 test_save = [["s14","s14"],[32,32,23,24,25,26,27,28],32,["点和","和单张"]] 8
    # 2 test_save = [["s36","s32"],[31,32,33,34,34,35,36,37],34,["自摸"]] 29
    # 喜相逢
    # 1 test_save = [[],[12,13,14,14,15,16,34,35,36,22,23,24,28,28],36,["点和"]] 8
    # 2 test_save = [["k42"],[18,18,18,32,33,34,22,23,24,45,45],34,["自摸"]] 9
    # 连六
    # 1 test_save = [[],[31,32,33,34,35,36,38,38,23,24,25,26,27,28],33,["点和","和单张"]] 8
    # 2 test_save = [["k39","g31","s15"],[13,13,17,18,19],18,["自摸","和单张"]] 8
    # 老少副
    # 1 test_save = [["s28","s12"],[17,18,19,39,39,21,22,23],19,["点和"]] 9
    # 幺九刻
    # 1 test_save = [["k43","k31"],[14,14,29,29,29,42,42,42],42,["点和","场风东","自风北"]] 10
    # 2 test_save = [["g41","k45","k44"],[17,18,19,19,19],18,["自摸","场风北","自风西"]] 17
    # 明杠
    # 1 test_save = [["g42","s36"],[11,11,16,17,18,24,25,26],16,["点和","场风西","自风西"]] 8
    # 2 test_save = [["s25","g11"],[14,15,16,21,22,23,27,27],23,["自摸","和单张"]] 8
    # 缺一门 
    # 1 test_save = [["s24","s22"],[33,33,25,26,27,43,43,43],43,["点和","场风北","自风北"]] 18
    # 2 test_save = [[],[11,12,13,17,18,18,18,19,24,25,26,27,28,29],29,["点和"]] 8
    # 3 test_save = [["k46","k11"],[19,19,31,32,33,37,38,39],33,["点和","和单张"]] 10
    # 4 test_save = [[],[11,11,15,15,17,17,34,34,42,42,44,44,47,47],47,["自摸"]] 26
    # 无字
    # 1 test_save = [["s14"],[18,18,31,32,33,22,23,24,29,29,29],29,["点和"]] 8
    # 2 test_save = [["s33","s12","g28","k26"],[39,39],39,["点和","和单张"]] 8
    # 3 test_save = [[],[12,12,14,14,17,17,19,19,36,36,24,24,25,25],17,["自摸","和单张"]] 26
    # 边张
    # 1 test_save = [[],[11,12,13,13,16,19,32,35,38,21,24,27,43,43],13,["点和","和单张"]] 15
    # 2 test_save = [["s18","s35"],[15,16,17,21,22,23,47,47],23,["点和","和单张"]] 9
    # 3 test_save = [["k37"],[18,18,18,26,27,27,27,27,28,28,29],27,["自摸","和单张"]] 16
    # 嵌张
    # 1 test_save = [[],[12,15,18,17,18,19,31,31,31,34,37,23,26,29],18,["自摸","和单张"]] 19
    # 2 test_save = [["s15"],[33,34,35,22,23,24,24,25,26,41,41],23,["点和","和单张"]] 8
    # 3 test_save = [["s25","s22"],[17,18,19,34,35,35,35,36],35,["自摸","和单张"]] 13
    # 单吊将
    # 1 test_save = [["k15"],[11,14,17,33,36,39,39,39,22,25,28],39,["点和","和单张"]] 14
    # 2 test_save = [["s28","s22","k29"],[11,12,13,18,18],18,["自摸","和单张"]] 9
    # 自摸
    # 1 test_save = [["s33","s13"],[12,13,14,32,32,33,34,35],35,["自摸"]] 8
    # 花牌
    # 2 test_save = [["s33","s13"],[12,13,14,32,32,33,34,35],35,["自摸","花牌"]] 9
    # 明暗杠
    # 1 test_save = [["g44","G28"],[12,12,12,13,13,35,35,35],13,["点和","场风西","自风南"]] 28
    # 2 test_save = [["G21","g18"],[32,32,32,25,26,27,27,27],27,["自摸"]] 10
    # 3 test_save = [["g46","G43","s12"],[33,34,35,36,36],36,["点和"]] 9
    # 测试完毕




    # 按照新编MCR的番种定义顺序测试
    # 11m,12,13,14,15,16,17,18,19, # 万
    # 21p,22,23,24,25,26,27,28,29, # 饼
    # 31s,32,33,34,35,36,37,38,39, # 条
    # 41东,42南,43西,44北,
    # 45中,46白,47发 


    

    way_to_hepai = test_save[3]
    hepai_tiles = test_save[2]
    tiles_list = test_save[1]
    combination_list = test_save[0]

    # 开始测试
    test_check = Chinese_Hepai_Check(debug=True)
    time_start = time()
    result = test_check.hepai_check(tiles_list,combination_list,way_to_hepai,hepai_tiles)
    logger.debug("最终结果(返回最大的牌型):",result)
    time_end = time()
    logger.debug("测试用时：",time_end - time_start,"秒")
