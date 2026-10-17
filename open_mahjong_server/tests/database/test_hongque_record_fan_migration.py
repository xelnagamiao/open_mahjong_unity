"""One-time fan repairs retain original records and never change payments."""
import asyncio
from copy import deepcopy
import json
import subprocess
import sys

import pytest
from psycopg2.extras import Json

from test_subrule_statistics import db
from scripts.repair_hongque_record_fans import BACKUP_TABLE, rebuild_record, repair_database
from server.database.subrule_fans import collect_subrule_fans
from server.gamestate.game_hongque.test_hongque_reconnect import stage_fixture


def record(labels=None, *, rule="hongque", sub_rule="hongque/v1.6"):
    return {"game_title": {"rule": rule, "sub_rule": sub_rule}, "game_round": {
        "round_index_1": {"seats": [2, 3, 0, 1], "action_ticks": [
            ["hu_first", 2, 20, labels if labels is not None else ["双同顺*2", "清顺*4", "门清"], [0, 0, 20, 0], 1001],
            ["hu_second", 3, 5, ["门清"], [0, 0, 0, 5], 1001], ["end"],
        ]}}}


def test_copy_repairs_each_winner_and_preserves_scores_counts_and_input():
    source = record()
    original = deepcopy(source)
    repaired, count = rebuild_record(source)
    assert count == 4 and source == original
    ticks = repaired["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[0][3] == ["双同顺*2|4", "清顺*4|4", "门清|1"]
    assert ticks[1][3] == ["门清|1"]
    for old, new in zip(original["game_round"]["round_index_1"]["action_ticks"], ticks):
        assert old[:3] == new[:3] and old[4:] == new[4:]
    for original_index in range(4):
        assert collect_subrule_fans(source, original_index, "hongque/v1.6") == collect_subrule_fans(repaired, original_index, "hongque/v1.6")
    assert rebuild_record(repaired) == (repaired, 0)


def test_zero_fan_win_and_already_explicit_values_are_preserved():
    source = record([])
    source["game_round"]["round_index_1"]["action_ticks"][1][3] = ["清顺|4", "双同顺*2|4", "无番|0"]
    assert rebuild_record(source) == (source, 0)


def test_colour_fan_revision_does_not_reprice_historical_or_explicit_records():
    historical = record(['全纯色', '全半色'])
    repaired, _ = rebuild_record(historical)
    assert repaired['game_round']['round_index_1']['action_ticks'][0][3] == ['全纯色|1', '全半色|1']
    for value in (1, 2):
        explicit = record([f'全纯色|{value}', f'全半色|{value}'])
        explicit['game_round']['round_index_1']['action_ticks'][1][3] = ['门清|1']
        assert rebuild_record(explicit) == (explicit, 0)


@pytest.mark.parametrize("label", ["不存在的番", "清顺*0", "清顺*-1", "清顺|NaN", "清顺|", None, ""])
def test_unrecognized_or_malformed_fans_do_not_mutate_source(label):
    source = record(["门清", label])
    original = deepcopy(source)
    with pytest.raises(ValueError):
        rebuild_record(source)
    assert source == original


def test_other_rules_are_untouched_and_unknown_versions_fail():
    source = record(["平和"], rule="guobiao", sub_rule="guobiao/standard")
    assert rebuild_record(source) == (source, 0)
    with pytest.raises(ValueError, match="v1.6"):
        rebuild_record(record(sub_rule="hongque/unknown"))


@pytest.mark.parametrize("stage", ["self_draw", "ron", "multi_ron"])
@pytest.mark.parametrize("dealer", range(4))
def test_real_settlements_round_trip_through_legacy_repair(stage, dealer):
    async def run():
        game, _ = await stage_fixture(stage, dealer=dealer)
        try:
            current = game.game_record
            # New rule values are already explicit and must never be rewritten.
            assert rebuild_record(current) == (current, 0)
            historical = deepcopy(current)
            for rd in historical['game_round'].values():
                for tick in rd['action_ticks']:
                    if tick[0].startswith('hu_'):
                        tick[3] = [label.replace('全纯色|2', '全纯色|1').replace('全半色|2', '全半色|1') for label in tick[3]]
            legacy = deepcopy(historical)
            for rd in legacy["game_round"].values():
                for tick in rd["action_ticks"]:
                    if tick[0].startswith("hu_"):
                        tick[3] = [label.rsplit("|", 1)[0] for label in tick[3]]
            repaired, changed = rebuild_record(legacy)
            assert changed > 0 and repaired == historical
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def insert_record(db, game_id, data):
    db.sql("INSERT INTO game_records(game_id,record) VALUES(%s,%s)", (game_id, Json(data)))


def test_database_preview_apply_backup_and_idempotence(db):
    source = record()
    unrelated = record(["平和"], rule="guobiao", sub_rule="guobiao/standard")
    insert_record(db, "hongque", source)
    insert_record(db, "guobiao", unrelated)
    preview = repair_database(db._get_connection(), batch_size=1)
    assert preview["changed_records"] == 1 and not preview["applied"]
    assert db.sql("SELECT record FROM game_records WHERE game_id='hongque'")[0][0] == source
    assert db.sql("SELECT to_regclass(%s)", (BACKUP_TABLE,))[0][0] is None
    applied = repair_database(db._get_connection(), apply=True, batch_size=1)
    assert applied["applied"] and applied["changed_records"] == 1
    assert db.sql(f"SELECT record FROM {BACKUP_TABLE} WHERE game_id='hongque'")[0][0] == source
    assert db.sql("SELECT record FROM game_records WHERE game_id='hongque'")[0][0] == rebuild_record(source)[0]
    assert db.sql("SELECT record FROM game_records WHERE game_id='guobiao'")[0][0] == unrelated
    repeated = repair_database(db._get_connection(), apply=True)
    assert repeated["applied"] and repeated["changed_records"] == 0
    assert db.sql(f"SELECT COUNT(*) FROM {BACKUP_TABLE}")[0][0] == 1


def test_database_error_rolls_back_all_repairs_and_backups(db):
    insert_record(db, "a_good", record())
    bad = record(["未知番"])
    insert_record(db, "z_bad", bad)
    result = repair_database(db._get_connection(), apply=True, batch_size=1)
    assert not result["applied"] and len(result["errors"]) == 1
    assert db.sql("SELECT record FROM game_records WHERE game_id='a_good'")[0][0] == record()
    assert db.sql("SELECT record FROM game_records WHERE game_id='z_bad'")[0][0] == bad
    assert db.sql("SELECT to_regclass(%s)", (BACKUP_TABLE,))[0][0] is None


def test_file_cli_keeps_source_and_refuses_overwrite(tmp_path):
    from scripts import repair_hongque_record_fans as repair
    old = tmp_path / "old.json"
    new = tmp_path / "new.json"
    old.write_text(json.dumps(record(), ensure_ascii=False), encoding="utf-8")
    original_bytes = old.read_bytes()
    command = [sys.executable, repair.__file__, "--input", str(old), "--output", str(new)]
    assert subprocess.run(command, capture_output=True).returncode == 0
    assert old.read_bytes() == original_bytes
    assert json.loads(new.read_text(encoding="utf-8")) == rebuild_record(record())[0]
    assert subprocess.run(command, capture_output=True).returncode != 0
    command[-1] = str(old)
    assert subprocess.run(command, capture_output=True).returncode != 0
    assert old.read_bytes() == original_bytes
