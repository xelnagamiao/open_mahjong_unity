# -*- coding: utf-8 -*-
"""Tag sources onto catalog rules, fold drawing-mahjong into classical, copy files."""
from __future__ import annotations

import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CAT = os.path.join(ROOT, "catalog")
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)
BOOKS = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rulebooks")
)

PHY_RULES = {
    "zj-classical": ["classical"],
    "zj-oldstyle": ["qingzhang", "hongkong"],
    "zj-newstyle": ["guangdong", "hongkong"],
    "zj-jp-classical": ["japanese-classical"],
    "zj-jp-modern": ["riichi"],
    "wiki-zh-origin": ["proto-mahjong"],
    "wiki-zh-play": ["proto-mahjong", "classical"],
    "wiki-zh-legend": ["proto-mahjong"],
    "cnnb-2018": ["proto-mahjong"],
    "paper-2018": ["proto-mahjong"],
    "mahjongtileset-1": ["proto-mahjong"],
    "mahjongtileset-2": ["proto-mahjong"],
    "wilkinson-1890": ["proto-mahjong"],
    "karuta-nagawa": ["japanese-classical"],
    "wiki-riichi": ["riichi"],
    "heinz-opb": ["babcock", "nmjl"],
    "heinz-book": ["babcock", "nmjl"],
    "mcr-1998": ["guobiao"],
    "wiki-16": ["fuzhou", "taiwan", "hongkong-16"],
    "wiki-tw": ["taiwan"],
    "cq-2012": ["chongqing", "sichuan"],
    "arxiv-2013": ["sichuan", "taiwan", "hongkong"],
    "wiki-en-sg": ["singapore"],
    "nmjl-joker": ["nmjl"],
    "wp-mahj": ["wright-patterson"],
    "ema-hist": ["ema"],
    "wiki-3p": ["japanese-3p"],
    "wiki-ningbo-local": ["ningbo-local"],
    "sloper-faq": ["proto-mahjong", "classical"],
    "drawing-local": ["classical"],
    "qingbai-cha-maque": ["proto-mahjong"],
    "qingbai-penghe": ["penghe"],
    "hu-shi-1927": ["proto-mahjong"],
    "zhang-deyi-1889": ["proto-mahjong"],
    "fan-zengxiang-1906": ["proto-mahjong"],
    "shenbao-1884-terms": ["proto-mahjong"],
    "shenbao-1885-cixi": ["proto-mahjong"],
    "shenbao-1887-concession": ["proto-mahjong", "penghe"],
    "culin-1895": ["proto-mahjong", "tianjiu"],
    "culin-1924": ["proto-mahjong"],
    "shinbara-1952": ["classical"],
    "ithinc-2009": ["classical"],
    "chen-ailu-1925": ["proto-mahjong"],
    "lian-yatang-1925": ["proto-mahjong"],
    "greene-2015": ["proto-mahjong"],
    "hochi-seesaawiki": ["riichi"],
    "chengdu-ifeng": ["sichuan"],
    "cctv-2009": ["proto-mahjong"],
    "youtube-chip": ["proto-mahjong"],
    "tianyige-nbwb-2017": ["proto-mahjong"],
    "atawmj-origin": ["proto-mahjong"],
    "jin-xueshi": ["mohe", "penghe", "madiao"],
    "chinanews-2009-xuezhan": ["sichuan"],
}

