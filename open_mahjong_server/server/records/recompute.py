"""Rebuild derived paired replay fields strictly from the two saved tables and durable board identities."""
from copy import deepcopy
from collections import Counter
from psycopg2.extras import Json

from .format import digest, public_players, restore_source, validate_match
from .wall_alignment import RecordConflict


def fill_missing(old, fresh):
    for field, value in fresh.items():
        if field not in old or old[field] is None:
            old[field] = deepcopy(value)
        elif isinstance(old[field], dict) and isinstance(value, dict):
            fill_missing(old[field], value)


def recover_settled_players(body, sources, snapshot):
    """Restore missing public seat fields from the same two officially recorded final tables."""
    if not snapshot or snapshot.get("status") != "completed" or snapshot.get("id") != body["match_id"]:
        return
    ids = {key: t["table_record_id"] for key, t in body["tables"].items() if t.get("payload") is not None}
    if set(ids) != {"open", "closed"} or snapshot.get("record_ids") != ids:
        return
    saved_tables = snapshot.get("final_players") or {}
    if not isinstance(saved_tables, dict):
        return
    for key, table in body["tables"].items():
        final_seats = {}
        rows = saved_tables.get(key)
        if not isinstance(rows, list):
            continue
        for row in rows:
            if not isinstance(row, dict):
                continue
            seat = row.get("original_player_index")
            if type(seat) is int and 0 <= seat < 4:
                # Two claims on the same original seat cannot prove a player's identity.
                final_seats[seat] = row if seat not in final_seats else None
        title = sources[key]["game_title"]
        original_seats = Counter(p.get("original_player_index") for p in table["players"])
        for player in table["players"]:
            seat, uid = player.get("original_player_index"), player.get("user_id")
            saved = final_seats.get(seat)
            title_uid = title.get(f"p{seat}_uid")
            if (not saved or type(seat) is not int or original_seats[seat] != 1
                    or type(uid) is not int or type(saved.get("user_id")) is not int
                    or type(title_uid) is not int or uid != saved["user_id"] or uid != title_uid):
                continue
            if any(player.get(f) is not None and saved.get(f) is not None and player[f] != saved[f]
                   for f in ("rank", "score")):
                continue
            for field in ("rank", "score", "title_used", "character_used", "profile_used", "avatar_frame_used", "voice_used"):
                value = saved.get(field)
                if player.get(field) is None and type(value) is int and (field != "rank" or 1 <= value <= 4):
                    player[field] = value


def recover_team_fields(body, sources, snapshot):
    """Use saved final results from either table, or the published match's own final barrier."""
    if body["match_format"] != "paired_tables_2v2" or body.get("result") and body.get("settlements"):
        return
    results, settlements = [], []
    for payload in sources.values():
        results.extend(filter(None, (payload.get("doubles_result"), payload["game_title"].get("doubles_result"))))
        boards = (payload.get("duplicate_match") or {}).get("boards")
        if boards:
            settlements.append(boards)
    source_ids = {key: table["table_record_id"] for key, table in body["tables"].items() if table.get("payload")}
    completed = (snapshot and snapshot.get("status") == "completed" and len(source_ids) == 2
                 and snapshot.get("record_ids") == source_ids and snapshot.get("id") == body["match_id"])
    if completed and snapshot.get("results"):
        settlements.append(snapshot["results"])
    for field, evidence in (("result", results), ("settlements", settlements)):
        if not body.get(field) and evidence:
            if any(value != evidence[0] for value in evidence[1:]):
                raise RecordConflict(f"{body['match_id']}: conflicting recorded {field}")
            body[field] = deepcopy(evidence[0])
    if body.get("result") or not completed:
        return
    from ..game_calculation.sichuan.duplicate_rules import XUELIU_DUPLICATE, score_totals
    scores, boards = snapshot.get("scores"), snapshot.get("results")
    planned, played = snapshot.get("planned_boards"), snapshot.get("completed_boards")
    profile = snapshot.get("profile") or {}
    if (not isinstance(scores, list) or len(scores) != 2 or any(type(n) is not int for n in scores)
            or not boards or not all(isinstance(b, dict) and "raw_difference" in b for b in boards)
            or type(planned) is not int or type(played) is not int or len(boards) != played
            or profile.get("scoring") not in ("imp", "raw_difference") or profile.get("sub_rule") != body["sub_rule"]):
        return
    scale = profile.get("imp_scale", "jj" if body["sub_rule"] == XUELIU_DUPLICATE else "sbr")
    totals = score_totals(boards, imp_scale=scale)
    if scores != totals["imp_scores" if profile["scoring"] == "imp" else "small_scores"]:
        raise RecordConflict(f"{body['match_id']}: final barrier scores differ from board settlements")
    winner = None if scores[0] == scores[1] else int(scores[1] > scores[0])
    def team_map(values):
        return {str(team): value for team, value in enumerate(values)}

    body["result"] = {"scores": team_map(scores), "winner_team_id": winner,
                      "small_scores": team_map(totals["small_scores"]), "imp_scores": team_map(totals["imp_scores"]),
                      "reason": profile["scoring"], "extra_boards": max(0, played - planned),
                      "match_points": team_map([0.5 if winner is None else int(team == winner) for team in (0, 1)])}


