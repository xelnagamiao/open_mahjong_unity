"""Verify, stage, and atomically replace legacy PostgreSQL replay storage."""
from collections import Counter, defaultdict
from itertools import islice

from psycopg2 import sql
from psycopg2.extras import Json, execute_values

from .format import PAIRED_RULES, digest, identity
from .migrate import aliases, make_replay, persisted_digest, write_json
from .wall_alignment import RecordConflict


def relation_kind(cur, name):
    cur.execute("SELECT relkind FROM pg_class WHERE oid=to_regclass(%s)", (name,))
    row = cur.fetchone()
    return row[0] if row else None


def read_batches(items, size):
    iterator = iter(items)
    while batch := list(islice(iterator, size)):
        yield batch


def reclaim_locator_storage(conn, *, force=False):
    """Remove dropped JSONB values from physical storage after the verified transaction commits."""
    def measure(cur):
        cur.execute("""SELECT pg_total_relation_size(c.oid), pg_relation_size(c.oid),
                       COALESCE(pg_relation_size(NULLIF(c.reltoastrelid,0)),0),
                       EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attisdropped),
                       (SELECT count(*) FROM game_record_tables), (SELECT count(*) FROM game_replays)
                       FROM pg_class c WHERE c.oid='game_record_tables'::regclass""")
        total, heap, toast, dropped, tables, replays = cur.fetchone()
        cur.execute("""SELECT oid,conrelid,confrelid,convalidated FROM pg_constraint
                     WHERE contype='f' AND 'game_record_tables'::regclass IN (conrelid,confrelid) ORDER BY oid""")
        return total, heap, toast, dropped, (tables, replays, cur.fetchall())

    autocommit = conn.autocommit
    try:
        conn.autocommit = True
        with conn.cursor() as cur:
            before, heap, toast, dropped, references = measure(cur)
            # Locator rows contain short IDs and timestamps. An old large TOAST relation
            # or heap is evidence that a previous committed cutover still needs its rewrite.
            perform = force or dropped and (toast > 8192 or heap > max(131072, 128 * references[0]))
            if perform:
                cur.execute("VACUUM (FULL, ANALYZE) game_record_tables")
            after, _, final_toast, _, observed = measure(cur)
            if observed != references:
                raise RecordConflict("Replay counts or locator foreign keys changed during physical cleanup")
        return {"rewritten": bool(perform), "bytes_before": before, "bytes_after": after,
                "toast_before": toast, "toast_after": final_toast, "foreign_keys": len(references[2])}
    except Exception as exc:
        raise RecordConflict("Canonical migration is committed. Run apply again to finish locator storage cleanup") from exc
    finally:
        conn.autocommit = autocommit


def inspect_canonical(cur):
    cur.execute("""SELECT count(*), count(*) FILTER (WHERE
                 r.record->>'format' IS DISTINCT FROM 'open_mahjong.match_record'
                 OR r.record->>'schema_version' IS DISTINCT FROM '2'
                 OR COALESCE(r.record->>'match_format','') NOT IN ('single_table','paired_tables_2v2')
                 OR NOT EXISTS (SELECT FROM game_record_tables t WHERE t.replay_id=r.game_id))
                 FROM game_replays r""")
    count, invalid = cur.fetchone()
    cur.execute("""SELECT count(*), count(*) FILTER (WHERE r.game_id IS NULL OR
                r.record->'tables'->t.table_id->>'table_record_id' IS DISTINCT FROM t.game_id)
                FROM game_record_tables t LEFT JOIN game_replays r ON r.game_id=t.replay_id""")
    tables, broken = cur.fetchone()
    if invalid or broken:
        raise RecordConflict("Invalid canonical bodies or table links")
    cur.execute("""SELECT EXISTS (SELECT FROM game_replays r CROSS JOIN LATERAL jsonb_each(r.record->'tables') e(k,t)
                   WHERE t->'payload'<>'null'::jsonb AND NOT EXISTS
                   (SELECT FROM game_record_tables a WHERE a.replay_id=r.game_id AND a.table_id=k
                    AND a.game_id=t->>'table_record_id'))""")
    if cur.fetchone()[0]:
        raise RecordConflict("Replay body contains a table without its original link")
    return {"matches": count, "tables": tables}


