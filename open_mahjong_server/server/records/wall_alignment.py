"""Physical wall identities, independent of table pace and replay node numbers."""
from copy import deepcopy


class RecordConflict(ValueError):
    pass


def wall_ref(instance, seat, offset):
    if not isinstance(instance, str) or not instance:
        raise RecordConflict("Missing board instance")
    if type(seat) is not int or seat not in (-1, 0, 1, 2, 3) or type(offset) is not int or offset < 0:
        raise RecordConflict("Invalid physical wall origin")
    return {"board_instance_id": instance, "zone": "common_wall" if seat == -1 else "seat_wall",
            "source_original_seat": None if seat == -1 else seat, "offset": offset}


def ref_key(ref):
    if not isinstance(ref, dict) or ref.get("zone") not in ("seat_wall", "common_wall"):
        raise RecordConflict("Invalid wall reference")
    seat = ref.get("source_original_seat")
    if ref["zone"] == "common_wall":
        if seat is not None:
            raise RecordConflict("Common wall has no seat owner")
        seat = -1
    normalized = wall_ref(ref.get("board_instance_id"), seat, ref.get("offset"))
    return (normalized["board_instance_id"], normalized["zone"], seat, normalized["offset"])


def validate_ref(ref, layout):
    _, zone, _, offset = ref_key(ref)
    limit = (24 if layout == "xueliu" else 0) if zone == "common_wall" else (8 if layout == "xueliu" else 14)
    if offset >= limit:
        raise RecordConflict("Wall origin outside frozen layout")


def round_origins(round_data, instance, *, layout="standard", reconstruct=False, board=None):
    """Normalize recorded dw; deterministically replay missing dw only with a full board.

    Current-player indices are inverted using seats. Remaining-wall array offsets
    are never mistaken for original slots (the dealer has already drawn slot 0).
    """
    seats = round_data.get("seats")
    if not isinstance(seats, list) or sorted(seats) != [0, 1, 2, 3]:
        raise RecordConflict("Invalid original-to-current seat mapping")
    inverse = {current: original for original, current in enumerate(seats)}
    current = round_data.get("start_player_index")
    dealer = round_data.get("dealer_index")
    if current not in inverse or dealer not in inverse:
        raise RecordConflict("Missing initial player/dealer")
    dealer_original = inverse[dealer]
    consumed = [0] * 4
    consumed[dealer_original] = 1
    retired = set()
    shared = None
    size = 8 if layout == "xueliu" else 14
    used = {(dealer_original, 0)}
    initial = [{"kind": "dealer_initial_draw", "wall_ref": wall_ref(instance, dealer_original, 0),
                "actor_original_seat": dealer_original, "node": 0, "phase": "after_draw"}]
    metadata = []
    reliable = True

    def next_player(player):
        for offset in range(1, 5):
            candidate = (player + offset) % 4
            if inverse[candidate] not in retired:
                return candidate
        return player

    def source_for(actor):
        nonlocal shared
        if shared is not None:
            return shared[0] if shared else None
        if consumed[actor] < size:
            return actor, consumed[actor]
        partner = (actor + 2) % 4
        if layout != "xueliu" and partner in retired and consumed[partner] < size:
            return partner, consumed[partner]
        return None

    def consume(source):
        nonlocal shared
        seat, offset = source
        if shared is not None:
            if not shared or shared[0] != source:
                raise RecordConflict("Shared wall consumption order conflicts")
            shared.pop(0)
        else:
            if seat < 0 or offset != consumed[seat]:
                raise RecordConflict("Fixed wall consumption order conflicts")
            consumed[seat] += 1
            if layout == "xueliu" and consumed[seat] == size:
                shared = [(s, i) for s in range(4) for i in range(consumed[s], size)]
                shared.extend((-1, i) for i in range(24))

    for index, tick in enumerate(round_data.get("action_ticks", [])):
        if not isinstance(tick, list) or not tick or not isinstance(tick[0], str):
            raise RecordConflict("Invalid action tick")
        code = tick[0]
        actor = inverse.get(current)
        meta = {"actor_original_seat": actor, "occurred_at_ms": None,
                "wall_ref": None, "wall_ref_basis": "not_applicable"}
        if code in ("d", "gd"):
            recorded = len(tick) >= 6 and tick[2] == "dw"
            if recorded:
                if tick[5] not in inverse:
                    raise RecordConflict("Invalid draw actor")
                current = tick[5]
                actor = inverse[current]
                source = (tick[3], tick[4])
                basis = "recorded"
            elif reconstruct and board is not None and reliable:
                # Blood battle can skip exhausted/retired walls without a tick.
                source = source_for(actor)
                for _ in range(4):
                    if source is not None:
                        break
                    current = next_player(current)
                    actor = inverse[current]
                    source = source_for(actor)
                basis = "reconstructed"
            else:
                source, basis = None, "unavailable"
                reliable = False
            meta.update(actor_original_seat=actor, wall_ref_basis=basis)
            if source is not None:
                ref = wall_ref(instance, *source)
                validate_ref(ref, layout)
                if source in used:
                    raise RecordConflict("Physical wall slot consumed twice")
                if board is not None:
                    origin_tiles = board.get("common_wall", []) if source[0] == -1 else board["walls"][source[0]]
                    if source[1] >= len(origin_tiles) or origin_tiles[source[1]] != tick[1]:
                        raise RecordConflict("Draw tile conflicts with original wall")
                if reliable:
                    expected = source_for(actor)
                    if source != expected:
                        raise RecordConflict("Draw origin conflicts with frozen wall policy")
                    consume(source)
                used.add(source)
                meta["wall_ref"] = ref
        elif code in ("p", "g", "cl", "cm", "cr", "bh"):
            if len(tick) > 2 and type(tick[2]) is int and tick[2] in inverse:
                current = tick[2]
                meta["actor_original_seat"] = inverse[current]
        elif code == "c":
            current = next_player(current)
        elif code == "reset":
            if len(tick) < 2 or tick[1] not in inverse:
                raise RecordConflict("Invalid reset actor")
            current = tick[1]
            meta["actor_original_seat"] = None
        elif code.startswith("hu_"):
            if len(tick) > 1 and type(tick[1]) is int and tick[1] in inverse:
                winner = tick[1]
                meta["actor_original_seat"] = inverse[winner]
                if layout != "xueliu":
                    retired.add(inverse[winner])
                if code == "hu_self":
                    current = next_player(winner)
                elif inverse.get(current) in retired:
                    current = next_player(current)
        elif code in ("ask_hand", "ask_other"):
            raise RecordConflict("Transient ask ticks must be migrated before indexing")
        elif code not in ("ag", "jg", "ca"):
            meta["actor_original_seat"] = None
        metadata.append(meta)
    missing = any(m["wall_ref_basis"] == "unavailable" for m in metadata)
    return {"initial_anchors": initial, "action_meta": metadata,
            "alignment_status": "partial" if missing else "exact", "alignment_reason": "missing_draw_origin" if missing else None}


