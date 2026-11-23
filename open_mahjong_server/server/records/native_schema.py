"""Explicit, transaction-owned finalization. No serving path creates compatibility objects."""
from psycopg2 import sql

from .format import digest, restore_source
from .wall_alignment import RecordConflict


def verify_sources(conn, *, original_column=False, progress=None, receipt=None, proofs=None):
    """Stream each physical table, including complete and partial paired matches."""
    count = 0
    trusted = {}
    if receipt is not None:
        trusted = receipt.get("tables")
        if receipt.get("format") != "omu.verified-native-tables.1" or not isinstance(trusted,dict) or digest(trusted) != receipt.get("sha256"):
            raise RecordConflict("Invalid native replay verification receipt")
    use_proofs = bool(trusted) or proofs is not None
    raw_column = ", t.record" if original_column else ""
    stamp = """encode(sha256(convert_to(jsonb_build_array(t.game_id,t.replay_id,t.table_id,
        t.created_at,r.record->>'match_id',r.record->'tables'->t.table_id)::text,'UTF8')),'hex')""" if use_proofs else "NULL::text"
    with conn.cursor(name="verify_replay_table_sources") as scan:
        scan.itersize = 16
        scan.execute(f"""SELECT t.game_id, {stamp} {raw_column}
            FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id
            ORDER BY t.game_id""")
        while rows := scan.fetchmany(scan.itersize):
            pending = [sid for sid, stamp_value,*_ in rows if original_column or not stamp_value or trusted.get(sid) != stamp_value]
            tables = {}
            if pending:
                with conn.cursor() as cur:
                    cur.execute("""SELECT t.game_id,r.record->'tables'->t.table_id FROM game_record_tables t
                            JOIN game_replays r ON r.game_id=t.replay_id WHERE t.game_id=ANY(%s)""", (pending,))
                    tables = dict(cur.fetchall())
            for sid, stamp_value,*original in rows:
                if sid in pending:
                    table = tables.get(sid)
                    if not table or table.get("table_record_id") != sid or not table.get("payload"):
                        raise RecordConflict(f"{sid}: invalid table locator")
                    raw = (restore_source(table) if any(table.get(k) for k in
                           ("transport_actions", "source_round_fields", "source_omitted_title_fields")) else table["payload"])
                    if digest(raw) != table["source_sha256"] or (original and raw != original[0]):
                        raise RecordConflict(f"{sid}: source checksum or original content mismatch")
                if proofs is not None:
                    proofs[sid] = stamp_value
                count += 1
                if progress and count % 1000 == 0:
                    progress(count)
    return count


def retire_projections(cur, *, preview):
    """RESTRICT is intentional: unknown database consumers must block the whole cutover."""
    cur.execute("SELECT current_schema()")
    schema = cur.fetchone()[0]
    removed = []
    cur.execute("SAVEPOINT replay_projection_retirement")
    for name in ("game_records", "game_record_metadata"):
        cur.execute("""SELECT c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                       WHERE n.nspname=%s AND c.relname=%s""", (schema, name))
        row = cur.fetchone()
        if row:
            if row[0] != "v":
                raise RecordConflict(f"Refusing to drop non-view {name}")
            cur.execute(sql.SQL("DROP VIEW {}.{} RESTRICT").format(sql.Identifier(schema), sql.Identifier(name)))
            removed.append(name)
    cur.execute("""SELECT p.prokind FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                   WHERE n.nspname=%s AND p.proname='replay_table_payload'
                     AND p.pronargs=1 AND p.proargtypes[0]='jsonb'::regtype""", (schema,))
    row = cur.fetchone()
    if row:
        if row[0] != "f":
            raise RecordConflict("Unexpected replay_table_payload object")
        cur.execute(sql.SQL("DROP FUNCTION {}.replay_table_payload(jsonb) RESTRICT").format(sql.Identifier(schema)))
        removed.append("replay_table_payload(jsonb)")
    if preview:
        cur.execute("ROLLBACK TO SAVEPOINT replay_projection_retirement")
    cur.execute("RELEASE SAVEPOINT replay_projection_retirement")
    return removed
