"""Physical identities and deterministic witnesses for wildcard Mahjong.

Protocol tile ids are 11..19, 21..29, 31..39 and 41..47 (中白发=45,46,47).
Physical supply and the closed logical multiplicity limit are separate rules.
"""

from dataclasses import dataclass

NUMBERS = tuple(s * 10 + r for s in (1, 2, 3) for r in range(1, 10))
HONORS = tuple(range(41, 48))
TILES = NUMBERS + HONORS
ORPHANS = (11, 19, 21, 29, 31, 39) + HONORS
INDEX = {t: i for i, t in enumerate(TILES)}


@dataclass(frozen=True)
class JokerPolicy:
    joker_tiles: tuple = ()
    natural_map: tuple = ()
    logical_tiles: tuple = TILES
    physical_tiles: tuple = TILES
    physical_limits: tuple = ()
    joker_targets: tuple | None = None
    meld_count: int = 4
    closed_cap: int | None = None
    shapes: tuple = ("standard",)
    pair_tiles: tuple | None = None

    def __post_init__(self):
        for field in ("joker_tiles", "logical_tiles", "physical_tiles", "shapes"):
            object.__setattr__(self, field, tuple(getattr(self, field)))
        for field in ("natural_map", "physical_limits"):
            object.__setattr__(self, field, tuple(tuple(x) for x in getattr(self, field)))
        for field in ("joker_targets", "pair_tiles"):
            value = getattr(self, field)
            if value is not None:
                object.__setattr__(self, field, tuple(value))
        if type(self.meld_count) is not int or not 0 <= self.meld_count <= 5:
            raise ValueError("meld_count must be an integer between zero and five")
        if self.closed_cap is not None and (type(self.closed_cap) is not int or self.closed_cap < 1):
            raise ValueError("closed_cap must be positive or None")
        if not self.logical_tiles or not self.physical_tiles:
            raise ValueError("tile domains cannot be empty")
        for name in ("joker_tiles", "logical_tiles", "physical_tiles"):
            values = getattr(self, name)
            if len(set(values)) != len(values) or any(type(t) is not int for t in values):
                raise ValueError("tile domains must contain unique integer ids")
        if not set(self.logical_tiles) <= set(TILES):
            raise ValueError("logical tiles must be ordinary project tile ids")
        if not set(self.physical_tiles) <= set(TILES + tuple(range(51, 59))):
            raise ValueError("unsupported physical tile id")
        if not set(self.joker_tiles) <= set(self.physical_tiles):
            raise ValueError("joker kinds must be physical tiles")
        for domain in (self.joker_targets, self.pair_tiles):
            if domain is not None and (len(set(domain)) != len(domain)
                                       or not set(domain) <= set(self.logical_tiles)):
                raise ValueError("replacement/pair domain must be within logical domain")
        if (any(len(x) != 2 for x in self.natural_map)
                or len(dict(self.natural_map)) != len(self.natural_map)
                or any(a not in self.physical_tiles or b not in self.logical_tiles for a, b in self.natural_map)):
            raise ValueError("invalid fixed natural mapping")
        if (any(len(x) != 2 for x in self.physical_limits)
                or len(dict(self.physical_limits)) != len(self.physical_limits)
                or any(t not in self.physical_tiles or type(n) is not int or n < 1
                       for t, n in self.physical_limits)):
            raise ValueError("invalid physical tile supply")
        if not set(self.shapes) <= {"standard", "seven_pairs", "eight_pairs", "thirteen_orphans"}:
            raise ValueError("unknown wildcard structural shape")

    def natural(self, physical):
        return dict(self.natural_map).get(physical, physical)

    def supply(self, physical):
        return dict(self.physical_limits).get(physical, 1 if physical >= 51 else 4)


@dataclass(frozen=True)
class ExternalMeld:
    kind: str
    physical: tuple
    logical: tuple = ()
    concealed: bool = False

    def __post_init__(self):
        object.__setattr__(self, "physical", tuple(self.physical))
        object.__setattr__(self, "logical", tuple(self.logical))


@dataclass(frozen=True)
class JokerUse:
    index: int
    physical: int
    natural: int
    logical: int

    @property
    def substituted(self):
        return self.logical != self.natural


@dataclass(frozen=True)
class ResolvedGroup:
    kind: str
    logical: tuple
    physical: tuple
    indices: tuple
    external: bool = False
    concealed: bool = True

    @property
    def tiles(self):
        return self.logical

    @property
    def tile(self):
        return self.logical[1] if self.kind == "sequence" else self.logical[0]


@dataclass(frozen=True)
class WinningShape:
    kind: str
    groups: tuple
    assignment: tuple
    joker_uses: tuple
    winning_group: int = -1
    winning_index: int = -1
    external_joker_uses: tuple = ()

    @property
    def melds(self):
        return tuple(g for g in self.groups if g.kind in ("sequence", "triplet", "kong"))

    @property
    def pair(self):
        pairs = [g.tile for g in self.groups if g.kind == "pair"]
        return pairs[0] if len(pairs) == 1 else 0

    @property
    def logical_tiles(self):
        return tuple(t for g in self.groups for t in g.logical)

    @property
    def closed_logical_tiles(self):
        return tuple(t for g in self.groups if not g.external for t in g.logical)

    @property
    def winning_logical_tile(self):
        return self.assignment[self.winning_index] if self.winning_index >= 0 else 0

    @property
    def is_natural(self):
        return all(not use.substituted for use in self.joker_uses + self.external_joker_uses)

    def concealed_pungs(self, self_draw):
        return tuple(g for i, g in enumerate(self.groups)
                     if g.kind in ("triplet", "kong") and g.concealed
                     and (self_draw or i != self.winning_group))
