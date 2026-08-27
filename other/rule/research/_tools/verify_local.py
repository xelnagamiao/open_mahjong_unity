# -*- coding: utf-8 -*-
import json
import os
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
PUBLIC = os.path.abspath(
    os.path.join(ROOT, "..", "..", "..", "open_mahjong_web", "client", "public", "rule-research")
)


def main() -> None:
    for slug in ("mahjong-phylogeny", "hongkong", "drawing-mahjong", "mahjong-studies"):
        for kind in ("files", "snapshots"):
            src = os.path.join(ROOT, slug, kind)
            dst = os.path.join(PUBLIC, slug, kind)
            if not os.path.isdir(src):
                continue
            os.makedirs(dst, exist_ok=True)
            for name in os.listdir(src):
                s = os.path.join(src, name)
                if os.path.isfile(s):
                    shutil.copy2(s, os.path.join(dst, name))
        srcj = os.path.join(ROOT, slug, "sources.json")
        dstj = os.path.join(PUBLIC, slug + ".json")
        shutil.copy2(srcj, dstj)
        data = json.load(open(srcj, encoding="utf-8"))
        ok = fail = miss = 0
        kinds = {}
        for s in data["sources"]:
            st = s.get("local_status")
            kinds[s.get("local_kind")] = kinds.get(s.get("local_kind"), 0) + 1
            if st == "ok":
                ok += 1
                rel = (s.get("local_path") or "").replace("/", os.sep)
                p = os.path.join(PUBLIC, rel)
                if not os.path.isfile(p):
                    miss += 1
                    print("MISSING", slug, s["id"], p)
            elif st == "failed":
                fail += 1
                print("FAILED", slug, s["id"])
            else:
                print("NOSTATUS", slug, s["id"], st)
        n = len(data["sources"])
        print(f"{slug}: ok={ok} fail={fail} miss={miss} n={n} kinds={kinds}")


if __name__ == "__main__":
    main()
