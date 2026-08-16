# -*- coding: utf-8 -*-
"""Build tagged regional/local source packs and sync public JSON."""
from __future__ import annotations

import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)
BOOKS = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rulebooks")
)

DAY = "2026-08-14"


def src(sid, url, title, typ, lang, excerpt, rules, **extra):
    row = {
        "id": sid,
        "url": url,
        "title": title,
        "type": typ,
        "lang": lang,
        "accessed": DAY,
        "excerpt": excerpt,
        "rules": rules,
        "source_level": extra.pop("source_level", "secondary"),
        "reliability": extra.pop("reliability", "medium"),
    }
    row.update(extra)
    return row


REGIONAL = [
    src(
        "wiki-zh-shanghai",
        "https://zh.wikipedia.org/wiki/上海麻将",
        "维基百科 · 上海麻将",
        "variant",
        "zh",
        "沪麻又称敲麻。常用 144 张（含八花）。须报听才能和；常见只许碰碰和、清一色、混一色等，不算普通平胡。另有花分、辣子封顶、三口包牌等本地约定。",
        ["shanghai"],
        source_level="secondary",
        reliability="medium",
    ),
    src(
        "wiki-zh-tuidao",
        "https://zh.wikipedia.org/wiki/推倒胡",
        "维基百科 · 推倒胡",
        "variant",
        "zh",
        "推倒胡：凑齐牌型即可和，不强调复杂番种。各地细则不同。MIL 另有推广竞赛规则。",
        ["tuidao"],
    ),
    src(
        "wiki-zh-jipinghu",
        "https://zh.wikipedia.org/wiki/鸡平胡",
        "维基百科 · 鸡平胡",
        "variant",
        "zh",
        "广东一线里偏鸡胡、平胡的打法。番种门槛比清章新章低，各地叫法不一。",
        ["jipinghu"],
        reliability="low",
    ),
    src(
        "dw-hangzhou",
        "https://doublewin.me/guides/mahjong-hangzhou",
        "杭州麻将规则整理（白板财神、飘牌、三摊）",
        "discussion",
        "zh",
        "杭麻常用 136 张。白板作固定财神；打出白板进入飘。可吃，但吃同一人三次会承包。MIL 另有杭州麻将推广竞赛规则。",
        ["hangzhou"],
        reliability="low",
    ),
    src(
        "mahjonget-local",
        "https://www.mahjonget.com/",
        "麻将运动技术等级评定 · 竞技规则与推广规则",
        "official",
        "zh",
        "竞技规则列国标、立直、四川血战。推广规则列广东、贵州、杭州等。杭麻写白板财神；贵麻写捉鸡（幺鸡、八筒）。",
        ["hangzhou", "guizhou", "guangdong", "guobiao", "riichi", "sichuan"],
        reliability="medium",
    ),
    src(
        "wiki-zh-wenzhou",
        "https://zh.wikipedia.org/wiki/温州麻将",
        "维基百科 · 温州麻将",
        "variant",
        "zh",
        "温州地方麻将，常打十六张，并有财神。MIL 有试点竞赛规则。",
        ["wenzhou"],
        reliability="low",
    ),
    src(
        "dw-xuezhan",
        "https://doublewin.me/guides/mahjong-xuezhandaodi",
        "血战到底规则整理（定缺、换三张、查花猪）",
        "discussion",
        "zh",
        "川麻血战：108 张、不能吃、定缺。一家和了其他人继续，一局最多三家和。换三张是开局常见选项，不是另一种麻将。",
        ["sichuan", "sichuan-huansanzhang"],
        reliability="low",
    ),
    src(
        "cmp-xueliu-xuezhan",
        "https://www.18183.com/xxpd/yxzx/8800977.html",
        "血流成河与血战到底的规则差异对照",
        "discussion",
        "zh",
        "血战：和了的人退出，其余人继续。血流：和了的人还留在局里，可以再和，直到牌墙摸完。",
        ["xueliu", "sichuan"],
        reliability="low",
    ),
    src(
        "wiki-zh-changsha",
        "https://zh.wikipedia.org/wiki/长沙麻将",
        "维基百科 · 长沙麻将",
        "variant",
        "zh",
        "长沙麻将去字牌，但可以吃，常要 258 做将，并有扎鸟。和川麻不是一条改法。",
        ["changsha"],
    ),
    src(
        "wiki-zh-beijing",
        "https://zh.wikipedia.org/wiki/北京麻将",
        "维基百科 · 北京麻将",
        "variant",
        "zh",
        "京麻常见混儿（癞子）。各地混儿怎么翻、怎么算，桌上约定不一样。",
        ["beijing"],
        reliability="low",
    ),
    src(
        "baike-wuhan",
        "https://baike.baidu.com/item/武汉麻将",
        "百度百科 · 武汉麻将",
        "variant",
        "zh",
        "武汉地方麻将。开口翻等细则按当地。",
        ["wuhan"],
        reliability="low",
    ),
    src(
        "baike-nanchang",
        "https://baike.baidu.com/item/南昌麻将",
        "百度百科 · 南昌麻将",
        "variant",
        "zh",
        "南昌地方麻将，常有翻精。",
        ["nanchang"],
        reliability="low",
    ),
    src(
        "baike-dongbei",
        "https://baike.baidu.com/item/东北麻将",
        "百度百科 · 东北麻将",
        "variant",
        "zh",
        "辽宁摇宝、吉林下蛋、黑龙江夹胡、长春各有听口和宝牌／蛋牌约定。",
        ["dongbei"],
        reliability="low",
    ),
    src(
        "baike-shenyang",
        "https://baike.baidu.com/item/沈阳麻将",
        "百度百科 · 沈阳麻将（摇宝）",
        "variant",
        "zh",
        "辽宁一带常见摇宝／翻宝。听牌后看宝，宝牌可当混。不是「东北麻将」四个字本身。",
        ["dongbei-yaobao"],
        reliability="low",
    ),
    src(
        "zadi-changchun",
        "https://www.zadiqp.com/rules/changchunmajiang.html",
        "长春麻将规则整理（旋风杠、喜杠、夹胡、宝牌）",
        "discussion",
        "zh",
        "长春麻将在推倒胡骨架上加旋风杠、喜杠、幺九蛋、夹胡、看宝。MIL 另有长春麻将推广竞赛规则。",
        ["changchun", "dongbei-xiadan"],
        reliability="low",
    ),
    src(
        "baike-changchun",
        "https://baike.baidu.com/item/长春麻将/534836",
        "百度百科 · 长春麻将",
        "variant",
        "zh",
        "常见要求三色全、带幺九、至少一叉。夹胡指边张、嵌张或单钓一类听口。",
        ["changchun", "dongbei-jiahu"],
        reliability="low",
    ),
    src(
        "baike-shanxi",
        "https://baike.baidu.com/item/山西麻将",
        "百度百科 · 山西麻将",
        "variant",
        "zh",
        "山西地方麻将，有的桌上禁吃或立四。MIL 有推广竞赛规则。目前没有记到可靠的上级。",
        ["shanxi"],
        reliability="low",
    ),
    src(
        "baike-xian",
        "https://baike.baidu.com/item/西安麻将",
        "百度百科 · 西安麻将",
        "variant",
        "zh",
        "西安／陕麻常禁吃、定缺。",
        ["xian"],
        reliability="low",
    ),
    src(
        "baike-kunming",
        "https://baike.baidu.com/item/昆明麻将",
        "百度百科 · 昆明麻将",
        "variant",
        "zh",
        "昆明／滇麻打法靠近川滇血战一线：去字、禁吃、定缺等常见。",
        ["kunming"],
        reliability="low",
    ),
    src(
        "wiki-en-malaysia",
        "https://en.wikipedia.org/wiki/Malaysian_mahjong",
        "Wikipedia · Malaysian mahjong",
        "variant",
        "en",
        "Malaysian tables are close to Singapore / old-style Cantonese scoring, often with animal tiles and extra flowers. Local house rules vary.",
        ["malaysia"],
    ),
    src(
        "wiki-en-vietnam",
        "https://en.wikipedia.org/wiki/Vietnamese_mahjong",
        "Wikipedia · Vietnamese mahjong",
        "variant",
        "en",
        "Vietnamese sets are often enlarged (extra tiles / animals). Whether this descends directly from old-style Cantonese play is not settled, so the lineage map does not draw a hard line.",
        ["vietnam"],
    ),
    src(
        "wiki-en-korean",
        "https://en.wikipedia.org/wiki/Mahjong#South_Korea",
        "Wikipedia · Mahjong in South Korea",
        "variant",
        "en",
        "Korean three-player mahjong is commonly played without one suit. It spread from Japanese three-player practice and then picked up local scoring habits.",
        ["korean-3p"],
        reliability="low",
    ),
    src(
        "wiki-en-ema",
        "https://en.wikipedia.org/wiki/European_Mahjong_Association",
        "Wikipedia · European Mahjong Association",
        "official",
        "en",
        "EMA tournament mahjong uses the Chinese Official / MCR fan table, with European event regulations on top.",
        ["ema", "guobiao"],
    ),
    src(
        "zj-official",
        "https://zj-mahjong.info/index.html",
        "中庸麻雀计分法公式网站",
        "rulebook",
        "zh",
        "关兆豪编的中庸麻雀计分法。另写一套加算番种。站点同时收《中庸麻雀史观》。",
        ["zungjung"],
        reliability="high",
        source_level="primary",
    ),
    src(
        "zj-rules-en",
        "http://www.zj-mahjong.info/zj33_rules_eng.html",
        "Zung Jung Mahjong Scoring System v3.3",
        "rulebook",
        "en",
        "44 patterns, additive scoring, no basic points. Chicken hand scores 1; an official variant requires 5 points to win.",
        ["zungjung"],
        reliability="high",
        source_level="primary",
    ),
    src(
        "wiki-zh-hongzhong",
        "https://zh.wikipedia.org/wiki/红中麻将",
        "维基百科 · 红中麻将",
        "variant",
        "zh",
        "红中当癞子的打法，各地细则不同。MIL 另有推广竞赛规则。",
        ["hongzhong"],
        reliability="low",
    ),
    src(
        "wiki-zh-chongqing",
        "https://zh.wikipedia.org/wiki/重庆麻将",
        "维基百科 · 重庆麻将",
        "variant",
        "zh",
        "渝麻近四川血战：去字、禁吃、定缺，细节按当地习惯。",
        ["chongqing"],
        reliability="low",
    ),
]


