from .ledger import Chicken, Kong, settle


def test_counts_describe_physical_items_without_multiplying_by_payers():
    players = [dict(hand_tiles=[], combination_tiles=[], discard_tiles=[]) for _ in range(4)]
    players[0]["combination_tiles"] = ["G31"]
    result = settle(players, winners=[0], source="self_draw", scores={0:3},
                    ready_values=[3]*4, indicator=39, kongs=[Kong(0,31,"concealed")])
    chickens = [t for t in result.transfers if t.category == "chicken"]
    kongs = [t for t in result.transfers if t.category == "kong"]
    assert len(chickens) == 3 and all((t.count,t.unit_points,t.points) == (4,2,8) for t in chickens)
    assert len(kongs) == 3 and all((t.count,t.unit_points,t.points) == (1,3,3) for t in kongs)
    assert result.changes == [42,-14,-14,-14]
    assert all(t["count"] is not None for t in result.as_dict()["transfers"] if t["category"] != "hand")


def test_responsibility_and_not_ready_chicken_preserve_quantity():
    players = [dict(hand_tiles=[], combination_tiles=[], discard_tiles=[]) for _ in range(4)]
    players[1]["combination_tiles"] = ["k31"]
    result = settle(players, winners=[0], source="discard", payer=2, scores={0:3},
                    ready_values=[0]*4, indicator=39, chickens=[Chicken(31,2,1)])
    entries = [t for t in result.transfers if t.category == "chicken"]
    assert {(t.reason,t.count,t.unit_points,t.points) for t in entries} == {("包鸡",3,2,6)}
