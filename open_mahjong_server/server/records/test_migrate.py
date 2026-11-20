from copy import deepcopy
import os
from uuid import uuid4

import psycopg2
from psycopg2 import sql
from psycopg2.extras import Json
import pytest

from .format import build_match, digest, restore_source
from .migrate import aliases, make_replay, persisted_digest, read_json, write_json
from .migrate_files import convert_entries, migrate_files
from .migrate_postgres import migrate_database
from .recompute import recompute_database
from .store import canonical_game_id
from .test_format import records
from .wall_alignment import RecordConflict


def test_transport_and_timestamps_are_exactly_recoverable():
    s = records()
    rd = s["open"]["record"]["game_round"]["round_index_1"]
    rd["action_ticks"].insert(0, ["ask_hand", 0, "cut"])
    rd["action_ticks"].append(["ask_other", 11, "p"])
    for times in ([], [12], [12,13,14,15,16,17]):
        rd["action_times_ms"] = times
        b = make_replay(list(s.values()))
        assert restore_source(b["tables"]["open"]) == s["open"]["record"]
        assert b["tables"]["open"]["round_meta"]["round_index_1"]["legacy_node_map"] == [0,0,1,2,2]
    del rd["action_ticks"]
    assert restore_source(make_replay(list(s.values()))["tables"]["open"]) == s["open"]["record"]


def test_snapshot_recovers_verified_identity_and_preserves_original():
    s = records()
    sid = s["open"]["game_id"]
    s["open"]["record"]["game_title"].pop("duplicate_match_id")
    s["open"]["record"]["game_title"].pop("duplicate_table")
    snap = {"id":"test-match", "record_ids":{"open":sid}, "status":"completed"}
    b = make_replay([s["open"]], snapshot=snap)
    assert b["completeness"] == "partial" and b["tables"]["closed"]["payload"] is None
    assert restore_source(b["tables"]["open"]) == s["open"]["record"]
    with pytest.raises(RecordConflict, match="identity"):
        make_replay([s["open"]])


def test_team_result_recovery_requires_official_tables_or_their_published_barrier():
    s = records()
    profile = s["open"]["record"]["game_title"]["duplicate_profile"]
    snapshot = {"id":"test-match", "status":"completed", "profile":profile, "scores":[2,0],
                "record_ids":{"open":"idopen","closed":"idclosed"}, "planned_boards":1, "completed_boards":1,
                "results":[{"raw_difference":[5,-5], "imp_scores":[2,0], "small_scores":[5,0]}]}
    body = make_replay(list(reversed(s.values())), snapshot=snapshot)
    assert body["settlements"] == snapshot["results"]
    assert body["result"]["match_points"] == {"0":1,"1":0}
    s["closed"]["record"]["game_title"]["doubles_result"] = body["result"]
    assert make_replay(list(s.values()))["result"] == body["result"]
    s["closed"]["record"]["game_title"].pop("doubles_result")
    assert make_replay([s["open"]], snapshot=snapshot)["result"] is None
    del s["open"]["record"]["game_title"]["sub_rule"]
    assert restore_source(make_replay(list(s.values()), snapshot=snapshot)["tables"]["open"]) == s["open"]["record"]


def test_recover_settlement_roster_only_from_matching_published_seats():
    s = records()
    for side in s:
        s[side]["record"]["game_title"].update(p0_uid=1,p0_name="AI")
    known = {"user_id":1, "original_player_index":0, "rank":2, "score":0, "voice_used":3,
             "note":"private", "is_favorite":True, "unknown_future_field":100}
    snap={"id":"test-match","status":"completed","record_ids":{"open":"idopen","closed":"idclosed"},
          "final_players":{"open":[known],"closed":[{**known,"user_id":2}]}}
    result = make_replay(list(s.values()),snapshot=snap)
    expected = {"user_id":1,"original_player_index":0,"username":"AI","rank":2,"score":0,"voice_used":3}
    assert result["tables"]["open"]["players"] == [expected]
    assert result["tables"]["closed"]["players"] == [{"user_id":1,"original_player_index":0,"username":"AI"}]
    for other in ({"status":"interrupted"},{"record_ids":{"open":"idopen","closed":"unrecorded"}},
                  {"final_players":{"open":[known,known]}}):
        b = make_replay(list(s.values()),snapshot={**snap,**other})
        assert "rank" not in b["tables"]["open"]["players"][0]
    s["open"]["players"]=[{"user_id":1,"original_player_index":0,"rank":1,"voice_used":5}]
    assert make_replay(list(s.values()),snapshot=snap)["tables"]["open"]["players"] == s["open"]["players"]
    s["open"]["players"]=[{"user_id":1,"original_player_index":0,"rank":2,"voice_used":5}]
    p=make_replay(list(s.values()),snapshot=snap)["tables"]["open"]["players"][0]
    assert p["score"] == 0 and p["voice_used"] == 5