LOCAL_BOOKS = [
    ("lib-classical", "古典麻将规则书", "/rulebooks/classical-rulebook.pdf", ["classical"], "平台现行古典麻将版本。"),
    ("lib-drawing", "绘图麻雀牌谱", "/rulebooks/drawing-mahjong.pdf", ["classical"], "沈一帆 1914 年牌谱。"),
    ("lib-shinbara", "想定宁波规则（榛原 1952）", "/rulebooks/shinbara-ningbo.html", ["classical"], "榛原茂树据五种民初麻将书想定的宁波打法。他自己写过：这不是最古现场规则。"),
    ("lib-guobiao", "国标麻将（新编 MCR）", "/rulebooks/guobiao-mcr.pdf", ["guobiao"], "平台使用的新编 MCR。"),
    ("lib-riichi", "GGHK 立直麻将规则书", "/rulebooks/riichi-rulebook.pdf", ["riichi"], "香港麻雀协会立直规则书。"),
    ("lib-qingque-one", "青雀一页纸", "/rulebooks/qingque-onepage.pdf", ["qingque"], "一页纸番种速记。"),
    ("lib-qingque-paili", "青雀牌例", "/rulebooks/qingque-paili.pdf", ["qingque"], "番种详解与牌例。"),
    ("lib-qingque-book", "青雀规则文档", "/rulebooks/qingque-rulebook.pdf", ["qingque"], "行牌逻辑与概念解释。"),
    ("lib-hongque", "虹雀² v1.6 规则书", "/rulebooks/hongque-v1.6.pdf", ["hongque"], "虹雀² v1.6 完整规则说明。"),
    ("lib-sichuan", "四川麻将（SBR）竞赛规则", "/rulebooks/sichuan-sbr.pdf", ["sichuan", "mil-sichuan"], "四川麻将（SBR）竞赛规则（试行 2025 版）。"),
    ("lib-changsha", "长沙麻将（双鸟）规则书", "/rulebooks/changsha-classic-double-bird-rulebook.pdf", ["changsha"], "本平台长沙麻将规则说明。"),
    ("lib-taiwan", "台湾麻将台数表", "/rulebooks/taiwan-yaku-table.pdf", ["taiwan"], "本平台台湾麻将采用的台数参考表。"),
    ("lib-shiyangjin", "十样锦麻将规则书", "/rulebooks/shiyangjin.pdf", ["shiyangjin"], "十样锦麻将规则说明。"),
    ("lib-kobayashi", "中国麻将（小林改版）规则书", "/rulebooks/guobiao-kobayashi.pdf", ["guobiao-kobayashi"], "小林改版修订条款说明。"),
    ("lib-kshen", "K神麻雀规则说明书", "/rulebooks/guobiao-kshen.pdf", ["guobiao-kshen"], "K 神改版规范说明书。"),
    ("lib-lanshi", "蓝十魔改规则第4版", "/rulebooks/guobiao-lanshi.pdf", ["guobiao-lanshi"], "蓝十改规则说明。"),
    ("mil-sbr", "四川麻将（SBR）竞赛规则（试行2025版）", "/rulebooks/mil/四川麻将（SBR）竞赛规则（试行2025版） (1).pdf", ["mil-sichuan", "sichuan"], "MIL 四川麻将竞赛规则。"),
    ("mil-mcr", "国标麻将（MCR）竞赛规则", "/rulebooks/mil/国标麻将（MCR）竞赛规则Chinese_mahjong_rules_try (1).pdf", ["guobiao"], "MIL 国标竞赛规则。"),
    ("mil-mcr-sup", "国标麻将（MCR）规则补充细则", "/rulebooks/mil/国标麻将（MCR）规则补充细则（试行，2025） (1).pdf", ["guobiao"], "MIL 国标补充细则。"),
    ("mil-shanxi", "山西麻将（推广）竞赛规则", "/rulebooks/mil/山西麻将（推广）竞赛规则（试行2023版）.pdf", ["shanxi"], "MIL 山西麻将推广竞赛规则。"),
    ("mil-gd", "广东麻将（推广）竞赛规则", "/rulebooks/mil/广东麻将（推广）竞赛规则（试行2023版）.pdf", ["guangdong"], "MIL 广东麻将推广竞赛规则。"),
    ("mil-tuidao", "推倒和麻将（推广）竞赛规则", "/rulebooks/mil/推倒和麻将（推广）竞赛规则（试行2024版）.pdf", ["tuidao"], "MIL 推倒和推广竞赛规则。"),
    ("mil-hz", "杭州麻将（推广）竞赛规则", "/rulebooks/mil/杭州麻将（推广）竞赛规则（试行2025版）.pdf", ["hangzhou"], "MIL 杭州麻将推广竞赛规则。"),
    ("mil-wz", "温州麻将（试点）竞赛规则", "/rulebooks/mil/温州麻将（试点）竞赛规则（试行2024版）.pdf", ["wenzhou"], "MIL 温州麻将试点竞赛规则。"),
    ("mil-rcr", "立直麻将竞赛规则（RCR）", "/rulebooks/mil/立直麻将竞赛规则riichirules2016.pdf", ["mil-riichi", "riichi"], "MIL 立直竞赛规则。"),
    ("mil-rcr-sup", "立直麻将（RCR）竞赛规则补充细则", "/rulebooks/mil/立直麻将（RCR）竞赛规则补充细则（2024版）.pdf", ["mil-riichi", "riichi"], "MIL 立直补充细则。"),
    ("mil-hzhong", "红中麻将（推广）竞赛规则", "/rulebooks/mil/红中麻将（推广）竞赛规则（试行2024版）.pdf", ["hongzhong"], "MIL 红中麻将推广竞赛规则。"),
    ("mil-gz", "贵州麻将（推广）竞赛规则", "/rulebooks/mil/贵州麻将（推广）竞赛规则（试行2023版）.pdf", ["guizhou"], "MIL 贵州麻将推广竞赛规则。"),
    ("mil-cc", "长春麻将（推广）竞赛规则", "/rulebooks/mil/长春麻将（推广）竞赛规则（试行2024版）.pdf", ["changchun"], "MIL 长春麻将推广竞赛规则。"),
]


