"""Incremental Unity text inventory and reproducible Source Han weight subsets.

Only generated outputs are written. Full sources remain editor-only. fontTools
is imported only when the effective subset changes, not for a cache hit.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import time
import unicodedata

VERSION = 2
REGULAR = "Assets/font/思源黑体/SourceHanSansSC-Regular.otf"
SOURCES = {w: f"Assets/Editor/FontAutomation/Sources/SourceHanSansSC-{w}.otf"
           for w in ("Medium", "Bold")}
BASE_RANGES = [(32, 591), (0x1E00, 0x1EFF), (0x2000, 0x206F),
               (0x2070, 0x209F), (0x20A0, 0x20CF), (0x2100, 0x214F),
               (0x2190, 0x21FF), (0x2200, 0x22FF), (0x25A0, 0x25FF),
               (0xFF01, 0xFF5E)]
EXTENSIONS = {".cs", ".unity", ".prefab", ".asset", ".json", ".csv", ".tsv", ".txt"}
SKIP_DIRS = {"Editor", "Plugins", "Tests", "TextMesh Pro", "FontAutomation"}
CS_STRINGS = re.compile(r'//[^\n]*|/\*.*?\*/|(?:\$?@|@\$)"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.S)
YAML_TEXT = re.compile(r'^\s*(?:-\s*)?(?:m_[Tt]ext|text|key|title|label|caption|description|message|simplifiedChinese|traditionalChinese|english|placeholder|hint|tooltip):\s*(.*)$', re.M)
TAGS = re.compile(r'</?(?:b|i|u|s|color|size|font|font-weight|sprite|link|align|alpha|cspace|mspace|voffset|mark|noparse|br|style|material)(?:\s[^<>]*|=[^<>]*)?/?>', re.I)


def read_json(path, default):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return default


def write_changed(path, value):
    data = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    if path.exists() and path.read_bytes() == data:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)
    return True


def decode_escaped(value):
    def replace(m):
        s = m.group(1)
        if s[0] in "uUx":
            return chr(int(s[1:], 16))
        return {"n": "\n", "r": "\r", "t": "\t", "0": "\0", "b": "\b", "f": "\f", "v": "\v", "a": "\a"}.get(s, s)
    return re.sub(r'\\(u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|x[0-9a-fA-F]{1,4}|.)', replace, value)


def strings(path, content):
    if path.suffix == ".cs":
        for m in CS_STRINGS.finditer(content):
            s = m.group()
            if s.startswith(("//", "/*")):
                continue
            quote = min(i for i in (s.find('"'), s.find("'")) if i >= 0)
            yield s[quote + 1:-1].replace('""', '"') if "@" in s[:quote] else decode_escaped(s[quote + 1:-1])
    elif path.suffix == ".json":
        def values(obj):
            if isinstance(obj, str):
                yield obj
            elif isinstance(obj, list):
                for x in obj:
                    yield from values(x)
            elif isinstance(obj, dict):
                for k, x in obj.items():
                    if k != "traditionalCharacters":
                        yield from values(x)
        try:
            yield from values(json.loads(content))
        except ValueError as error:
            raise ValueError(f"Invalid text JSON: {path}: {error}") from error
    elif path.suffix in {".unity", ".prefab", ".asset"}:
        # YAML quoted strings may span lines; their physical line breaks are spaces.
        for m in YAML_TEXT.finditer(content):
            value = m.group(1).strip()
            if value.startswith('"'):
                end = m.end()
                while not re.search(r'(?<!\\)"\s*$', value[1:]) and end < len(content):
                    stop = content.find("\n", end + 1)
                    if stop < 0:
                        stop = len(content)
                    value += " " + content[end:stop].strip()
                    end = stop
                yield decode_escaped(value[1:-1])
            elif value.startswith("'"):
                yield value[1:-1].replace("''", "'")
            elif value in {"|", ">", "|-", ">-"}:
                indent = len(m.group()) - len(m.group().lstrip())
                for line in content[m.end():].splitlines()[1:]:
                    if line.strip() and len(line) - len(line.lstrip()) <= indent:
                        break
                    yield line.strip()
            elif not value.startswith("{"):
                yield value
    else:
        yield content


def codepoints(text):
    # Explicit surrogate pairs can appear in Unity YAML and C# literals.
    text = text.encode("utf-16", "surrogatepass").decode("utf-16", "replace")
    return {ord(c) for c in TAGS.sub("", text)
            if unicodedata.category(c) not in {"Cc", "Cf", "Cs", "Cn"}}


def scoped_text(content):
    result = {}
    for doc in re.split(r'^--- !u!', content, flags=re.M):
        font = re.search(r'm_fontAsset:.*?guid: ([0-9a-f]{32})', doc)
        if font:
            values = list(strings(Path("text.prefab"), doc))
            if values:
                result.setdefault(font.group(1), []).extend(values)
    return result


def cjk(cp):
    return (0x2E80 <= cp <= 0x9FFF or 0xAC00 <= cp <= 0xD7AF or
            0xF900 <= cp <= 0xFAFF or 0x20000 <= cp <= 0x323AF)


def inputs(project):
    for root, dirs, files in os.walk(project / "Assets"):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS and not d.startswith("."))
        for name in sorted(files):
            path = Path(root) / name
            if path.suffix not in EXTENSIONS:
                continue
            rel = path.relative_to(project).as_posix()
            if any(s in rel for s in ("/Localization/MixedFonts/", "/Localization/Materials/")):
                continue
            if path.suffix in {".json", ".csv", ".tsv", ".txt"} and "/Resources/" not in rel and "/StreamingAssets/" not in rel:
                continue
            # These are old font-creator character inventories, not displayed UI.
            if path.suffix == ".txt" and any(p.lower() in {"font", "fonts"} for p in path.parts):
                continue
            if any(s in name.lower() for s in ("license", "ofl", "notice", "opencc")):
                continue
            yield rel, path


def digest(data):
    return hashlib.sha256(data).hexdigest()


def font_info(path, previous):
    stamp = [path.stat().st_size, path.stat().st_mtime_ns]
    if previous.get("stamp") == stamp:
        return previous
    from fontTools.ttLib import TTFont
    with TTFont(path) as font:
        return {"stamp": stamp, "sha256": digest(path.read_bytes()), "cmap": sorted(font.getBestCmap())}


def subset_font(source, target, chars, weight):
    from fontTools import subset
    from fontTools.ttLib import TTFont
    with TTFont(source, recalcTimestamp=False) as font:
        options = subset.Options()
        options.layout_features = ["*"]
        options.name_IDs = [0, 1, 2, 3, 4, 5, 6, 13, 14, 16, 17]
        tool = subset.Subsetter(options=options)
        tool.populate(unicodes=chars)
        tool.subset(font)
        # The OFL reserves 'Source'; the derivative uses a distinct family name.
        family = "Salasasa UI Sans"
        names = {1: family, 2: weight, 3: f"SalasasaUISans-{weight}-Subset1",
                 4: f"{family} {weight}", 6: f"SalasasaUISans-{weight}", 16: family, 17: weight}
        table = font["name"]
        for record in list(table.names):
            if record.nameID in names:
                table.removeNames(nameID=record.nameID, platformID=record.platformID,
                                  platEncID=record.platEncID, langID=record.langID)
        for key, value in names.items():
            table.setName(value, key, 3, 1, 0x409)
        if "CFF " in font:
            cff = font["CFF "].cff
            cff.fontNames = [f"SalasasaUISans-{weight}"]
            cff.topDictIndex[0].FamilyName = family
            cff.topDictIndex[0].FullName = f"{family} {weight}"
        target.parent.mkdir(parents=True, exist_ok=True)
        tmp = target.with_suffix(".tmp")
        font.save(tmp)
        with TTFont(tmp) as check:
            if set(chars) - check.getBestCmap().keys():
                raise RuntimeError(f"Subset lost required characters: {weight}")
        os.replace(tmp, target)


def run(project, output, cache_dir):
    started = time.perf_counter()
    cache_path = cache_dir / "inventory.json"
    old = read_json(cache_path, {})
    if old.get("version") != VERSION:
        old = {}
    records = {}
    corpus = set(range(32, 127))
    changed_files = 0
    for rel, path in inputs(project):
        stat = path.stat()
        stamp = [stat.st_size, stat.st_mtime_ns]
        record = old.get("files", {}).get(rel, {})
        if record.get("stamp") != stamp:
            # Ignore font/texture assets without reading megabytes of atlas YAML.
            with path.open(encoding="utf-8-sig", errors="replace") as stream:
                prefix = stream.read(5000)
                if path.suffix == ".asset" and ("m_FaceInfo:" in prefix or "Texture2D:" in prefix):
                    record = {"stamp": stamp, "chars": []}
                else:
                    content = prefix + stream.read()
                    record = {"stamp": stamp, "chars": sorted(set().union(*(codepoints(s) for s in strings(path, content)))),
                              "scoped": scoped_text(content) if path.suffix in {".unity", ".prefab", ".asset"} else {}}
            changed_files += 1
        records[rel] = record
        corpus.update(record["chars"])
    info = {}
    for weight, rel in {"Regular": REGULAR, **SOURCES}.items():
        info[weight] = font_info(project / rel, old.get("sources", {}).get(weight, {}))
    regular = sorted(corpus & set(info["Regular"]["cmap"]))
    latin = {cp for lo, hi in BASE_RANGES for cp in range(lo, hi + 1)}
    latin.update(cp for cp in corpus if not cjk(cp))
    manifest_path = output / "manifest.json"
    committed = project / "Assets/Editor/FontAutomation/Generated/manifest.json"
    previous = read_json(manifest_path, read_json(committed, {}))
    result = {"version": VERSION, "regularCharacters": regular, "regularSourceHash": info["Regular"]["sha256"], "fonts": [], "extraFonts": []}
    generated = []
    for weight, source in SOURCES.items():
        chars = sorted(latin & set(info[weight]["cmap"]))
        signature = digest((str(VERSION) + info[weight]["sha256"] + ",".join(map(str, chars))).encode())
        old_font = next((x for x in previous.get("fonts", []) if x["weight"] == weight), {})
        target = output / f"SalasasaSans-{weight}.otf"
        shipped = project / f"Assets/Resources/Localization/FontSources/{target.name}"
        candidate = target if target.exists() else shipped
        if old_font.get("signature") != signature or not candidate.exists() or digest(candidate.read_bytes()) != old_font.get("sha256"):
            subset_font(project / source, target, chars, weight)
            candidate = target
            generated.append(weight)
        result["fonts"].append({"weight": weight, "signature": signature, "characters": chars, "sourceHash": info[weight]["sha256"],
                                "sha256": digest(candidate.read_bytes()), "bytes": candidate.stat().st_size})
    catalog = read_json(cache_dir / "font-inputs.json", {"fonts": [], "aliases": []})
    aliases = {a["guid"]: a["target"] for a in catalog["aliases"]}
    translations = {}
    for entry in read_json(project / "Assets/Resources/Localization/PlatformUI.json", {}).get("entries", []):
        values = [entry.get(k, "") for k in ("key", "zh", "tw", "en")]
        for value in values:
            translations[value] = values
    scoped = {}
    for record in records.values():
        for guid, texts in record.get("scoped", {}).items():
            target = aliases.get(guid, guid)
            chars = scoped.setdefault(target, set())
            for value in texts:
                for localized in translations.get(value, [value]):
                    chars.update(codepoints(localized))
    for entry in catalog["fonts"]:
        source = entry["source"]
        if not source:
            raise ValueError(f"No font source for {entry['path']}")
        info[source] = font_info(project / source, old.get("sources", {}).get(source, {}))
        requested = set(entry["seed"]) | scoped.get(entry["guid"], set())
        if entry["family"].startswith("Font Awesome"):
            requested.update(cp for cp in corpus if unicodedata.category(chr(cp)) == "Co")
        else:
            requested.update(range(32, 127))
        result["extraFonts"].append({"path": entry["path"], "source": source, "sourceHash": info[source]["sha256"],
                                     "characters": sorted(requested & set(info[source]["cmap"]))})
    result["glyphSignature"] = digest(json.dumps(result, sort_keys=True).encode())
    write_changed(manifest_path, result)
    write_changed(cache_path, {"version": VERSION, "files": records, "sources": info})
    missing = sorted(corpus - set(info["Regular"]["cmap"]))
    report = {"scannedFiles": len(records), "changedFiles": changed_files, "generatedSubsets": generated,
              "regularCharacters": len(regular), "uncoveredCorpus": [f"U+{x:04X}" for x in missing],
              "elapsedMs": round((time.perf_counter() - started) * 1000, 1)}
    write_changed(cache_dir / "last-run.json", report)
    print(json.dumps(report, ensure_ascii=False))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    args = parser.parse_args()
    run(args.project.resolve(), args.output.resolve(), args.cache.resolve())
