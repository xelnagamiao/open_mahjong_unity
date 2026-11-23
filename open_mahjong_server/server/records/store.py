"""Authoritative replay storage. A match owns one body and each played table one short locator."""
import base64
from copy import deepcopy
import hashlib

from psycopg2.extras import Json

from .format import PAIRED_RULES, build_match, digest, match_identity, public_players, restore_source, validate_match
from .wall_alignment import RecordConflict


def paired_game_id(match_id):
    checksum = hashlib.sha256(match_id.encode("utf-8")).digest()
    return "P" + base64.b32encode(checksum).decode("ascii")[:15]


def canonical_game_id(record):
    if record["match_format"] == "single_table":
        return record["tables"]["main"]["table_record_id"]
    return paired_game_id(record["match_id"])


def legacy_cursor_map(table):
    rounds = table["round_meta"]
    return {"round_map": {k: m["table_round_id"] for k, m in rounds.items()},
            "node_map": {k: m["legacy_node_map"] for k, m in rounds.items()}}


def create_replay_table(cursor):
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS game_replays (
            game_id VARCHAR(16) PRIMARY KEY,
            record JSONB NOT NULL,
            summary JSONB,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
        )
    """)


def ensure_tables(cursor):
    cursor.execute("""SELECT relkind FROM pg_class WHERE oid=to_regclass('game_records')""")
    existing = cursor.fetchone()
    if existing and existing[0] != "v":
        raise RuntimeError("Existing game_records requires the one-time replay migration before startup")
    create_replay_table(cursor)
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS game_record_tables (
            game_id VARCHAR(16) PRIMARY KEY,
            replay_id VARCHAR(16) NOT NULL REFERENCES game_replays(game_id) ON DELETE CASCADE,
            table_id TEXT NOT NULL CHECK (table_id IN ('main','open','closed')),
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE (replay_id, table_id)
        );
        ALTER TABLE game_record_tables ADD COLUMN IF NOT EXISTS training_match_id UUID;
        CREATE INDEX IF NOT EXISTS idx_game_record_tables_created_game
            ON game_record_tables(created_at DESC,game_id DESC);
    """)


def lock_record_ids(cursor, game_id, replay_id):
    # Table aliases and canonical public IDs share one externally visible namespace.
    for value in sorted({game_id, replay_id}):
        cursor.execute("SELECT pg_advisory_xact_lock(hashtextextended(%s,0))", ("replay:" + value,))


def load_paired_snapshot(cursor, title, game_id, *, training):
    """Verify a historical paired replay using its stored board and identity fields."""
    side = title.get("duplicate_table")
    cursor.execute("""SELECT jsonb_build_object(
               'id',id::text, 'profile',data->'profile', 'sync_mode',data->'sync_mode',
               'boards',COALESCE(data->'boards','[]'::jsonb),
               'board_instances',COALESCE(data->'board_instances','[]'::jsonb)),
            wall_id, data->>'key', data->>'wall_source', data->'record_ids'->>%s,
            data->'room_snapshots'->%s->>'duplicate_game_id'
        FROM sichuan_duplicate_matches WHERE id=%s::uuid FOR SHARE""",
        (side, side, title.get("duplicate_match_id")))
    row = cursor.fetchone()
    if not row:
        raise RecordConflict("Paired settlement has no durable match")
    snapshot, wall_id, key, source, saved_id, reserved_id = row
    if any(value and value != game_id for value in (saved_id, reserved_id)):
        raise RecordConflict("Paired settlement does not own the saved table ID")
    if training:
        if wall_id is not None or source != "training":
            raise RecordConflict("Training settlement belongs to a formal match")
    elif wall_id is None or key != title.get("duplicate_key"):
        raise RecordConflict("Paired settlement uses a different wall key")
    profile = snapshot["profile"] or {}
    if profile.get("sub_rule") != title.get("sub_rule") or (
            title.get("duplicate_profile") is not None and title["duplicate_profile"] != profile):
        raise RecordConflict("Paired rule differs from its durable match")
    return snapshot


