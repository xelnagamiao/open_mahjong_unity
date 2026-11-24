from copy import deepcopy
import pytest

from server.records.format import build_match, validate_match
from server.records.test_format import records
from server.records.wall_alignment import RecordConflict


@pytest.mark.parametrize("value", [True, False])
@pytest.mark.parametrize("table", ["open", "closed"])
def test_sync_mode_can_be_recovered_from_either_table_without_rewriting_sources(value, table):
    source = records()
    source[table]["record"]["game_title"]["duplicate_sync_mode"] = value
    before = deepcopy(source)
    bundle = build_match(source)
    assert bundle["sync_mode"] is value
    assert source == before
    validate_match(bundle)


def test_missing_mode_stays_unknown():
    assert build_match(records())["sync_mode"] is None


@pytest.mark.parametrize("value", [False, "true", 1])
def test_conflicting_or_malformed_mode_is_not_silently_overridden(value):
    source = records()
    source["open"]["record"]["game_title"]["duplicate_sync_mode"] = True
    source["closed"]["record"]["game_title"]["duplicate_sync_mode"] = value
    with pytest.raises(RecordConflict, match="synchronization"):
        build_match(source)


def test_archive_validation_detects_disagreement_with_its_table():
    bundle = build_match(records())
    bundle["sync_mode"] = True
    bundle["tables"]["closed"]["payload"]["game_title"]["duplicate_sync_mode"] = False
    with pytest.raises(RecordConflict, match="synchronization"):
        validate_match(bundle)


def test_training_wall_source_and_seed_survive_legacy_conversion():
    source = records()
    for entry in source.values():
        title = entry["record"]["game_title"]
        title.update(wall_source="training", duplicate_wall_type="training", duplicate_seed="9123456789123456789")
    before = deepcopy(source)
    bundle = build_match(source)
    for table in bundle["tables"].values():
        title = table["payload"]["game_title"]
        assert title["wall_source"] == "training"
        assert title["duplicate_seed"] == "9123456789123456789"
        assert "duplicate_key" not in title
    assert source == before
