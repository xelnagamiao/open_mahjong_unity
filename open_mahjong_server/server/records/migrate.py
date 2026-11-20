"""One-time conversion of table records and previously published archives to a single replay."""
import argparse
import hashlib
import re
from copy import deepcopy
import json
import os
from pathlib import Path
import uuid

from .format import PAIRED_RULES, build_match, canonical, digest, identity, preserve_source, restore_source, validate_match
from .wall_alignment import RecordConflict

# A persisted JSON object's keys are already strings. Integer-only JSON with
# BMP keys has exactly the C encoder's compact sorted spelling.
_FALLBACK = re.compile(r"[\U00010000-\U0010ffff]|(?:^|[\[:,])-?\d+(?:\.\d|[Ee])")


def persisted_digest(value):
    compact = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
    if _FALLBACK.search(compact):
        return digest(value)
    return hashlib.sha256(compact.encode("utf-8")).hexdigest()


def write_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    temporary.write_text(canonical(data), encoding="utf-8")
    os.replace(temporary, path)


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def source_identity(source, archive=None, snapshot=None):
    sid, payload = source["game_id"], source["record"]
    title = payload.get("game_title")
    if not isinstance(title, dict):
        raise RecordConflict(f"{sid}: missing game_title")
    mid, table = title.get("duplicate_match_id"), title.get("duplicate_table")
    candidates = []
    if archive and archive.get("match_format") == "paired_tables_2v2":
        candidates.extend((archive["match_id"], key) for key, val in archive["tables"].items()
                          if val.get("table_record_id") == sid)
    if snapshot:
        candidates.extend((snapshot["id"], key) for key, value in snapshot.get("record_ids", {}).items() if value == sid)
    if title.get("sub_rule") not in PAIRED_RULES and not candidates:
        return identity("single", sid), "main"
    if "sub_rule" in title and title["sub_rule"] not in PAIRED_RULES:
        raise RecordConflict(f"{sid}: existing rule contradicts paired record identity")
    if mid and table:
        candidates.append((mid, table))
    if len(set(candidates)) != 1:
        raise RecordConflict(f"{sid}: missing or contradictory paired identity")
    match_id, table_id = candidates[0]
    if table_id not in ("open", "closed") or mid and mid != match_id or table and table != table_id:
        raise RecordConflict(f"{sid}: conflicting table identity")
    return match_id, table_id


def make_replay(sources, archive=None, snapshot=None):
    """Retain verified published metadata; recalculate only what the durable inputs establish."""
    tables, originals, additions = {}, {}, {}
    match_ids = set()
    for source in sources:
        mid, key = source_identity(source, archive, snapshot)
        if key in tables:
            raise RecordConflict(f"{mid}: multiple sources for {key}")
        match_ids.add(mid)
        originals[key] = source["record"]
        corrected = source
        if key != "main":
            title = source["record"]["game_title"]
            missing = {k: v for k, v in (("duplicate_match_id", mid), ("duplicate_table", key)) if k not in title}
            if "sub_rule" not in title:
                rule = (archive or {}).get("sub_rule") or ((snapshot or {}).get("profile") or {}).get("sub_rule")
                if rule not in PAIRED_RULES:
                    raise RecordConflict(f"{mid}/{key}: missing historical sub-rule")
                missing["sub_rule"] = rule
            if missing:
                corrected = {**source, "record": {**source["record"], "game_title": {**title, **missing}}}
                additions[key] = list(missing)
        tables[key] = corrected
    if len(match_ids) != 1:
        raise RecordConflict("A canonical replay requires one unambiguous match ID")
    mid = match_ids.pop()
    if archive and archive["match_id"] != mid:
        raise RecordConflict(f"{mid}: published match ID mismatch")
    if snapshot and snapshot.get("status") in ("waiting", "running") and not archive:
        raise RecordConflict(f"{mid}: unresolved in-progress match")
    old_tables = {k for k, t in (archive or {}).get("tables", {}).items() if t.get("payload") is not None}
    if archive:
        try:
            validate_match(archive)
        except (KeyError, TypeError, RecordConflict):
            # Reconstruct only missing derived data. Conflicting recorded facts must still fail validation.
            from .recompute import repair_fields
            archive = repair_fields(archive, build_match(tables, match_id=mid, snapshot=snapshot, reconstruct=True))
    if archive and old_tables == tables.keys():
        result = {**archive, "tables": {k: {**t, "players": deepcopy(t.get("players", []))}
                                       for k,t in archive["tables"].items()}}
    else:
        if old_tables - tables.keys():
            raise RecordConflict(f"{mid}: published table has no source")
        result = build_match(tables, match_id=mid, snapshot=snapshot, reconstruct=True)
        if archive:
            # Retain unknown extensions and the validated cursor map of the previously saved table.
            for k, v in archive.items():
                result.setdefault(k, deepcopy(v))
            for k in old_tables:
                result["tables"][k] = deepcopy(archive["tables"][k])
            result["record_revision"] = archive["record_revision"] + 1
    for key, entry in tables.items():
        target = result["tables"][key]
        if target["table_record_id"] != entry["game_id"]:
            raise RecordConflict(f"{mid}/{key}: published table points to another source")
        preserve_source(target, originals[key])
        if key in additions:
            previous_title = target["payload"]["game_title"]
            recovered = {field: entry["record"]["game_title"][field] for field in additions[key]}
            if any(field in previous_title and previous_title[field] != value for field,value in recovered.items()):
                raise RecordConflict(f"{mid}/{key}: saved title contradicts the verified table identity")
            target["payload"] = {**target["payload"], "game_title": {**previous_title, **recovered}}
            target["source_omitted_title_fields"] = additions[key]
        needs_restore = target["payload"] != originals[key] or any(target.get(field) for field in (
            "transport_actions", "source_round_fields", "source_omitted_title_fields"))
        if needs_restore and restore_source(target) != originals[key]:
            raise RecordConflict(f"{mid}/{key}: source content differs from the playback projection")
        # Existing archived source digests remain valid even when they cover omitted asks.
        if key in additions:
            if archive and target["source_sha256"] not in (persisted_digest(originals[key]), persisted_digest(entry["record"])):
                raise RecordConflict(f"{mid}/{key}: archived source checksum mismatch")
            target["source_sha256"] = digest(originals[key])
            result["provenance"] = deepcopy(result.get("provenance", {}))
            for p in result.get("provenance", {}).get("sources", []):
                if p.get("table_id") == key:
                    p["sha256"] = target["source_sha256"]
        elif archive and persisted_digest(originals[key]) != target["source_sha256"]:
            raise RecordConflict(f"{mid}/{key}: archived source checksum mismatch")
        if key != "main":
            # Frozen titles include bot seats even when no game_player_records row was emitted.
            roster = target["players"]
            seats = {p.get("original_player_index") for p in roster}
            title = originals[key]["game_title"]
            for seat in range(4):
                uid = title.get(f"p{seat}_uid")
                if seat not in seats and uid is not None:
                    known = next((p for p in roster if p.get("user_id") == uid and p.get("original_player_index") is None), None)
                    if known is not None:
                        known["original_player_index"] = seat
                    else:
                        roster.append({"original_player_index": seat, "user_id": uid, "username": title.get(f"p{seat}_name")})
    if archive and result.get("sync_mode") is None and result.get("match_format") == "paired_tables_2v2":
        from .format import resolve_sync_mode
        result["sync_mode"] = resolve_sync_mode([(snapshot or {}).get("sync_mode"),
            *[p["game_title"].get("duplicate_sync_mode") for p in originals.values()]])
    # Each paired replay is checked against both sources and its persisted barriers
    # even if an older publisher already marked all of its actions as aligned.
    if archive and result["match_format"] == "paired_tables_2v2":
        from .recompute import repair_fields
        rebuilt = build_match(tables, match_id=mid, snapshot=snapshot, reconstruct=True)
        result = repair_fields(result, rebuilt)
    if result["match_format"] == "paired_tables_2v2":
        from .recompute import recover_settled_players, recover_team_fields
        recover_settled_players(result, originals, snapshot)
        recover_team_fields(result, originals, snapshot)
    return validate_match(result)