def publish_table(cursor, game_id, payload, players, *, snapshot=None):
    """Return True for the first settlement of this table in the caller's transaction."""
    title = payload["game_title"]
    paired = title.get("sub_rule") in PAIRED_RULES
    match_id = match_identity(title, game_id)
    table_id = title.get("duplicate_table") if paired else "main"
    if paired and (not title.get("duplicate_match_id") or table_id not in ("open", "closed")):
        raise RecordConflict("Paired settlement lacks verified match and table identifiers")
    replay_id = paired_game_id(match_id) if paired else game_id
    if paired and game_id == replay_id:
        raise RecordConflict("A paired table ID collides with the match ID")
    lock_record_ids(cursor, game_id, replay_id)
    cursor.execute("""SELECT game_id,replay_id,table_id FROM game_record_tables
                      WHERE game_id IN (%s,%s)""", (game_id, replay_id))
    for existing_id, owner, side in cursor.fetchall():
        if owner != replay_id or existing_id != game_id or side != table_id:
            raise RecordConflict("Table identifier belongs to another replay")
    if replay_id != game_id:
        cursor.execute("SELECT 1 FROM game_replays WHERE game_id=%s", (game_id,))
        if cursor.fetchone():
            raise RecordConflict("Table identifier already names a replay")
    cursor.execute("SELECT record FROM game_replays WHERE game_id=%s FOR UPDATE", (replay_id,))
    row = cursor.fetchone()
    previous = row[0] if row else None
    sources = {}
    if previous:
        if previous["match_id"] != match_id:
            raise RecordConflict("Replay ID collision")
        for key, table in previous["tables"].items():
            if table.get("payload") is None:
                continue
            if key == table_id:
                if table["table_record_id"] != game_id:
                    raise RecordConflict("Conflicting duplicate table settlement")
                if table["source_sha256"] != digest(payload):
                    # Older replays were committed before the wallet snapshot existed.
                    # The caller already obtained the entitlement from server-side commerce.
                    original = restore_source(table)
                    if ("currency_reward" in original or "currency_reward" not in payload
                            or digest(original) != table["source_sha256"]):
                        raise RecordConflict("Conflicting duplicate table settlement")
                    from ..database.game_rewards import apply_currency_to_match_result
                    original["currency_reward"] = payload["currency_reward"]
                    apply_currency_to_match_result(original)
                    if digest(original) != digest(payload):
                        raise RecordConflict("Conflicting duplicate table settlement")
                return False
            restored = restore_source(table)
            restored['game_title'].update({field: table['payload']['game_title'][field]
                                            for field in table.get('source_omitted_title_fields', [])})
            sources[key] = {"game_id": table["table_record_id"], "record": restored, "players": table["players"]}
    sources[table_id] = {"game_id": game_id, "record": payload, "players": players}
    record = build_match(sources, match_id=match_id, snapshot=snapshot, reconstruct=True)
    if previous:
        # Rebuilding from the sibling preserves its legacy cursor map and raw source;
        # keep any previously verified metadata/extensions that are absent on a live write.
        for key, table in previous["tables"].items():
            if key != table_id and table.get("payload") is not None:
                for rk, old in table["round_meta"].items():
                    recalculated = record["tables"][key]["round_meta"][rk]
                    if old["alignment_status"] != "exact" and recalculated["alignment_status"] == "exact":
                        # The first end packet already exposed its round anchor and cursor positions.
                        recalculated["table_round_id"] = old["table_round_id"]
                        if old["legacy_node_map"] != recalculated["legacy_node_map"]:
                            raise RecordConflict("Sibling publication would move an existing replay cursor")
                        old.update(recalculated)
                record["tables"][key] = {**table, "players": public_players(table["players"])}
        for source in record["provenance"]["sources"]:
            source["sha256"] = record["tables"][source["table_id"]]["source_sha256"]
        record["record_revision"] = previous["record_revision"] + 1
        validate_match(record)
    summary = summarize_match(record) if paired else None
    cursor.execute("""INSERT INTO game_replays(game_id,record,summary) VALUES(%s,%s,%s)
                      ON CONFLICT(game_id) DO UPDATE SET record=EXCLUDED.record,summary=EXCLUDED.summary""",
                   (replay_id, Json(record), Json(summary) if summary is not None else None))
    cursor.execute("INSERT INTO game_record_tables(game_id,replay_id,table_id) VALUES(%s,%s,%s)",
                   (game_id, replay_id, table_id))
    return True


def has_shared_content(payload):
    title = payload.get("game_title") or {}
    match = payload.get("duplicate_match") or {}
    fields = ((payload, ("duplicate_boards", "doubles_result")),
              (title, ("doubles_result", "duplicate_seed", "duplicate_tiles", "duplicate_round_tiles")),
              (match, ("boards", "scores", "small_scores", "imp_scores")))
    for container, names in fields:
        for key in names:
            value = container.get(key)
            if isinstance(value, (dict, list, str)):
                if value:
                    return True
            elif value is not None:  # Numeric zero is also a valid saved seed.
                return True
    return False


