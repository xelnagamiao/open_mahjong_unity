# Vietnamese Mahjong: Classic 160

## Authority, frozen edition and scope

`vietnamese/classic160` implements the **four-player Classic 160** game from
Bridget Young's *How to Play Vietnamese Mahjong, Revised and Expanded Edition*,
© 2026. It was the newest publicly accessible edition found through the author's
[updated guide announcement](https://www.reddit.com/r/Mahjong/comments/1vut8jt/updated_vietnamese_mahjong_guide_and_cheat_sheet/)
on 2026-10-12. The authoritative
[92-page PDF](https://drive.google.com/file/d/1ETjCEPo-fVDKkcQwZ_mK3qCsop5fhagD/view?usp=drive_link)
was fetched publicly; its PDF metadata modification date is 2026-08-22 10:33:55 UTC.

- File size: 10,620,484 bytes.
- Exact source SHA-256: `c8c94ceacde9ac518e874f0b32e467c9615cf697302537d5a214b080f1624ade`.
- Configuration version: `young-revised-20260822-classic160-v1`.

The complete copyrighted PDF is not included in the repository. Page references
below use **PDF page numbers**, starting at the cover; printed page numbers are
generally eight lower. The user confirmed Classic 160 as this delivery's scope.
**Modern 168/176/184 is not implemented**, including its repeated/framed jokers
and Modern-only scoring. Five/six-player rotation is also outside this four-seat
room implementation. No Modern room identity or silently accepted option exists.

## Rule traceability

| Rule area | PDF pages | Implementation |
| --- | --- | --- |
| Classic tiles, joker scopes/colors | 11–18 | `game_calculation/vietnamese/tiles.py` |
| Dice, chips, seats, wall/deal | 20–26 | `game_vietnamese/domain.py` opening flow |
| Calls, kong, bounce, discard restrictions | 27–31 | authoritative action windows and physical meld masks |
| Win, draw and dead wall | 32–33 | domain turn/response/end phases |
| Phán/mủn and payment | 35–36, 81–86 | `rules.py` payment functions and table regression tests |
| Hand patterns/exclusions | 37–46 | scored legal decompositions |
| No Flowers, No Leaves | 46–48 | eligibility, discard tracking and special declaration window |
| Situational and bonus scoring | 49–54 | flags, flower sets and joker replacement batches |
| Progressive counting | 55–56 | `progressive()` |
| Liability and exemption | 56–58 | domain feeding history and legal safe-discard search |
| Physical table penalties | 58–60 | requests rejected before mutation; physical misdeals are not simulated |

## Implemented Classic behavior

Four players start at 300 each. The set contains 136 ordinary tiles, 16 flowers
(four quartets) and eight unique special tiles. Seven special tiles are scoped
wildcards; Big Flower is a bonus tile rather than an in-hand wildcard. Project
IDs are 51–66 for flowers, 71 Anything, 72 Dot, 73 Bamboo, 74 Character, 75 Big
Flower, 76 Wind, 77 Dragon and 78 Number. Classic Number uses the blue discard
classification described in the book, independently of its scoring quartet.
The extra tile faces are original text artwork, not scans from the book.

Three dice break the wall. Deal three packets of four, then the last tiles with
the dealer taking the first and third stack tops. Opening replacement proceeds
around the seats; new replacements are considered on the next cycle. Flowers
must be replaced unless the hand is eligible to discard bonus tiles; joker
replacement is optional and is selected explicitly. Replacement batches are
recorded because their timing changes scoring. Replacement draws come from the
wall tail. The final 26 tiles are reserved; neither ordinary nor supplemental
draws can consume them. The last discard can still win.

A normal win uses four groups and a pair with constrained joker assignments.
Only a real fourth tile can kong; joker-filled pungs cannot be promoted. Only
an added kong can be robbed. Bounce exchanges the newly drawn natural tile for
a wildcard in one's own exposed pung, exposes that wildcard as a bonus and takes
a supplemental draw. A discarded flower can only complete an Anything-Joker
win; a discarded joker retains its normal scope when called. The server keeps
physical tiles separate from represented faces in every meld and in replays.

Discard responses use win > kong > pung > chow, and only the next seat may chow.
Competing pungs compare joker count, versatility and seat distance. Opaque
claim IDs refer to the exact offered physical selection; clients cannot invent
wildcard assignments. A passed win/pung blocks that physical tile until a draw
and discard; self-draw remains possible. A claim cannot strand a player with
only bonus tiles they are prohibited from discarding.

No Flowers, No Leaves tracks whether any bonus/wildcard has been exposed,
compulsory honor discards, safe discarded bonus tiles, immediate declarations,
and the permitted later arbitrary-shape draw. A special declaration is offered
after winning claims are resolved but before a nonwinning call commits. This
interprets “safe” as not causing a win: an otherwise valid nonwinning call resumes
if the discarder declines their special declaration. True No Flowers, No Leaves
uses the Classic 1 mủn value. A six-bonus immediate declaration does not require
a conventional hand. Fully detailed branch tests cover the distinct cases.

Dealer wins and exhaustive draws repeat; otherwise the seats rotate one step.
A room ends after `game_round` winds finish, including any required dealer
repeats. Negative scores do not truncate the book's match. Scoring chooses the
highest valid payment decomposition and applies explicit exclusions and
progressive counting. Zero phán pays 1 from each opponent. Six phán is 1 mủn:
single payment 32, double payment 64. For 7 phán under defaults the payments are
38/70, **not** 38/76: the additional phán is added after doubling. All three
opponents pay; a discarder normally pays double and other opponents single,
while a self-draw makes all three pay double. Robbing-kong incremental liability
and the book's other feeding liabilities use the applicable exemption check.

## Combination interpretation

The book states general composition patterns and lists certain exclusions, but
does not provide an exhaustive combination matrix for every special shape.
This implementation applies the written general definitions where no exclusion
is stated: Thirteen Orphans also satisfies Terminals and Honors; optional Seven
Pairs can also satisfy suit/honor composition patterns; self-drawn closed special
hands qualify as fully closed. No-joker/progressive bonuses then follow their
written conditions. This is a disclosed interpretation of the definitions,
not a claim that the author supplied those exact combined examples. The tests
pin these choices to this rule version so an author clarification can produce a
new version without silently changing saved games.

## Optional rules from the book

| Key | Default | Accepted values |
| --- | --- | --- |
| `seven_pairs` | false | boolean; self-draw only |
| `same_number_bouquet` | false | boolean |
| `eight_flowers` | false | boolean |
| `discarding_flowers` | false | boolean; optional three-flower declaration |
| `blessing_of_man` | false | boolean |
| `six_consecutive_wins` | false | boolean |
| `orphans_phan` | 13 | 12/13 |
| `additional_phan` | 6 | 1/2/6 |
| `mun_bonus` | 0 | 0/6/10/40 |
| `quad_bonus` | 0 | 0/5/6 |

Unknown fields, types and rule versions are rejected. Defaults follow the main
Classic presentation; alternatives remain visible in the create-room panel.
Configuration, represented faces, physical masks, replacement batches, eligible
claims, remaining wall and action ticks survive reconnect and saved replay.
Basic game/score/win/deal-in metrics are persisted using the unified writer.
Settlement pattern labels explain payments, but fan-category aggregates remain
empty and no matching/ranking queue has been introduced.

See [integration and validation](variant-integration.md). The tests and isolated
Windows build provide finite coverage; they do not establish all device targets
or four human clients on an external network.
