# -*- coding: utf-8 -*-
"""Download rule-research sources into snapshots/ (webpages) or files/ (documents)."""
from __future__ import annotations

import json
import os
import re
import shutil
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)
TMP = os.path.abspath(os.path.join(ROOT, "..", "..", "..", ".om_workspace", "tmp"))
UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
)
CTX = ssl.create_default_context()
SLUGS = ["mahjong-phylogeny", "hongkong", "drawing-mahjong", "mahjong-studies"]

FILE_HOST_HINTS = (
    "archive.org/details/",
    "arxiv.org/",
    "escholarship.org/",
    "wikisource.org/",
    "docs.google.com/document",
    "zj-mahjong.info/",
    "themahjongtileset.co.uk/",
    "mjradio.web.fc2.com/",
    "gamesmuseum.uwaterloo.ca/",
    "web.archive.org/",
    "parlettgames.uk/",
    "japanplayingcardmuseum.com/",
)


def is_file_source(src: dict) -> bool:
    url = src.get("url") or ""
    low = url.lower()
    if src.get("type") == "rulebook":
        return True
    if low.endswith((".pdf", ".djvu", ".zip")):
        return True
    if any(h in low for h in FILE_HOST_HINTS):
        # wayback of a html article still counts as 资料 if it is the text itself
        if "wikipedia.org" in low:
            return False
        return True
    lp = src.get("local_path") or ""
    if str(lp).lower().endswith((".pdf", ".djvu", ".png", ".jpg")):
        return True
    return False


def safe_name(sid: str, ext: str) -> str:
    sid = re.sub(r"[^a-zA-Z0-9._-]+", "_", sid)
    return f"{sid}{ext}"


def request(url: str, timeout: int = 120) -> tuple[bytes, str, str]:
    req = urllib.request.Request(
        url,
        headers={
            "User-Agent": UA,
            "Accept": "*/*",
            "Accept-Language": "zh-CN,zh;q=0.9,ja;q=0.8,en;q=0.7",
        },
    )
    with urllib.request.urlopen(req, timeout=timeout, context=CTX) as resp:
        data = resp.read()
        ctype = (resp.headers.get("Content-Type") or "").split(";")[0].strip().lower()
        final = resp.geturl()
    return data, ctype or "", final


def request_retry(url: str, timeout: int = 120, tries: int = 3) -> tuple[bytes, str, str]:
    last = None
    for i in range(tries):
        try:
            return request(url, timeout=timeout)
        except Exception as e:
            last = e
            time.sleep(1.2 * (i + 1))
    raise last


def sniff_ext(data: bytes, ctype: str, url: str) -> str:
    if data[:5] == b"%PDF-":
        return ".pdf"
    if data[:4] == b"PK\x03\x04":
        return ".zip"
    low = url.lower()
    if "pdf" in ctype or low.endswith(".pdf"):
        return ".pdf"
    if "html" in ctype or data.lstrip()[:15].lower().startswith(b"<!doctype") or b"<html" in data[:400].lower():
        return ".html"
    if "json" in ctype:
        return ".json"
    if low.endswith(".txt"):
        return ".txt"
    return ".bin"


def ia_pdf_url(identifier: str) -> str | None:
    meta_url = "https://archive.org/metadata/" + identifier
    raw, _, _ = request(meta_url, timeout=40)
    meta = json.loads(raw.decode("utf-8", "replace"))
    files = meta.get("files") or []
    prefer = []
    for f in files:
        name = f.get("name") or ""
        fmt = f.get("format") or ""
        if not name.lower().endswith(".pdf"):
            continue
        if name.lower().endswith("_text.pdf"):
            continue
        score = 0
        if fmt in ("Image Container PDF", "Text PDF", "PDF"):
            score += 2
        if "original" in (f.get("source") or ""):
            score += 1
        prefer.append((score, int(f.get("size") or 0), name))
    if not prefer:
        return None
    prefer.sort(key=lambda x: (-x[0], -x[1]))
    name = prefer[0][2]
    return "https://archive.org/download/" + identifier + "/" + urllib.parse.quote(name)


