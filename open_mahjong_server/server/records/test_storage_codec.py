from copy import deepcopy

import pytest

from .format import build_match, validate_match
from .storage_codec import CODEC, compact_record, expand_record
from .test_format import records
from .wall_alignment import RecordConflict


def single(times=None, prompts=False):
    ticks = [["d", 11], ["c", 11, "T"]]
    if prompts:
        ticks.insert(1, ["ask_hand", 0])
    rd = {"action_ticks": ticks}
    if times is not None:
        rd["action_times_ms"] = times
    return build_match({"main": {"game_id": "single", "record": {
        "game_title": {"rule": "guobiao"}, "game_round": {"round_index_1": rd}}}})


def metadata(record):
    return record["tables"]["main"]["round_meta"]["round_index_1"]


@pytest.mark.parametrize("times", [None, [], [0, 121], [17], [-1, False], ["8", None]])
def test_compact_defaults_and_restore_the_exact_valid_wire_body(times):
    original = single(times)
    before = deepcopy(original)
    stored = compact_record(original)
    assert stored["storage_codec"] == CODEC
    assert "legacy_node_map" not in metadata(stored) and "action_meta" not in metadata(stored)
    expanded = expand_record(stored)
    assert validate_match(expanded) == before == original
    assert expand_record(expanded) == before and compact_record(stored) == stored
    assert "action_meta" not in metadata(stored)
    assert metadata(expanded)["action_meta"][0] is not metadata(expanded)["action_meta"][1]


def test_explicit_exceptions_and_original_cursor_positions_survive_independently():
    record = single([1, 2, 3], prompts=True)
    meta = metadata(record)
    stored = compact_record(record)
    assert metadata(stored)["legacy_node_map"] == [0, 1, 1, 2]
    assert "action_meta" not in metadata(stored)
    assert expand_record(stored) == record
    meta["action_meta"][0]["extension_from_future_client"] = {"keep": 4}
    assert compact_record(record) is record
    record = single([0, 5])
    metadata(record)["action_meta"][0]["occurred_at_ms"] = False  # False == 0 in Python
    stored = compact_record(record)
    assert metadata(stored)["action_meta"][0]["occurred_at_ms"] is False
    assert "legacy_node_map" not in metadata(stored)
    assert metadata(expand_record(stored))["action_meta"][0]["occurred_at_ms"] is False


@pytest.mark.parametrize("time", [2.0, 2**53 + 1])
def test_timestamps_ambiguous_to_old_javascript_readers_stay_explicit(time):
    original = single([time])
    stored = compact_record(original)
    assert "action_meta" in metadata(stored)
    assert expand_record(stored) == original


def test_paired_unknown_codecs_and_unmarked_missing_fields_are_unchanged():
    paired = build_match(records(), reconstruct=True)
    extension = dict(single(), storage_codec="future.encoding")
    missing = single()
    del metadata(missing)["legacy_node_map"]
    for original in (paired, extension, missing):
        assert compact_record(original) is original
        assert expand_record(original) is original


def test_writing_or_reading_a_compact_body_requires_exactly_the_main_table():
    malformed = single()
    malformed["tables"]["extra"] = {"table_id": "extra"}
    assert compact_record(malformed) is malformed
    with pytest.raises(RecordConflict, match="Invalid compact single"):
        expand_record(dict(malformed, storage_codec=CODEC))
