# -*- coding: utf-8 -*-
"""Add blurbs, fix parents, copy catalog JSON to public."""
from __future__ import annotations

import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CAT = os.path.join(ROOT, "catalog")
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)

PARENT_FIX = {
    "zungjung": None,
    "jiandan": None,
    "vietnam": None,
    "ningbo-local": None,
    "fuzhou": None,
    "wenzhou": None,
    "shanghai": None,
    "hangzhou": None,
    "sichuan-nochi": None,
    "changsha": None,
    "dongbei": None,
    "beijing": None,
    "tianjin": None,
    "shanxi": None,
    "xian": None,
}

BLURBS = {
    "yezi": "宋明时候的纸牌戏。后来的马吊、默和都从这条线下来，还不是麻将。",
    "madiao": "大约四十张，以大击小。万、索、钱后来变成麻将的万、索、筒，玩法并没有留下来。",
    "mohe": "四人凑刻顺。乾隆《牧猪闲话》里写过：牌张从马吊改来，打法已经不是斗大。",
    "penghe": "默和加一倍、做成四副，就叫碰和。「碰」这个说法从这里来。",
    "tianjiu": "骨牌戏，按点数组合定输赢。麻将后来改成竹骨牌时借了牌具样子，打法不是牌九。",
    "proto-mahjong": "晚清宁波一带把纸牌戏做到竹骨牌上，当时叫中发、叉麻雀、撮麻雀。还没有统一的规则书。",
    "classical": "1910 年代前后印出来的规则书所记的打法：有番有副，可以吃碰杠。平台上的古典麻将按这些书来还原。",
    "drawing-mahjong": "沈一帆《绘图麻雀牌谱》（1914）。",
    "qingzhang": "从早期打法里废掉副、改出铳算法，变成粤港一带的清章／旧章。",
    "hongkong": "香港现在通行的清章。三番起糊，协会有现行总例。",
    "guangdong": "广东桌上的打法，底子是清章，再叠新章番种。各地细则不一样。",
    "jipinghu": "广东一线里偏鸡胡、平胡的打法，番种比清章新。",
    "tuidao": "推倒胡：和了就算，不算复杂番。MIL 有竞赛规则。",
    "hongkong-16": "香港也有人打的十六张。当地有时叫「台湾牌」，只是香港这么叫。",
    "japanese-classical": "1909 年名川彦作把牌带回日本后的早期日本打法，章法还接近当时的中国规矩。",
    "riichi-hochi": "1952 年 11 月 30 日至 12 月 5 日，天野大三在《报知新闻》把途中立直、振听写成连载规则。",
    "riichi": "天凤前身「半熟荘」2006 年 2 月 20 日公开测试，8 月 1 日正式，2007 年 3 月 1 日改名。平台立直按天凤／雀魂：赤宝、里宝、食断。",
    "japanese-3p": "日本麻将的三人打法，去掉一门牌。",
    "korean-3p": "韩国流行的三人麻将，从日本三人打法传过去，细节有本地改动。",
    "babcock": "1920 年 Babcock 红皮书。按当时中国打法简化，卖给美国人。",
    "nmjl": "1937 年美国全国麻将联盟成立。当时用花当百搭，还没有后来的专用 joker。年卡从这时就开始出。",
    "nmjl-joker": "1961 年起 NMJL 改用专用 joker，不再只靠花当百搭。",
    "nmjl-card": "1971 年稳住 8 花 8 joker。合法型和牌型每年换卡，2026 年卡仍在发售。",
    "wright-patterson": "美军俱乐部里留下来的美式打法，不用 joker。",
    "fuzhou": "福州把十三张加成十六张，并有开金。常被当成台湾十六张的来路之一。",
    "taiwan": "战后十六张在桌上定型，按台数、拉庄。平台按后来常见台数规则做了对局。",
    "shanghai": "上海现在的地方打法，已加花牌。",
    "ningbo-local": "当代宁波桌上的打法，加了财神、花牌。",
    "hangzhou": "杭州地方麻将。MIL 有推广竞赛规则。",
    "wenzhou": "温州地方麻将，常打十六张。MIL 有试点竞赛规则。",
    "sichuan": "约 2000 年后成都一带流行血战到底，并改成开局定缺。平台川麻是这一层。",
    "sichuan-huansanzhang": "川麻开局先换三张，是血战桌上常见的附加规则。",
    "xueliu": "血流成河：和了之后还可以再和，在血战上再改一档。",
    "chongqing": "重庆桌上的打法，近四川血战，细节按当地习惯。",
    "guizhou": "贵州地方麻将。MIL 有推广竞赛规则。",
    "changsha": "长沙麻将：去字牌，可以吃，要 258 做将。平台做了双鸟。",
    "hongzhong": "红中当癞子的打法，各地细则不同。",
    "beijing": "京麻用混儿，可以吃，也有点炮。",
    "tianjin": "津麻：不许吃、不点炮（只自摸／杠开），中下滚混儿。常见和型是混儿吊、捉伍儿、龙。",
    "wuhan": "武汉地方麻将。",
    "nanchang": "南昌地方麻将。",
    "dongbei": "辽宁摇宝、吉林下蛋、黑龙江夹胡、长春各打各的。",
    "dongbei-yaobao": "辽宁一带的摇宝／翻宝。",
    "dongbei-xiadan": "吉林一带的下蛋。蛋牌约定跟辽宁摇宝不是一套。",
    "dongbei-jiahu": "黑龙江一带的夹胡。听口规则跟摇宝、下蛋都不同。",
    "shanxi": "山西地方麻将。MIL 有推广竞赛规则。",
    "changchun": "长春桌上把吉林下蛋、夹胡听口和宝牌捆在一起。MIL 有推广竞赛规则。",
    "xian": "西安地方麻将。",
    "kunming": "昆明地方麻将，打法靠近川滇血战一线。",
    "singapore": "新加坡麻将，从清章一路传去，桌上常见动物牌。",
    "malaysia": "马来西亚麻将，从新加坡、清章一线再传，细则按当地。",
    "vietnam": "越南也有人打麻将。",
    "guobiao": "1998 年国家体育总局定的竞赛规则，8 番起和。",
    "ema": "欧洲麻将协会办赛时用的国标赛制，番种库跟国标同一套。",
    "mil-sichuan": "国际麻将联盟的四川麻将竞赛规则，在血战上写成赛场办法。",
    "mil-riichi": "国际麻将联盟的立直竞赛规则。",
    "qingque": "莫莫柴 2025 年写的规则。传统行牌，番种和分数另编。",
    "hongque": "Null 2025 年写的彩虹主题拉密类麻将。十四色九数各一张。",
    "guobiao-kobayashi": "2026 年小林改：取消 8 番起和与底分，点和×2，自摸番三。国标改版。",
    "guobiao-lanshi": "2026 年蓝十改：重写番种表，5 分起和，半全铳。国标改版，平台可以打。",
    "guobiao-kshen": "K神改：在国标上重写番种表并改授受。",
    "zungjung": "关兆豪大约 2000 年写的中庸麻雀。另起一套计分。",
    "shiyangjin": "十样锦。平台还没有做成对局，图书馆里有规则书。",
    "jiandan": "南瓜饼 2026 年写的规则。无起和，现行一人和即止。",
}


def main():
    path = os.path.join(CAT, "rules.json")
    data = json.load(open(path, encoding="utf-8"))
    missing = []
    for r in data["rules"]:
        slug = r["slug"]
        if slug in PARENT_FIX:
            r["parent"] = PARENT_FIX[slug]
        blurb = BLURBS.get(slug)
        if not blurb:
            missing.append(slug)
            continue
        r["blurb"] = blurb
        if slug == "guobiao-lanshi":
            r["library_key"] = "guobiao-lanshi"
    data["updated"] = "2026-08-14"
    json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(path, "a", encoding="utf-8").write("\n")

    os.makedirs(PUBLIC, exist_ok=True)
    for name in ("rules.json", "phylogeny.json", "areal.json"):
        src = os.path.join(CAT, name)
        dest = "catalog.json" if name == "rules.json" else name
        shutil.copy2(src, os.path.join(PUBLIC, dest))
    print("rules", len(data["rules"]), "missing blurbs", missing or "none")


if __name__ == "__main__":
    main()