def test_rehydrate_board_from_verified_snapshot_and_preserve_early_round_link():
    from ..game_calculation.sichuan.duplicate_rules import make_board
    s = records(dw=False)
    early = make_replay([s["open"]])
    original_link = early["tables"]["open"]["round_meta"]["round_index_1"]["table_round_id"]
    board = make_board("v2-fixture", 1)
    snapshot = {"id":"test-match", "boards":[board], "board_instances":[{"board_instance_id":"published", "attempt":1}]}
    combined = make_replay(list(s.values()), early, snapshot)
    meta = combined["tables"]["open"]["round_meta"]["round_index_1"]
    assert meta["table_round_id"] == original_link and meta["alignment_status"] == "exact"
    assert meta["action_meta"][1]["wall_ref"]["board_instance_id"] == "published"
    for row in s.values():
        row["record"]["game_title"].pop("duplicate_round_tiles")
    assert make_replay(list(s.values()), snapshot=snapshot)["board_catalog"][0]["wall_definition"] == board
    snap_conflict = {**snapshot, "boards":[make_board("not the same attempt",1)]}
    with pytest.raises(RecordConflict,match="walls disagree"):
        make_replay(list(records().values()),snapshot=snap_conflict)


@pytest.mark.parametrize("value", [{"name": "text,1.0", "bool":False}, {"𐐷":1, "汉字":1e-8},
                                     {"n":[1,1.0,True,2**64,None,-0.0]}, {"a":'":9.0'}])
def test_persisted_checksum_matches_existing_converter(value):
    assert persisted_digest(value) == digest(value)


def test_desktop_merge_preserves_old_closed_link_and_is_repeatable(tmp_path):
    root = tmp_path/"local"
    root.mkdir()
    s = records()
    both = build_match(s, reconstruct=True)
    write_json(root/"index.json", [{"game_id":"idclosed","is_favorite":True,"note":"closed"},
                                   {"game_id":"idopen","note":"open"}])
    for key,row in s.items():
        write_json(root/(row["game_id"]+".json"), {**row,"match_record":both,"cloud_saved":True})
    original = {p.name:p.read_bytes() for p in root.iterdir()}
    assert migrate_files(root)["matches"] == 1
    backup = tmp_path/"backup"
    migrate_files(root,apply=True,backup=backup)
    cid = canonical_game_id(both)
    assert read_json(root/"index.json")[0]["game_id"] == cid
    assert read_json(root/"index.json")[0]["is_favorite"] is True
    assert read_json(root/"record-aliases.json")["idclosed"]["table_id"] == "closed"
    detail = read_json(root/(cid+".json"))
    assert "match_record" not in detail and detail["record"]["tables"]["open"]["payload"] == s["open"]["record"]
    assert migrate_files(root,apply=True,backup=backup)["status"] == "already_unified"
    assert all((backup/name).read_bytes()==value for name,value in original.items())


