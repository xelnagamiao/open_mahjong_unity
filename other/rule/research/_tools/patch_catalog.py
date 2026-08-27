# -*- coding: utf-8 -*-
"""Insert new catalog rules and copy JSON to public."""
from __future__ import annotations

import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CAT = os.path.join(ROOT, "catalog")
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)

NEW_RULES = [
    {
        "slug": "yezi",
        "name_zh": "叶子戏",
        "names": ["叶子", "叶格"],
        "family": ["precursor-tiles"],
        "origin": ["CN-Song", "CN-Ming"],
        "governance": ["historical"],
        "scope": ["precursor"],
        "players": [4],
        "status": "historical",
        "research_state": "identified",
        "lineage_ids": ["pre"],
        "areal_ids": ["pre-paper"],
        "parent": None,
        "library_key": None,
        "archive": "mahjong-phylogeny",
        "features": {"note": "纸牌源头。马吊、默和都从这一线下来。"},
    },
    {
        "slug": "tianjiu",
        "name_zh": "天九／牌九",
        "names": ["牌九", "天九牌", "花将牌", "骨牌"],
        "family": ["precursor-tiles"],
        "origin": ["CN"],
        "governance": ["historical"],
        "scope": ["precursor"],
        "players": [2, 4],
        "status": "historical",
        "research_state": "identified",
        "lineage_ids": ["pre"],
        "areal_ids": ["pre-domino"],
        "parent": None,
        "library_key": None,
        "archive": "mahjong-phylogeny",
        "features": {"note": "骨牌戏。麻将借用牌具形制，玩法不是牌九。"},
    },
    {
        "slug": "guobiao-lanshi",
        "name_zh": "国标蓝十改",
        "names": ["蓝十改", "蓝氏"],
        "family": ["13-tile"],
        "origin": ["community"],
        "governance": ["custom", "platform"],
        "scope": ["constructed"],
        "players": [4],
        "status": "active",
        "research_state": "identified",
        "lineage_ids": ["now"],
        "areal_ids": ["mcr-family"],
        "parent": "guobiao",
        "library_key": None,
        "archive": None,
        "features": {
            "hand_size": 13,
            "scoring": "fan",
            "min_win": "5-fan",
            "note": "改番种表与评分；5 分起和；半全铳半分付。",
        },
    },
    {
        "slug": "sichuan-huansanzhang",
        "name_zh": "四川麻将（换三张）",
        "names": ["换三张"],
        "family": ["13-tile"],
        "origin": ["CN-SC", "CN-CQ"],
        "governance": ["local", "platform"],
        "scope": ["local-branch"],
        "players": [4],
        "status": "active",
        "research_state": "identified",
        "lineage_ids": ["now"],
        "areal_ids": ["chuanyu-108"],
        "parent": "sichuan",
        "library_key": None,
        "archive": None,
        "features": {
            "hand_size": 13,
            "tiles": 108,
            "honors": False,
            "chi": False,
            "dingque": True,
            "after_first_win": "xuezhan",
            "note": "开局互换三张，血战桌上常见选项。",
        },
    },
    {
        "slug": "dongbei-yaobao",
        "name_zh": "辽宁摇宝",
        "names": ["摇宝", "沈阳麻将"],
        "family": ["13-tile"],
        "origin": ["CN-LN"],
        "governance": ["local"],
        "scope": ["local-branch"],
        "players": [4],
        "status": "active",
        "research_state": "candidate",
        "lineage_ids": ["now"],
        "areal_ids": ["dongbei-family"],
        "parent": "dongbei",
        "library_key": None,
        "archive": None,
        "features": {"hand_size": 13, "tiles": 136, "honors": True, "chi": True},
        "notes": "东北子规则。翻宝／摇宝，不是「东北麻将」这一个名字。",
    },
    {
        "slug": "dongbei-xiadan",
        "name_zh": "吉林下蛋",
        "names": ["下蛋", "长春下蛋"],
        "family": ["13-tile"],
        "origin": ["CN-JL"],
        "governance": ["local"],
        "scope": ["local-branch"],
        "players": [4],
        "status": "active",
        "research_state": "candidate",
        "lineage_ids": ["now"],
        "areal_ids": ["dongbei-family"],
        "parent": "dongbei",
        "library_key": None,
        "archive": None,
        "features": {"hand_size": 13, "tiles": 136, "honors": True},
        "notes": "东北子规则。蛋牌约定与辽宁摇宝不同。",
    },
    {
        "slug": "dongbei-jiahu",
        "name_zh": "黑龙江夹胡",
        "names": ["夹胡", "哈麻夹胡"],
        "family": ["13-tile"],
        "origin": ["CN-HL"],
        "governance": ["local"],
        "scope": ["local-branch"],
        "players": [4],
        "status": "active",
        "research_state": "candidate",
        "lineage_ids": ["now"],
        "areal_ids": ["dongbei-family"],
        "parent": "dongbei",
        "library_key": None,
        "archive": None,
        "features": {"hand_size": 13, "tiles": 136, "honors": True, "chi": True},
        "notes": "东北子规则。夹胡听口与摇宝、下蛋不是同一套。",
    },
]