def repair_fields(archive, proved):
    """Keep existing facts/extensions; fill verified derived fields and only upgrade alignment quality."""
    repaired = deepcopy(archive)
    for k in ("rule","sub_rule","rule_profile","teams","settlements","result","sync_mode"):
        if repaired.get(k) in (None, [], {}) and proved.get(k) not in (None, [], {}):
            repaired[k] = deepcopy(proved[k])
    catalog = repaired.setdefault("board_catalog", [])
    indexed = {b["board_instance_id"]:b for b in catalog}
    for board in proved["board_catalog"]:
        if board["board_instance_id"] in indexed:
            before = indexed[board["board_instance_id"]]
            if (before.get("wall_definition") and board.get("wall_definition")
                    and before["wall_definition"] != board["wall_definition"]):
                raise RecordConflict("Recomputed original wall conflicts with a published board")
            fill_missing(before, board)
        else:
            if any(b.get("board_no") == board["board_no"] for b in catalog):
                raise RecordConflict("Cannot change an established board attempt")
            catalog.append(deepcopy(board))
    for key, new_table in proved["tables"].items():
        table = repaired["tables"].get(key)
        if not table or not table.get("payload"):
            continue
        rounds = table.setdefault("round_meta", {})
        for r,new_meta in new_table["round_meta"].items():
            if r not in rounds:
                rounds[r] = deepcopy(new_meta)
                continue
            old = rounds[r]
            if old.get("legacy_node_map") not in (None,new_meta["legacy_node_map"]):
                raise RecordConflict("Recomputation would change a historical node link")
            if old.get("board_instance_id") and new_meta.get("board_instance_id") and old["board_instance_id"] != new_meta["board_instance_id"]:
                raise RecordConflict("Recomputation would change the board attempt of a historical round")
            if old.get("alignment_status") != "exact" and new_meta.get("alignment_status") == "exact":
                for field in ("alignment_status","alignment_reason","initial_anchors"):
                    old[field] = deepcopy(new_meta[field])
            fill_missing(old, new_meta)
            if old.get("action_meta") == [] and new_meta["action_meta"]:
                old["action_meta"] = deepcopy(new_meta["action_meta"])
            if "action_meta" in old and len(old["action_meta"]) == len(new_meta["action_meta"]):
                for action, new_action in zip(old["action_meta"], new_meta["action_meta"]):
                    if action.get("wall_ref") and new_action.get("wall_ref") and action["wall_ref"] != new_action["wall_ref"]:
                        raise RecordConflict("Recomputed wall origin conflicts with a recorded origin")
                    if not action.get("wall_ref") and new_action.get("wall_ref"):
                        action["wall_ref_basis"] = new_action["wall_ref_basis"]
                    fill_missing(action, new_action)
    return validate_match(repaired)