def google_doc_pdf(url: str) -> str | None:
    m = re.search(r"/document/d/([a-zA-Z0-9_-]+)", url)
    if not m:
        return None
    return f"https://docs.google.com/document/d/{m.group(1)}/export?format=pdf"


def arxiv_pdf(url: str) -> str:
    m = re.search(r"arxiv\.org/(?:abs|pdf)/([0-9.]+)(v\d+)?", url)
    if m:
        return f"https://arxiv.org/pdf/{m.group(1)}.pdf"
    return url


def wrap_html(url: str, title: str, body: bytes) -> bytes:
    text = body.decode("utf-8", "replace")
    if re.search(r"<base\s", text, re.I):
        return text.encode("utf-8")
    base = f'<base href="{url}">'
    meta = (
        f"<!-- saved locally from {url} -->\n"
        f'<meta charset="utf-8">\n{base}\n'
    )
    if re.search(r"<head[^>]*>", text, re.I):
        text = re.sub(r"(<head[^>]*>)", r"\1\n" + meta, text, count=1, flags=re.I)
    else:
        text = f"<!DOCTYPE html><html><head>{meta}<title>{title}</title></head><body>{text}</body></html>"
    return text.encode("utf-8")


def copy_existing(src_path: str, dest: str) -> bool:
    if src_path and os.path.isfile(src_path) and os.path.getsize(src_path) > 200:
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        shutil.copy2(src_path, dest)
        return True
    return False


def mark_ok(src: dict, cache: dict, url: str, kind: str, rel: str) -> dict:
    src["local_kind"] = kind
    src["local_path"] = rel
    src["local_status"] = "ok"
    if url:
        cache[url] = {"local_kind": kind, "local_path": rel, "local_status": "ok"}
    return src


def already_ok(src: dict, cache: dict, url: str) -> dict | None:
    rel = src.get("local_path")
    if src.get("local_status") != "ok" or not rel:
        return None
    for base in (PUBLIC, ROOT):
        p = os.path.join(base, rel) if not os.path.isabs(rel) else rel
        # public paths are relative to rule-research/
        pub = os.path.join(PUBLIC, rel)
        research = os.path.join(ROOT, rel)
        for cand in (p, pub, research):
            if cand and os.path.isfile(cand) and os.path.getsize(cand) > 200:
                kind = src.get("local_kind") or ("file" if "/files/" in rel.replace("\\", "/") else "snapshot")
                mark_ok(src, cache, url, kind, rel.replace("\\", "/"))
                print("SKIP", src["id"], rel, flush=True)
                return src
    return None


