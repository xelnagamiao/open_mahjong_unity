# Filipino Mahjong: classic16

## Authority and version

The room identity is `filipino/classic16`; its immutable configuration version is
`classic16-ging2010-v1`. This is a documented house profile, not a claim that the
Philippines has one national standard. Sources checked on 2026-10-12:

1. Ging, [How to Play Filipino Mahjong](https://gingsbrain.blogspot.com/2010/10/how-to-play-filipino-mahjong.html), October 2010. Primary description used for classic16. Retrieved HTML SHA-256: `946371c3d1aa54b408e623f0978e28650581732302a57e9d9bd76707f399730f`.
2. MahjLife, [Filipino Mahjong Quick Reference](https://mahjlife.com/wp-content/uploads/2023/09/MahjLife_FilipinoMahjongQuickReference.pdf), September 2023.
3. MahjLife, [Manila quick reference](https://mahjlife.com/wp-content/uploads/2023/09/MahjLife_FilipinoMahjongQuickReferenceManilla.pdf), September 2023 (the source filename spells “Manilla”).

The two MahjLife sheets cite Ging and describe variations; they are secondary
summaries, not independent evidence of a national ruleset. The website's direct
PDF download returned HTTP 403 during this work; its publicly indexed content
was readable. Their CC BY-NC-ND sheets and the full article are not redistributed.

## Classic16 behavior

Four players use 144 physical tiles. Only the 108 suit tiles form hands; all 28
honor tiles and eight flowers are bonus tiles. A resting hand has 16 tiles and a
complete hand has 17: five sets and a pair, or seven distinct pairs plus one
chow/pung. Four equal tiles are not two pairs by default. The opening deal is
eight to each player, then nine to mano and eight to each other player.

Opening bonuses are replaced around the table; a newly drawn bonus is dealt
with on that player's next replacement cycle. Normal draws use the wall front,
bonus/kong replacement uses the back. There is no reserved dead wall. A final
discard may still win. Only the next player may chow; win takes priority over
pung/kong, which take priority over chow; simultaneous wins go to the nearest
eligible seat. The fourth tile for sagasa must be the current draw.

Secret/concealed kong, open kong and sagasa pay immediately. Their default
per-opponent values are respectively 1, 2 and 2. Thirteen bonuses and opening
no-bonus each pay 1 per opponent, once per qualifying occurrence. The winner's
ordinary value defaults to 4; bunot/self-draw doubles the total hand payment.
Discard wins are paid by all three opponents in classic16. All accounting is
zero-sum integer game points; these numbers are not currency.

Ambitions include winning without bonus tiles, seven pairs plus a group, all-up,
all-down, all chow, all pung, single/back-to-back/in-between waits, escalera,
flush and an early win. In-between replaces the generic single-wait award.
The server checks the winning decomposition and the actual wait, not a client's
claimed award. Winner-roll selects the next dealer; a wall draw retains the
dealer. A platform match lasts `game_round * 4` completed hands, including draws.

## Source differences and explicit choices

| Topic | Sources differ or are silent | classic16 choice |
| --- | --- | --- |
| Ordinary payment | Ging does not fix a universal base payment | Platform default 4 points, configurable; not attributed to Ging |
| Honors | Ging treats winds/dragons as bonus tiles; the general sheet contains inconsistent honor wording | All 28 honors are bonuses |
| Winning length | General sheet says five sets of five in one line | Five groups of three plus a pair, consistent with Ging's 16/17 tiles |
| Seven pairs | Ging requires an additional chow/pung; Manila's brief row omits it | Seven pairs **plus one group**, never a 14-tile win |
| Duplicate pair ranks | Wording does not settle four equal tiles as two pairs | Distinct pair ranks by default; explicit house option permits two |
| Self-draw/discard payments | Ging bunot is double; Manila has 1x/self and discarder-only | Ging defaults; Manila payment preset changes these two fields |
| Kong awards | Ging secret/open/sagasa = quarter/half/half; Manila = half/half/quarter | Normalize quarter to 1 game point; defaults 1/2/2, preset 2/2/1 |
| Bonus thresholds | Ging names 13; Manila names 13/16/19 | 13 default; optional additional thresholds |
| Opening award | Ging no flowers; Manila zero or one with a different fractional amount | No flowers = 1 default; optional one flower = 1 is a normalized house option, not an exact Manila currency table |
| Escalera, flush, early win | Ging describes ambitions but leaves these payment amounts unclear | Manila's quarter/quarter/half normalized to 1/1/2 |
| Double dice / dealer | Not a single consistent general convention | Double dice off; winner-roll default; rotation/winner are explicit house alternatives |
| Dealer after a drawn hand | Ging says to rebuild the wall but does not explicitly select the next dealer | Retain the current dealer for winner-roll and winner modes; only the explicit rotation option rotates after a draw |
| Physical errors | Printed rules permit table penalties for mistakes | Illegal requests rejected before state mutation; no simulated physical misdeal/dead-hand penalty |

The UI's **Manila payment options** preset only changes payment/bonus settings;
it deliberately keeps Ging's classic16 hand structure. It does not claim to
implement every Manila practice. Joker, Popeye, Goulash and Jai Alai variants
are not supported.

## Room options

| Key | Default | Accepted server values |
| --- | --- | --- |
| `base_points` | 4 | integers 1–1000; UI 4/8/16 |
| `self_draw_multiplier` | 2 | 1/2 |
| `discard_payment` | all | all/discarder |
| `seven_pairs` | true | boolean |
| `pair_quads_as_two` | false | boolean |
| `ambitions` | true | boolean |
| `opening_no_flowers` | true | boolean |
| `opening_one_flower` | false | boolean |
| `extra_flower_thresholds` | false | boolean |
| `secret_points` | 1 | integers 0–100; UI 0/1/2 |
| `kong_points` | 2 | integers 0–100; UI 0/1/2 |
| `sagasa_points` | 2 | integers 0–100; UI 0/1/2 |
| `dice_double` | false | boolean |
| `dealer_mode` | winner_roll | winner_roll/rotate/winner |

Unknown fields, wrong types and unknown rule versions are rejected. The room,
reconnect snapshot and saved game record retain the normalized complete config.
Basic statistics use the existing unified record/metrics writer. Award labels
may appear in a hand settlement for explanation, but `recorded_fans` and persisted
`fan_stats` stay empty; this profile has no fan-category ranking or matchmaking.

See [integration and validation](variant-integration.md) for architecture and
reproducible tests. Automated replay/scene checks do not establish four-machine
network reliability or every human input/device combination.
