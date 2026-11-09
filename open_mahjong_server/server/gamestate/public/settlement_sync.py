"""Authoritative, viewer-scoped round-result restoration shared by every rule.

Settlement presentation is a snapshot, not another scoring operation. The wire
metadata is added at delivery, so a queued packet never restarts a ready clock.
Cached packets retain their original seat projection and contain no live objects.
"""
from __future__ import annotations

from copy import deepcopy
import hashlib
import json
import logging
import math
import time

from .connection_delivery import send_bounded

logger = logging.getLogger(__name__)


RESULT_FIELDS = {"show_result": "show_result_info", "show_shuhewei": "show_shuhewei_info"}


def _scope(state):
    return ":".join(str(getattr(state, name, 0)) for name in
                    ("gamestate_id", "round_index", "current_round"))


def _store(state):
    scope = _scope(state)
    store = getattr(state, "_settlement_sync", None)
    if store is None or store["round_id"] != scope:
        store = dict(round_id=scope, records={}, latest=None, revision=0,
                     ready=None, deadline=None, clock=None, ready_id=None)
        state._settlement_sync = store
    return store


def _players(state):
    return getattr(state, "player_list", None) or getattr(state, "players", ())


def _seat(player):
    return getattr(player, "player_index", getattr(player, "index", -1))


def _viewer(state, websocket):
    connections = getattr(getattr(state, "game_server", None), "user_id_to_connection", {})
    for player in _players(state):
        connection = connections.get(player.user_id)
        if connection is not None and getattr(connection, "websocket", None) is websocket:
            return _seat(player)
    return None


def _packet(payload):
    if isinstance(payload, dict):
        return payload
    return payload.model_dump(exclude_none=True)


def _waiting_ready(state):
    return (getattr(state, "game_status", None) in ("waiting_ready", "READY")
            or bool(getattr(state, "_ready_phase_active", False)))


def _ended(state):
    return (_waiting_ready(state)
            or str(getattr(state, "game_status", "")).upper() in ("END", "FINISHED", "ROUND_END", "WAITING_DEALER_CHOICE")
            or getattr(state, "phase", None) == "round_end")


def _ready(state):
    if not _waiting_ready(state):
        return {}
    if hasattr(state, "_ready_players"):
        return {str(_seat(p)): _seat(p) in state._ready_players for p in _players(state)}
    actions = getattr(state, "action_dict", {})
    return {str(_seat(p)): "ready" not in actions.get(_seat(p), ()) for p in _players(state)}


def begin_ready(state, seconds, *, clock=None):
    """Publish the same clock the ready waiter enforces, before its first send."""
    # Taiwan-derived rules keep one public terminal packet even when every
    # human is offline. Bind its identity before starting the ready deadline.
    terminal = getattr(state, "_terminal_result", None)
    if terminal:
        for player in _players(state):
            remember_result(state, deepcopy(terminal), _seat(player))
    store = _store(state)
    store["clock"] = clock or time.monotonic
    store["deadline"] = store["clock"]() + max(0.0, float(8 if seconds is None else seconds))
    store["ready_id"] = store["latest"]
    store["revision"] += 1
    return store["deadline"]


def ready_remaining(state):
    store = _store(state)
    if store["deadline"] is None or store["ready_id"] != store["latest"]:
        return None
    return max(0.0, store["deadline"] - store["clock"]())


def _identity(kind, info):
    # These identify a panel independently of recipient, tick refresh, private
    # hand projections and silent restoration. A later win changes its scores.
    fields = ("hu_class", "hepai_player_index", "blood_battle_step", "blood_end_reason",
              "liuju_step", "cha_payer_index", "liuju_status", "liuju_status_final",
              "player_to_score", "score_changes", "blood_round_changes", "player_fu",
              "guizhou_round_changes", "hu_score", "next_status", "defer_score_settlement")
    identity = {key: info[key] for key in fields if key in info}
    # Blood-flow can win twice with identical deferred scores in one hand.
    # Each original broadcast has a distinct tick, retained by its cached view.
    if info.get("round_continues") or info.get("blood_battle_step") == "mid_win":
        identity["action_tick"] = info.get("action_tick")
    return hashlib.sha256(json.dumps([kind, identity], sort_keys=True, default=str).encode()).hexdigest()[:20]


def remember_result(state, payload, viewer_index=None):
    """Cache after building the viewer projection, including for offline seats."""
    packet = _packet(payload)
    kind = packet.get("type", "").rsplit("/", 1)[-1]
    field = RESULT_FIELDS.get(kind)
    if not field or not packet.get(field):
        return packet
    store = _store(state)
    key = _identity(kind, packet[field])
    record = store["records"].get(key)
    if record is None:
        sequence = getattr(state, "_settlement_sequence", 0) + 1
        state._settlement_sequence = sequence
        record = dict(id=f'{store["round_id"]}:{sequence}', sequence=sequence, views={})
        store["records"][key] = record
        store["latest"] = key
        store["revision"] += 1
    # A legacy reconnect can rebuild an earlier panel. It must not move the
    # current step backwards or reset the current deadline.
    cached = deepcopy(packet)
    cached.pop("settlement_sync", None)
    cached.pop("settlement_restore", None)
    record["views"][viewer_index] = cached
    # Pin the originating round before any queued delivery can outlive it.
    metadata = snapshot(state, record=record)
    if isinstance(payload, dict):
        payload.setdefault("settlement_sync", metadata)
    elif hasattr(payload, "settlement_sync"):
        payload.settlement_sync = metadata
    return packet


