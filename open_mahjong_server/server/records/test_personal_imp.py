from copy import deepcopy
import json

import pytest

from .personal_imp import personal_imp


def sample(opened, closed, scale="sbr"):
    match = {"completeness": "complete", "rule_profile": {"imp_scale": scale, "dealer_policy": "fixed_ESWN"},
             "result": {"winner_team_id": 1, "imp_scores": {"0": 7, "1": 9}}, "tables": {}}
    for table_id, values in (("open", opened), ("closed", closed)):
        table = {"record_state": "complete", "round_meta": {}, "payload": {"game_round": {}}}
        for i, changes in enumerate(values):
            key = f"round_index_{i + (1 if table_id == 'open' else 8)}"
            table["round_meta"][key] = {"board_instance_id": f"board-{i}", "alignment_status": "exact"}
            table["payload"]["game_round"][key] = {"seats": [0, 1, 2, 3], "action_ticks": [
                ["liuju", "board_summary", json.dumps({"changes": dict(enumerate(changes))})], ["end"]]}
        match["tables"][table_id] = table
    return match


def contract_cases():
    cases = []
    def add(label, match, scores=None):
        cases.append({"label": label, "match": match, "expected_open": scores})
    base = sample([[3, -3, 0, 0], [3, -3, 0, 0]], [[0]*4, [0]*4])
    add("convert each board before summing", base, [4, -4, 0, 0])
    permuted = deepcopy(base)
    rd = permuted["tables"]["closed"]["payload"]["game_round"]["round_index_8"]
    rd["seats"] = [2, 3, 0, 1]
    rd["action_ticks"][0][2] = {"changes": {"0": 3, "1": -3, "2": 0, "3": 0}}
    add("map current seats to original ESWN", permuted, [4, -4, -2, 2])
    for scale, points, expected in (("sbr", [0, 2, 3, -40], [0, 1, 2, -10]),
                                    ("sbr", [5, 8, 11, 15], [2, 3, 4, 5]),
                                    ("sbr", [19, 25, 31, 39], [6, 7, 8, 9]),
                                    ("jj", [9, 10, 29, 30], [1, 2, 2, 3]),
                                    ("jj", [59, 60, -60, 0], [3, 4, -4, 0])):
        add(f"{scale} boundaries {points}", sample([points], [[0]*4], scale), expected)
    for label, mutate in (
        ("partial table", lambda m: m["tables"]["closed"].update(record_state="partial")),
        ("partial match", lambda m: m.update(completeness="partial")),
        ("missing board", lambda m: m["tables"]["closed"]["payload"]["game_round"].pop("round_index_8")),
        ("different board identity", lambda m: m["tables"]["closed"]["round_meta"]["round_index_8"].update(board_instance_id="different")),
        ("unverified alignment", lambda m: m["tables"]["closed"]["round_meta"]["round_index_8"].update(alignment_status="partial")),
        ("missing settlement", lambda m: m["tables"]["closed"]["payload"]["game_round"]["round_index_8"].update(action_ticks=[["end"]])),
        ("malformed settlement", lambda m: m["tables"]["closed"]["payload"]["game_round"]["round_index_8"].update(action_ticks=[["liuju", "board_summary", "{"]])),
        ("duplicate seat", lambda m: m["tables"]["closed"]["payload"]["game_round"]["round_index_8"].update(seats=[0, 0, 2, 3])),
        ("unknown scale", lambda m: m["rule_profile"].update(imp_scale="unknown")),
    ):
        changed = deepcopy(base)
        mutate(changed)
        add(label, changed)
    return cases


@pytest.mark.parametrize("case", contract_cases(), ids=lambda c: c["label"])
def test_personal_comparison_contract(case):
    before = deepcopy(case["match"])
    result = personal_imp(case["match"])
    assert case["match"] == before  # Derived display data must not alter the official outcome.
    if case["expected_open"] is None:
        assert result == {"version": 1, "status": "unavailable"}
    else:
        assert result["status"] == "complete"
        assert result["tables"]["open"] == case["expected_open"]
        assert result["tables"]["closed"] == [-v for v in case["expected_open"]]
        assert result["board_count"] == len(case["match"]["tables"]["open"]["round_meta"])


def test_summary_includes_personal_metric_and_preserves_team_result():
    from .store import summarize_match
    match = sample([[3, -3, 0, 0]], [[0]*4])
    match["match_id"] = "test-match"
    summary = summarize_match(match)
    assert summary["personal_imp"]["tables"]["open"] == [2, -2, 0, 0]
    assert summary["result"] == match["result"]