def locate(table, ref, phase="after_draw"):
    """Only inspect published table data; absence before finality is not evidence."""
    key = ref_key(ref)
    if phase not in ("before_draw", "after_draw"):
        return {"status": "PHASE_UNAVAILABLE"}
    if not table or table.get("record_state") == "missing":
        return {"status": "TABLE_MISSING"}
    matches = []
    for round_key, meta in table.get("round_meta", {}).items():
        if meta.get("board_instance_id") != key[0]:
            continue
        if meta.get("alignment_status") == "unavailable":
            return {"status": "ORIGIN_UNAVAILABLE"}
        for anchor in meta.get("initial_anchors", []):
            if ref_key(anchor["wall_ref"]) == key:
                if phase != "after_draw":
                    return {"status": "PHASE_UNAVAILABLE"}
                matches.append({"status": "EXACT", "table_round_id": meta["table_round_id"],
                                "round_key": round_key, "node": 0, "actor_original_seat": anchor["actor_original_seat"]})
        for i, action in enumerate(meta.get("action_meta", [])):
            if action.get("wall_ref") and ref_key(action["wall_ref"]) == key:
                matches.append({"status": "EXACT", "table_round_id": meta["table_round_id"],
                                "round_key": round_key, "node": i + (phase == "after_draw"),
                                "actor_original_seat": action.get("actor_original_seat")})
        if len(matches) > 1:
            return {"status": "DATA_CONFLICT"}
        if matches:
            return matches[0]
        if meta.get("alignment_status") != "exact":
            return {"status": "ORIGIN_UNAVAILABLE"}
        rd = table["payload"]["game_round"][round_key]
        ended = any(t and t[0] == "end" for t in rd.get("action_ticks", []))
        return {"status": "NOT_CONSUMED" if ended or table.get("record_state") == "complete" else "NOT_VISIBLE_YET"}
    return {"status": "INSTANCE_UNVERIFIED" if table.get("record_state") == "complete" else "NOT_VISIBLE_YET"}
