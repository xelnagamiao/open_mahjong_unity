"""Lossless PostgreSQL representation of ordinary single-table V2 replays."""
from .format import FORMAT, VERSION, single_action_metadata
from .wall_alignment import RecordConflict


CODEC = "om.single_round_meta.v1"


def _main_table(record):
    tables = record.get("tables")
    if isinstance(tables, dict) and tables.keys() == {"main"}:
        table = tables["main"]
        if isinstance(table, dict) and table.get("table_id") == "main":
            return table
    return None


def _replace_round_meta(record, rounds):
    table = {**record["tables"]["main"], "round_meta": rounds}
    return {**record, "tables": {**record["tables"], "main": table}}


def compact_record(record):
    """Omit only fields that old V2 consumers can recover exactly."""
    if (record.get("format") != FORMAT or record.get("schema_version") != VERSION
            or record.get("match_format") != "single_table" or "storage_codec" in record):
        return record
    table = _main_table(record)
    if not table or not table.get("payload"):
        return record
    rounds = table["payload"]["game_round"]
    metadata = table["round_meta"].copy()
    if metadata.keys() != rounds.keys():
        return record
    changed = False
    for key, meta in table["round_meta"].items():
        # A marker applies to the whole body. Do not reinterpret an already
        # missing field in an unmarked historical record as one we omitted.
        if "action_meta" not in meta or "legacy_node_map" not in meta:
            return record
        rd = rounds[key]
        stored = meta.copy()
        times = rd.get("action_times_ms", [])
        # JavaScript cannot distinguish 2.0 from 2 or represent all int64s.
        # Keep explicit metadata in those cases for identical cross-client reads.
        if (not any(type(t) is float or type(t) is int and t > 2**53 - 1 for t in times)
                and meta["action_meta"] == single_action_metadata(rd)
                and all(a["occurred_at_ms"] is None or type(a["occurred_at_ms"]) is int
                        for a in meta["action_meta"])):
            del stored["action_meta"]
        nodes = meta["legacy_node_map"]
        if (isinstance(nodes, list) and len(nodes) == len(rd["action_ticks"]) + 1
                and all(type(node) is int and node == i for i, node in enumerate(nodes))):
            del stored["legacy_node_map"]
        if len(stored) != len(meta):
            changed = True
            metadata[key] = stored
    if not changed:
        return record
    return {**_replace_round_meta(record, metadata), "storage_codec": CODEC}


def expand_record(record):
    """Restore the public V2 body, leaving explicit exceptions and extensions intact."""
    if (record.get("storage_codec") != CODEC or record.get("match_format") != "single_table"
            or record.get("format") != FORMAT or record.get("schema_version") != VERSION):
        return record
    table = _main_table(record)
    if not table or not table.get("payload"):
        raise RecordConflict("Invalid compact single replay table")
    rounds = table["payload"]["game_round"]
    if table["round_meta"].keys() != rounds.keys():
        raise RecordConflict("Compact replay round metadata mismatch")
    metadata = {}
    for key, meta in table["round_meta"].items():
        rd = rounds[key]
        expanded = meta.copy()
        if "action_meta" not in meta:
            expanded["action_meta"] = single_action_metadata(rd)
        if "legacy_node_map" not in meta:
            expanded["legacy_node_map"] = list(range(len(rd["action_ticks"]) + 1))
        metadata[key] = expanded
    result = _replace_round_meta(record, metadata)
    del result["storage_codec"]
    return result
