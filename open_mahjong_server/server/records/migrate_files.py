"""Conversion shared by desktop file migration and the optional IndexedDB export/import tool."""
from collections import defaultdict
from contextlib import contextmanager, nullcontext
from copy import deepcopy
from pathlib import Path
import hashlib
import os
import shutil
import tempfile

from .format import FORMAT, digest, restore_source, validate_match
from .migrate import aliases, make_replay, read_json, source_identity, write_json
from .store import canonical_game_id, summarize_match
from .wall_alignment import RecordConflict


def convert_entries(entries):
    """Inputs and outputs are the actual key/value pairs used by the client."""
    index = entries.get("__index__", [])
    if not isinstance(index, list):
        raise RecordConflict("Invalid local index")
    cat = entries.get("__match_v2_catalog__") or {}
    details = {k: v for k,v in entries.items() if not k.startswith("__")}
    listed = list(dict.fromkeys(i["game_id"] for i in index))
    ordered = list(dict.fromkeys(listed + list(details) + list(cat.get("aliases", {}))))
    grouped, published = defaultdict(list), {}
    original_details, originals = defaultdict(list), {}
    old_locators = entries.get("__record_aliases__", {})
    for sid in ordered:
        detail = details.get(sid)
        old_alias = cat.get("aliases", {}).get(sid)
        if detail is None and old_alias:
            mid = old_alias["match_id"]
            filename = cat["matches"][mid]
            # Evicting a legacy table file could leave its published replay accessible by ID.
            # Keep that address without adding an evicted entry to the user's recent list.
            detail = {"game_id": sid, "record": entries.get("__match_v2/" + filename)}
        if not isinstance(detail, dict) or not isinstance(detail.get("record"), dict):
            raise RecordConflict(f"{sid}: indexed local source is missing")
        name = detail.get("game_id", sid)
        if name != sid:
            raise RecordConflict(f"{sid}: local key and detail identity differ")
        if "game_id" not in detail:
            detail = {**detail, "game_id": sid}
        raw = detail["record"]
        archive = detail.get("match_record")
        if raw.get("format") == FORMAT:
            archive = raw
            raw = None
        if old_alias:
            mid = old_alias["match_id"]
            filename = cat["matches"][mid]
            catalogued = entries.get("__match_v2/" + filename)
            if not catalogued or digest(catalogued) != cat["archive_hashes"][mid]:
                raise RecordConflict(f"{sid}: local archive failed its catalog checksum")
            if raw and digest(raw) != old_alias["source_sha256"]:
                raise RecordConflict(f"{sid}: local source failed its catalog checksum")
            if archive and archive != catalogued:
                raise RecordConflict(f"{sid}: embedded and published archives differ")
            archive = catalogued
        if archive:
            if archive.get("format") != FORMAT or archive.get("schema_version") != 2 or not isinstance(archive.get("tables"), dict):
                raise RecordConflict(f"{sid}: unrecognized saved replay")
            mid = archive["match_id"]
            if mid in published and published[mid] != archive:
                # A retained first-table detail may have an earlier partial revision.
                prev = published[mid]
                first, latest = sorted((prev, archive), key=lambda b: sum(t.get("payload") is not None for t in b["tables"].values()))
                if any(t.get("payload") is not None and (t["source_sha256"] != latest["tables"][k].get("source_sha256")
                        or t.get("round_meta") != latest["tables"][k].get("round_meta")) for k,t in first["tables"].items()):
                    raise RecordConflict(f"{mid}: incompatible embedded replay revisions")
                if first["completeness"] == latest["completeness"]:
                    raise RecordConflict(f"{mid}: two divergent versions of the same local replay")
                archive = latest
            published[mid] = archive
        if raw:
            source = {"game_id": sid, "record": raw, "players": detail.get("players", [])}
            mid, _ = source_identity(source, archive)
            grouped[mid].append(source)
            originals[sid] = raw
        original_details[mid].append(detail)
    result, locators, canonical_for = {}, dict(old_locators), {}
    for mid, group_details in original_details.items():
        sources = grouped[mid]
        archive = published.get(mid)
        if archive:
            present = {s["game_id"] for s in sources}
            # A saved eight-player replay can contain both tables even if only one old detail was downloaded.
            for t in archive["tables"].values():
                if t.get("payload") is not None and t["table_record_id"] not in present:
                    raw = restore_source(t)
                    if digest(raw) != t["source_sha256"]:
                        # The old archive may retain a node map for prompts whose native source is absent.
                        # Preserve its verified public timeline verbatim; mark original-source reconstruction unavailable.
                        continue
                    sources.append({"game_id": t["table_record_id"], "record": raw, "players": t["players"]})
        source_ids = {s["game_id"] for s in sources}
        unavailable = {k for k,t in (archive or {}).get("tables", {}).items()
                       if t.get("payload") is not None and t["table_record_id"] not in source_ids}
        if unavailable:
            body = deepcopy(archive)
            for s in sources:
                _, key = source_identity(s, body)
                from .format import preserve_source
                t = preserve_source(body["tables"][key], s["record"])
                if restore_source(t) != s["record"] or digest(s["record"]) != t["source_sha256"]:
                    raise RecordConflict(f"{mid}: incomplete sources conflict with published replay")
            for key in unavailable:
                body["tables"][key]["original_source_unavailable"] = True
            validate_match(body)
        else:
            body = make_replay(sources, archive=archive)
        game_id = canonical_game_id(body)
        if not game_id or game_id in result or body["match_format"] != "single_table" and game_id in originals:
            raise RecordConflict(f"{mid}: local replay key collision")
        available = next((d for d in group_details if d["game_id"] in details), group_details[0])
        primary = deepcopy({k:v for k,v in available.items()
                            if k not in ("record", "match_record", "selected_table_id", "legacy_cursor_map")})
        primary["game_id"], primary["record"] = game_id, body
        if body["match_format"] == "paired_tables_2v2":
            primary["players"] = [p for side in ("open","closed") for p in body["tables"][side].get("players", [])]
        for d in group_details:
            canonical_for[d["game_id"]] = game_id
            other = {k:v for k,v in d.items() if k not in ("record","players","game_id","match_record","selected_table_id",
                                                            "legacy_cursor_map") and primary.get(k) != v}
            if other:
                primary.setdefault("local_table_metadata", {})[d["game_id"]] = other
        result[game_id] = primary
        for sid, a in aliases(body, game_id).items():
            if sid in locators and locators[sid] != a:
                raise RecordConflict(f"{sid}: inconsistent local replay locator")
            if sid in cat.get("aliases", {}):
                original = cat["aliases"][sid]
                checksum = body["tables"][a["table_id"]]["source_sha256"]
                if checksum != original.get("source_sha256") or any(a[k] != original[k] for k in ("table_id","node_map","round_map")):
                    raise RecordConflict(f"{sid}: old cursor would move")
            locators[sid] = a
    rows, by_id = [], {}
    row_metadata = {row["game_id"]: row for row in index}
    for sid in listed:
        game_id = canonical_for[sid]
        source = row_metadata.get(sid, {})
        if game_id in by_id:
            row = by_id[game_id]
            row["is_favorite"] = bool(row.get("is_favorite") or source.get("is_favorite"))
            if source.get("note") and source.get("note") != row.get("note"):
                row.setdefault("table_notes", {})[sid] = source["note"]
            metadata = {k:v for k,v in source.items() if k not in (
                "game_id", "players", "match_summary", "is_favorite", "note") and row.get(k) != v}
            if metadata:
                row.setdefault("local_table_metadata", {})[sid] = metadata
            continue
        detail = result[game_id]
        body = detail["record"]
        row = {**{k:detail[k] for k in ("rule","sub_rule","created_at","match_type","players") if k in detail},
               **source, "game_id": game_id}
        if body["match_format"] == "paired_tables_2v2":
            row.update(match_summary=summarize_match(body), players=detail["players"])
        rows.append(row)
        by_id[game_id] = row
    for sid, locator in locators.items():
        if locator["game_id"] not in result:
            raise RecordConflict(f"{sid}: local alias points outside available records")
    referenced = {a.get("match_id") for a in cat.get("aliases", {}).values()}
    if set(cat.get("matches", {})) != referenced or set(cat.get("aliases", {})) - set(locators):
        raise RecordConflict("An old local archive or alias is unaccounted for")
    filenames = set(cat.get("matches", {}).values())
    for key,value in entries.items():
        if key.startswith("__match_v2_run/") and (not isinstance(value, dict)
                or value.get("status") not in ("published","rolled_back")):
            raise RecordConflict("Unfinished previous browser migration")
        if key.startswith("__match_v2/") and key.split("/",1)[1] not in filenames:
            if not isinstance(value, dict) or published.get(value.get("match_id")) != value:
                raise RecordConflict("Unpublished archive contains data absent from the active replay: " + key)
        if key.startswith("__") and key not in ("__index__","__record_aliases__","__match_v2_catalog__") and not key.startswith(("__match_v2/", "__match_v2_run/")):
            result[key] = value
    result["__index__"], result["__record_aliases__"] = rows, locators
    return result, {"sources":len(details), "matches":len(original_details), "locators":len(locators),
                    "paired":sum(d.get("record", {}).get("match_format") == "paired_tables_2v2" for k,d in result.items() if not k.startswith("__"))}