def dump(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def patch_catalog():
    path = os.path.join(ROOT, "catalog", "rules.json")
    data = json.load(open(path, encoding="utf-8"))
    extra_feat = {
        "korean-3p": {
            "hand_size": 13,
            "tiles": 108,
            "honors": True,
            "chi": True,
            "riichi": True,
            "scoring": "fu-han",
            "note": "三人，常去掉一门数牌。",
        },
        "ema": {
            "hand_size": 13,
            "tiles": 144,
            "honors": True,
            "flowers": "8",
            "chi": True,
            "scoring": "mcr-fan",
            "min_win": 8,
        },
        "mil-riichi": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "riichi": True,
            "furiten": True,
            "scoring": "fu-han",
        },
        "qingque": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "scoring": "constructed",
        },
        "hongque": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "scoring": "constructed",
        },
        "guobiao-kobayashi": {
            "hand_size": 13,
            "tiles": 144,
            "honors": True,
            "flowers": "8",
            "chi": True,
            "scoring": "mcr-fan",
            "min_win": 0,
        },
        "guobiao-lanshi": {
            "hand_size": 13,
            "tiles": 144,
            "honors": True,
            "flowers": "8",
            "chi": True,
            "scoring": "mcr-fan",
            "min_win": 5,
            "note": "5 分起和，半全铳。",
        },
        "guobiao-kshen": {
            "hand_size": 13,
            "tiles": 144,
            "honors": True,
            "chi": True,
            "scoring": "mcr-fan",
        },
        "zungjung": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "scoring": "additive",
            "min_win": 0,
        },
        "shiyangjin": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
        },
        "jiandan": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "scoring": "constructed",
        },
        "wuhan": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "jokers": "fan",
            "jiang_258": True,
        },
        "shanxi": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
        },
        "changchun": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "jokers": "bao",
        },
        "xian": {
            "hand_size": 13,
            "tiles": 108,
            "honors": False,
            "chi": False,
            "dingque": True,
        },
        "malaysia": {
            "hand_size": 13,
            "tiles": 148,
            "jokers": "animals",
            "chi": True,
            "honors": True,
        },
        "vietnam": {
            "hand_size": 13,
            "jokers": "animals",
            "honors": True,
        },
        "dongbei-xiadan": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "jokers": "dan",
        },
        "dongbei-jiahu": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
        },
        "dongbei-yaobao": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "chi": True,
            "jokers": "bao",
        },
        "nanchang": {
            "hand_size": 13,
            "tiles": 136,
            "honors": True,
            "jokers": "jing",
        },
    }
    for r in data["rules"]:
        if r.get("slug") == "shanghai":
            r["areal_ids"] = ["ningbo-trunk"]
            r["features"] = {
                "hand_size": 13,
                "tiles": 144,
                "honors": True,
                "flowers": "8",
                "chi": True,
                "min_win": "pung-or-above",
                "scoring": "hua-lezi",
                "note": "须报听；常见只许碰碰和以上。",
            }
        slug = r.get("slug")
        if slug in extra_feat:
            feat = dict(r.get("features") or {})
            feat.update(extra_feat[slug])
            r["features"] = feat
    dump(path, data)


