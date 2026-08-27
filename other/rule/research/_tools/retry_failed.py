# -*- coding: utf-8 -*-
"""Retry the two sources that failed on the first pass."""
from __future__ import annotations

import json
import os
import re
import shutil
import ssl
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)
UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
)
CTX = ssl.create_default_context()


def get(url: str, timeout: int = 60) -> tuple[bytes, str, str]:
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"})
    with urllib.request.urlopen(req, timeout=timeout, context=CTX) as resp:
        data = resp.read()
        ctype = (resp.headers.get("Content-Type") or "").split(";")[0].strip().lower()
        return data, ctype, resp.geturl()


def wrap(url: str, body: bytes) -> bytes:
    text = body.decode("utf-8", "replace")
    meta = f'<!-- saved locally from {url} -->\n<meta charset="utf-8">\n<base href="{url}">\n'
    if re.search(r"<head[^>]*>", text, re.I):
        text = re.sub(r"(<head[^>]*>)", r"\1\n" + meta, text, count=1, flags=re.I)
    else:
        text = f"<!DOCTYPE html><html><head>{meta}</head><body>{text}</body></html>"
    return text.encode("utf-8")


def save_file(rel: str, data: bytes) -> None:
    dest = os.path.join(ROOT, rel)
    pub = os.path.join(PUBLIC, rel)
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    os.makedirs(os.path.dirname(pub), exist_ok=True)
    with open(dest, "wb") as f:
        f.write(data)
    shutil.copy2(dest, pub)
    print("WROTE", rel, len(data))


def patch_source(slug: str, sid: str, kind: str, rel: str, status: str) -> None:
    src_path = os.path.join(ROOT, slug, "sources.json")
    data = json.load(open(src_path, encoding="utf-8"))
    for s in data.get("sources") or []:
        if s.get("id") == sid:
            s["local_kind"] = kind
            s["local_path"] = rel if status == "ok" else None
            s["local_status"] = status
    json.dump(data, open(src_path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(src_path, "a", encoding="utf-8").write("\n")
    pub_json = os.path.join(PUBLIC, f"{slug}.json")
    json.dump(data, open(pub_json, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(pub_json, "a", encoding="utf-8").write("\n")


def main() -> None:
    orig = (
        "https://web.archive.org/web/20110516224304/"
        "http://www.gamesmuseum.uwaterloo.ca/Archives/Culin/Majong1924/index.html"
    )
    id_url = (
        "https://web.archive.org/web/20110516224304id_/"
        "http://www.gamesmuseum.uwaterloo.ca/Archives/Culin/Majong1924/index.html"
    )
    raw, _, _ = get(id_url)
    save_file("mahjong-phylogeny/files/culin-1924.html", wrap(orig, raw))
    patch_source("mahjong-phylogeny", "culin-1924", "file", "mahjong-phylogeny/files/culin-1924.html", "ok")

    queries = [
        "https://web.archive.org/cdx/search/cdx?url=atawmj.org.tw/memu009.htm&output=json&fl=timestamp,original,statuscode,mimetype,length&limit=30",
        "https://web.archive.org/cdx/search/cdx?url=http://atawmj.org.tw/memu009.htm&output=json&fl=timestamp,original,statuscode,mimetype,length&limit=30",
        "https://web.archive.org/cdx/search/cdx?url=atawmj.org.tw/*&matchType=prefix&output=json&fl=original,timestamp,statuscode,mimetype&filter=statuscode:200&limit=80",
    ]
    found = None
    for q in queries:
        try:
            raw, _, _ = get(q, timeout=40)
            print("CDX", q, raw[:800])
            rows = json.loads(raw.decode("utf-8", "replace"))
            if isinstance(rows, list) and len(rows) > 1:
                for row in rows[1:]:
                    print(" row", row)
                    if str(row[0]).isdigit() and (len(row) < 3 or str(row[2]) in ("200", "301", "302", "-")):
                        found = row
                        break
                    if len(row) >= 3 and str(row[2]) == "200":
                        found = row
                        break
                if found:
                    break
        except Exception as e:
            print("CDX fail", q, type(e).__name__, e)

    if not found:
        # try a few known timestamps blindly
        guesses = [
            "https://web.archive.org/web/20150801000000id_/http://atawmj.org.tw/memu009.htm",
            "https://web.archive.org/web/20120801000000id_/http://atawmj.org.tw/memu009.htm",
            "https://web.archive.org/web/20200101000000id_/http://atawmj.org.tw/memu009.htm",
            "https://web.archive.org/web/20100815000000id_/http://www.atawmj.org.tw/memu009.htm",
        ]
        for g in guesses:
            try:
                raw, ctype, final = get(g, timeout=40)
                print("GUESS", g, len(raw), ctype, final)
                if raw[:5] != b"%PDF-" and (b"<html" in raw[:500].lower() or b"<!doctype" in raw[:200].lower() or len(raw) > 400):
                    if b"404" in raw[:800] and len(raw) < 4000:
                        print(" looks like 404 page")
                        continue
                    save_file("mahjong-phylogeny/snapshots/atawmj-origin.html", wrap("http://atawmj.org.tw/memu009.htm", raw))
                    patch_source(
                        "mahjong-phylogeny",
                        "atawmj-origin",
                        "snapshot",
                        "mahjong-phylogeny/snapshots/atawmj-origin.html",
                        "ok",
                    )
                    return
            except Exception as e:
                print("GUESS fail", g, type(e).__name__, e)
        print("atawmj still missing")
        return

    # found CDX row: timestamp, original, ...
    ts = found[0] if str(found[0]).isdigit() else found[1]
    original = found[1] if str(found[0]).isdigit() else found[0]
    snap = f"https://web.archive.org/web/{ts}id_/{original}"
    print("FETCH", snap)
    raw, _, _ = get(snap, timeout=60)
    save_file("mahjong-phylogeny/snapshots/atawmj-origin.html", wrap(original, raw))
    patch_source(
        "mahjong-phylogeny",
        "atawmj-origin",
        "snapshot",
        "mahjong-phylogeny/snapshots/atawmj-origin.html",
        "ok",
    )


if __name__ == "__main__":
    main()