def snapshot(state, *, record=None, restoring=False):
    store = _store(state)
    if record is None:
        record = store["records"].get(store["latest"])
    ready = _ready(state)
    if ready != store["ready"]:
        store["ready"] = ready
        store["revision"] += 1
    remaining = ready_remaining(state) if _waiting_ready(state) else None
    now = time.time() * 1000
    phase = "waiting_ready" if _waiting_ready(state) else "presenting" if record and _ended(state) else "playing"
    if record:
        sample = next(iter(record["views"].values()), {})
        info = sample.get("show_result_info") or sample.get("show_shuhewei_info") or {}
        if info.get("next_status") == "match_end":
            phase = "match_end"
    dealer_choice = None
    if getattr(state, '_dealer_choice', None):
        from ..game_riichi.dealer_choice import snapshot as dealer_choice_snapshot
        dealer_choice = dealer_choice_snapshot(state)
        if dealer_choice['selected'] is None:
            phase = 'waiting_dealer_choice'
            remaining = dealer_choice['remaining_ms'] / 1000
    metadata = dict(gamestate_id=str(getattr(state, "gamestate_id", "")),
                round_id=store["round_id"], result_id=record["id"] if record else "",
                sequence=record["sequence"] if record else getattr(state, "_settlement_sequence", 0),
                revision=store["revision"], phase=phase, restore=restoring,
                player_to_ready=ready, server_time_ms=now,
                remaining_ms=None if remaining is None else max(0, math.ceil(remaining * 1000)),
                deadline_ms=None if remaining is None else now + remaining * 1000)
    if dealer_choice is not None:
        metadata['dealer_choice'] = dealer_choice
    return metadata


def restore_result(state, viewer_index):
    if getattr(state, "room_rule", "") == "hongque":
        return None  # Its reconnect snapshot already contains round_result.
    if not _ended(state):
        return None
    store = _store(state)
    record = store["records"].get(store["latest"])
    packet = record["views"].get(viewer_index) if record else None
    terminal = getattr(state, "_terminal_result", None)
    if packet is None and terminal:
        # Taiwan-derived rules already keep one public final result. Capture it
        # before game_start so their legacy resend cannot act like another win.
        packet = terminal
        remember_result(state, packet, viewer_index)
        record = store["records"].get(store["latest"])
    if packet is None and hasattr(state, "build_final_settlement_payload"):
        # State-machine rules already expose a pure, viewer-safe projection.
        packet = state.build_final_settlement_payload(viewer_index)
        remember_result(state, packet, viewer_index)
        record = store["records"].get(store["latest"])
    if packet is None or record is None:
        return None
    result = deepcopy(packet)
    result.pop("game_info", None)  # The preceding table snapshot is authoritative.
    result["settlement_sync"] = snapshot(state, record=record, restoring=True)
    return result


def prepare_packet(state, payload, viewer_index=None):
    packet = _packet(payload)
    kind = packet.get("type", "").rsplit("/", 1)[-1]
    native = packet.get("hongque_state")
    if native is not None:
        if native.get("round_result"):
            # Native Hongque uses a snapshot, rather than a show_result event.
            remember_result(state, {"type": "gamestate/hongque/show_result",
                "show_result_info": {"hu_class": "hongque", "player_to_score": {
                    _seat(p): p.score for p in _players(state)}}}, viewer_index)
        packet = deepcopy(packet)
        packet["settlement_sync"] = snapshot(state, restoring=native.get("sync_mode") == "reconnect")
        return packet
    if kind not in (*RESULT_FIELDS, "ready_status", "game_start", "reconnect"):
        return packet
    packet = deepcopy(packet)
    if kind in RESULT_FIELDS:
        if not packet.get(RESULT_FIELDS[kind]):
            return packet
        remember_result(state, packet, viewer_index)
        store = _store(state)
        record = store["records"][_identity(kind, packet[RESULT_FIELDS[kind]])]
        packet["settlement_sync"] = snapshot(state, record=record)
    elif kind == "ready_status":
        packet["settlement_sync"] = snapshot(state)
    else:
        restored = restore_result(state, viewer_index) if viewer_index is not None else None
        packet["settlement_sync"] = snapshot(state, restoring=restored is not None)
        if restored is not None:
            packet["settlement_restore"] = [restored]
    return packet


async def send_game_packet(state, websocket, payload, viewer_index=None):
    """Bind every game message to its originating table before delivery."""
    packet = _packet(payload)
    if packet.get('game_end_info'):
        from ...database.game_rewards import attach_currency_to_end_packet
        packet = attach_currency_to_end_packet(state, packet)
    gid = getattr(state, "gamestate_id", None)
    if gid and (packet.get("type", "").startswith("gamestate/")
                or packet.get("type") in ("switch_seat", "refresh_player_tag_list")):
        # Copy the envelope: cached/per-viewer payloads must remain reusable.
        # Preserve an existing origin rather than relabelling a queued old packet.
        packet = {**packet, "gamestate_id": packet.get("gamestate_id") or str(gid)}
    origin = packet.get("settlement_sync")
    if origin and origin.get("round_id") != _scope(state):
        return
    kind = packet.get("type", "").rsplit("/", 1)[-1]
    if kind in (*RESULT_FIELDS, "ready_status", "game_start", "reconnect") or "hongque_state" in packet:
        if viewer_index is None:
            viewer_index = _viewer(state, websocket)
        packet = prepare_packet(state, packet, viewer_index)
    try:
        await send_bounded(websocket, packet)
    except TimeoutError:
        # A stalled recipient must not suspend start, final delivery or cleanup
        # for every other seat. Cancellation from the lifecycle owner still propagates.
        logger.warning("Game delivery timed out game=%s type=%s", gid, packet.get("type"))


def accepts_ready(state, result_id):
    """Older clients omit the id; new clients cannot confirm another settlement."""
    if result_id is None:
        return True
    store = _store(state)
    record = store["records"].get(store["latest"])
    return bool(_waiting_ready(state) and record and result_id == record["id"])
