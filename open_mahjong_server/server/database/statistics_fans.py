"""完整子规则的番种显示目录；次数的字段与牌谱解析器保持一致。

已有统计字段继续沿用，中文字段不借用其他规则的译名。这个目录也生成
Web 和 Unity 的零次数条目，不能依赖某个玩家先达成番种才显示它。
"""
from functools import lru_cache


def _names(values):
    return {name: name for name in values}


SICHUAN_NAMES = "平和 杠 根 大对子 金钩钓 清一色 七对 杠上花 杠上炮 抢杠 海底".split()
HONGQUE_NAMES = """清顺 清刻 双同刻 双同顺 双同花 三连顺 一条龙 七归一 四连顺
三同刻 三同顺 三同花 九归一 彩虹 双虹会 清一色 全彩 光谱 双色 三色 全纯色
全半色 清一数 二数 三数 四数 全带幺 天和 门清 海底 碰碰和 平和 金龙 二金 三金
四同刻 四同顺 四同花""".split()
QIAOMA_NAMES = "门清 大吊车 混一色 清一色 碰碰和 天敲 杠开 抢杠".split()
HONGKONG_OLD_NAMES = """小花糊 大花糊 混一色 清一色 全番子 清么九 花么 门前清 十三么
九子连环 天糊 地糊 抢杠 无花 门风花 一台花 平糊 对对糊 小三元 大三元 小四喜
大四喜 十八罗汉 坎坎糊 三元牌 门风 圈风 自摸 海底捞月 杠上自摸 杠上杠自摸""".split()


def _hongkong_sixteen_names():
    local = """一般高 相逢 老少顺 双般高 双同顺 双老少顺 老少刻 两同刻 大三同刻
两连刻 双同刻 双连刻 双老少刻 小三同刻""".split()
    shared = """底 庄家 拉庄 自摸 门清 门摸 天叮 地叮 人叮
门叮 叮牌 即食 海底捞月 抢杠 三响 双响 尾牌连摸 杠上自摸 花上自摸 天糊 地糊 人糊
无字 无花 无字花 混一色 清一色 字一色 缺一门 缺五 断么九 清么九 混么九 全带么
混带么 满庭芳 混满庭芳 大七门齐 小七门齐 大五门齐 小五门齐 两台花 一台花 花 正花""".split()
    standard = """字牌 正字 正字（门风） 正字（圈风） 大三元 小三元 大四喜 小四喜 大三风 小三风 杠
对对糊 坎坎糊 平糊 无字花平糊 五同顺 四同顺 将眼 对碰 独独 假独 半求人 全求人
十三么九 十三扉十三么 十六不搭 十六扉不搭 不搭三相 不搭杂龙""".split()
    likugu = "嚦咕嚦咕 八对嚦咕 三元嚦咕 四喜嚦咕 三色同对".split()
    returns = ["八归", "十二归"]
    shared += [f"{n}只内" for n in (4, 7, 10)]
    standard += [f"{n}暗刻" for n in range(2, 6)]
    likugu += [f"{n}连对" for n in range(3, 9)]
    local += [f"{prefix}{n}连刻" for prefix, sizes in (("大", range(3, 6)),
              ("三色", range(3, 6)), ("小", range(3, 7)), ("小三色", range(3, 7))) for n in sizes]
    for prefix in ("明", "暗"):
        local += [prefix + name for name in ("三般高", "四般高", "清龙", "杂龙", "三相逢", "连环套", "金门大桥")]
        local += [f"{prefix}{suit}{n}步高" for suit in ("一色", "三色") for n in range(3, 6)]
        returns += [f"{prefix}四归{n}" for n in range(2, 5)]
    # 嚦咕两食只给嚦咕解释的计分加前缀，不能生成“嚦咕·三般高”等普通面子番。
    secondary = ["嚦咕·" + name for name in shared + likugu + returns if name not in ("底", "门清")]
    return local + shared + standard + likugu + returns + ["七抢一", "大鸡糊", "大鸭糊"] + secondary


@lru_cache(maxsize=None)
def fan_names(sub_rule):
    """返回字段 -> 显示名。未配置的规则不能冒用主规则番表。"""
    family = sub_rule.split("/")[0]
    if family in ("guobiao", "riichi", "qingque", "classical"):
        from importlib import import_module
        mapping = import_module(f".{family}.store_{family}", __package__).FAN_NAME_TO_FIELD
        return {field: name for name, field in mapping.items()}
    if sub_rule in ("sichuan/standard", "sichuan/doubles", "sichuan/doubles_duplicate"):
        return _names(SICHUAN_NAMES)
    if sub_rule in ("sichuan/xueliu_doubles", "sichuan/xueliu_doubles_duplicate"):
        from ..game_calculation.sichuan.xueliu_rules import XUELIU_FAN_VALUES
        return _names(XUELIU_FAN_VALUES)
    if sub_rule.startswith("changsha/"):
        from ..game_calculation.changsha.changsha_hepai_check import BIG_HU_NAMES, INITIAL_HU_NAMES
        names = ["小胡", *sorted(BIG_HU_NAMES), *INITIAL_HU_NAMES.values()]
        if sub_rule == "changsha/exchange_six":
            names += ["十三将", "无对十三将", "全幺九", "报听", "明牌", "闷杠"]
        return _names(names)
    if sub_rule == "zhongyong/standard":
        from ..game_calculation.zhongyong.scoring import FANS
        return _names([*(definition[0] for definition in FANS.values()), "鸡和"])
    if sub_rule == "taiwan/standard":
        from ..game_calculation.taiwan.fan import FAN_DEFINITIONS
        return _names(definition[0] for definition in FAN_DEFINITIONS.values())
    if sub_rule == "shanghai/qiaoma":
        return _names(QIAOMA_NAMES)
    if sub_rule == "hongkong/qingzhang":
        return _names(HONGKONG_OLD_NAMES)
    if sub_rule == "hongkong/new16":
        return _names(_hongkong_sixteen_names())
    if sub_rule == "hongzhong/mil2024":
        from ..game_calculation.hongzhong.scoring import FANS
        return _names(FANS)
    if sub_rule == "hongque/v1.6":
        return _names(HONGQUE_NAMES)
    raise ValueError(f"缺少子规则番种目录: {sub_rule}")