def convert_browser(export):
    if export.get("format") != "omu.localRecords.export.unify" or not isinstance(export.get("entries"), dict):
        raise RecordConflict("Expected a same-origin replay migration export")
    entries = export["entries"]
    result, stats = convert_entries(entries)
    checksums = {k:digest(v) for k,v in entries.items()}
    return {"format":"omu.localRecords.import.unified", "sources":checksums, "entries":result,
            "checksums":{k:digest(v) for k,v in result.items()}}, stats


def read_local(root):
    entries = {}
    root = Path(root)
    if not (root / "index.json").is_file():
        raise RecordConflict("Expected LocalRecords/index.json")
    entries["__index__"] = read_json(root / "index.json")
    for path in root.glob("*.json"):
        if path.name == "index.json":
            continue
        value = read_json(path)
        if path.name == "record-aliases.json":
            entries["__record_aliases__"] = value
        elif isinstance(value,dict) and "record" in value:
            entries[path.stem] = value
        else:
            raise RecordConflict(f"Unexpected local file {path.name}; refusing to replace it")
    old = root / "MatchRecordsV2"
    if old.is_dir():
        cat = read_json(old / "catalog.json")
        entries["__match_v2_catalog__"] = cat
        for filename in cat["matches"].values():
            if Path(filename).name != filename or not filename.endswith(".match.json"):
                raise RecordConflict("Invalid local archive path")
            entries["__match_v2/" + filename] = read_json(old / filename)
        extra = {p.name for p in old.glob("*.match.json")} - set(cat["matches"].values())
        for name in extra:
            entries["__match_v2/" + name] = read_json(old / name)
        for path in old.rglob("*"):
            if not path.is_file():
                continue
            if path.parent != old:
                raise RecordConflict(f"Unrecognized nested archive file {path.name}")
            if path.name == "catalog.json" or path.name in cat["matches"].values() or path.name in extra:
                continue
            value = read_json(path)
            if path.name.endswith(".journal.json") and value.get("status") in ("published","rolled_back"):
                continue
            if path.name == "browser-import.json" and value.get("format") == "omu.localRecords.import.v2":
                # A browser package may have been made on a different machine. Verify every body.
                if all((name in cat["matches"].values() and digest(body) == cat["archive_hashes"].get(body["match_id"]))
                       for name,body in value.get("archives", {}).items()):
                    continue
            raise RecordConflict(f"Unrecognized or unfinished local archive file {path.name}")
    return entries