STUDIES_RULES = {
    "wiki-yezi": ["yezi"],
    "wiki-madiao": ["madiao"],
    "wiki-en-cards": ["yezi", "madiao"],
    "tang-guoshibu": ["yezi"],
    "jin-xueshi": ["mohe", "penghe", "madiao"],
    "zhangdai-shuihu": ["madiao"],
    "qingbai-087": ["proto-mahjong", "penghe", "madiao"],
    "hu-shi-1927": ["proto-mahjong"],
    "wilkinson-1895": ["madiao", "proto-mahjong"],
    "wilkinson-wayback": ["madiao", "proto-mahjong"],
    "culin-korean-games": ["madiao"],
    "culin-1895": ["proto-mahjong", "tianjiu"],
    "culin-1924": ["proto-mahjong"],
    "tileset-1": ["proto-mahjong"],
    "tileset-2": ["proto-mahjong"],
    "tileset-flowers": ["proto-mahjong"],
    "tileset-terms": ["proto-mahjong"],
    "chen-xiyuan-2009": ["madiao", "proto-mahjong"],
    "cctv-yezi-madiao": ["yezi", "madiao", "proto-mahjong"],
    "shen-yifan-1914": ["classical"],
    "wiki-maque-pai": ["classical"],
    "shinbara-1952": ["classical"],
    "zj-classical": ["classical"],
    "wiki-mcr": ["guobiao"],
    "mcr-1998": ["guobiao"],
    "worldcat-mcr-1998": ["guobiao"],
    "gmw-1998": ["guobiao"],
    "sina-2008": ["guobiao"],
    "chinaso-dama": ["beijing", "hongzhong"],
    "baike-jingji": ["guobiao"],
    "jiankang-1998": ["guobiao"],
    "southcn-2016": ["guangdong"],
    "shengqi-bio": ["guobiao"],
    "majiang-yundong": ["guobiao"],
    "majiang-yundong-jendow": ["guobiao"],
    "majiangxue-douban": ["guobiao"],
    "majiangxue-wiki": ["guobiao"],
    "shengqi-2012": ["guobiao"],
    "du-weizhong-2006": ["guobiao"],
    "wmo-2006": ["guobiao"],
    "xinmin-yuguangyuan": ["guobiao"],
    "rmwz-2014": ["guobiao"],
    "wiki-en-mcr": ["guobiao"],
    "greene-2015": ["proto-mahjong"],
    "heinz-site": ["babcock", "nmjl"],
    "heinz-douban": ["babcock", "nmjl"],
    "heinz-opb": ["babcock", "nmjl"],
    "zhao-nblib": ["proto-mahjong"],
    "zhao-paper-2018": ["proto-mahjong"],
    "wiki-zh-mahjong": ["proto-mahjong"],
    "ouyang-guitianlu": ["yezi"],
    "su-e-duyang": ["yezi"],
    "paijing-13": ["madiao"],
    "fengmenglong-index": ["madiao"],
    "madiao-jiaoli-blog": ["madiao"],
    "parlett-leaf": ["yezi"],
    "lo-game-of-leaves": ["yezi"],
    "fan-zengxiang-1906": ["proto-mahjong"],
    "cinii-boshi": ["classical"],
    "japan-karuta-haibara": ["classical", "proto-mahjong"],
    "lsqn-jiejin": ["proto-mahjong"],
    "lsqn-jiejin-2": ["proto-mahjong"],
    "wmo-intro": ["guobiao"],
    "majiangxue-dushu": ["guobiao"],
    "youyi-worldcat": ["guobiao"],
    "zhangpusheng-1987": ["guobiao"],
    "lixueyan-2009": ["guobiao"],
}

HK_RULES = {
    "hkma-rules-hub": ["hongkong", "hongkong-16"],
    "hkma-16tile-zh-doc": ["hongkong-16"],
    "hkma-16tile-en-doc": ["hongkong-16"],
    "hkma-16tile-pdf-ref": ["hongkong-16"],
    "wiki-hk-16tile": ["hongkong-16"],
    "zj-new-style": ["guangdong", "hongkong"],
    "hkma-hk-zh-doc": ["hongkong", "qingzhang"],
    "hkma-hk-en-doc": ["hongkong", "qingzhang"],
    "wiki-hk-mahjong": ["hongkong", "qingzhang"],
    "wiki-hk-winning-list": ["hongkong"],
    "aimj-hk-pdf": ["hongkong"],
}

TAG_TO_RULES = {
    "cards-to-mahjong": ["proto-mahjong"],
    "early-manual": ["classical"],
    "mcr-1998": ["guobiao"],
    "mcr": ["guobiao"],
    "oldstyle": ["qingzhang", "hongkong"],
}