def recompute_database(conn, *, dry_run=False):
    from .migrate import make_replay
    from .store import summarize_match
    from .migrate_postgres import relation_kind
    try:
        with conn.cursor() as cur:
            if relation_kind(cur,"game_records") != "v":
                raise RecordConflict("Run the one-time cutover first")
            cur.execute("SELECT DISTINCT replay_id FROM game_record_tables WHERE table_id IN ('open','closed') ORDER BY 1")
            ids = [r[0] for r in cur.fetchall()]
            snapshots = relation_kind(cur,"sichuan_duplicate_matches")
            repaired = partial = unproven = 0
            for game_id in ids:
                query = "SELECT record,summary FROM game_replays WHERE game_id=%s"
                cur.execute(query if dry_run else query + " FOR UPDATE", (game_id,))
                row = cur.fetchone()
                if not row:
                    continue
                body, summary = row
                snapshot = None
                if snapshots:
                    cur.execute("""SELECT data - 'final_records' - 'checkpoints' - 'table_checkpoints' - 'room_snapshots'
                                - 'pending_checkpoints' FROM sichuan_duplicate_matches WHERE id::text=%s""", (body["match_id"],))
                    item = cur.fetchone()
                    snapshot = item[0] if item else None
                if snapshot is None:
                    # Older restored source titles can lack the paired ID which was repaired at cutover.
                    snapshot = {"id": body["match_id"], "profile": body.get("rule_profile"),
                                "record_ids": {key: t["table_record_id"] for key, t in body["tables"].items()
                                               if t.get("payload") is not None}}
                sources = []
                for t in body["tables"].values():
                    if t.get("payload") is None:
                        continue
                    raw = restore_source(t)
                    if digest(raw) != t["source_sha256"]:
                        raise RecordConflict(f"{game_id}: original table checksum differs")
                    sources.append({"game_id":t["table_record_id"], "record":raw, "players":t.get("players",[])})
                if not sources:
                    raise RecordConflict(f"{game_id}: no recorded table")
                rebuilt = make_replay(sources, snapshot=snapshot)
                candidate = repair_fields(body, rebuilt)
                for table, new in rebuilt["tables"].items():
                    if new.get("payload") is None:
                        continue
                    stored = candidate["tables"][table]["players"]
                    # Reconstruction starts from these exact rows and only appends verified seats.
                    # Fill in place so all other stored fields and the established order survive.
                    for old_player, new_player in zip(stored, new["players"]):
                        if old_player.get("user_id") != new_player.get("user_id"):
                            raise RecordConflict("Recomputation would replace an existing player")
                        fill_missing(old_player, new_player)
                    stored.extend(deepcopy(new["players"][len(stored):]))
                new_summary = summarize_match(candidate)
                if summary and "tables" in summary:
                    # A legacy library field hidden by every public reader is not a missing replay fact.
                    summary = {**summary, "tables": {key: {**t, "players": public_players(t.get("players", []))}
                                                      for key, t in summary["tables"].items()}}
                partial += int(candidate.get("completeness") == "partial")
                unproven += sum(m["alignment_status"] in ("partial","unavailable") for t in candidate["tables"].values()
                                for m in t.get("round_meta",{}).values())
                if candidate != body or new_summary != summary:
                    repaired += 1
                    if not dry_run:
                        cur.execute("UPDATE game_replays SET record=%s,summary=%s WHERE game_id=%s",
                                    (Json(candidate),Json(new_summary),game_id))
        if dry_run:
            conn.rollback()
        else:
            conn.commit()
        return {"checked":len(ids), "changed":repaired, "partial_matches":partial,"unresolved_rounds":unproven}
    except BaseException:
        conn.rollback()
        raise
