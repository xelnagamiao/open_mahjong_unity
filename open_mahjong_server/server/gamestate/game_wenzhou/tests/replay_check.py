"""Independent physical replay reducer, deliberately not a game-state clone.

It consumes persisted ticks, never current seeds or scoring defaults. Only
physical movement and already-authoritative delta arithmetic are reproduced.
"""

from collections import Counter

from ..WenzhouGameState import TILES


def reduce_round(round_data):
    hands = [list(round_data[f"p{i}_tiles"]) for i in range(4)]
    wall = list(round_data["tiles_list"])
    rivers, melds, won = [[] for _ in range(4)], [[] for _ in range(4)], [[] for _ in range(4)]
    actor = round_data.get("start_player_index", 0)
    scores = list(round_data["wenzhou"]["start_scores"])
    final_meta = None
    for tick in round_data["action_ticks"]:
        kind = tick[0]
        if kind == "reset":
            actor = tick[1]
        elif kind in ("d", "gd"):
            tile = wall.pop(0 if kind == "d" else -1)
            assert tile == tick[1], ("wall order", kind, tile, tick)
            hands[actor].append(tile)
        elif kind == "c":
            tile = tick[1]
            hands[actor].remove(tile)
            rivers[actor].append(tile)
        elif kind in ("cl", "cm", "cr", "p", "g"):
            target = tick[2]
            expected = 3 if kind == "g" else 2
            for tile in tick[3:3 + expected]:
                hands[target].remove(tile)
            assert rivers[actor].pop() == tick[1]
            actor = target
        elif kind == "ag":
            for tile in tick[3:7]:
                hands[actor].remove(tile)
        elif kind == "jg":
            hands[actor].remove(tick[3])
        elif kind == "wenzhou":
            subtype = tick[1]
            if subtype == "meld":
                seat, mask, code = tick[2:5]
                existing = next((i for i, m in enumerate(melds[seat]) if m["code"] == "k" + code[1:]), None)
                value = dict(code=code, physical=list(mask[1::2]))
                if code[0] == "g" and existing is not None:
                    melds[seat][existing] = value
                else:
                    melds[seat].append(value)
            elif subtype == "win_source":
                winner, payer, source, tile = tick[2:6]
                if source == "discard":
                    assert rivers[payer].pop() == tile
                    won[winner].append(tile)
                elif source == "rob_kong":
                    hands[payer].remove(tile)
                    won[winner].append(tile)
            elif subtype == "kong_claim_source":
                seat, tile = tick[2:4]
                hands[seat].remove(tile)
                rivers[seat].append(tile)
                actor = seat
            elif subtype == "state":
                final_meta = tick[2]
                assert scores == tick[3], ("score arithmetic", scores, tick[3])
        elif kind.startswith("hu"):
            scores = [score + delta for score, delta in zip(scores, tick[4])]
        elif kind not in ("liuju", "end"):
            raise AssertionError(("unknown replay operation", tick))
        if "gs" in tick:
            offset = tick.index("gs") + 1
            scores = [score + delta for score, delta in zip(scores, tick[offset:offset+4])]
    physical = (wall + [round_data["wenzhou"]["indicator"]]
                + [t for hand in hands for t in hand]
                + [t for river in rivers for t in river]
                + [t for declarations in melds for m in declarations for t in m["physical"]]
                + [t for group in won for t in group])
    assert Counter(physical) == Counter({t: 4 for t in TILES})
    assert round_data["action_ticks"][-1] == ["end"]
    assert final_meta is not None
    assert final_meta["caishen"] == round_data["wenzhou"]["caishen"]
    assert final_meta["indicator_index"] == round_data["wenzhou"]["indicator_index"]
    assert final_meta["dice"] == round_data["wenzhou"]["dice"]
    return dict(hands=hands, rivers=rivers, melds=melds, won=won, wall=wall,
                scores=scores, final_meta=final_meta)