def aliases(bundle, canonical_id):
    result = {}
    for key, table in bundle["tables"].items():
        sid = table.get("table_record_id")
        if sid:
            meta = table["round_meta"]
            result[sid] = {"game_id": canonical_id, "table_id": key,
                           "node_map": {r: m["legacy_node_map"] for r,m in meta.items()},
                           "round_map": {r: m["table_round_id"] for r,m in meta.items()}}
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("verify", "apply", "recompute"))
    parser.add_argument("--backend", choices=("postgres", "files", "browser"), required=True)
    parser.add_argument("--input", type=Path, help="Unity LocalRecords directory, or exported IndexedDB JSON")
    parser.add_argument("--backup", type=Path, help="External recovery directory for desktop file replacement")
    parser.add_argument("--output", type=Path, help="Destination for converted browser JSON")
    parser.add_argument("--receipt-out", type=Path, help="Write verified unchanged single-table fingerprints after a full PostgreSQL rehearsal")
    parser.add_argument("--receipt", type=Path, help="Reuse a completed rehearsal; PostgreSQL rechecks each source and archive fingerprint")
    parser.add_argument("--dry-run", action="store_true", help="Preview derived data changes from recompute")
    args = parser.parse_args(argv)
    if args.operation == "recompute" and args.backend != "postgres":
        parser.error("recompute requires --backend postgres")
    if args.dry_run and args.operation != "recompute":
        parser.error("--dry-run applies to recompute; use verify for migration")
    if args.backend == "postgres":
        import psycopg2
        from .migrate_postgres import migrate_database
        dsn = os.environ.get("OM_MATCH_MIGRATION_DSN")
        if not dsn:
            parser.error("set OM_MATCH_MIGRATION_DSN to the intended database")
        conn = psycopg2.connect(dsn)
        try:
            if args.operation == "recompute":
                from .recompute import recompute_database
                result = recompute_database(conn,dry_run=args.dry_run)
            else:
                result = migrate_database(conn, apply=args.operation == "apply", receipt=read_json(args.receipt) if args.receipt else None,
                                          receipt_out=args.receipt_out, progress=lambda n: print("verified matches", n, flush=True))
        finally:
            conn.close()
    elif not args.input:
        parser.error("--input is required")
    elif args.backend == "files":
        from .migrate_files import migrate_files
        result = migrate_files(args.input, apply=args.operation == "apply", backup=args.backup)
    else:
        from .migrate_files import convert_browser
        output, result = convert_browser(read_json(args.input))
        if args.operation == "apply":
            if not args.output or args.output.resolve() == args.input.resolve():
                parser.error("--output must be a separate path for a browser import")
            write_json(args.output, output)
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
