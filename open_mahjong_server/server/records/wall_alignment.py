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


class RoundOriginTracker:
    """Stateful wall-origin aligner supporting O(delta) incremental advancement."""

    def __init__(self, round_data, instance, *, layout="standard", reconstruct=False, board=None):
        seats = round_data.get("seats")
        if not isinstance(seats, list) or sorted(seats) != [0, 1, 2, 3]:
            raise RecordConflict("Invalid original-to-current seat mapping")
        self.instance = instance
        self.layout = layout
        self.reconstruct = reconstruct
        self.board = board
        self.inverse = {current: original for original, current in enumerate(seats)}
        self.current = round_data.get("start_player_index")
        dealer = round_data.get("dealer_index")
        if self.current not in self.inverse or dealer not in self.inverse:
            raise RecordConflict("Missing initial player/dealer")
        dealer_original = self.inverse[dealer]
        self.consumed = [0] * 4
        self.consumed[dealer_original] = 1
        self.retired = set()
        self.shared = None
        self.size = 8 if layout == "xueliu" else 14
        self.used = {(dealer_original, 0)}
        self.initial_anchors = [{
            "kind": "dealer_initial_draw",
            "wall_ref": wall_ref(instance, dealer_original, 0),
            "actor_original_seat": dealer_original,
            "node": 0,
            "phase": "after_draw",
        }]
        self.reliable = True
        self.missing = False

    def fork(self):
        cloned = RoundOriginTracker.__new__(RoundOriginTracker)
        cloned.instance = self.instance
        cloned.layout = self.layout
        cloned.reconstruct = self.reconstruct
        cloned.board = self.board
        cloned.inverse = self.inverse
        cloned.current = self.current
        cloned.consumed = list(self.consumed)
        cloned.retired = set(self.retired)
        cloned.shared = list(self.shared) if self.shared is not None else None
        cloned.size = self.size
        cloned.used = set(self.used)
        cloned.initial_anchors = self.initial_anchors
        cloned.reliable = self.reliable
        cloned.missing = self.missing
        return cloned

    @property
    def alignment_status(self):
        return "partial" if self.missing else "exact"

    @property
    def alignment_reason(self):
        return "missing_draw_origin" if self.missing else None

    def _next_player(self, player):
        for offset in range(1, 5):
            candidate = (player + offset) % 4
            if self.inverse[candidate] not in self.retired:
                return candidate
        return player

    def _source_for(self, actor):
        if self.shared is not None:
            return self.shared[0] if self.shared else None
        if self.consumed[actor] < self.size:
            return actor, self.consumed[actor]
        partner = (actor + 2) % 4
        if self.layout != "xueliu" and partner in self.retired and self.consumed[partner] < self.size:
            return partner, self.consumed[partner]
        return None

    def _consume(self, source):
        seat, offset = source
        if self.shared is not None:
            if not self.shared or self.shared[0] != source:
                raise RecordConflict("Shared wall consumption order conflicts")
            self.shared.pop(0)
        else:
            if seat < 0 or offset != self.consumed[seat]:
                raise RecordConflict("Fixed wall consumption order conflicts")
            self.consumed[seat] += 1
            if self.layout == "xueliu" and self.consumed[seat] == self.size:
                self.shared = [(s, i) for s in range(4) for i in range(self.consumed[s], self.size)]
                self.shared.extend((-1, i) for i in range(24))

    def advance(self, ticks, times_ms=None):
        metadata = []
        for idx, tick in enumerate(ticks):
            if not isinstance(tick, list) or not tick or not isinstance(tick[0], str):
                raise RecordConflict("Invalid action tick")
            code = tick[0]
            actor = self.inverse.get(self.current)
            occurred = None
            if times_ms is not None and idx < len(times_ms):
                ts = times_ms[idx]
                occurred = ts if type(ts) is int and ts >= 0 else None
            meta = {"actor_original_seat": actor, "occurred_at_ms": occurred,
                    "wall_ref": None, "wall_ref_basis": "not_applicable"}
            if code in ("d", "gd"):
                recorded = len(tick) >= 6 and tick[2] == "dw"
                if recorded:
                    if tick[5] not in self.inverse:
                        raise RecordConflict("Invalid draw actor")
                    self.current = tick[5]
                    actor = self.inverse[self.current]
                    source = (tick[3], tick[4])
                    basis = "recorded"
                elif self.reconstruct and self.board is not None and self.reliable:
                    # Blood battle can skip exhausted/retired walls without a tick.
                    source = self._source_for(actor)
                    for _ in range(4):
                        if source is not None:
                            break
                        self.current = self._next_player(self.current)
                        actor = self.inverse[self.current]
                        source = self._source_for(actor)
                    basis = "reconstructed"
                else:
                    source, basis = None, "unavailable"
                    self.reliable = False
                meta.update(actor_original_seat=actor, wall_ref_basis=basis)
                if basis == "unavailable":
                    self.missing = True
                if source is not None:
                    ref = wall_ref(self.instance, *source)
                    validate_ref(ref, self.layout)
                    if source in self.used:
                        raise RecordConflict("Physical wall slot consumed twice")
                    if self.board is not None:
                        origin_tiles = self.board.get("common_wall", []) if source[0] == -1 else self.board["walls"][source[0]]
                        if source[1] >= len(origin_tiles) or origin_tiles[source[1]] != tick[1]:
                            raise RecordConflict("Draw tile conflicts with original wall")
                    if self.reliable:
                        expected = self._source_for(actor)
                        if source != expected:
                            raise RecordConflict("Draw origin conflicts with frozen wall policy")
                        self._consume(source)
                    self.used.add(source)
                    meta["wall_ref"] = ref
            elif code in ("p", "g", "cl", "cm", "cr", "bh"):
                if len(tick) > 2 and type(tick[2]) is int and tick[2] in self.inverse:
                    self.current = tick[2]
                    meta["actor_original_seat"] = self.inverse[self.current]
            elif code == "c":
                self.current = self._next_player(self.current)
            elif code == "reset":
                if len(tick) < 2 or tick[1] not in self.inverse:
                    raise RecordConflict("Invalid reset actor")
                self.current = tick[1]
                meta["actor_original_seat"] = None
            elif code.startswith("hu_"):
                if len(tick) > 1 and type(tick[1]) is int and tick[1] in self.inverse:
                    winner = tick[1]
                    meta["actor_original_seat"] = self.inverse[winner]
                    if self.layout != "xueliu":
                        self.retired.add(self.inverse[winner])
                    if code == "hu_self":
                        self.current = self._next_player(winner)
                    elif self.inverse.get(self.current) in self.retired:
                        self.current = self._next_player(self.current)
            elif code in ("ask_hand", "ask_other"):
                raise RecordConflict("Transient ask ticks must be migrated before indexing")
            elif code not in ("ag", "jg", "ca"):
                meta["actor_original_seat"] = None
            metadata.append(meta)
        return metadata


def round_origins(round_data, instance, *, layout="standard", reconstruct=False, board=None):
    """Normalize recorded dw; deterministically replay missing dw only with a full board.

    Current-player indices are inverted using seats. Remaining-wall array offsets
    are never mistaken for original slots (the dealer has already drawn slot 0).
    """
    tracker = RoundOriginTracker(round_data, instance, layout=layout, reconstruct=reconstruct, board=board)
    metadata = tracker.advance(round_data.get("action_ticks", []))
    return {"initial_anchors": tracker.initial_anchors, "action_meta": metadata,
            "alignment_status": tracker.alignment_status, "alignment_reason": tracker.alignment_reason}


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