def write_local(root, entries):
    for key, val in entries.items():
        if key == "__index__":
            name = "index.json"
        elif key == "__record_aliases__":
            name = "record-aliases.json"
        else:
            if key in (".","..") or Path(key).name != key or any(c in key for c in '/\\:\x00'):
                raise RecordConflict("Invalid local record filename")
            name = key + ".json"
        write_json(root / name, val)


def inventory(root):
    observed = {}
    for path in root.rglob("*"):
        if path.is_file() and path.name != ".write.lock":
            with path.open("rb") as stream:
                observed[str(path.relative_to(root))] = hashlib.file_digest(stream, "sha256").hexdigest()
    return observed


@contextmanager
def local_lock(root):
    path = root / ".write.lock"
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        create = kernel.CreateFileW
        create.argtypes = [wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,wintypes.LPVOID,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE]
        create.restype = wintypes.HANDLE
        handle = create(str(path), 0xC0000000, 4, None, 4, 0x80, None)
        if handle == wintypes.HANDLE(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            yield
        finally:
            kernel.CloseHandle.argtypes=[wintypes.HANDLE]
            kernel.CloseHandle(handle)
    else:
        import fcntl
        with path.open("a+b") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            yield


def finish_local(root, journal, txn):
    stage = Path(txn["stage"]).resolve()
    backup = Path(txn["backup"]).resolve()
    if stage.parent != root.parent or not stage.name.startswith("." + root.name + ".unify-stage-"):
        raise RecordConflict("Invalid local staging directory")
    before = inventory(backup)
    if digest(before) != txn["backup_hash"]:
        raise RecordConflict("Recovery backup has changed")
    after = inventory(stage)
    if digest(after) != txn["stage_hash"]:
        raise RecordConflict("Prepared local records have changed")
    observed = inventory(root)
    for name, checksum in observed.items():
        if checksum not in (before.get(name), after.get(name)):
            raise RecordConflict(f"{name}: changed during migration")
    for name in before:
        if name not in observed and name in after:
            raise RecordConflict(f"{name}: required source disappeared during migration")
    # Single IDs can be read in either format during publication; new paired IDs
    # and their locators are installed before the active index is replaced.
    for name in sorted(after, key=lambda n:(n=="index.json",n=="record-aliases.json",n)):
        if observed.get(name) == after[name]:
            continue
        dest = root / name
        # A crash during copy must leave both the protected source inventory and
        # the checksummed stage intact. A retried transaction rewrites this scratch file.
        tmp = stage.with_name(stage.name + ".publishing")
        try:
            shutil.copyfile(stage/name, tmp)
            os.replace(tmp, dest)
        finally:
            tmp.unlink(missing_ok=True)
    active = inventory(root)
    if any(active.get(n) != h for n,h in after.items()):
        raise RecordConflict("Published local records failed readback")
    for name in before.keys() - after.keys():
        if active.get(name) != before[name] and name in active:
            raise RecordConflict(f"{name}: cleanup target changed")
        path = (root/name).resolve()
        if root not in path.parents:
            raise RecordConflict("Recovery path escapes local records")
        if (path.parent == root and path.suffix == ".json") or root/"MatchRecordsV2" in path.parents:
            path.unlink(missing_ok=True)
    retired = root/"MatchRecordsV2"
    if retired.is_dir():
        for child in sorted((p for p in retired.rglob("*") if p.is_dir()),key=lambda p:len(p.parts),reverse=True):
            child.rmdir()
        retired.rmdir()
    if read_local(root) != read_local(stage):
        raise RecordConflict("Local record coverage changed during publication")
    journal.unlink()
    shutil.rmtree(stage)


def migrate_files(root, *, apply=False, backup=None):
    root = Path(root).resolve()
    journal = root.with_name("." + root.name + ".unify-transaction.json")
    if not root.is_dir():
        raise RecordConflict("LocalRecords directory not found")
    with local_lock(root) if apply else nullcontext():
        if journal.exists():
            if not apply:
                raise RecordConflict("Unfinished local cutover; run apply to recover")
            txn=read_json(journal)
            finish_local(root,journal,txn)
            return {**txn["stats"], "status":"applied", "backup":txn["backup"]}
        entries = read_local(root)
        converted, stats = convert_entries(entries)
        if entries == converted:
            return {**stats, "status":"already_unified"}
        if not apply:
            return {**stats, "status":"verified"}
        if not backup:
            raise RecordConflict("--backup is required before replacing local records")
        backup = Path(backup).resolve()
        if backup == root or root in backup.parents or backup in root.parents:
            raise RecordConflict("Backup must be separate from LocalRecords")
        original = inventory(root)
        if not backup.exists():
            shutil.copytree(root, backup, ignore=shutil.ignore_patterns(".write.lock"))
        if inventory(backup) != original:
            raise RecordConflict("Desktop backup does not match source files")
        stage = Path(tempfile.mkdtemp(prefix="." + root.name + ".unify-stage-", dir=root.parent))
        try:
            write_local(stage, converted)
            if read_local(stage) != converted or inventory(root) != original:
                raise RecordConflict("Local migration inputs or staged output changed")
            txn = {"stage":str(stage), "backup":str(backup), "backup_hash":digest(original),
                   "stage_hash":digest(inventory(stage)), "stats":stats}
            write_json(journal, txn)
            finish_local(root,journal,txn)
        finally:
            if not journal.exists() and stage.exists():
                shutil.rmtree(stage)
    return {**stats, "status":"applied", "backup":str(backup)}