def main():
    patch_catalog()

    regional_dir = os.path.join(ROOT, "regional-rules")
    dump(
        os.path.join(regional_dir, "sources.json"),
        {
            "slug": "regional-rules",
            "label": "地方规则资料",
            "aliases": ["地方麻将", "子规则"],
            "collected_at": DAY,
            "notes": "按规则标签归档的地方打法介绍。百科和网站整理只作入口，细则以规则书和现场约定为准。",
            "sources": REGIONAL,
        },
    )

    books = []
    for sid, title, url, rules, excerpt in LOCAL_BOOKS:
        kind = "file"
        local = url.lstrip("/")
        books.append(
            src(
                sid,
                url,
                title,
                "rulebook",
                "zh",
                excerpt,
                rules,
                local_path=local,
                local_kind=kind,
                local_status="ok",
                source_level="primary" if "rulebooks/" in url else "secondary",
                reliability="high",
            )
        )
    dump(
        os.path.join(ROOT, "local-rulebooks", "sources.json"),
        {
            "slug": "local-rulebooks",
            "label": "馆藏规则书",
            "collected_at": DAY,
            "notes": "平台与 MIL 已收入的规则书，按规则标签挂到对应条目。",
            "sources": books,
        },
    )

    index_path = os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research", "index.json")
    # also keep a copy next to research root
    index = {
        "rules": [
            {"slug": "mahjong-phylogeny", "label": "麻将谱系与分类", "collected_at": DAY, "count": None},
            {"slug": "mahjong-studies", "label": "麻将通论与书志", "collected_at": DAY, "count": None},
            {"slug": "hongkong", "label": "香港麻将", "collected_at": "2026-07-20", "count": None},
            {"slug": "drawing-mahjong", "label": "绘图麻雀牌谱（已归入古典麻将）", "collected_at": "2026-08-04", "count": None},
            {"slug": "regional-rules", "label": "地方规则资料", "collected_at": DAY, "count": None},
            {"slug": "local-rulebooks", "label": "馆藏规则书", "collected_at": DAY, "count": None},
        ]
    }
    for item in index["rules"]:
        src_path = os.path.join(ROOT, item["slug"], "sources.json")
        if os.path.isfile(src_path):
            n = len(json.load(open(src_path, encoding="utf-8")).get("sources") or [])
            item["count"] = n
    dump(os.path.join(ROOT, "index.json"), index)

    os.makedirs(PUBLIC, exist_ok=True)
    os.makedirs(BOOKS, exist_ok=True)
    copies = [
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
    ]
    for name, dest in copies:
        srcp = os.path.join(ROOT, name)
        if os.path.isfile(srcp):
            shutil.copy2(srcp, os.path.join(PUBLIC, dest))

    shin = os.path.join(ROOT, "mahjong-phylogeny", "files", "shinbara-1952.html")
    if os.path.isfile(shin):
        shutil.copy2(shin, os.path.join(BOOKS, "shinbara-ningbo.html"))
        print("shinbara copied")
    print("regional", len(REGIONAL), "books", len(books))


if __name__ == "__main__":
    main()
