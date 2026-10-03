"""Seeded dice seating from the updated user rulebook."""

import random


def arrange_seats(seed):
    rng = random.Random(seed)
    rolls = [[] for _ in range(4)]

    def rank(group):
        if len(group) == 1:
            return group
        buckets = {}
        for index in group:
            dice = [rng.randint(1,6),rng.randint(1,6)]
            rolls[index].append(dice)
            buckets.setdefault(sum(dice),[]).append(index)
        # Ties roll again; no seat or user-id preference.
        return [index for value in sorted(buckets,reverse=True) for index in rank(buckets[value])]

    order = rank(list(range(4)))
    direction_dice = [rng.randint(1,6),rng.randint(1,6)]
    return order, dict(rolls=rolls,east_position=(sum(direction_dice)-1) % 4,
                       direction_dice=direction_dice,original_seat_order=order)


def wall_break(dice):
    return (((sum(dice)-1) % 4)*36 + min(dice)*2) % 144