def apply_rules(path, mapping, default_from_tags=False):
    data = json.load(open(path, encoding="utf-8"))
    missing = []
    for src in data.get("sources") or []:
        sid = src.get("id")
        rules = list(mapping.get(sid) or [])
        if not rules and default_from_tags:
            for tag in src.get("tags") or []:
                for r in TAG_TO_RULES.get(tag) or []:
                    if r not in rules:
                        rules.append(r)
        if not rules:
            missing.append(sid)
        else:
            src["rules"] = rules
    json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(path, "a", encoding="utf-8").write("\n")
    return missing


def main():
    phy = os.path.join(ROOT, "mahjong-phylogeny", "sources.json")
    stu = os.path.join(ROOT, "mahjong-studies", "sources.json")
    hk = os.path.join(ROOT, "hongkong", "sources.json")
    dr = os.path.join(ROOT, "drawing-mahjong", "sources.json")

    m1 = apply_rules(phy, PHY_RULES)
    m2 = apply_rules(stu, STUDIES_RULES, default_from_tags=True)
    data_hk = json.load(open(hk, encoding="utf-8"))
    for src in data_hk.get("sources") or []:
        sid = src.get("id")
        src["rules"] = HK_RULES.get(sid) or ["hongkong"]
    json.dump(data_hk, open(hk, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(hk, "a", encoding="utf-8").write("\n")

    data_dr = json.load(open(dr, encoding="utf-8"))
    for src in data_dr.get("sources") or []:
        src["rules"] = ["classical"]
        src["tags"] = ["early-manual", "classical"]
    json.dump(data_dr, open(dr, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(dr, "a", encoding="utf-8").write("\n")

    rules_path = os.path.join(CAT, "rules.json")
    cat = json.load(open(rules_path, encoding="utf-8"))
    for r in cat["rules"]:
        if r.get("slug") == "drawing-mahjong":
            r["fold_into"] = "classical"
            r["library_key"] = "classical"
            r["parent"] = "classical"
            r["blurb"] = "沈一帆《绘图麻雀牌谱》（1914）。"
        if r.get("slug") == "classical":
            r["archive"] = "drawing-mahjong"
    json.dump(cat, open(rules_path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(rules_path, "a", encoding="utf-8").write("\n")

    os.makedirs(PUBLIC, exist_ok=True)
    os.makedirs(BOOKS, exist_ok=True)
    for name, dest in (
        ("mahjong-phylogeny/sources.json", "mahjong-phylogeny.json"),
        ("mahjong-studies/sources.json", "mahjong-studies.json"),
        ("hongkong/sources.json", "hongkong.json"),
        ("drawing-mahjong/sources.json", "drawing-mahjong.json"),
        ("regional-rules/sources.json", "regional-rules.json"),
        ("local-rulebooks/sources.json", "local-rulebooks.json"),
        ("catalog/rules.json", "catalog.json"),
        ("catalog/phylogeny.json", "phylogeny.json"),
        ("catalog/areal.json", "areal.json"),
        ("index.json", "index.json"),
    ):
        src = os.path.join(ROOT, name)
        if os.path.isfile(src):
            shutil.copy2(src, os.path.join(PUBLIC, dest))

    pdf = os.path.join(ROOT, "drawing-mahjong", "files", "drawing-mahjong-local.pdf")
    alt = os.path.join(ROOT, "mahjong-studies", "files", "shen-yifan-1914.pdf")
    src_pdf = pdf if os.path.isfile(pdf) else alt
    if os.path.isfile(src_pdf):
        shutil.copy2(src_pdf, os.path.join(BOOKS, "drawing-mahjong.pdf"))
        shutil.copy2(src_pdf, os.path.join(PUBLIC, "drawing-mahjong.pdf"))
        print("pdf", src_pdf)
    else:
        print("pdf MISSING")

    print("phy missing", m1 or "none")
    print("studies missing", m2 or "none")


if __name__ == "__main__":
    main()
