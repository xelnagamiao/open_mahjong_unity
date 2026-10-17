"""Offline preview reference, never called by the server."""
from dataclasses import replace
from server.game_calculation.guangdong import config, rules

def calculate_tip_batch(requests):
    """可序列化的纯计算任务，在公共 CPU 工作池运行。"""
    result = []
    for hand, melds, context, passed_score in requests:
        payload = {"hand": list(hand), "melds": list(melds), "waits": [], "discard_waits": {}}
        nominal = len(hand) + 3 * len(melds)
        if nominal == 13:
            payload["waits"] = rules.waiting_scores(hand, melds, context=context, passed_base_score=passed_score)
        elif nominal == 14:
            for cut in sorted(set(hand)):
                remainder = list(hand)
                remainder.remove(cut)
                after_cut = replace(context, heavenly=False, earthly=False, kong_flower=False,
                    discarded_ghosts=context.discarded_ghosts + (cut in config.GHOSTS))
                waits = rules.waiting_scores(remainder, melds, context=after_cut)
                if waits:
                    payload["discard_waits"][str(cut)] = waits
        result.append(payload)
    return result
