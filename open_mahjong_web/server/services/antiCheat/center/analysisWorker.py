"""Persistent NDJSON worker unifying ruleBridge and featureBridge in one process.

Sharing one warm Python process across decisions and features avoids 2 cold
interpreter startups per game and preserves tingpai/hepai/shanten LRU caches
across consecutive games.
"""
import json
import sys
from pathlib import Path

CENTER_DIR = Path(__file__).resolve().parent
ANTI_CHEAT_DIR = CENTER_DIR.parent
sys.path.insert(0, str(ANTI_CHEAT_DIR))
sys.path.insert(0, str(CENTER_DIR))

from ruleBridge import one_decision  # noqa: E402
from featureBridge import meld_feature, donor_cost  # noqa: E402


def handle_decisions(decisions):
    results = []
    for decision in decisions:
        try:
            results.append(one_decision(decision))
        except Exception as exc:
            results.append({
                "id": decision.get("id"),
                "status": "unavailable",
                "reason": str(exc)[:160],
            })
    return {"calculator": "repository_standard_mcr_v1", "results": results}


def handle_features(payload):
    result = {"melds": {}, "donors": {}}
    for kind, function, output in [
        ("melds", meld_feature, "melds"),
        ("donors", donor_cost, "donors"),
    ]:
        for item in payload.get(kind, []):
            try:
                result[output][item["id"]] = function(item)
            except Exception as error:
                result[output][item["id"]] = {
                    "status": "unavailable",
                    "reason": str(error)[:160],
                }
    return result


def main():
    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue
        req_id = None
        try:
            request = json.loads(line)
            req_id = request.get("id")
            op = request.get("op")
            if op == "decisions":
                out = handle_decisions(request.get("decisions", []))
            elif op == "features":
                out = handle_features(request.get("payload", {}))
            else:
                raise ValueError(f"unknown_op:{op}")
            response = {"id": req_id, "ok": True, "result": out}
        except Exception as exc:
            response = {"id": req_id, "ok": False, "error": str(exc)[:200]}
        sys.stdout.write(json.dumps(response, ensure_ascii=False, separators=(",", ":")) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