def summarize_match(match):
    from .personal_imp import personal_imp
    result = {"match_id": match["match_id"], "teams": match.get("teams", []), "result": match.get("result"),
              "personal_imp": personal_imp(match), "tables": {}, "access": {}}
    for key, table in match["tables"].items():
        title = (table.get("payload") or {}).get("game_title", {})
        result["tables"][key] = {"players": public_players(table.get("players", [])), "payload": {
            "game_title": {"team_by_player_index": title.get("team_by_player_index")}}}
        if table.get("payload") is not None:
            access_title = {k: title[k] for k in ("is_duplicate", "duplicate_key", "duplicate_seed",
                "wall_source", "duplicate_wall_type", "sub_rule", "duplicate_match_id", "duplicate_table") if k in title}
            if isinstance(title.get("duplicate_tiles"), list):
                access_title["duplicate_tiles"] = []
            result["access"][key] = {"id": table.get("table_record_id"), "game_title": access_title,
                                     "has_shared_content": has_shared_content(table["payload"])}
    return result


def _visible(db, title, source_id):
    from ..database.duplicate_walls import duplicate_record_is_visible
    return duplicate_record_is_visible(db, {"game_title": title}, source_id)


def prepare_public_roster(table, side, names):
    # Older stored bodies and summaries may contain private game_player_records columns.
    table["players"] = public_players(table.get("players", []))
    for player in table["players"]:
        uid = player.get("user_id")
        username = names.get((side, uid))
        if isinstance(uid, int) and uid > 10 and username is not None:
            player["username"] = username


def read_summary(db, summary, names):
    if summary is None:
        return None
    summary = deepcopy(summary)
    access = summary.pop("access")
    blocked = {key for key, meta in access.items() if not _visible(db, meta["game_title"], meta["id"])}
    visible = set(access) - blocked
    if not visible or (blocked and any(access[key].get("has_shared_content", True) for key in visible)):
        return None
    for key, table in summary["tables"].items():
        if key in blocked:
            table.update(players=[], payload={})
        else:
            prepare_public_roster(table, key, names)
    if blocked:
        summary.update(result=None, personal_imp={"version": 1, "status": "unavailable"})
        for team in summary["teams"]:
            team["members"] = [p for p in team["members"] if p["table_id"] not in blocked]
    return summary


def _redact(record, blocked):
    for side in blocked:
        record["tables"][side] = {"table_id": side, "table_record_id": None, "record_state": "missing",
            "payload": None, "round_meta": {}, "players": [], "reason": "access_denied"}
    record.update(completeness="partial", result=None, settlements=[], provenance={})
    for team in record["teams"]:
        team["members"] = [p for p in team["members"] if p["table_id"] not in blocked]
    for board in record["board_catalog"]:
        board.pop("wall_definition", None)


def read_record(db, game_id):
    from ..database.duplicate_walls import DuplicateRecordLockedError
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("""SELECT r.game_id,r.record,r.created_at,t.table_id FROM game_replays r
                LEFT JOIN game_record_tables t ON t.game_id=%s AND t.replay_id=r.game_id
                WHERE r.game_id=COALESCE((SELECT replay_id FROM game_record_tables WHERE game_id=%s),%s)""",
                (game_id, game_id, game_id))
            row = cursor.fetchone()
            if not row:
                return None
            canonical_id, record, created, selected = row
            cursor.execute("""SELECT t.table_id,p.user_id,p.username,p.room_type,p.match_type
                FROM game_player_records p JOIN game_record_tables t USING(game_id)
                WHERE t.replay_id=%s ORDER BY p.original_player_index NULLS LAST,p.user_id""", (canonical_id,))
            indexed = cursor.fetchall()
            names = {(side, uid): name for side, uid, name, _, _ in indexed}
    finally:
        db._put_connection(conn)
    present = {k: t for k,t in record["tables"].items() if t.get("payload") is not None}
    blocked = {key for key,t in present.items() if not _visible(db, t["payload"]["game_title"], t["table_record_id"])}
    available = [key for key in ("main","open","closed") if key in present and key not in blocked]
    if selected in blocked or not available or (blocked and any(has_shared_content(present[key]["payload"]) for key in available)):
        raise DuplicateRecordLockedError()
    alias_map = legacy_cursor_map(present[selected]) if selected in present else None
    selected = selected or available[0]
    if blocked:
        _redact(record, blocked)
    for key, table in record["tables"].items():
        prepare_public_roster(table, key, names)
    players = [{**player, "table_id": key} for key in available for player in present[key]["players"]]
    title = present[selected]["payload"]["game_title"]
    scene = next(((room, mode) for side,_,_,room,mode in indexed if side == selected), (None, None))
    return {"game_id": canonical_id, "record": record, "created_at": str(created), "rule": record["rule"],
            "sub_rule": record["sub_rule"], "room_type": title.get("room_type") or scene[0],
            "match_type": scene[1] or title.get("match_type"),
            "players": players, "selected_table_id": selected, "legacy_cursor_map": alias_map}