def fetch_one(slug: str, src: dict, cache: dict) -> dict:
    sid = src["id"]
    url = src.get("url") or ""
    kind = "file" if is_file_source(src) else "snapshot"
    folder = "files" if kind == "file" else "snapshots"
    out_dir = os.path.join(ROOT, slug, folder)
    pub_dir = os.path.join(PUBLIC, slug, folder)
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(pub_dir, exist_ok=True)

    skipped = already_ok(src, cache, url)
    if skipped:
        return skipped

    # reuse same URL
    if url in cache:
        prev = cache[url]
        src["local_kind"] = prev["local_kind"]
        src["local_path"] = prev["local_path"]
        src["local_status"] = prev["local_status"]
        print("DUP", sid, prev.get("local_path"), flush=True)
        return src

    # already-local drawing pdf / hongkong rulebooks
    existing_candidates = []
    lp = src.get("local_path")
    if lp:
        existing_candidates += [
            os.path.join(ROOT, slug, lp),
            os.path.join(ROOT, slug, "rulebooks", os.path.basename(lp)),
            os.path.join(PUBLIC, os.path.basename(lp)) if "绘图" in str(lp) else "",
            os.path.join(PUBLIC, "绘图麻雀牌谱.pdf"),
        ]
    if sid == "fan-zengxiang-1906":
        existing_candidates.append(os.path.join(TMP, "fanshan", "02106614.cn.pdf"))
    if sid == "shenbao-1887-concession":
        existing_candidates.append(os.path.join(TMP, "shenbao_ocr", "1887-11-14.pdf"))
    if sid == "drawing-local" or sid == "drawing-mahjong-local" or sid == "shen-yifan-1914":
        existing_candidates.append(os.path.join(PUBLIC, "绘图麻雀牌谱.pdf"))
        existing_candidates.append(os.path.join(ROOT, "..", "绘图麻雀牌谱.pdf"))
        existing_candidates.append(os.path.join(ROOT, "mahjong-phylogeny", "files", "drawing-local.pdf"))

    reuse_other = {
        "culin-1895": os.path.join(ROOT, "mahjong-phylogeny", "files", "culin-1895.pdf"),
        "culin-1924": os.path.join(ROOT, "mahjong-phylogeny", "files", "culin-1924.html"),
        "greene-2015": os.path.join(ROOT, "mahjong-phylogeny", "files", "greene-2015.pdf"),
        "shinbara-1952": os.path.join(ROOT, "mahjong-phylogeny", "files", "shinbara-1952.html"),
        "jin-xueshi": os.path.join(ROOT, "mahjong-phylogeny", "files", "jin-xueshi.html"),
        "hu-shi-1927": os.path.join(ROOT, "mahjong-phylogeny", "files", "hu-shi-1927.html"),
        "mcr-1998": os.path.join(ROOT, "mahjong-phylogeny", "files", "mcr-1998.html"),
        "zj-classical": os.path.join(ROOT, "mahjong-phylogeny", "files", "zj-classical.html"),
        "tileset-1": os.path.join(ROOT, "mahjong-phylogeny", "files", "mahjongtileset-1.html"),
        "tileset-2": os.path.join(ROOT, "mahjong-phylogeny", "files", "mahjongtileset-2.html"),
        "tileset-terms": os.path.join(ROOT, "mahjong-phylogeny", "files", "shenbao-1884-terms.html"),
        "zhao-paper-2018": os.path.join(ROOT, "mahjong-phylogeny", "snapshots", "paper-2018.html"),
        "heinz-opb": os.path.join(ROOT, "mahjong-phylogeny", "snapshots", "heinz-opb.html"),
        "heinz-site": os.path.join(ROOT, "mahjong-phylogeny", "snapshots", "heinz-book.html"),
        "wiki-zh-mahjong": os.path.join(ROOT, "mahjong-phylogeny", "snapshots", "wiki-zh-origin.html"),
        "qingbai-087": os.path.join(ROOT, "mahjong-phylogeny", "files", "qingbai-cha-maque.html"),
        "fan-zengxiang-1906": os.path.join(ROOT, "mahjong-phylogeny", "files", "fan-zengxiang-1906.pdf"),
    }
    if sid in reuse_other and os.path.isfile(reuse_other[sid]):
        src_f = reuse_other[sid]
        ext = os.path.splitext(src_f)[1] or ".html"
        dest = os.path.join(out_dir, safe_name(sid, ext))
        if copy_existing(src_f, dest):
            fname = os.path.basename(dest)
            shutil.copy2(dest, os.path.join(pub_dir, fname))
            rel = f"{slug}/{folder}/{fname}"
            print("COPY", sid, rel, flush=True)
            return mark_ok(src, cache, url, kind, rel)

    dest_guess_pdf = os.path.join(out_dir, safe_name(sid, ".pdf"))
    for cand in existing_candidates:
        if cand and copy_existing(cand, dest_guess_pdf):
            rel = f"{slug}/{folder}/{os.path.basename(dest_guess_pdf)}"
            shutil.copy2(dest_guess_pdf, os.path.join(pub_dir, os.path.basename(dest_guess_pdf)))
            print("COPY", sid, rel, flush=True)
            return mark_ok(src, cache, url, "file", rel)

    fetch_url = url
    if url.startswith("/"):
        # already a site-local file
        print("LOCAL", sid, url, flush=True)
        return mark_ok(src, cache, url, "file", url.lstrip("/"))

    try:
        if "archive.org/details/" in url:
            ident = url.rstrip("/").split("/")[-1].split("?")[0]
            pdf = ia_pdf_url(ident)
            if pdf:
                fetch_url = pdf
                kind = "file"
                folder = "files"
                out_dir = os.path.join(ROOT, slug, folder)
                pub_dir = os.path.join(PUBLIC, slug, folder)
                os.makedirs(out_dir, exist_ok=True)
                os.makedirs(pub_dir, exist_ok=True)
        elif "arxiv.org/" in url:
            fetch_url = arxiv_pdf(url)
            kind = "file"
        elif "docs.google.com/document" in url:
            g = google_doc_pdf(url)
            if g:
                fetch_url = g
                kind = "file"
        timeout = 300 if any(x in fetch_url for x in ("archive.org", ".pdf", "escholarship")) else 90
        data, ctype, final = request_retry(fetch_url, timeout=timeout)
        ext = sniff_ext(data, ctype, fetch_url)
        if kind == "file" and ext == ".html" and "archive.org/details/" in url:
            # details page html — keep as file html only if no pdf
            pass
        if kind == "snapshot" and ext == ".pdf":
            kind = "file"
            folder = "files"
            out_dir = os.path.join(ROOT, slug, folder)
            pub_dir = os.path.join(PUBLIC, slug, folder)
            os.makedirs(out_dir, exist_ok=True)
            os.makedirs(pub_dir, exist_ok=True)
        if ext == ".html":
            data = wrap_html(final or url, src.get("title") or sid, data)
        fname = safe_name(sid, ext)
        dest = os.path.join(out_dir, fname)
        with open(dest, "wb") as f:
            f.write(data)
        shutil.copy2(dest, os.path.join(pub_dir, fname))
        rel = f"{slug}/{folder}/{fname}"
        print("OK", sid, rel, len(data), flush=True)
        return mark_ok(src, cache, url, kind, rel)
    except Exception as e:
        # wayback fallback
        if not url.startswith("https://web.archive.org/"):
            try:
                wb = "https://web.archive.org/web/2026/" + url
                data, ctype, final = request_retry(wb, timeout=90, tries=2)
                ext = sniff_ext(data, ctype, wb)
                if ext == ".html":
                    data = wrap_html(url, src.get("title") or sid, data)
                fname = safe_name(sid, ext or ".html")
                dest = os.path.join(out_dir, fname)
                with open(dest, "wb") as f:
                    f.write(data)
                shutil.copy2(dest, os.path.join(pub_dir, fname))
                rel = f"{slug}/{folder}/{fname}"
                print("WAYBACK", sid, rel, len(data), flush=True)
                return mark_ok(src, cache, url, kind, rel)
            except Exception as e2:
                print("FAIL", sid, type(e).__name__, e, "| wb", type(e2).__name__, e2, flush=True)
                src["local_status"] = "failed"
                src["local_kind"] = kind
                src["local_path"] = None
                return src
        print("FAIL", sid, type(e).__name__, e, flush=True)
        src["local_status"] = "failed"
        src["local_kind"] = kind
        src["local_path"] = None
        return src


