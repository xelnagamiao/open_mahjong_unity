import json
from pathlib import Path
import pytest
from .record_stats import analyze_riichi_record

CASES = json.loads(Path(__file__).with_name("stats_cases.json").read_text(encoding="utf-8"))


@pytest.mark.parametrize("case", CASES, ids=[c["name"] for c in CASES])
def test_statistics_contract(case):
    stats = analyze_riichi_record(case["record"], case["index"], case.get("score"), case.get("rank"))
    for key, expected in case["expected"].items():
        if isinstance(expected, dict):
            for subkey, value in expected.items():
                assert stats[key][subkey] == value, (key, subkey)
        else:
            assert stats[key] == expected, key


def test_non_riichi_and_missing_original_are_unavailable():
    assert analyze_riichi_record({"game_title": {"rule": "guobiao"}}, 0) is None
    assert analyze_riichi_record(CASES[0]["record"], -1) is None
