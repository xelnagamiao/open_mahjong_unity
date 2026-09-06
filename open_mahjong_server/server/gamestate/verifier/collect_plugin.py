"""pytest --collect-only 插件：把 nodeid + docstring 写成 JSON（LAB_COLLECT_JSON）。"""
from __future__ import annotations

import json
import os
from pathlib import Path


def _first_line(text) -> str:
    if not text:
        return ""
    for line in str(text).splitlines():
        stripped = line.strip()
        if stripped:
            return stripped
    return ""


def pytest_configure(config):
    path = os.environ.get("LAB_COLLECT_JSON")
    if not path:
        return
    config._lab_collect_json = path  # type: ignore[attr-defined]


def pytest_collection_finish(session):
    path = getattr(session.config, "_lab_collect_json", None)
    if not path:
        return
    rows = []
    for item in session.items:
        obj = getattr(item, "obj", None)
        doc = _first_line(getattr(obj, "__doc__", None))
        module = getattr(item, "module", None)
        file_doc = _first_line(getattr(module, "__doc__", None) if module is not None else None)
        nodeid = getattr(item, "nodeid", "") or ""
        fspath = str(getattr(item, "fspath", "") or "")
        rows.append(
            {
                "nodeid": nodeid,
                "file": fspath,
                "func": nodeid.split("::")[-1] if nodeid else "",
                "doc": doc,
                "file_doc": file_doc,
            }
        )
    Path(path).write_text(json.dumps(rows, ensure_ascii=False), encoding="utf-8")