def process_slug(slug: str) -> None:
    src_path = os.path.join(ROOT, slug, "sources.json")
    if not os.path.isfile(src_path):
        print("skip missing", src_path)
        return
    data = json.load(open(src_path, encoding="utf-8"))
    cache = {}
    out_sources = []
    for src in data.get("sources") or []:
        out_sources.append(fetch_one(slug, dict(src), cache))
        data["sources"] = out_sources + (data.get("sources") or [])[len(out_sources) :]
        json.dump(data, open(src_path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        open(src_path, "a", encoding="utf-8").write("\n")
        time.sleep(0.15)
    data["sources"] = out_sources
    json.dump(data, open(src_path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(src_path, "a", encoding="utf-8").write("\n")
    pub_json = os.path.join(PUBLIC, f"{slug}.json")
    json.dump(data, open(pub_json, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    open(pub_json, "a", encoding="utf-8").write("\n")
    ok = sum(1 for s in out_sources if s.get("local_status") == "ok")
    print(f"==== {slug} {ok}/{len(out_sources)}", flush=True)


def main():
    os.makedirs(PUBLIC, exist_ok=True)
    for slug in SLUGS:
        process_slug(slug)


if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1:
        os.makedirs(PUBLIC, exist_ok=True)
        for slug in sys.argv[1:]:
            process_slug(slug)
    else:
        main()
