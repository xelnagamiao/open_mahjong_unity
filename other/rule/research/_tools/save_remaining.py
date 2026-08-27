# -*- coding: utf-8 -*-
"""Write the two remaining local copies and patch sources.json."""
from __future__ import annotations

import html
import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)
CULIN_MD = r"C:\Users\Administrator\.cursor\projects\d-open-mahjong-unity\agent-tools\33292c59-60f3-4d29-8196-bf5a3a10c37d.txt"
ATAWMJ_CANDIDATES = [
    r"C:\Users\Administrator\.cursor\projects\d-open-mahjong-unity\agent-tools",
]


def write_pair(rel: str, content: str) -> None:
    data = content.encode("utf-8")
    for base in (ROOT, PUBLIC):
        path = os.path.join(base, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(data)
    print("wrote", rel, len(data))


def page(lang: str, title: str, url: str, note: str, body: str) -> str:
    return (
        "<!DOCTYPE html>\n"
        f'<html lang="{lang}">\n'
        "<head>\n"
        '<meta charset="utf-8">\n'
        f'<base href="{html.escape(url, quote=True)}">\n'
        f"<title>{html.escape(title)}</title>\n"
        "<style>body{max-width:46rem;margin:2rem auto;padding:0 1.2rem;"
        "font:16px/1.65 Georgia,'Noto Serif SC',serif;color:#222}"
        ".src{font-size:.85rem;color:#555}pre{white-space:pre-wrap;font:inherit}</style>\n"
        "</head>\n<body>\n"
        f'<p class="src">{html.escape(note)}</p>\n'
        f"<pre>{html.escape(body)}</pre>\n"
        "</body>\n</html>\n"
    )


def patch(slug: str, sid: str, kind: str, rel: str) -> None:
    src_path = os.path.join(ROOT, slug, "sources.json")
    data = json.load(open(src_path, encoding="utf-8"))
    for s in data.get("sources") or []:
        if s.get("id") == sid:
            s["local_kind"] = kind
            s["local_path"] = rel
            s["local_status"] = "ok"
    json.dump(data, open(src_path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(src_path, "a", encoding="utf-8").write("\n")
    pub = os.path.join(PUBLIC, f"{slug}.json")
    json.dump(data, open(pub, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(pub, "a", encoding="utf-8").write("\n")


def find_atawmj() -> str | None:
    folder = ATAWMJ_CANDIDATES[0]
    newest = None
    newest_mtime = 0.0
    for name in os.listdir(folder):
        if not name.endswith(".txt"):
            continue
        path = os.path.join(folder, name)
        try:
            text = open(path, encoding="utf-8").read(4000)
        except OSError:
            continue
        if "▌麻將起源" in text or "麻將起源" in text and "atawmj" in text.lower():
            m = os.path.getmtime(path)
            if m > newest_mtime:
                newest_mtime = m
                newest = path
    return newest


def main() -> None:
    culin = open(CULIN_MD, encoding="utf-8").read()
    idx = culin.find("## The Game of Ma-Jong")
    if idx < 0:
        idx = culin.find("The Game of Ma-Jong")
    body = culin[idx:] if idx >= 0 else culin
    culin_url = (
        "https://web.archive.org/web/20110516224304/"
        "http://www.gamesmuseum.uwaterloo.ca/Archives/Culin/Majong1924/index.html"
    )
    write_pair(
        "mahjong-phylogeny/files/culin-1924.html",
        page(
            "en",
            "The Game of Ma-Jong — Stewart Culin, 1924",
            culin_url,
            "Saved from " + culin_url + " (Waterloo Games Museum transcription of Brooklyn Museum Quarterly XI, 1924). Original English retained.",
            body,
        ),
    )
    patch("mahjong-phylogeny", "culin-1924", "file", "mahjong-phylogeny/files/culin-1924.html")

    at_path = find_atawmj()
    print("atawmj source file:", at_path)
    if not at_path:
        print("skip atawmj; write separately")
        return
    raw = open(at_path, encoding="utf-8").read()
    idx = raw.find("▌麻將起源")
    if idx < 0:
        idx = raw.find("麻將起源")
    body = raw[idx:] if idx >= 0 else raw
    at_url = "http://atawmj.org.tw/memu009.htm"
    write_pair(
        "mahjong-phylogeny/snapshots/atawmj-origin.html",
        page(
            "zh-Hant",
            "麻將起源 — 中華競技麻將網",
            at_url,
            "Saved from Wayback 20080609234546 of " + at_url + ". Original Chinese retained.",
            body,
        ),
    )
    patch(
        "mahjong-phylogeny",
        "atawmj-origin",
        "snapshot",
        "mahjong-phylogeny/snapshots/atawmj-origin.html",
    )


if __name__ == "__main__":
    main()
