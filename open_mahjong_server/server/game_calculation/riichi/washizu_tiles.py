"""Stable Washizu tile IDs. Shape and visibility are separate properties.

Ordinary IDs are opaque; 2000 + ordinary ID is transparent. Each face has
exactly one opaque and three interchangeable transparent copies. These IDs
survive network messages and records; only calculation normalizes them.
"""
SUB_RULE = 'riichi/washizu'
REALTIME_SUB_RULE = 'riichi/washizu_realtime'
SUB_RULES = (SUB_RULE, REALTIME_SUB_RULE)
OFFSET = 2000
FACES = tuple(range(11, 20)) + tuple(range(21, 30)) + tuple(range(31, 40)) + tuple(range(41, 48))

def is_transparent(tile):
    return isinstance(tile, int) and tile - OFFSET in FACES

def face(tile):
    return tile - OFFSET if is_transparent(tile) else tile

def normalize(tile):
    tile = face(tile)
    return {105: 15, 205: 25, 305: 35}.get(tile, tile)

def same_physical_kind(a, b):
    # Never silently replace an opaque tile with a transparent copy (or vice versa).
    return is_transparent(a) == is_transparent(b) and normalize(a) == normalize(b)

def make_tiles():
    return [tile for base in FACES for tile in (base, base + OFFSET, base + OFFSET, base + OFFSET)]
