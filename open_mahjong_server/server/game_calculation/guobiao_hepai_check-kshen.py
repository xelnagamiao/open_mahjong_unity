from typing import Dict, List, Tuple
from time import time
import logging

logger = logging.getLogger(__name__)

class PlayerTiles:
    def __init__(self, tiles_list, combination_list, complete_step):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step  # +3 +3 +3 +3 +2 = 14
        self.fan_list = []
        self.point_count_dict = {}  # 存储和牌得分
        self.fan_count_list = []  # 存储和牌文本

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
    quandan_set = {11,13,15,17,19,21,23,25,27,29,31,33,35,37,39} # 全单
    quanshuang_set = {12,14,16,18,22,24,26,28,32,34,36,38} # 全双

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
    repel_model_dict:Dict[str,list] ={
        "dasixi":["pengpenghe","quanfengke","menfengke"]+["yaojiuke"]*4,"dasanyuan":["yaojiuke"]*3, # 大四喜 大三元
        "lvyise":["hunyise"],"sigang":["pengpenghe","dandiaojiang"], # 绿一色 四杠子
        "jiulianbaodeng_dianhe":["qingyise","wuzi","yaojiuke","menqianqing"],"jiulianbaodeng_zimo":["qingyise","wuzi","buqiuren","yaojiuke"], # 九莲宝灯 点和/自摸
        "lianqidui_dianhe":["qiduizi","qingyise","wuzi","menqianqing"],"lianqidui_zimo":["qiduizi","qingyise","wuzi","buqiuren"], # 连七对 点和/自摸
        "shisanyao_dianhe":["hunyaojiu","wumenqi","menqianqing"],"shisanyao_zimo":["hunyaojiu","wumenqi","buqiuren"], # 十三幺 点和/自摸
        "qingyaojiu":["pengpenghe","quandaiyao","qingquandaiyao","santongke","shuangtongke","shuangtongke","wuzi"] + ["yaojiuke"]*4,"xiaosixi":["sanfengke"] + ["yaojiuke"]*3, # 清幺九 小四喜
        "xiaosanyuan":["shuangjianke"] + ["yaojiuke"]*2 ,"ziyise":["pengpenghe","quandaiyao"] + ["yaojiuke"]*4, # 小三元 字一色
        "sianke_dianhe":["pengpenghe","menqianqing"],"sianke_zimo":["pengpenghe","buqiuren"], # 四暗刻 点和/自摸
        "yiseshuanglonghui":["qingyise","pinghe","wuzi","yibangao","yibangao"], "yisesitongshun":["siguiyi"]*4, # 一色双龙会 一色四同顺
        "yisesijiegao":["pengpenghe"],"yisesibugao":["pinghe"],"sangang":[], # 一色四节高 一色四步高 三杠
        "hunyaojiu":["pengpenghe","quandaiyao","qingquandaiyao","santongke"] + ["yaojiuke"]*4, # 混幺九
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

        # 补齐K神规则新增役与调整役的键，避免回退检索结构时抛出异常
        "anke":[], "gang":[], "qingquandaiyao":[], "quandan":[], "quanshuang":[], "silianke":[],
        "jingtongshuangxitongke":["xixiangfeng","xixiangfeng","shuangtongke","shuangtongke","yibangao","yibangao"],
        "jingtongliangbangao":["yibangao","yibangao","pinghe"],
        "jingtongshuanglonghui":["pinghe","laoshaofu","laoshaofu","yibangao","xixiangfeng","xixiangfeng"]

        }
    # 存储番种的番数
    count_model_dict:Dict[str,int] = {
        "dasixi":160,"dasanyuan":64,"lvyise":0,"jiulianbaodeng":160,"sigang":160,
        "lianqidui":0,"shisanyao":64,
        "qingyaojiu":120,"xiaosixi":120,"xiaosanyuan":24,"ziyise":120,"sianke":64,"yiseshuanglonghui":0,
        "yisesitongshun":160,"yisesijiegao":0,"yisesibugao":48,"sangang":48,"hunyaojiu":32,
        "qiduizi":16,"qixingbukao":16,"quanshuangke":0,
        "qingyise":32,"yisesantongshun":48,"yisesanjiegao":24,"quanda":32,"quanzhong":32,"quanxiao":32,
        "qinglong":16,"sanseshuanglonghui":0,"yisesanbugao":16,"quandaiwu":16,"santongke":32,"sananke":16,
        "quanbukao":8,"zuhelong":0,"dayuwu":16,"xiaoyuwu":16,"sanfengke":32,
        "hualong":12,"tuibudao":0,"sansesantongshun":12,"sansesanjiegao":16,"wufanhe":0,"miaoshouhuichun":8,"haidilaoyue":8,
        "gangshangkaihua":8,"qiangganghe":8,"pengpenghe":12,"hunyise":16,"sansesanbugao":4,"wumenqi":4,"quanqiuren":0,"shuangangang":0,"shuangjianke":0,
        "quandaiyao":12,"buqiuren":0,"shuangminggang":0,"hejuezhang":0,"jianke":4,"quanfengke":0,"menfengke":4,"menqianqing":4,
        "pinghe":2,"siguiyi":4,"shuangtongke":4,"shuanganke":4,"angang":0,"duanyao":2,"yibangao":4,"xixiangfeng":2,
        "lianliu":0,"laoshaofu":0,"yaojiuke":0,"minggang":0,"queyimen":2,"wuzi":0,"bianzhang":2,
        "qianzhang":2,"dandiaojiang":2,"zimo":2,"huapai":1,"mingangang":0,

        "anke":2,
        "gang":4,
        "qingquandaiyao":24,
        "quandan":12,
        "quanshuang":24,
        "silianke":120,
        "jingtongshuangxitongke":16,
        "jingtongliangbangao":32,
        "jingtongshuanglonghui":32,
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
        "qiduizi":"七对",
        "qixingbukao":"七星不靠",
        "quanshuangke":"全双刻",
        "qingyise":"清一色",
        "yisesantongshun":"一色三同顺",
        "yisesanjiegao":"一色三连刻",
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
        "sansesanjiegao":"三色连刻",
        "wufanhe":"无番和",
        "miaoshouhuichun":"妙手回春",
        "haidilaoyue":"海底捞月",
        "gangshangkaihua":"杠上开花",
        "qiangganghe":"抢杠和",
        "pengpenghe":"碰碰和/对对和",
        "hunyise":"混一色",
        "sansesanbugao":"三色三步高",
        "wumenqi":"五门齐",
        "quanqiuren":"全求人",
        "shuangangang":"双暗杠",
        "shuangjianke":"双箭刻",
        "quandaiyao":"混全带幺",
        "buqiuren":"不求人",
        "shuangminggang":"双明杠",
        "hejuezhang":"和绝张",
        "jianke":"役牌/箭刻",
        "quanfengke":"圈风刻",
        "menfengke":"役牌/门风刻",
        "menqianqing":"门前清",
        "pinghe":"平和",
        "siguiyi":"四归一",
        "shuangtongke":"两同刻/双同刻",
        "shuanganke":"暗刻×2/双暗刻",
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
        "bianzhang":"独听·边张",
        "qianzhang":"独听·嵌张",
        "dandiaojiang":"独听·单钓",
        "zimo":"自摸",
        "huapai":"花牌",
        "mingangang":"明暗杠",

        "anke":"暗刻",
        "gang":"杠",
        "qingquandaiyao":"清·全带幺",
        "quandan":"全单",
        "quanshuang":"全双",
        "silianke":"四连刻",
        "jingtongshuangxitongke":"镜同·双喜同刻",
        "jingtongliangbangao":"镜同·两般高",
        "jingtongshuanglonghui":"镜同·双龙会"
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
            if not player_tiles_list:
                self.GS_check(player_tiles,player_tiles_list)  # 国士无双检查
            if not player_tiles_list:
                self.QBK_check(player_tiles,player_tiles_list)  # 全不靠检查
            if not player_tiles_list:
                self.QD_check(player_tiles,player_tiles_list)  # 七对检查
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
    
    def QD_check(self, player_tiles:PlayerTiles,player_tiles_list):
        temp_player_tiles = player_tiles.__deepcopy__(None)
        tile_counts = {}
        for tile_id in temp_player_tiles.hand_tiles:
            if tile_id in tile_counts:
                tile_counts[tile_id] += 1
            else:
                tile_counts[tile_id] = 1
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
        else: 
            if double_pair == False:
                temp_player_tiles.fan_list.append("lianqidui") # 连七对
                temp_player_tiles.complete_step = 14
                player_tiles_list.append(temp_player_tiles)
                return False
            
        temp_player_tiles.complete_step = 14
        temp_player_tiles.fan_list.append("qiduizi") # 七对
        player_tiles_list.append(temp_player_tiles)
        return False

    def QBK_check(self, player_tiles:PlayerTiles,player_tiles_list):
        hand_kind_set = len(set(player_tiles.hand_tiles))
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
                    elif zipai_count == 5: 
                        temp_player_tiles.fan_list.append("quanbukao") # 全不靠
                        temp_player_tiles.fan_list.append("zuhelong") # 组合龙
                        player_tiles_list.append(temp_player_tiles)
                    else:
                        temp_player_tiles.fan_list.append("quanbukao") # 全不靠
                        player_tiles_list.append(temp_player_tiles)
                    return False
                
        elif hand_kind_set >= 9:
            ZHL_case_list = [{11,14,17,22,25,28,33,36,39}, {11,14,17,32,35,38,23,26,29}, {21,24,27,12,15,18,33,36,39}, 
                            {21,24,27,32,35,38,13,16,19}, {31,34,37,22,25,28,13,16,19}, {31,34,37,12,15,18,23,26,29}]
            for index,case in enumerate(ZHL_case_list):
                ZHL_set = set()
                for i in player_tiles.hand_tiles:
                    if i in case:
                        ZHL_set.add(i)
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
        if player_tiles.complete_step == 14:
            check_done_list.append(player_tiles)
            return
        elif player_tiles.complete_step == 0:
            if not self.normal_check_block(player_tiles):
                return

        all_list = self.normal_check_traverse_quetou(player_tiles)
        end_list = []
        self.debug_print("所有雀头可能",[i.hand_tiles for i in all_list])
        count_count = 0
        while all_list:
            count_count += 1
            temp_list = all_list.pop()
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
        
        if all(i in self.quandan_set for i in hand_tiles_list):
            player_tiles.fan_list.append("quandan") # 全单

        if all(i in self.quanshuang_set for i in hand_tiles_list):
            player_tiles.fan_list.append("quanshuang") # 全双

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
        if all(i in self.quandaiwu_set for i in player_tiles.combination_list):
            player_tiles.fan_list.append("quandaiwu") # 全带五

        if all(i in self.quandaiyao_set for i in player_tiles.combination_list):
            has_zipai = any(i[1:].isdigit() and int(i[1:]) in self.zipai_set for i in player_tiles.combination_list)
            if has_zipai:
                player_tiles.fan_list.append("quandaiyao") # 混全带幺
            else:
                player_tiles.fan_list.append("qingquandaiyao") # 清·全带幺

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
        if ("z" in combination_str and combination_str.count("s") + combination_str.count("S") == 1) or (combination_str.count("s") + combination_str.count("S") == 4):
            if all(i <= 40 for i in hand_tiles_list):
                player_tiles.fan_list.append("pinghe") # 平和
        
        gang_count = combination_str.count("G") + combination_str.count("g")
        if gang_count == 4:
            player_tiles.fan_list.append("sigang") # 四杠
        elif gang_count == 3:
            player_tiles.fan_list.append("sangang") # 三杠
        elif gang_count == 2:
            player_tiles.fan_list.append("gang") # 杠
            player_tiles.fan_list.append("gang") # 杠
        elif gang_count == 1:
            player_tiles.fan_list.append("gang") # 杠

        concealed_triplet_like_count = combination_str.count("G") + combination_str.count("K")
        real_anke_count = combination_str.count("K")

        if concealed_triplet_like_count == 4:
            player_tiles.fan_list.append("sianke") # 四暗刻
        elif concealed_triplet_like_count == 3:
            player_tiles.fan_list.append("sananke") # 三暗刻
        elif concealed_triplet_like_count == 2:
            player_tiles.fan_list.append("shuanganke") # 暗刻×2/双暗刻
        elif real_anke_count == 1:
            player_tiles.fan_list.append("anke") # 暗刻

        if combination_str.count("G") + combination_str.count("g") + combination_str.count("K") + combination_str.count("k") == 4:
            player_tiles.fan_list.append("pengpenghe") # 碰碰和

    def get_meld_models(self, combination_str):
        melds = []
        quetou = None
        index = 0
        while index < len(combination_str):
            tile_type = combination_str[index]
            if tile_type in {"s", "S", "k", "K", "g", "G", "q"}:
                tile = int(combination_str[index + 1:index + 3])
                suit = tile // 10
                rank = tile % 10
                if tile_type in {"s", "S"}:
                    melds.append(("s", suit, rank))
                elif tile_type in {"k", "K", "g", "G"}:
                    melds.append(("k", suit, rank))
                elif tile_type == "q":
                    quetou = (suit, rank)
                index += 3
            else:
                index += 1
        return melds, quetou

    def check_jingtong_shuangxi_tongke(self, combination_str):
        melds, _ = self.get_meld_models(combination_str)
        melds = [meld for meld in melds if meld[0] in {"s", "k"} and meld[1] in {1, 2, 3}]

        if len(melds) != 4:
            return False

        suits = {meld[1] for meld in melds}
        if len(suits) != 2:
            return False

        groups = {}
        for meld_type, suit, rank in melds:
            key = (meld_type, rank)
            groups.setdefault(key, []).append(suit)

        if len(groups) != 2:
            return False

        for suits_list in groups.values():
            if len(suits_list) != 2 or len(set(suits_list)) != 2:
                return False

        return True

    def check_jingtong_shuanglonghui(self, combination_str):
        melds, quetou = self.get_meld_models(combination_str)
        if quetou is None or quetou[1] != 5 or quetou[0] not in {1, 2, 3}:
            return False
        suit_to_starts = {1: set(), 2: set(), 3: set()}
        for meld_type, suit, rank in melds:
            if meld_type == "s" and suit in suit_to_starts:
                suit_to_starts[suit].add(rank)
        long_suits = 0
        for starts in suit_to_starts.values():
            # 关键修复：因为当前脚本顺子用中张标记（123存为2，789存为8），这里必须改判 2 和 8
            if 2 in starts and 8 in starts:
                long_suits += 1
        return long_suits >= 2

    def check_jingtong_liangbangao(self, combination_str):
        melds, _ = self.get_meld_models(combination_str)
        shunzi_keys = []
        for meld_type, suit, rank in melds:
            if meld_type == "s":
                shunzi_keys.append((suit, rank))
        pair_count = 0
        counted = set()
        for key in shunzi_keys:
            if key not in counted and shunzi_keys.count(key) >= 2:
                counted.add(key)
                pair_count += 1
        return pair_count >= 2

    def check_silianke(self, combination_str):
        melds, _ = self.get_meld_models(combination_str)
        suit_to_ranks = {}

        for meld_type, suit, rank in melds:
            if meld_type == "k" and suit in {1, 2, 3}:
                suit_to_ranks.setdefault(suit, []).append(rank)

        for ranks in suit_to_ranks.values():
            ranks = sorted(set(ranks))
            for start_index in range(len(ranks) - 3):
                window = ranks[start_index:start_index + 4]
                if window[0] + 1 == window[1] and window[1] + 1 == window[2] and window[2] + 1 == window[3]:
                    return True

        return False

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
        
        if self.check_jingtong_shuangxi_tongke(combination_str):
            player_tiles.fan_list.append("jingtongshuangxitongke") # 镜同·双喜同刻

        if self.check_jingtong_liangbangao(combination_str):
            player_tiles.fan_list.append("jingtongliangbangao") # 镜同·两般高

        if self.check_jingtong_shuanglonghui(combination_str):
            player_tiles.fan_list.append("jingtongshuanglonghui") # 镜同·双龙会

        if self.check_silianke(combination_str):
            player_tiles.fan_list.append("silianke") # 四连刻
        
        if len(save_dazi_sign) >= 2:
            sign_pointer = int(save_dazi_sign[0])
            sign_count = 1
            for sign in save_dazi_sign:
                if int(sign) == sign_pointer + 1:
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

            sanseshuanglonghui_list = [{"12","18","22","28","q35"},{"12","18","32","38","q25"},{"32","38","22","28","q15"}]
            for set_item in sanseshuanglonghui_list:
                shunzi_in_set = [i for i in set_item if not i.startswith("q")]
                quetou_in_set = [i for i in set_item if i.startswith("q")]
                if all(i in save_dazi_sign for i in shunzi_in_set):
                    if quetou_in_set and f"q{save_quetou_sign[0]}" in quetou_in_set:
                        player_tiles.fan_list.append("sanseshuanglonghui") # 三色双龙会
                        break

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

            suit_list = [wan_list,bing_list,tiao_list]
            for rank_list in suit_list:
                if len(rank_list) >= 3:
                    if any(i == "2" for i in rank_list):
                        if any(i == "5" for i in rank_list):
                            if any(i == "8" for i in rank_list):
                                player_tiles.fan_list.append("qinglong") # 清龙
                                break
            
            hualong_form_list = [["2","5","8"],["2","8","5"],["5","2","8"],["5","8","2"],["8","2","5"],["8","5","2"]]
            for form in hualong_form_list:
                if form[0] in wan_list:
                    if form[1] in bing_list:
                        if form[2] in tiao_list:
                            player_tiles.fan_list.append("hualong") # 花龙
                            break

            order_kind_list = [0,1,2]
            counted_pointer_list = []

            for order in order_kind_list:
                if order == 0:
                    for i in suit_list[0]:
                        if i in suit_list[1]:
                            if i in suit_list[2]:
                                player_tiles.fan_list.append("sansesantongshun") # 三色三同顺
                                break
                    for i in suit_list[0]:
                        i = int(i)
                        self.debug_print(i)
                        if str(i+1) in suit_list[1]:
                            if str(i+2) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i-1) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                        if str(i-1) in suit_list[1]:
                            if str(i-2) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i+1) in suit_list[2]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                        if str(i+1) in suit_list[2]:
                            if str(i+2) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i-1) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                        if str(i-1) in suit_list[2]:
                            if str(i-2) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break
                            if str(i+1) in suit_list[1]:
                                player_tiles.fan_list.append("sansesanbugao")
                                break

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

            for lst in [wan_list,bing_list,tiao_list]:
                if len(lst) >= 2:
                    for i in lst:
                        i = int(i)
                        if str(i + 3) in lst:
                            player_tiles.fan_list.append("lianliu") # 连六
                min_count = min(lst.count("2"),lst.count("8"))
                if min_count != 0:
                    if min_count == 2 and "qingyise" in player_tiles.fan_list and int(save_quetou_sign[0]) % 10 == 5:
                        player_tiles.fan_list.append("yiseshuanglonghui") # 一色双龙会
                    else:
                        for i in range(min_count):
                            player_tiles.fan_list.append("laoshaofu") # 老少副

        if len(save_kezi_sign) >= 2:
            suit_to_ranks = {1: [], 2: [], 3: []}
            for sign in save_kezi_sign:
                suit = int(sign[0])
                rank = int(sign[1])
                if suit in suit_to_ranks:
                    suit_to_ranks[suit].append(rank)

            has_yisesanjiegao = False
            for ranks in suit_to_ranks.values():
                ranks = sorted(set(ranks))
                for start_index in range(len(ranks) - 2):
                    window = ranks[start_index:start_index + 3]
                    if window[0] + 1 == window[1] and window[1] + 1 == window[2]:
                        has_yisesanjiegao = True
                        break
                if has_yisesanjiegao:
                    break

            if has_yisesanjiegao and "silianke" not in player_tiles.fan_list:
                player_tiles.fan_list.append("yisesanjiegao") # 一色三连刻

            
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
        for i in way_to_hepai:
            match i:
                case "和单张":
                    if get_tile % 10 == 3:
                        if f"S{get_tile-1}" in player_tiles.combination_list:
                            player_tiles.fan_list.append("bianzhang") # 边张
                            continue
                    elif get_tile % 10 == 7:
                        if f"S{get_tile+1}" in player_tiles.combination_list:
                            player_tiles.fan_list.append("bianzhang") # 边张
                            continue
                    if f"S{get_tile}" in player_tiles.combination_list:
                        player_tiles.fan_list.append("qianzhang") # 嵌张
                        continue
                    if f"q{get_tile}" in player_tiles.combination_list:
                        player_tiles.fan_list.append("dandiaojiang") # 单吊将
                        continue
                case "妙手回春":
                    player_tiles.fan_list.append("miaoshouhuichun") # 妙手回春
                case "杠上开花":
                    player_tiles.fan_list.append("gangshangkaihua") # 杠上开花
                case "抢杠和":
                    player_tiles.fan_list.append("qiangganghe") # 抢杠和
                case "和绝张":
                    player_tiles.fan_list.append("hejuezhang") # 和绝张
                case "花牌":
                    if any(idx in ["妙手回春", "自摸", "杠上开花"] for idx in way_to_hepai):
                        player_tiles.fan_list.append("huapai") # 花牌
                case "海底捞月":
                    player_tiles.fan_list.append("haidilaoyue") # 海底捞月
                case "点和":
                    self.debug_print(player_tiles.combination_list)
                    if combination_str != "" and all(idx not in ["S","K","G","z"] for idx in combination_str) and "和单张" in way_to_hepai:
                        player_tiles.fan_list.append("quanqiuren") # 全求人
                    elif combination_str.count("s") + combination_str.count("k") + combination_str.count("g") == 0:
                        player_tiles.fan_list.append("menqianqing") # 门前清
                    elif "暗转明" in way_to_hepai:
                        if combination_str.count("s") + combination_str.count("k") + combination_str.count("g") == 1:
                            player_tiles.fan_list.append("menqianqing") # 门前清
                case "自摸":
                    special_zimo_fans = {"qiduizi", "jiulianbaodeng", "lianqidui", "shisanyao", "sianke", "qixingbukao", "quanbukao"}
                    if combination_str.count("s") + combination_str.count("k") + combination_str.count("g") == 0 and not any(fan in special_zimo_fans for fan in player_tiles.fan_list):
                        player_tiles.fan_list.append("menqianqing") # 门前清
                    # 核心改动：如果勾选了四大偶发/依存和牌方式，则不加计普通自摸的2分

                    if any(idx in {"妙手回春", "杠上开花", "海底捞月", "抢杠和"} for idx in way_to_hepai):
                        continue
                    player_tiles.fan_list.append("zimo")

    def fan_count_output(self, player_tiles:PlayerTiles, combination_str, zimo_or_not, way_to_hepai):
        # ==========================================================
        # 步骤 1：先根据顺子的组合原则对牌型进行处理 (移植自原版国标，优先折叠剪枝)
        # ==========================================================
        repeatable_fan_list = []
        origin_fan_list = []
        for i in player_tiles.fan_list:
            if i in {"yibangao", "xixiangfeng", "lianliu", "laoshaofu"}:
                repeatable_fan_list.append(i)
            else:
                origin_fan_list.append(i)

        if len(repeatable_fan_list) > 0:
            # A. 四顺子番种成立时，不得加计任何双顺子番种
            if any(i in player_tiles.fan_list for i in {"yiseshuanglonghui", "yisesitongshun", "yisesibugao", "sanseshuanglonghui"}):
                player_tiles.fan_list = origin_fan_list
            # B. 三顺子番种成立时，剥离冲突的双顺子番
            elif "yisesantongshun" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            elif "sansesanbugao" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            elif "sansesantongshun" in player_tiles.fan_list:
                for i in repeatable_fan_list:
                    if i in ["yibangao", "lianliu", "laoshaofu"]: # 剔除喜相逢
                        origin_fan_list.append(i)
                        break
            elif "yisesanbugao" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            elif "qinglong" in player_tiles.fan_list:
                for i in repeatable_fan_list:
                    if i in ["yibangao", "xixiangfeng"]: # 剔除连六、老少副
                        origin_fan_list.append(i)
                        break
            elif "hualong" in player_tiles.fan_list:
                origin_fan_list.append(repeatable_fan_list[0])
            # C. 兜底判定
            else:
                max_fan_count = combination_str.count("s") + combination_str.count("S") - 1
                if len(repeatable_fan_list) <= max_fan_count:
                    origin_fan_list = origin_fan_list + repeatable_fan_list
                else:
                    for i in range(max_fan_count):
                        origin_fan_list.append(repeatable_fan_list[i])
        else:
            origin_fan_list = player_tiles.fan_list

        player_tiles.fan_list = origin_fan_list

        # ==========================================================
        # 步骤 2：在确立了最终存活番型后，统一跑排斥链条移除冲突番
        # ==========================================================
        need_to_remove = []
        max_yaojiuke_count = 0
        
        # 安全查表逻辑，调整优先级：优先查精准特例后缀，后回退本体 Key
        for fan in player_tiles.fan_list:
            if self.count_model_dict.get(fan, 0) <= 0:
                continue
                
            suffix = "_zimo" if zimo_or_not else "_dianhe"
            suffix_key = f"{fan}{suffix}"
            
            # 优先查特例（如 qixingbukao_dianhe）
            if suffix_key in self.repel_model_dict:
                repel_list = self.repel_model_dict[suffix_key]
            # 特例不存在，再退回查本体（如 qixingbukao），若也无则兜底空列表
            else:
                repel_list = self.repel_model_dict.get(fan, [])
                
            for i in repel_list:
                if i != "yaojiuke":
                    need_to_remove.append(i)
                elif i == "yaojiuke":
                    yaojiuke_count = repel_list.count("yaojiuke")
                    if yaojiuke_count > max_yaojiuke_count:
                        max_yaojiuke_count = yaojiuke_count
        
        if "sanfengke" in player_tiles.fan_list:
            need_to_remove.append("yaojiuke")
            need_to_remove.append("yaojiuke")
            need_to_remove.append("yaojiuke")
        else:
            if "quanfengke" in player_tiles.fan_list:
                need_to_remove.append("yaojiuke")
            if "menfengke" in player_tiles.fan_list:
                if "门风圈风相同" not in way_to_hepai:
                    need_to_remove.append("yaojiuke")

        self.debug_print("全部被添加的番种", player_tiles.fan_list)
        player_tiles.fan_list.sort(key=lambda x: self.count_model_dict.get(x, 0), reverse=True)

        self.debug_print("需要被阻挡的番种", need_to_remove)

        for i in need_to_remove:
            if i in player_tiles.fan_list:
                player_tiles.fan_list.remove(i)
        self.debug_print("需要移除的幺九刻数量", max_yaojiuke_count)
        for i in range(max_yaojiuke_count):
            if "yaojiuke" in player_tiles.fan_list:
                player_tiles.fan_list.remove("yaojiuke")

        # ==========================================================
        # 步骤 3：过滤 0 分番、处理荣和花牌限制
        # ==========================================================
        # 过滤 0 分番：不显示、不计分
        player_tiles.fan_list = [
            fan for fan in player_tiles.fan_list
            if self.count_model_dict.get(fan, 0) > 0
        ]

        # 荣和不计花牌；自摸花牌作为添头
        if not zimo_or_not:
            player_tiles.fan_list = [fan for fan in player_tiles.fan_list if fan != "huapai"]

        final_repel_map = {
            "yisesibugao": ["pinghe"],

            # 镜同类：大番成立后，排除内部低番
            "jingtongshuangxitongke": ["xixiangfeng", "xixiangfeng", "shuangtongke", "shuangtongke", "yibangao", "yibangao"],
            "jingtongliangbangao": ["yibangao", "yibangao", "xixiangfeng", "xixiangfeng"],
            "jingtongshuanglonghui": [
                "jingtongshuangxitongke",
                "pinghe",
                "laoshaofu", "laoshaofu",
                "yibangao", "yibangao",
                "xixiangfeng", "xixiangfeng",
                "lianliu", "lianliu",
            ],

            # 幺九类：排除内部刻子/碰碰/带幺
            "hunyaojiu": [
                "pengpenghe",
                "quandaiyao",
                "qingquandaiyao",
                "santongke",
                "shuangtongke", "shuangtongke",
                "jingtongshuangxitongke",
                "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke",
            ],
            "qingyaojiu": [
                "pengpenghe",
                "quandaiyao",
                "qingquandaiyao",
                "santongke",
                "shuangtongke", "shuangtongke",
                "jingtongshuangxitongke",
                "wuzi",
                "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke",
            ],


            # 四连刻：排除内部碰碰和/三连刻/刻子类
            "silianke": [
                "pengpenghe",
                "yisesanjiegao",
                "sananke",
                "shuanganke",
                "anke",
                "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke",
            ],

            # 全双：全双本身包含断幺
            "quanshuang": ["duanyao"],
        }


        for big_fan, small_fans in final_repel_map.items():
            if big_fan in player_tiles.fan_list:
                for small_fan in small_fans:
                    if small_fan in player_tiles.fan_list:
                        player_tiles.fan_list.remove(small_fan)


        self.debug_print("最终番种", player_tiles.fan_list)
        # 结算得分和展示文本
        fuji_set = {
            "siguiyi","shuangtongke","yibangao","xixiangfeng",
            "lianliu","yaojiuke","huapai","gang"
        }
        fuji_list = [
            "siguiyi","shuangtongke","yibangao","xixiangfeng",
            "lianliu","yaojiuke","huapai","gang"
        ]

        hand_fan_count = 0
        flower_fan_count = 0
        temp_fan_count_list = []

        for fan in player_tiles.fan_list:
            if fan in fuji_set:
                continue
            fan_value = self.count_model_dict.get(fan, 0)
            hand_fan_count += fan_value
            self.debug_print(f"添加番数{fan},{fan_value}")
            temp_fan_count_list.append(f"{self.eng_to_chinese_dict[fan]}")

        for fan in fuji_list:
            fan_count = player_tiles.fan_list.count(fan)
            if fan_count <= 0:
                continue
            fan_value = fan_count * self.count_model_dict.get(fan, 0)
            if fan == "huapai":
                flower_fan_count += fan_value
            else:
                hand_fan_count += fan_value
            self.debug_print(f"添加番数{fan},{fan_value}")
            temp_fan_count_list.append(f"{self.eng_to_chinese_dict[fan]}*{fan_count}")

        # 复合最高 100；独立 100+ 役按实际；花牌为添头，可继续加
        big_fans = [
            self.count_model_dict.get(fan, 0)
            for fan in player_tiles.fan_list
            if fan != "huapai" and self.count_model_dict.get(fan, 0) > 100
        ]

        if big_fans:
            fan_count = max(big_fans) + flower_fan_count
        else:
            fan_count = min(hand_fan_count, 100) + flower_fan_count

        player_tiles.fan_count_list = temp_fan_count_list
        self.debug_print("和牌文本",player_tiles.fan_count_list)
        self.debug_print("和牌得分",fan_count)
        return fan_count,player_tiles.fan_count_list

    def filter_zero_value_fans(self, fan_score: int, fan_count_list: List[str]) -> Tuple[int, List[str]]:
        return fan_score, fan_count_list

    def fan_count(self, player_tiles: PlayerTiles,get_tile,way_to_hepai):
        zimo_or_not = False
        if any(i in ["妙手回春","自摸","杠上开花"] for i in way_to_hepai):
            zimo_or_not = True
        if zimo_or_not == False:
            for i in player_tiles.combination_list:
                if i == f"G{get_tile}":
                    if any(idx in player_tiles.combination_list for idx in [f"S{get_tile}",f"S{get_tile+1}",f"S{get_tile-1}"]):
                        pass
                    else:
                        player_tiles.combination_list.remove(i)
                        player_tiles.combination_list.append(f"g{i[1]}{i[2]}")
                        way_to_hepai.append("暗转明")
                        break
                elif i == f"K{get_tile}":
                    if any(idx in player_tiles.combination_list for idx in [f"S{get_tile}",f"S{get_tile+1}",f"S{get_tile-1}"]):
                        pass
                    else:
                        player_tiles.combination_list.remove(i)
                        player_tiles.combination_list.append(f"k{i[1]}{i[2]}")
                        way_to_hepai.append("暗转明")
                        break
        
        hand_tiles_list = []
        combination_str = ""
        if any(i in player_tiles.fan_list for i in ["qiduizi","lianqidui"]):
            hand_tiles_list = player_tiles.hand_tiles
        elif any(i in player_tiles.fan_list for i in ["quanbukao","qixingbukao"]):
            hand_tiles_list = []
        else:
            for i in player_tiles.combination_list:
                if i in self.combination_to_tiles_dict:
                    hand_tiles_list.extend(self.combination_to_tiles_dict[i])
                hand_tiles_list.sort()
        for i in player_tiles.combination_list:
            combination_str += i
        self.debug_print("组合映射：",combination_str)
        self.debug_print("手牌映射：",hand_tiles_list)

        self.fan_count_hand_check(player_tiles,hand_tiles_list,get_tile)
        self.fan_count_combination_check(player_tiles)
        self.fan_count_combination_str_check(player_tiles,combination_str,hand_tiles_list)
        self.fan_count_combination_sign_check(player_tiles,combination_str,way_to_hepai)
        self.fan_count_hepai_relationship_check(player_tiles,combination_str,get_tile,way_to_hepai)
            
        self.debug_print("现在存在的组合",player_tiles.combination_list)
        result = self.fan_count_output(player_tiles, combination_str, zimo_or_not, way_to_hepai)
        return result 

    def hepai_decompose(self, hand_list: list, tiles_combination: list, way_to_hepai: list, get_tile: int) -> list:
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

        results.sort(key=lambda x: x["score"], reverse=True)
        return results


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

    test_save = [["s37"],[11,14,17,23,26,29,32,35,38,39,39],39,[]]

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