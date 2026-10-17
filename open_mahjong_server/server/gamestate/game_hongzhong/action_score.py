"""Prepare the actual drawn hand for authoritative win validation."""
from ...game_calculation.hongzhong import rules as book
from ..public.local_hint_context import hint_context

def prepare_draw_score(hand, melds, winning_tile, replacement):
    # Only the actual drawn hand is authoritative server work. Preview rows
    # and hypothetical discards are calculated by the viewing client.
    payload = hint_context("hongzhong", hand, melds)
    detail = book.score(hand, melds, winning_tile=winning_tile, replacement=replacement) if len(hand)+3*len(melds)==14 else None
    return payload, detail