def test_desktop_resumes_interrupted_switch_with_old_index_still_readable(tmp_path, monkeypatch):
    root, backup = tmp_path/"LocalRecords", tmp_path/"backup"
    root.mkdir()
    sources = list(records().values())
    write_json(root/"index.json", [{"game_id":r["game_id"]} for r in sources])
    for row in sources:
        write_json(root/(row["game_id"]+".json"),row)
    real_replace = os.replace
    def interrupt(stage, dest):
        if dest == root/"record-aliases.json":
            raise OSError("interrupted publish")
        real_replace(stage, dest)
    with monkeypatch.context() as changes:
        changes.setattr(os,"replace",interrupt)
        with pytest.raises(OSError, match="interrupted publish"):
            migrate_files(root,apply=True,backup=backup)
    for row in read_json(root/"index.json"):
        assert (root/(row["game_id"]+".json")).exists()
    assert migrate_files(root,apply=True,backup=backup)["matches"] == 1
    assert len(read_json(root/"record-aliases.json")) == 2
    assert migrate_files(root,apply=True,backup=backup)["status"] == "already_unified"


def test_browser_missing_full_source_preserves_known_timeline_and_flags_unknown():
    s = records()
    s["open"]["record"]["game_round"]["round_index_1"]["action_ticks"].insert(0,["ask_hand"])
    archive = build_match(s)
    archive["tables"]["open"].pop("transport_actions")
    detail = {**s["closed"], "match_record":archive}
    output, _ = convert_entries({"idclosed":detail, "__index__":[{"game_id":"idclosed"}]})
    b = output[canonical_game_id(archive)]["record"]
    assert b["tables"]["open"]["original_source_unavailable"]
    assert b["tables"]["open"]["round_meta"] == archive["tables"]["open"]["round_meta"]


def test_catalog_only_replay_stays_reachable_without_resurrecting_evicted_list_entries():
    b = make_replay(list(records().values()))
    mid, cid = b["match_id"], canonical_game_id(b)
    links = {sid: {**a,"match_id":mid,"source_sha256":b["tables"][a["table_id"]]["source_sha256"]}
             for sid,a in aliases(b,cid).items()}
    entries = {"__index__":[], "__match_v2/test.match.json":b, "__match_v2_catalog__":{
        "matches":{mid:"test.match.json"}, "aliases":links, "archive_hashes":{mid:digest(b)}}}
    output, stats = convert_entries(entries)
    assert stats["matches"] == 1 and list(output["__index__"]) == []
    assert output[cid]["record"] == b and output["__record_aliases__"]["idclosed"]["table_id"] == "closed"
    assert convert_entries(output)[0] == output
    entries["__index__"] = [{"game_id":"idclosed", "note":"keep"}]
    listed, _ = convert_entries(entries)
    assert listed["__index__"][0]["game_id"] == cid and listed["__index__"][0]["note"] == "keep"


@pytest.fixture
def isolated_postgres():
    dsn = os.getenv("OM_RECENT_TEST_DSN")
    if not dsn:
        pytest.skip("Needs PostgreSQL for isolated schema migration")
    schema = "replay_migrate_test_"+uuid4().hex
    c = psycopg2.connect(dsn)
    try:
        with c, c.cursor() as cur:
            cur.execute(sql.SQL("CREATE SCHEMA {}").format(sql.Identifier(schema)))
            cur.execute(sql.SQL("SET search_path TO {}").format(sql.Identifier(schema)))
            cur.execute("CREATE TABLE game_records(game_id varchar(16) PRIMARY KEY,record jsonb,created_at timestamp DEFAULT NOW())")
        yield c
    finally:
        c.rollback()
        with c,c.cursor() as cur:
            cur.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(schema)))
        c.close()


