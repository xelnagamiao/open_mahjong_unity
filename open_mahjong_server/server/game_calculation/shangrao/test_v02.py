import json
from pathlib import Path

import pytest

from . import rules as r


CASES = [
    ("无宝清一色", [11,12,13,14,15,16,17,18,19,12,13,14,16,16], 45, 16, {"qingyise": True}, 4, 8),
    ("清一色", [11,12,13,14,15,16,17,18,19,12,13,14,47,16], 45, 16, {"qingyise": True}, 2, 4),
    ("清一色清碰", [11]*3+[13]*3+[15]*3+[17]*3+[19]*2, 45, 19, {"qingyise": True}, 8, 16),
    ("清一色碰碰和", [11]*3+[13]*3+[15]*3+[17]*3+[47,19], 45, 19, {"qingyise": True}, 4, 8),
    ("清一色硬七对", [11,11,13,13,15,15,16,16,17,17,18,18,19,19], 45, 19, {"qingyise": True}, 8, 8),
    ("清一色七对", [11,11,13,13,15,15,16,16,17,17,18,18,47,19], 45, 19, {"qingyise": True}, 4, 4),
    ("九幺碰碰和", [19]*3+[21]*3+[29]*3+[39]*3+[12,41], 11, 41, {"allow_special_bao": True}, 4, 8),
    ("九幺七对", [19,19,21,21,29,29,31,31,39,39,41,41,12,45], 11, 45, {"allow_special_bao": True}, 4, 4),
    ("九幺", [19,19,21,21,29,29,31,31,39,39,41,41,45,12], 11, 12, {"allow_special_bao": True}, 1, 2),
    ("碰碰和", [19]*3+[21]*3+[29]*3+[39]*3+[12,24], 11, 24, {"allow_special_bao": True}, 2, 4),
    ("七对", [19,19,21,21,29,29,31,31,39,39,41,41,12,24], 11, 24, {"allow_special_bao": True}, 2, 2),
    ("字一色", [41]*3+[42]*3+[43]*3+[44,45,46,47,41], 11, 41, {"ziyise": True}, 2, 4),
    ("字一色清碰", [41]*3+[42]*3+[43]*3+[44]*3+[45]*2, 11, 45, {"ziyise": True}, 16, 32),
    ("字一色硬七对", [41,41,42,42,43,43,44,44,45,45,46,46,47,47], 11, 47, {"ziyise": True}, 16, 16),
    ("字一色碰碰和", [41]*3+[42]*3+[43]*3+[44]*3+[12,45], 11, 45, {"allow_special_bao": True,"ziyise": True}, 8, 16),
    ("字一色七对", [41,41,42,42,43,43,44,44,45,45,46,46,12,47], 11, 47, {"allow_special_bao": True,"ziyise": True}, 8, 8),
]


@pytest.mark.parametrize("pattern,hand,indicator,win,config,base,points", CASES)
def test_v02_document_combination_scores(pattern, hand, indicator, win, config, base, points):
    result = r.score_hand(hand, indicator=indicator, winning_tile=win, config=config)
    assert (result["pattern"], result["base"], result["points"]) == (pattern, base, points)


def test_nine_bao_option_does_not_make_thirteen_lan_wild_or_supply_stars():
    hand = [11,14,17,21,24,27,31,34,41,42,43,44,45,12]
    assert r.score_hand(hand, indicator=11, winning_tile=45, config={"allow_special_bao": True}) is None
    assert r.score_hand(hand, indicator=11, winning_tile=45,
                        config={"allow_special_bao": True, "rule_version": r.LEGACY_VERSION})["pattern"] == "十三烂"
    hand = [19,19,21,29,31,39,41,42,43,44,45,46,12,12]
    result = r.score_hand(hand, indicator=11, winning_tile=46, config={"allow_special_bao": True})
    assert result["pattern"] == "九幺" and result["points"] == 2


@pytest.mark.parametrize("win", [12,19])
def test_ziyise_combined_shapes_require_honor_winning_tile(win):
    hand = [41,41,42,42,43,43,44,44,45,45,46,46,12,win]
    result = r.score_hand(hand, indicator=11, winning_tile=win,
                         config={"allow_special_bao": True, "ziyise": True})
    assert result["pattern"] != "字一色七对"


def test_v02_defaults_and_old_version_are_explicit():
    assert r.normalize_config()["rule_version"] == "shangrao-v0.2"
    assert not any(v for k,v in r.normalize_config().items() if k != "rule_version")
    assert r.normalize_config({"rule_version":r.LEGACY_VERSION})["rule_version"] == r.LEGACY_VERSION


def test_all_persisted_parity_fixtures():
    path = Path(__file__).resolve().parents[3] / "tests/fixtures/shangrao_calculator.json"
    for i, case in enumerate(json.loads(path.read_text(encoding="utf8"))):
        actual = r.score_hand(case["hand"], case["melds"], indicator=case["indicator"], winning_tile=case["win"],
                              config=case["config"], flying_bao=case.get("flying_bao",0),
                              flying_seven_stars=case.get("flying_seven_stars",False),
                              after_kong=case.get("after_kong",False), opening=case.get("opening",False))
        assert actual == case["expected"], i