_UNCHANGED = """FROM record_aliases a JOIN match_record_heads h USING(match_id)
    JOIN match_records r USING(match_id,record_revision) JOIN game_records g ON g.game_id=a.legacy_game_id
    WHERE a.source_namespace='cloud' AND a.table_id='main'
    AND r.record->>'match_format'='single_table' AND g.record=r.record->'tables'->'main'->'payload'
    AND a.source_sha256=r.record->'tables'->'main'->>'source_sha256'"""
_FINGERPRINT = """encode(sha256(convert_to(jsonb_build_array(r.record,r.content_sha256,a.source_sha256,
    a.round_map,a.node_map)::text,'UTF8')),'hex')"""


def migrate_database(conn, *, apply=False, batch_size=32, progress=None, receipt=None, receipt_out=None):
    from .store import canonical_game_id, create_replay_table, summarize_match
    from .native_schema import retire_projections, verify_sources
    if not 1 <= batch_size <= 1000:
        raise ValueError("batch_size must be between 1 and 1000")
    try:
        # Inspect outside the conversion snapshot. Freeze native writers BEFORE
        # acquiring the repeatable-read snapshot so a just-committed table is
        # included even when a rehearsal receipt is being reused.
        freeze_native = False
        if apply:
            with conn.cursor() as probe:
                probe.execute("""SELECT to_regclass('game_record_tables') IS NOT NULL
                                    AND to_regclass('game_replays') IS NOT NULL,
                    to_regclass('game_records') IS NOT NULL OR to_regclass('game_record_metadata') IS NOT NULL
                    OR to_regprocedure('replay_table_payload(jsonb)') IS NOT NULL""")
                native, pending = probe.fetchone()
                freeze_native = native and pending
            conn.rollback()
        with conn.cursor() as cur:
            cur.execute("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ")
            if freeze_native:
                cur.execute("SET LOCAL lock_timeout='5s'")
                cur.execute("LOCK TABLE game_replays,game_record_tables IN SHARE MODE")
            kind = relation_kind(cur, "game_records")
            if kind in (None, "v") and all(relation_kind(cur,n) == "r" for n in ("game_record_tables","game_replays")):
                cur.execute("SET LOCAL lock_timeout='5s'")
                stats = inspect_canonical(cur)
                proofs = {} if receipt_out else None
                if not apply or freeze_native or proofs is not None:
                    stats["verified_sources"] = verify_sources(conn, progress=progress, receipt=receipt, proofs=proofs)
                    if stats["verified_sources"] != stats["tables"]:
                        raise RecordConflict("Incomplete source verification")
                retired = retire_projections(cur, preview=not apply)
                conn.commit() if apply else conn.rollback()
                if receipt_out:
                    write_json(receipt_out, {"format":"omu.verified-native-tables.1","tables":proofs,"sha256":digest(proofs)})
                return {**stats, "status": ("applied" if apply else "ready") if retired else "already_unified",
                        "retired" if apply else "would_retire": retired}
            if kind != "r" or relation_kind(cur, "game_record_tables") or relation_kind(cur, "game_replays"):
                raise RecordConflict("Expected a legacy game_records table with no partial cutover")
            old = ("record_aliases", "match_record_heads", "match_records", "record_migration_runs")
            existing = [name for name in old if relation_kind(cur, name)]
            if existing and set(existing) != set(old):
                raise RecordConflict("Incomplete legacy archive schema")
            if apply:
                for name in ["game_records", *existing]:
                    cur.execute(sql.SQL("LOCK TABLE {} IN ACCESS EXCLUSIVE MODE").format(sql.Identifier(name)))
            if existing:
                cur.execute("SELECT count(*) FROM record_migration_runs WHERE status NOT IN ('published','rolled_back')")
                if cur.fetchone()[0]:
                    raise RecordConflict("A legacy archive migration is still staged")
                cur.execute("""SELECT count(*) FROM record_aliases a LEFT JOIN game_records g ON g.game_id=a.legacy_game_id
                               LEFT JOIN match_record_heads h USING(match_id) LEFT JOIN match_records r USING(match_id,record_revision)
                               WHERE a.source_namespace<>'cloud' OR g.game_id IS NULL OR r.match_id IS NULL""")
                if cur.fetchone()[0]:
                    raise RecordConflict("Unresolved legacy aliases; would lose or resurrect a deleted source")
                cur.execute("""SELECT count(*) FROM match_record_heads h WHERE NOT EXISTS
                            (SELECT 1 FROM record_aliases a WHERE a.match_id=h.match_id)""")
                if cur.fetchone()[0]:
                    raise RecordConflict("Published archive without a source locator")
                cur.execute("""SELECT count(*) FROM match_records previous
                    LEFT JOIN match_record_heads h USING(match_id)
                    LEFT JOIN match_records active ON (active.match_id,active.record_revision)=(h.match_id,h.record_revision)
                    CROSS JOIN LATERAL jsonb_each(previous.record->'tables') entry(k,t)
                    WHERE previous.record_revision IS DISTINCT FROM h.record_revision AND t->'payload'<>'null'::jsonb
                    AND (active.match_id IS NULL OR t->'payload' IS DISTINCT FROM active.record->'tables'->k->'payload'
                    OR t->'table_record_id' IS DISTINCT FROM active.record->'tables'->k->'table_record_id'
                    OR t->'source_sha256' IS DISTINCT FROM active.record->'tables'->k->'source_sha256')""")
                if cur.fetchone()[0]:
                    raise RecordConflict("An earlier archive has table content absent from the active revision")
            # Only lightweight table identities are inventoried globally. JSON bodies are read in whole-match batches.
            cur.execute("""SELECT game_id,record->'game_title'->>'sub_rule', record->'game_title'->>'duplicate_match_id'
                           FROM game_records ORDER BY game_id""")
            inventory = cur.fetchall()
            snapshot_ids = {}
            has_snapshots = relation_kind(cur, "sichuan_duplicate_matches")
            if has_snapshots:
                cur.execute("""SELECT id::text,data->'record_ids' FROM sichuan_duplicate_matches WHERE data ? 'record_ids'""")
                for mid, ids in cur.fetchall():
                    for sid in (ids or {}).values():
                        if sid and sid in snapshot_ids and snapshot_ids[sid] != mid:
                            raise RecordConflict(f"{sid}: ambiguous paired match snapshot")
                        if sid:
                            snapshot_ids[sid] = mid
            old_aliases = {}
            if existing:
                cur.execute("SELECT legacy_game_id,match_id,table_id FROM record_aliases WHERE source_namespace='cloud'")
                old_aliases = {sid: (mid, table) for sid,mid,table in cur.fetchall()}
            groups = defaultdict(list)
            for sid, sub, mid in inventory:
                if sub in PAIRED_RULES or sid in snapshot_ids or old_aliases.get(sid, (None,None))[1] in ("open","closed"):
                    mid = mid or snapshot_ids.get(sid) or old_aliases.get(sid, (None,None))[0]
                    if not mid:
                        raise RecordConflict(f"{sid}: cannot establish paired match identity")
                else:
                    mid = identity("single", sid)
                if sid in old_aliases and old_aliases[sid][0] != mid:
                    raise RecordConflict(f"{sid}: archived identity differs from source")
                groups[mid].append(sid)
            players_table = relation_kind(cur, "game_player_records")
            source_ids = {row[0] for row in inventory}
            canonical_ids = set()
            eligible = {}
            if existing and (receipt or receipt_out):
                cur.execute("SELECT a.legacy_game_id,h.match_id," + _FINGERPRINT + " " + _UNCHANGED)
                eligible = {sid: [mid, checksum] for sid,mid,checksum in cur.fetchall()}
            reused = []
            if receipt:
                if receipt.get("format") != "omu.migration.verified-singles.1" or digest(receipt.get("singles")) != receipt.get("sha256"):
                    raise RecordConflict("Invalid rehearsal receipt")
                reused = [sid for sid,proof in eligible.items()
                          if proof == receipt["singles"].get(sid) and groups.get(proof[0]) == [sid]
                          and proof[0] == identity("single",sid)]
                for sid in reused:
                    del groups[eligible[sid][0]]
                canonical_ids.update(reused)
            if apply:
                create_replay_table(cur)
                cur.execute("ALTER TABLE game_records ADD COLUMN replay_id VARCHAR(16), ADD COLUMN table_id TEXT")
            counts = Counter()
            if reused:
                counts.update(matches=len(reused), tables=len(reused), reused=len(reused))
                if apply:
                    # All inputs were verified in this same locked transaction. Join by
                    # the proven keys; repeating a JSONB equality here distorts row
                    # estimates and can turn this batch into minutes of repeated detoasting.
                    cur.execute("""INSERT INTO game_replays(game_id,record,created_at)
                        SELECT g.game_id,r.record,COALESCE(g.created_at,CURRENT_TIMESTAMP)
                        FROM unnest(%s::text[],%s::text[]) proof(sid,mid)
                        JOIN game_records g ON g.game_id=proof.sid
                        JOIN match_record_heads h ON h.match_id=proof.mid
                        JOIN match_records r ON (r.match_id,r.record_revision)=(h.match_id,h.record_revision)""",
                                (reused, [eligible[sid][0] for sid in reused]))
                    if cur.rowcount != len(reused):
                        raise RecordConflict("Rehearsed replay batch changed")
                    cur.execute("UPDATE game_records SET replay_id=game_id,table_id='main' WHERE game_id=ANY(%s)", (reused,))
                if progress:
                    progress(len(reused))
            for mids in read_batches(groups, batch_size):
                ids = [sid for mid in mids for sid in groups[mid]]
                cur.execute("SELECT game_id,record,created_at FROM game_records WHERE game_id=ANY(%s)", (ids,))
                sources, dates = {}, {}
                for sid, body, created in cur.fetchall():
                    sources[sid] = {"game_id": sid, "record": body}
                    dates[sid] = created
                snapshots, archives = {}, {}
                if has_snapshots and any(sid in snapshot_ids for sid in ids):
                    cur.execute("""SELECT id::text, data - 'final_records' - 'checkpoints' - 'table_checkpoints'
                                - 'pending_checkpoints' - 'room_snapshots' FROM sichuan_duplicate_matches
                                WHERE id::text=ANY(%s)""", (mids,))
                    snapshots = dict(cur.fetchall())
                if existing:
                    cur.execute("""SELECT h.match_id,r.record,r.content_sha256 FROM match_record_heads h
                                JOIN match_records r USING(match_id,record_revision) WHERE h.match_id=ANY(%s)""", (mids,))
                    for mid, archive, checksum in cur.fetchall():
                        if persisted_digest(archive) != checksum:
                            raise RecordConflict(f"{mid}: published archive checksum mismatch")
                        archives[mid] = archive
                missing = [sid for mid in mids if mid not in archives for sid in groups[mid]]
                if players_table and missing:
                    cur.execute("""SELECT p.game_id, to_jsonb(p) - 'note' - 'is_favorite'
                                   FROM game_player_records p WHERE p.game_id=ANY(%s)""", (missing,))
                    for sid, p in cur.fetchall():
                        sources[sid].setdefault("players", []).append(p)
                written, locators = [], []
                for mid in mids:
                    body = make_replay([sources[sid] for sid in groups[mid]], archives.get(mid), snapshots.get(mid))
                    api_id = canonical_game_id(body)
                    if api_id in canonical_ids or body["match_format"] == "paired_tables_2v2" and api_id in source_ids:
                        raise RecordConflict(f"{api_id}: public replay ID collision")
                    canonical_ids.add(api_id)
                    # The fast path copies the archive byte-for-byte. Record a fingerprint only
                    # when the thoroughly verified converter actually produced that same body.
                    if api_id in eligible and body != archives[mid]:
                        del eligible[api_id]
                    ids_for_match = aliases(body, api_id)
                    if set(ids_for_match) != set(groups[mid]):
                        raise RecordConflict(f"{mid}: table coverage mismatch")
                    for sid, locator in ids_for_match.items():
                        if sid in old_aliases and locator["table_id"] != old_aliases[sid][1]:
                            raise RecordConflict(f"{sid}: old link would change its selected table")
                    if existing:
                        # Old links include node selections that predate filtering of transport prompts.
                        expected = [sid for sid in ids_for_match if sid in old_aliases]
                        if expected:
                            cur.execute("SELECT legacy_game_id,source_sha256,round_map,node_map FROM record_aliases WHERE legacy_game_id=ANY(%s)", (expected,))
                            for sid, checksum, rounds, nodes in cur.fetchall():
                                if body["tables"][ids_for_match[sid]["table_id"]]["source_sha256"] != checksum:
                                    raise RecordConflict(f"{sid}: original table checksum differs from its published link")
                                if (rounds, nodes) != (ids_for_match[sid]["round_map"], ids_for_match[sid]["node_map"]):
                                    raise RecordConflict(f"{sid}: old link would change its replay position")
                    stats = summarize_match(body) if body["match_format"] == "paired_tables_2v2" else None
                    counts["paired"] += int(body["match_format"] == "paired_tables_2v2")
                    counts["partial"] += int(body["completeness"] != "complete")
                    counts["matches"] += 1
                    counts["tables"] += len(ids_for_match)
                    if apply:
                        created = [dates[sid] for sid in ids_for_match if dates[sid] is not None]
                        written.append((api_id, Json(body), Json(stats) if stats is not None else None, min(created) if created else None))
                        locators.extend((sid, api_id, val["table_id"]) for sid,val in ids_for_match.items())
                if apply and written:
                    execute_values(cur, "INSERT INTO game_replays(game_id,record,summary,created_at) SELECT v.id,v.body::jsonb,v.summary::jsonb,COALESCE(v.created::timestamp,CURRENT_TIMESTAMP) FROM (VALUES %s) v(id,body,summary,created)", written)
                    execute_values(cur, "UPDATE game_records g SET replay_id=v.r,table_id=v.t FROM (VALUES %s) v(s,r,t) WHERE g.game_id=v.s", locators)
                if progress and counts["matches"] // 1000 != (counts["matches"]-len(mids)) // 1000:
                    progress(counts["matches"])
            if counts["tables"] != len(inventory):
                raise RecordConflict("Incomplete source coverage")
            if apply:
                # The table OID and all its existing FKs, player rows, event links, and scores survive the rename.
                cur.execute("""ALTER TABLE game_records RENAME TO game_record_tables;
                    ALTER TABLE game_record_tables ALTER COLUMN replay_id SET NOT NULL, ALTER COLUMN table_id SET NOT NULL,
                    ADD FOREIGN KEY(replay_id) REFERENCES game_replays(game_id) ON DELETE CASCADE,
                    ADD UNIQUE(replay_id,table_id), ADD CHECK(table_id IN ('main','open','closed'))""")
                if inspect_canonical(cur) != {"matches": counts["matches"], "tables": counts["tables"]}:
                    raise RecordConflict("Staged replay and locator verification failed")
                if verify_sources(conn, original_column=True, progress=progress) != counts["tables"]:
                    raise RecordConflict("Incomplete original source coverage")
                cur.execute("ALTER TABLE game_record_tables DROP COLUMN record")
                for name in existing:
                    cur.execute(sql.SQL("DROP TABLE {}").format(sql.Identifier(name)))
                if has_snapshots:
                    from ..database.sichuan_duplicate import compact_completed_replays
                    counts["compacted_snapshots"] = compact_completed_replays(cur)
        if apply:
            conn.commit()
        else:
            conn.rollback()
        if receipt_out:
            write_json(receipt_out, {"format":"omu.migration.verified-singles.1", "singles":eligible, "sha256":digest(eligible)})
        stats = dict(counts)
        if apply:
            stats["locator_storage"] = reclaim_locator_storage(conn, force=True)
        return {**stats, "status": "applied" if apply else "verified"}
    except BaseException:
        conn.rollback()
        raise