def list_replays(db, *, user_id=None, event_id=None, limit=20, offset=0, favorites_only=False):
    """Page whole matches first; player rows remain keyed by their original table IDs."""
    from psycopg2.extras import RealDictCursor
    scope = "p.user_id = %s" if user_id is not None else "p.event_id = %s"
    if favorites_only and user_id is not None:
        scope += " AND p.is_favorite"
    library_fields = ("""bool_or(p.is_favorite) AS is_favorite,
                      (array_agg(p.note ORDER BY (p.note IS NULL), t.game_id))[1] AS note"""
                      if user_id is not None else "false AS is_favorite, NULL::text AS note")
    conn = db._get_connection()
    try:
        with conn.cursor(cursor_factory=RealDictCursor) as cursor:
            cursor.execute(f"""WITH selected AS (
                SELECT t.replay_id, {library_fields},
                       (array_agg(t.table_id ORDER BY (t.table_id='closed'),t.game_id))[1] AS selected
                FROM game_player_records p JOIN game_record_tables t USING(game_id)
                WHERE {scope} GROUP BY t.replay_id
            )
            SELECT r.game_id,r.created_at,r.summary,s.is_favorite,s.note,s.selected
            FROM selected s JOIN game_replays r ON r.game_id=s.replay_id
            ORDER BY r.created_at DESC,r.game_id DESC LIMIT %s OFFSET %s""",
                (user_id if user_id is not None else event_id, limit, offset))
            page = cursor.fetchall()
            if not page:
                return []
            cursor.execute("""SELECT t.replay_id,t.table_id,p.user_id,p.username,p.score,p.rank,
                    p.original_player_index,p.title_used,p.character_used,p.profile_used,p.voice_used,
                    p.avatar_frame_used,p.match_type,p.room_type,
                    COALESCE(NULLIF(btrim(p.rule),''),r.record->>'rule','') AS rule,
                    COALESCE(NULLIF(btrim(p.sub_rule),''),r.record->>'sub_rule') AS sub_rule
                FROM game_player_records p JOIN game_record_tables t USING(game_id)
                JOIN game_replays r ON r.game_id=t.replay_id WHERE t.replay_id=ANY(%s)
                ORDER BY p.rank,p.original_player_index NULLS LAST,p.score DESC""",
                ([row["game_id"] for row in page],))
            participants = {}
            for player in cursor.fetchall():
                participants.setdefault(player.pop("replay_id"), []).append(player)
    finally:
        db._put_connection(conn)
    result = []
    for row in page:
        players = participants.get(row["game_id"], [])
        selected = row["selected"]
        selected_players = [p for p in players if p["table_id"] == selected]
        if not selected_players:  # A concurrently deleted game can disappear between the two reads.
            continue
        summary = read_summary(db, row["summary"], {(p["table_id"], p["user_id"]): p["username"] for p in players})
        first = selected_players[0]
        roster = selected_players
        if summary:
            roster = []
            for side in ("open", "closed"):
                table = summary["tables"][side]
                if table.get("payload"):
                    indexed = [p for p in players if p["table_id"] == side]
                    roster.extend([{**p, "table_id": side} for p in table.get("players", [])] or indexed)
        card = {"game_id": row["game_id"], "created_at": str(row["created_at"]),
            "rule": first["rule"], "sub_rule": first["sub_rule"], "match_type": first["match_type"],
            "room_type": first["room_type"], "players": roster, "match_summary": summary}
        if user_id is not None:
            card.update(is_favorite=bool(row["is_favorite"]), note=row["note"] or "")
        result.append(card)
    return result