def upsert(rules: list, item: dict) -> None:
    for i, r in enumerate(rules):
        if r.get("slug") == item["slug"]:
            rules[i] = {**r, **item}
            return
    rules.append(item)


def insert_after(rules: list, after_slug: str, item: dict) -> None:
    for i, r in enumerate(rules):
        if r.get("slug") == item["slug"]:
            rules[i] = {**r, **item}
            return
    for i, r in enumerate(rules):
        if r.get("slug") == after_slug:
            rules.insert(i + 1, item)
            return
    rules.append(item)


def main() -> None:
    path = os.path.join(CAT, "rules.json")
    data = json.load(open(path, encoding="utf-8"))
    rules = data["rules"]

    for r in rules:
        if r.get("slug") == "madiao":
            r["names"] = ["马吊牌"]
            r["parent"] = "yezi"
            r["areal_ids"] = ["pre-paper"]
        if r.get("slug") in ("mohe", "penghe"):
            r["areal_ids"] = ["pre-paper"]
        if r.get("slug") == "mohe":
            r["parent"] = "madiao"
        if r.get("slug") == "penghe":
            r["parent"] = "mohe"
        if r.get("slug") == "dongbei":
            r["notes"] = "辽宁摇宝、吉林下蛋、黑龙江夹胡、长春麻将各打各的。"
            r["areal_ids"] = ["dongbei-family"]
        if r.get("slug") == "changchun":
            r["areal_ids"] = ["dongbei-family"]
        if r.get("slug") == "guobiao":
            r["areal_ids"] = ["mcr-family"]
        if r.get("slug") in ("guobiao-kobayashi", "guobiao-kshen", "ema"):
            r["areal_ids"] = ["mcr-family"]
        if r.get("slug") == "xueliu":
            r["areal_ids"] = ["chuanyu-108"]
        if r.get("slug") in ("proto-mahjong", "classical", "drawing-mahjong"):
            extra = "ningbo-trunk"
            ids = r.get("areal_ids") or []
            if extra not in ids:
                r["areal_ids"] = ids + [extra]

    insert_after(rules, None if False else "yezi", NEW_RULES[0])
    # yezi at front
    rules[:] = [r for r in rules if r.get("slug") != "yezi"]
    rules.insert(0, NEW_RULES[0])
    insert_after(rules, "penghe", NEW_RULES[1])
    insert_after(rules, "guobiao-kobayashi", NEW_RULES[2])
    insert_after(rules, "sichuan", NEW_RULES[3])
    insert_after(rules, "dongbei", NEW_RULES[4])
    insert_after(rules, "dongbei-yaobao", NEW_RULES[5])
    insert_after(rules, "dongbei-xiadan", NEW_RULES[6])

    data["updated"] = "2026-08-14"
    json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(path, "a", encoding="utf-8").write("\n")

    os.makedirs(PUBLIC, exist_ok=True)
    for name in ("rules.json", "phylogeny.json", "areal.json"):
        src = os.path.join(CAT, name)
        if name == "rules.json":
            shutil.copy2(src, os.path.join(PUBLIC, "catalog.json"))
        else:
            shutil.copy2(src, os.path.join(PUBLIC, name))
    print("rules", len(data["rules"]))
    slugs = [r["slug"] for r in data["rules"]]
    for s in ("yezi", "tianjiu", "guobiao-lanshi", "sichuan-huansanzhang", "dongbei-yaobao", "dongbei-xiadan", "dongbei-jiahu"):
        print(s, "OK" if s in slugs else "MISSING")


if __name__ == "__main__":
    main()
