from copy import deepcopy
from types import SimpleNamespace as NS
import pytest

from .format import build_match, digest, identity, validate_match
from .wall_alignment import RecordConflict, locate, wall_ref
from ..game_calculation.sichuan.duplicate_rules import make_board, flatten_board, rule_profile
from ..gamestate.game_sichuan.duplicate_wall import init_duplicate_board
from ..gamestate.game_sichuan.SichuanGameState import SichuanPlayer
from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match


def records(layout="standard", *, dw=True):
    sub = "sichuan/xueliu_doubles_duplicate" if layout == "xueliu" else "sichuan/doubles_duplicate"
    board = make_board("v2-fixture", 1, layout=layout)
    result = {}
    for table in ("open", "closed"):
        title = {"rule": "sichuan", "sub_rule": sub, "duplicate_match_id": "test-match",
                 "duplicate_table": table, "duplicate_profile": rule_profile(sub),
                 "duplicate_round_tiles": [flatten_board(board)]}
        hands = deepcopy(board["hands"])
        hands[0].append(board["walls"][0][0])
        rd = {"round_index": 1, "current_round": 1, "seats": [0, 1, 2, 3], "dealer_index": 0,
              "start_player_index": 0, "duplicate_wall_layout": layout,
              "action_ticks": [["c", hands[0][-1], "T"],
                               ["d", board["walls"][1][0], "dw", 1, 0, 1] if dw else ["d", board["walls"][1][0]]],
              **{f"p{i}_tiles": h for i, h in enumerate(hands)}}
        result[table] = {"game_id": "id" + table, "record": {"game_title": title, "game_round": {"round_index_1": rd},
                                                               "extension_for_future_rule": {"keep": True}}}
    return result


@pytest.mark.parametrize("layout", ["standard", "xueliu"])
@pytest.mark.parametrize("dw", [True, False])
def test_pair_conversion_preserves_actions_and_builds_precise_anchors(layout, dw):
    source = records(layout, dw=dw)
    before = deepcopy(source)
    bundle = build_match(source, reconstruct=True)
    assert source == before
    table = bundle["tables"]["open"]
    assert table["payload"] == source["open"]["record"]
    meta = table["round_meta"]["round_index_1"]
    ref = meta["action_meta"][1]["wall_ref"]
    assert meta["action_meta"][1]["wall_ref_basis"] == ("recorded" if dw else "reconstructed")
    assert locate(bundle["tables"]["closed"], ref)["node"] == 2
    assert locate(table, ref, "before_draw")["node"] == 1
    assert meta["action_meta"][1]["occurred_at_ms"] is None
    assert build_match(source, reconstruct=True) == bundle


def test_different_action_counts_still_align_and_source_player_is_not_part_of_key():
    source = records()
    rd = source["closed"]["record"]["game_round"]["round_index_1"]
    rd["action_ticks"].insert(1, ["ca", 2, "p", 11])
    bundle = build_match(source)
    ref = bundle["tables"]["open"]["round_meta"]["round_index_1"]["action_meta"][1]["wall_ref"]
    assert locate(bundle["tables"]["closed"], ref)["node"] == 3


def test_unpublished_and_finished_absence_are_different():
    bundle = build_match(records(), complete=False)
    instance = bundle["board_catalog"][0]["board_instance_id"]
    ref = wall_ref(instance, 3, 10)
    assert locate(bundle["tables"]["closed"], ref)["status"] == "NOT_VISIBLE_YET"
    bundle["tables"]["closed"]["record_state"] = "complete"
    assert locate(bundle["tables"]["closed"], ref)["status"] == "NOT_CONSUMED"


def test_initial_dealer_anchor_is_after_only():
    bundle = build_match(records())
    table = bundle["tables"]["open"]
    ref = table["round_meta"]["round_index_1"]["initial_anchors"][0]["wall_ref"]
    assert locate(table, ref)["node"] == 0
    assert locate(table, ref, "before_draw")["status"] == "PHASE_UNAVAILABLE"


def test_missing_origins_remain_unknown_without_reconstruction():
    bundle = build_match(records(dw=False))
    table = bundle["tables"]["open"]
    assert table["round_meta"]["round_index_1"]["alignment_status"] == "partial"
    assert locate(table, wall_ref(bundle["board_catalog"][0]["board_instance_id"], 1, 0))["status"] == "ORIGIN_UNAVAILABLE"


@pytest.mark.parametrize("field", ["duplicate_match_id", "duplicate_table", "sub_rule"])
def test_conflicting_pair_identity_is_rejected(field):
    source = records()
    source["closed"]["record"]["game_title"][field] = "wrong"
    with pytest.raises(RecordConflict):
        build_match(source)


def test_different_board_instances_are_never_paired_by_equal_walls():
    source = records()
    for table in source:
        source[table]["record"]["game_round"]["round_index_1"]["board_instance_id"] = table
    with pytest.raises(RecordConflict, match="attempt"):
        build_match(source)


def test_new_archive_retains_attempt_number_without_live_pair_snapshot():
    source = records()
    for entry in source.values():
        entry["record"]["game_round"]["round_index_1"].update(board_instance_id="attempt-two", board_attempt=2)
    assert build_match(source)["board_catalog"][0]["attempt"] == 2
    source["closed"]["record"]["game_round"]["round_index_1"]["board_attempt"] = 3
    with pytest.raises(RecordConflict): build_match(source)


@pytest.mark.parametrize("mutation", ["duplicate", "tile", "offset"])
def test_corrupt_physical_origin_is_rejected(mutation):
    source = records()
    ticks = source["open"]["record"]["game_round"]["round_index_1"]["action_ticks"]
    if mutation == "duplicate":
        ticks.append(deepcopy(ticks[-1]))
    elif mutation == "tile":
        ticks[-1][1] = 99
    else:
        ticks[-1][4] = 100
    with pytest.raises(RecordConflict):
        build_match(source)


def test_restarting_training_board_changes_identity_even_when_tiles_stay_equal():
    source = new_match("restart", "sichuan/doubles_duplicate", {}, [make_board(4, 1)], "seed")
    source.update(status="interrupted", wall_source="training")
    pair = PairedMatch(source, lambda *_: None)
    before, wall = pair.data["board_instances"][0]["board_instance_id"], deepcopy(pair.data["boards"])
    pair.restart_unfinished_board()
    assert before != pair.data["board_instances"][0]["board_instance_id"]
    assert pair.data["boards"] == wall
    assert pair.data["voided_boards"][0]["board_instance_id"] == before


def test_single_table_and_unknown_extensions_survive_conversion():
    payload = {"game_title": {"rule": "riichi", "sub_rule": "riichi/standard"},
               "game_round": {"round_index_1": {"action_ticks": [["reset", 0], ["end"]]}}, "extra": [1, 2]}
    result = build_match({"main": {"game_id": "old", "record": payload}})
    assert result["tables"]["main"]["payload"] == payload
    assert result["match_format"] == "single_table"
    assert result["tables"]["main"]["round_meta"]["round_index_1"]["alignment_status"] == "not_applicable"


def test_transport_asks_have_explicit_old_prefix_mapping():
    source = records()
    ticks = source["open"]["record"]["game_round"]["round_index_1"]["action_ticks"]
    ticks.insert(1, ["ask_hand", 1, "c"])
    result = build_match(source)
    meta = result["tables"]["open"]["round_meta"]["round_index_1"]
    assert meta["legacy_node_map"] == [0, 1, 1, 2]
    assert len(meta["action_meta"]) == 2