def test_postgres_atomic_replacement_preserves_references_and_exact_sources(isolated_postgres):
    c = isolated_postgres
    with c, c.cursor() as cur:
        cur.execute("CREATE TABLE fav(game_id varchar(16) REFERENCES game_records(game_id),note text)")
        cur.execute("CREATE TABLE game_player_records(game_id varchar(16), user_id bigint, username text, note text, is_favorite bool)")
        cur.execute("INSERT INTO game_player_records VALUES('idclosed',101,'player','private before migration',TRUE)")
        s = records()
        s["open"]["record"]["game_round"]["round_index_1"]["action_ticks"].insert(0, ["ask_hand",1])
        for row in s.values():
            cur.execute("INSERT INTO game_records(game_id,record) VALUES(%s,%s)",(row["game_id"],Json(row["record"])))
        cur.execute("INSERT INTO fav VALUES ('idclosed','note')")
    assert migrate_database(c)["tables"] == 2
    with c, c.cursor() as cur:
        cur.execute("UPDATE game_records SET record=jsonb_set(record,'{game_title,duplicate_table}','\"open\"'::jsonb) WHERE game_id='idclosed'")
    with pytest.raises(RecordConflict):
        migrate_database(c,apply=True)
    with c,c.cursor() as cur:
        cur.execute("SELECT to_regclass('game_replays')")
        assert cur.fetchone()[0] is None
        cur.execute("UPDATE game_records SET record=%s WHERE game_id='idclosed'",(Json(s['closed']['record']),))
    assert migrate_database(c,apply=True)["matches"] == 1
    assert migrate_database(c,apply=True)["status"] == "already_unified"
    with c,c.cursor() as cur:
        cur.execute("SELECT game_id,record FROM game_records")
        assert dict(cur.fetchall()) == {v["game_id"]:v["record"] for v in s.values()}
        cur.execute("SELECT f.note, t.table_id FROM fav f JOIN game_record_tables t USING(game_id)")
        assert cur.fetchone() == ("note","closed")
        cur.execute("SELECT record->'tables'->'closed'->'players'->0 FROM game_replays")
        player = cur.fetchone()[0]
        assert player["user_id"] == 101 and "is_favorite" not in player and "note" not in player
        cur.execute("SELECT note,is_favorite FROM game_player_records")
        assert cur.fetchone() == ("private before migration", True)
        cur.execute("UPDATE game_replays SET record=record #- '{tables,open,round_meta,round_index_1,action_meta}',summary=NULL")
    assert recompute_database(c, dry_run=True)["changed"] == 1
    assert recompute_database(c)["changed"] == 1
    assert recompute_database(c)["changed"] == 0
    with c,c.cursor() as cur:
        cur.execute("SELECT game_id,record FROM game_records")
        assert dict(cur.fetchall()) == {v["game_id"]:v["record"] for v in s.values()}


def test_unchanged_singles_receipt_and_historical_revision_guard(isolated_postgres, tmp_path):
    c = isolated_postgres
    payload = {"game_title":{"rule":"gb"}, "game_round":{}}
    bundle = make_replay([{"game_id":"single","record":payload}])
    mid, table = bundle["match_id"], bundle["tables"]["main"]
    locator = aliases(bundle, "single")["single"]
    with c, c.cursor() as cur:
        cur.execute("""CREATE TABLE match_records(match_id text,record_revision int,record jsonb,content_sha256 text);
            CREATE TABLE match_record_heads(match_id text,record_revision int);
            CREATE TABLE record_aliases(source_namespace text,legacy_game_id text,match_id text,table_id text,
                source_sha256 text,round_map jsonb,node_map jsonb);
            CREATE TABLE record_migration_runs(status text)""")
        cur.execute("INSERT INTO game_records(game_id,record) VALUES('single',%s)", (Json(payload),))
        cur.execute("INSERT INTO match_records VALUES(%s,2,%s,%s)", (mid,Json(bundle),digest(bundle)))
        cur.execute("INSERT INTO match_record_heads VALUES(%s,2)", (mid,))
        cur.execute("INSERT INTO record_aliases VALUES('cloud','single',%s,'main',%s,%s,%s)",
                    (mid,table["source_sha256"],Json(locator["round_map"]),Json(locator["node_map"])))
        old = deepcopy(bundle)
        old["tables"]["main"]["payload"]["old_unique_facts"] = 1
        cur.execute("INSERT INTO match_records VALUES(%s,1,%s,%s)", (mid,Json(old),digest(old)))
    with pytest.raises(RecordConflict,match="earlier archive"):
        migrate_database(c,apply=True)
    with c,c.cursor() as cur:
        cur.execute("UPDATE match_records SET record=%s,content_sha256=%s WHERE record_revision=1",
                    (Json(bundle),digest(bundle)))
    receipt = tmp_path/"receipt.json"
    assert migrate_database(c,receipt_out=receipt)["matches"] == 1
    assert migrate_database(c,apply=True,receipt=read_json(receipt))["reused"] == 1
    with c,c.cursor() as cur:
        cur.execute("SELECT record FROM game_records")
        assert cur.fetchone()[0] == payload
