# Jiandan Fan Details

This document is a working reference for per-fan rule details, edge cases, examples, and implementation notes.

Chinese version: [简单逐番说明](fan_details.zh-CN.md)

Source table: [Jiandan Fan Scoring](fan_scoring.md)

## Template

Each fan entry should eventually define:

- Value: point value before win-payment multiplication.
- Definition: exact hand condition.
- Exclusions / row rule: what higher or same-row pattern replaces it. Most fan rows score only the highest-value matching pattern; the three-suit-number row instead scores the highest total of non-overlapping patterns.
- Edge cases: ambiguous situations to test.
- Examples: sample hand shapes.
- Implementation notes: calculation hints or pitfalls.

## Global Row Rule

For every row except the three-suit-number row, if multiple patterns in the same row match the hand, score only the highest-value matching pattern from that row. Do not add lower-value row patterns as extra points when a higher-value row pattern applies.

Example: in the wind row, a hand with two wind triplets and one wind pair scores `小三风`, not `小三风 + 二风刻`.

For the three-suit-number row, score the highest total of patterns built from non-overlapping resolved units (sequence, triplet, declared kong, and pair). A unit cannot be reused by two patterns in that row. Thus `小三色同刻` may coexist with a disjoint `二色同刻`, and `二色同刻` may repeat when each occurrence uses different triplets/kongs. This is not a greedy “take the highest pattern first” rule.

The consecutive-triplet row already permits multiple disjoint `二连刻` occurrences; higher consecutive-triplet patterns still replace the lower patterns formed from the same triplets.

## Wind Fan Design Notes

This rule intentionally does not include seat-wind or round-wind triplet scoring.

Reasons:

- Lower new-player complexity.
- Remove dealer/seat asymmetry from scoring.
- Keep east-only, half-game, and full-game formats equivalent for fan calculation.
- Avoid requiring a full wind round just to make wind/round scoring feel complete.

For wind-row fans:

- Wind triplets and wind kongs both count as wind sets.
- Exposed triplets, concealed triplets, exposed kongs, added kongs, and concealed kongs all count.
- Pairs only count for `小三风` and `小四喜`.
- Seven-pairs hands do not count wind pairs as wind triplets or wind sets for this row.

## Terminal / Honor / Straight Fan Design Notes

This row contains `断幺九`, `一气通贯`, `混全带幺`, `混幺九`, `纯全带幺`, `清幺九`, and `十三幺九`.

For this row:

- Score only the highest-value matching pattern from the row.
- `一气通贯` is in this row for table consistency, but it cannot practically coexist with the other row patterns: it needs `123`, `456`, and `789` in one suit, which includes both terminals and simples and therefore conflicts with `断幺九`, all terminal/honor-only hands, and all every-set-contains-terminal/honor hands.
- Honors are winds and dragons.
- Terminals are suited 1 and 9 tiles.
- "Standard hand" means four sets plus one pair. `十三幺九` is a special hand and does not need the standard-hand structure.
- Exposed and concealed sets both count unless an individual fan says otherwise.
- The winning tile counts as part of the final hand structure, whether it completes a sequence, triplet, pair, or special hand.

## Shape Fan Design Notes

This row contains `平和`, `对对和`, and `七对子`.

For this row:

- Score only the highest-value matching pattern from the row. Since `对对和` and `七对子` both score 3 points, either one replaces `平和` when applicable.
- These fans describe the winning hand's structure, not wait shape, concealed status, or pair value.
- The winning tile counts as part of the final hand structure.
- `平和` in this rule is not Japanese riichi pinfu and not Guobiao `平和`: it only means four sequences plus one pair.
- `七对子` allows repeated pairs: four identical tiles may be treated as two pairs.
- There is no extra fan for repeated pairs or four identical tiles in a seven-pairs hand; this rule set has no `四归一`.
- A scoring candidate is either a seven-pairs special shape or a standard four-sets-plus-pair decomposition, not both. If the same 14 tiles can be decomposed both ways, evaluate both candidates and choose the higher-scoring candidate, but do not combine `七对子` with sequence/triplet decomposition-only fans from the standard candidate.
- Outside `七对子`, four identical concealed tiles that were not declared as a kong cannot be split or reused as standard-hand components: they do not count as a triplet plus a pair, and they do not count as an undeclared kong.

## Suit / Honor Color Fan Design Notes

This row contains `混一色`, `清一色`, `字一色`, and `九莲宝灯`.

For this row:

- Score only the highest-value matching pattern from the row.
- `混一色`, `清一色`, and `字一色` judge the final winning hand's tile composition, including concealed tiles, exposed melds, declared kongs, and the winning tile.
- `混一色`, `清一色`, and `字一色` may be standard hands or seven-pairs hands.
- `九莲宝灯` is stricter: it must be closed, must be the true nine-sided wait from the 13-tile shape `1112345678999` in one suit, and the winning tile must be any tile from 1 through 9 in that same suit.
- `九莲宝灯` necessarily also has `清一色` tile composition, but this row scores only `九莲宝灯`.
- There is no looser "final 14 tiles contain 1112345678999 plus any same-suit tile" version of `九莲宝灯`.

## Three-Suit Number Pattern Fan Design Notes

This row contains `二色同刻`, `小三色同刻`, `三色同顺`, and `三色同刻`.

For this row:

- These fans only use suited tiles. Honors do not participate.
- The common object is the same number across different suits.
- For triplet/kong patterns, the matching tiles must be part of the resolved winning structure as triplets or declared kongs; merely holding three identical tiles is not enough if they are not used as a triplet/kong structure.
- Triplets and declared kongs both count. Exposed and concealed triplets/kongs both count.
- `小三色同刻` requires its pair to be the actual pair in the resolved winning structure.
- `七对子` does not participate in triplet/kong patterns in this row.
- Four identical concealed tiles that were not declared as a kong cannot be split or reused as triplet/kong material for this row.
- `三色同顺` requires the same sequence in all three suits in one resolved winning decomposition. Exposed sequences count.
- `三色同顺` cannot coexist with `二色同刻`, `小三色同刻`, or `三色同刻` in a legal winning hand because the three sequences already consume three meld slots, leaving too few meld slots for the triplet/kong patterns.
- `三色同顺` also cannot coexist with `一气通贯` in a legal winning hand because the required meld counts would exceed four.
- Score the highest total of non-overlapping patterns in this row. A triplet/kong or pair used by `小三色同刻` cannot also be used by `二色同刻`; different unused triplets/kongs may form additional `二色同刻` occurrences.
- Example: `222m 222p 22s 333m 333p` scores `小三色同刻` (using the 2s structures) plus `二色同刻` (using the 3s structures).

## Closed / Opening Win Fan Design Notes

This row contains `门前清`, `天和`, and `地和`.

For this row:

- Score only the highest-value matching pattern from the row. `天和` and `地和` replace `门前清`.
- `门前清` means no chi, pon, exposed kong, or added kong. Concealed kong does not break closed-hand status.
- `天和` and `地和` are checked only during the uninterrupted opening of the hand.
- `天和` and `地和` are naturally closed, but no extra `门前清` score is added because they are in the same row and are already capped at 20 points.
- A concealed-kong supplement draw may still leave the hand closed for `门前清`, but it cannot trigger `天和` or `地和`.
- Implementation needs opening-flow state: whether the hand is still in uninterrupted natural opening flow, whether any chi/pon/kong/win has occurred, and whether each non-dealer is on their first natural draw.

## Last-Tile Win Fan Design Notes

This row contains `海底捞月` and `河底捞鱼`.

For this row:

- There is no dead wall and no 14-tile reserve. The "last tile" is the true last tile in the live wall.
- `海底捞月` applies when the winning self-draw tile is the true last tile in the wall, whether it was drawn as a normal draw or as a kong supplement draw.
- If the true last tile is drawn as a kong supplement and wins, `海底捞月` may combine with `杠上开花`; these are in different rows.
- `河底捞鱼` applies when the wall is empty and a player's discard is won by another player.
- If a kong supplement draw empties the wall and the player discards instead of winning, a win on that discard counts as `河底捞鱼`.
- Once the wall is empty, the final discard can only be won. Chi, peng, and kong are not allowed on that discard.
- Same-tile discard-win lockout still applies to `河底捞鱼`, because it is a discard win.
- If multiple players can win the final discard, fixed seat priority selects the first legal winner.
- If the final discard is not won by anyone, the hand ends by exhausted wall after all responses resolve.

## Kong-Triggered Win Fan Design Notes

This row contains `杠上开花` and `抢杠`.

For this row:

- `杠上开花` is a self-draw win on the supplement tile after a legal kong.
- Concealed kongs, exposed kongs, and added kongs can all lead to `杠上开花`.
- `抢杠` applies only to added kong. Concealed kongs and exposed kongs cannot be robbed.
- Added kong must open a robbing-kong response window before the kong is finalized. If any player wins by robbing it, the kong does not form and no supplement tile is drawn.
- `抢杠` is treated as discard-win-like for response and payment purposes: same-tile discard-win lockout applies, and fixed seat priority selects the first legal winner.
- If the wall is empty, no kong action should be offered because no supplement tile can be drawn.
- `杠上开花` may combine with `海底捞月` if the supplement tile is the true last tile in the live wall.

## Concealed Triplet Fan Design Notes

This row contains `二暗刻`, `三暗刻`, and `四暗刻`.

For this row:

- Count concealed triplets and concealed kongs in the resolved winning structure.
- Concealed kongs count as concealed triplets for this row.
- Exposed triplets, exposed kongs, and added kongs do not count.
- A triplet completed by self-draw counts as concealed.
- A triplet completed by discard win or robbing a kong does not count as concealed.
- Concealed triplets already fully present in the hand still count when the win is by discard on some other part of the hand.
- The tiles must be used as triplet/kong structures in the resolved winning structure; merely holding three identical tiles is not enough.
- Four identical concealed tiles that were not declared as a kong cannot be split or reused as standard-hand components for this row.
- Seven-pairs hands do not participate in this row.
- `四暗刻` is naturally closed and naturally also `对对和`, but because it is already 20 points, those facts normally do not affect the final capped score.
- No special extra score for single-wait `四暗刻`; all `四暗刻` hands score 20 points.

## Consecutive Triplet Fan Design Notes

This row contains `二连刻`, `三连刻`, and `四连刻`.

For this row:

- Count consecutive-number triplets/kongs in the same suited family.
- Honors do not participate.
- Different suits do not connect.
- Triplets and declared kongs both count.
- Exposed triplets, concealed triplets, exposed kongs, added kongs, and concealed kongs all count.
- A triplet completed by discard win or robbing a kong counts, because this row does not care whether the triplet is concealed.
- The matching tiles must be used as triplet/kong structures in the resolved winning structure; merely holding three identical tiles is not enough.
- Four identical concealed tiles that were not declared as a kong cannot be split or reused as standard-hand components for this row.
- Seven-pairs hands do not participate in this row.
- Within one consecutive chain, score only the highest pattern: `222m 333m 444m` is `三连刻`, not `三连刻 + 二连刻`.
- Special row-scoring exception: multiple non-overlapping `二连刻` instances may score separately. Examples: `222m 333m 777m 888m 55p` and `222m 333m 777p 888p 55s` each score two `二连刻` instances.
- Do not reuse the same triplet/kong in multiple consecutive-triplet scores.
- `四连刻` is naturally also `对对和`, but because `四连刻` is already 20 points, this normally does not affect the final capped score.

## Identical Sequence Fan Design Notes

This row contains `一般高`, `二般高`, `一色三同顺`, and `一色四同顺`.

For this row:

- Count repeated identical sequences.
- Only suited sequences participate. Honors do not participate.
- The sequences must have the same suit and same sequence start.
- Exposed chi sequences and concealed sequences both count.
- The sequences must exist in the resolved winning decomposition; raw tile composition alone is not enough.
- Seven-pairs hands do not participate in this row.
- `二般高` is not riichi `两杯口`: it does not require a closed hand.
- This rule set intentionally does not include Guobiao `喜相逢` for same sequence in different suits, because that pattern occurs too often to match the intended 1-point difficulty.
- Within one repeated-sequence group, score only the highest pattern: three identical sequences score `一色三同顺`, not `一般高`; four identical sequences score `一色四同顺`.
- `二般高` represents two different `一般高` groups and replaces scoring two separate `一般高` instances.
- This differs from the three-suit triplet and consecutive-triplet rows: repeated lowest-level patterns such as "double `二色同刻`" or "double `二连刻`" are allowed as repeated scores, while this row gives the repeated-pair case its own 8-point pattern, `二般高`.
- Design reason: `二般高` has a distinct aesthetic of two repeated sequence pairs and a hand shape where tiles neatly come in pairs; "double `二色同刻`" and "double `二连刻`" do not have enough extra flavor or beauty to justify separate named fan patterns.
- `一色四同顺` is naturally also `平和`, but because `一色四同顺` is already 20 points, this normally does not affect the final capped score.

## Kong Count Fan Design Notes

This row contains `一杠`, `二杠`, `三杠`, and `四杠`.

For this row:

- Count only the winning player's finalized kongs.
- Concealed kongs, exposed kongs, and added kongs all count once finalized.
- An added kong that is robbed does not count, because the kong does not form.
- Four identical concealed tiles that were not declared as a kong do not count as a kong.
- Kongs made by other players do not count for the winning player's score.
- There is no four-kong abortive draw in this rule set. The hand ends on the first win or when the wall is exhausted.
- `四杠` is naturally also `对对和`; if all four kongs are concealed, it may also satisfy `四暗刻`. Because `四杠` is already 20 points, these facts normally do not affect the final capped score.
- `杠上开花` is a separate win-method fan and does not affect kong count.

## 1-Point Fans

### 二风刻

- Value: 1 point.
- Definition: A standard hand with exactly two distinct wind triplets/kongs, and no additional wind pair that upgrades the hand to `小三风`.
- Exclusions / row rule: Same row as `小三风`, `大三风`, `小四喜`, and `大四喜`; score only the highest matching pattern from this row.
- Edge cases: Seven-pairs hands do not use wind pairs as wind triplets/sets for this fan. A hand with two wind triplets/kongs plus a third wind pair scores `小三风`.
- Examples: `东东东 南南南 123m 456p 77s`.
- Implementation notes: Count distinct wind tile types that form triplets/kongs in the resolved winning structure. Do not count seat wind or round wind as separate fans.

### 中

- Value: 1 point.
- Definition: A standard hand with a triplet/kong of red dragons, and no second dragon triplet/kong that upgrades the hand to `二元刻`.
- Exclusions / row rule: Same row as `发`, `白`, `二元刻`, `小三元`, and `大三元`; score only the highest matching pattern from this row. Two dragon triplets score `二元刻` instead of two separate 1-point patterns.
- Edge cases: Dragon pairs in seven-pairs hands do not count as dragon triplets/sets for this fan.
- Examples: `中中中 123m 456p 789s 55p`.
- Implementation notes: Triplets and kongs both count. Exposed triplets, concealed triplets, exposed kongs, added kongs, and concealed kongs all count.

### 发

- Value: 1 point.
- Definition: A standard hand with a triplet/kong of green dragons, and no second dragon triplet/kong that upgrades the hand to `二元刻`.
- Exclusions / row rule: Same row as `中`, `白`, `二元刻`, `小三元`, and `大三元`; score only the highest matching pattern from this row. Two dragon triplets score `二元刻` instead of two separate 1-point patterns.
- Edge cases: Dragon pairs in seven-pairs hands do not count as dragon triplets/sets for this fan.
- Examples: `发发发 123m 456p 789s 55p`.
- Implementation notes: Triplets and kongs both count. Exposed triplets, concealed triplets, exposed kongs, added kongs, and concealed kongs all count.

### 白

- Value: 1 point.
- Definition: A standard hand with a triplet/kong of white dragons, and no second dragon triplet/kong that upgrades the hand to `二元刻`.
- Exclusions / row rule: Same row as `中`, `发`, `二元刻`, `小三元`, and `大三元`; score only the highest matching pattern from this row. Two dragon triplets score `二元刻` instead of two separate 1-point patterns.
- Edge cases: Dragon pairs in seven-pairs hands do not count as dragon triplets/sets for this fan.
- Examples: `白白白 123m 456p 789s 55p`.
- Implementation notes: Triplets and kongs both count. Exposed triplets, concealed triplets, exposed kongs, added kongs, and concealed kongs all count.

### 断幺九

- Value: 1 point.
- Definition: A hand containing no terminals and no honors. All tiles are suited 2-8 tiles.
- Exclusions / row rule: Same row as `一气通贯`, `混全带幺`, `混幺九`, `纯全带幺`, `清幺九`, and `十三幺九`; score only one pattern from this row.
- Edge cases: Open hands may qualify. Seven-pairs hands may qualify if every pair is made from suited 2-8 tiles.
- Examples: `234m 345m 456p 678s 55p`; seven pairs such as `22m 33m 44p 55p 66s 77s 88s`.
- Implementation notes: Check the final 14 tiles, including exposed meld tiles, for any terminal or honor.

### 二色同刻

- Value: 1 point.
- Definition: For one number, two different suits each have a triplet or declared kong of that number.
- Exclusions / row rule: Same row as `小三色同刻`, `三色同顺`, and `三色同刻`. It cannot reuse a triplet/kong or pair already assigned to another pattern in this row. For the same number, `小三色同刻` or `三色同刻` uses the relevant units instead; `二色同刻` may score from other unused numbers.
- Edge cases: `七对子` does not qualify. Three identical concealed tiles only count if the resolved winning structure us…3406 tokens truncated…hand, so they cannot support `对对和`. A seven-pairs hand is not `对对和`.
- Examples: `111m 222p 333s 东东东 55m`; `111m 222m 333m 444p 55s`.
- Implementation notes: Evaluate standard-hand decompositions, but reject decompositions that split four identical undeclared concealed tiles into standard-hand components. If a hand qualifies as `七对子`, score it as `七对子` for this row, not also as `对对和`.

### 七对子

- Value: 3 points.
- Definition: A special hand made from seven pairs.
- Exclusions / row rule: Same row as `平和` and `对对和`; score only one pattern from this row.
- Edge cases: Repeated pairs are allowed: four identical tiles may be treated as two pairs. Repeated pairs do not give extra points; this rule set has no `四归一`. Declared kongs are not pair slots for `七对子`, because the hand has already left the seven-pairs shape.
- Examples: `11m 22m 33p 44p 55s 66s 东东`; repeated-pair shape `1111m 22p 33p 44s 55s 东东`.
- Implementation notes: Count pair slots, not only distinct tile types. A tile count of 4 can contribute two pair slots. The hand must have exactly seven pair slots across 14 tiles.

### 二杠

- Value: 3 points.
- Definition: The winning player has exactly two finalized kongs.
- Exclusions / row rule: Same row as `一杠`, `三杠`, and `四杠`; score only one pattern from this row.
- Edge cases: Any mix of concealed, exposed, and finalized added kongs qualifies. If an attempted added kong was robbed, it is not included.
- Examples: One concealed kong plus one exposed kong; two finalized added kongs.
- Implementation notes: Count finalized kong melds in the winning player's own state and apply row maximum.

## 8-Point Fans

### 大三风

- Value: 8 points.
- Definition: A standard hand with exactly three distinct wind triplets/kongs, and no pair of the fourth wind.
- Exclusions / row rule: Same row as `二风刻`, `小三风`, `小四喜`, and `大四喜`; score only the highest matching pattern from this row. Replaces `二风刻` and `小三风` when applicable.
- Edge cases: Three wind triplets/kongs plus a pair of the fourth wind scores `小四喜`.
- Examples: `东东东 南南南 西西西 123m 77p`.
- Implementation notes: Count distinct wind tile types with triplets/kongs. If all three wind sets exist and the fourth wind is the pair, upgrade to `小四喜`.

### 小三元

- Value: 8 points.
- Definition: Two dragon triplets/kongs plus a pair of the third dragon.
- Exclusions / row rule: Same row as `中`, `发`, `白`, `二元刻`, and `大三元`; score only the highest matching pattern from this row. Replaces `二元刻` and separate 1-point dragon triplet fans.
- Edge cases: Seven-pairs hands do not qualify. Three dragon triplets/kongs score `大三元`.
- Examples: `中中中 发发发 白白 123m 456p`.
- Implementation notes: Requires all three dragon types to be represented: two as triplets/kongs and one as the pair.

### 混幺九

- Value: 8 points.
- Definition: A hand made only of terminals and honors, with at least one terminal tile and at least one honor tile.
- Exclusions / row rule: Same row as `断幺九`, `一气通贯`, `混全带幺`, `纯全带幺`, `清幺九`, and `十三幺九`; score only one pattern from this row.
- Edge cases: The hand can be either all triplets/kongs plus a pair, or seven pairs. If all tiles are terminals with no honors, use `清幺九`. If the shape is thirteen orphans, use `十三幺九`; do not also treat it as `混幺九`.
- Examples: `111m 999p 东东东 白白白 99s`; seven pairs such as `11m 99m 11p 99p 东东 南南 白白`.
- Implementation notes: For normal winning shapes, this will always combine with either `对对和` or `七对子`; since those have the same value in this rule set, no special distinction is needed here. Check `十三幺九` before `混幺九` or explicitly exclude the thirteen-orphans tile-count shape.

### 纯全带幺

- Value: 8 points.
- Definition: A standard hand where every set and the pair contains at least one terminal, the hand contains at least one sequence, and the hand contains no honors.
- Exclusions / row rule: Same row as `断幺九`, `一气通贯`, `混全带幺`, `混幺九`, `清幺九`, and `十三幺九`; score only one pattern from this row.
- Edge cases: Sequences that qualify are only `123` or `789`. Triplets/kongs must be terminal triplets/kongs. If the hand has honors, it may be `混全带幺`; if it has no sequences, it may be `清幺九`.
- Examples: `123m 789m 111p 999s 11s`.
- Implementation notes: Evaluate against a resolved standard-hand decomposition.

### 清一色

- Value: 8 points.
- Definition: A hand made entirely from one suited tile family, with no honors.
- Exclusions / row rule: Same row as `混一色`, `字一色`, and `九莲宝灯`; score only one pattern from this row.
- Edge cases: May be open or closed. May be a standard hand or seven pairs. If the hand also satisfies true closed nine-sided-wait `九莲宝灯`, score `九莲宝灯` instead.
- Examples: `123m 456m 789m 111m 99m`; seven pairs such as `11p 22p 33p 44p 55p 66p 77p`.
- Implementation notes: Count all tiles in the final hand, including exposed melds and declared kongs. The set of suited families used must have size 1, and no honors may be present.

### 三色同刻

- Value: 8 points.
- Definition: For one number, all three suits each have a triplet or declared kong of that number.
- Exclusions / row rule: Same row as `二色同刻`, `小三色同刻`, and `三色同顺`; for the same number, replaces lower same-number triplet/kong patterns.
- Edge cases: `七对子` does not qualify. Undeclared four-of-a-kind cannot be used as triplet/kong material. Exposed and concealed triplets/kongs all count if they are part of the final structure.
- Examples: `777m 777p 777s 123m 东东`.
- Implementation notes: Iterate by number 1-9. Count suits with triplet/declared-kong structures; all three suits must match.

### 三暗刻

- Value: 8 points.
- Definition: A winning hand with exactly three concealed triplets/kongs in the resolved winning structure.
- Exclusions / row rule: Same row as `二暗刻` and `四暗刻`; score only one pattern from this row.
- Edge cases: If the winning discard completes one of the three triplets, that completed triplet is not concealed, so the hand may fall to `二暗刻`. If the winning tile completes the pair or a sequence while three concealed triplets already exist, `三暗刻` still counts.
- Examples: `111m 222p 333s 456m 东东`, winning on `东` for the pair.
- Implementation notes: Use the win source and winning tile position in the resolved decomposition to decide whether the completed set is concealed.

### 三连刻

- Value: 8 points.
- Definition: Three consecutive numbers in the same suit each have a triplet or declared kong.
- Exclusions / row rule: Same row as `二连刻` and `四连刻`. Replaces lower overlapping `二连刻` patterns in the same chain; `四连刻` replaces it when four consecutive triplet/kong numbers exist.
- Edge cases: A three-length chain should not also score its two internal `二连刻` instances. A separate non-overlapping `二连刻` can also score if the legal hand structure contains one.
- Examples: `222m 333m 444m 678p 东东`.
- Implementation notes: For each suit, find maximal consecutive chains of resolved triplet/kong numbers. Prefer the highest-value non-overlapping chain set.

### 二般高

- Value: 8 points.
- Definition: Two different `一般高` groups in the same winning hand.
- Exclusions / row rule: Same row as `一般高`, `一色三同顺`, and `一色四同顺`; score only one pattern from this row.
- Edge cases: Does not require closed hand; it is not riichi `两杯口`. The two `一般高` groups may be in different suits. The two groups must be different sequence identities; four copies of one sequence are `一色四同顺`, not `二般高`.
- Examples: `234m 234m 678p 678p 东东`; open versions also qualify if the resolved melds contain two distinct identical-sequence pairs.
- Implementation notes: Find all `(suit, start)` sequence groups with count at least 2. `二般高` requires at least two different such groups after excluding groups that are better scored as `一色三同顺` or `一色四同顺`.

### 一色三同顺

- Value: 8 points.
- Definition: Three identical sequences in the same suit.
- Exclusions / row rule: Same row as `一般高`, `二般高`, and `一色四同顺`; score only one pattern from this row.
- Edge cases: Does not also score `一般高` from the same three sequences. Exposed sequences count. Four identical sequences upgrade to `一色四同顺`.
- Examples: `234m 234m 234m 789p 东东`.
- Implementation notes: In a resolved decomposition, any `(suit, start)` sequence group with count exactly 3 qualifies, unless count 4 upgrades to `一色四同顺`.

### 三杠

- Value: 8 points.
- Definition: The winning player has exactly three finalized kongs.
- Exclusions / row rule: Same row as `一杠`, `二杠`, and `四杠`; score only one pattern from this row.
- Edge cases: Any mix of concealed, exposed, and finalized added kongs qualifies. No special draw condition is attached to having three kongs.
- Examples: Two concealed kongs plus one exposed kong.
- Implementation notes: Count finalized kong melds in the winning player's own state and apply row maximum.

## 20-Point Fans

### 小四喜

- Value: 20 points.
- Definition: A standard hand with three distinct wind triplets/kongs plus a pair of the fourth wind.
- Exclusions / row rule: Same row as `二风刻`, `小三风`, `大三风`, and `大四喜`; score only the highest matching pattern from this row. Replaces all lower wind-row patterns.
- Edge cases: Four wind triplets/kongs score `大四喜`.
- Examples: `东东东 南南南 西西西 123m 北北`.
- Implementation notes: Requires all four wind types to be represented: three as sets and one as the pair.

### 大四喜

- Value: 20 points.
- Definition: A standard hand with four distinct wind triplets/kongs.
- Exclusions / row rule: Same row as `二风刻`, `小三风`, `大三风`, and `小四喜`; score only the highest matching pattern from this row. Replaces all lower wind-row patterns.
- Edge cases: Since `小四喜` and `大四喜` are both 20 points, if both are somehow represented by alternate decomposition, prefer reporting `大四喜` when four wind sets exist.
- Examples: `东东东 南南南 西西西 北北北 55m`.
- Implementation notes: Requires all four wind types as triplets/kongs. Implementation should handle kongs as sets for wind counting.

### 大三元

- Value: 20 points.
- Definition: Three dragon triplets/kongs.
- Exclusions / row rule: Same row as `中`, `发`, `白`, `二元刻`, and `小三元`; score only the highest matching pattern from this row. Replaces all lower dragon-row patterns.
- Edge cases: Seven-pairs hands do not qualify.
- Examples: `中中中 发发发 白白白 123m 55p`.
- Implementation notes: Requires all three dragon types as triplets/kongs. Implementation should handle kongs as sets for dragon counting.

### 清幺九

- Value: 20 points.
- Definition: A hand made only of suited terminal tiles: 1s and 9s, with no honors and no simples.
- Exclusions / row rule: Same row as `断幺九`, `一气通贯`, `混全带幺`, `混幺九`, `纯全带幺`, and `十三幺九`; score only one pattern from this row.
- Edge cases: The hand can be either all terminal triplets/kongs plus a pair, or seven pairs. Since there are only six suited terminal tile types, a seven-pairs `清幺九` requires treating four identical tiles as two pairs; this depends on the final `七对子` definition. If honors are present, the hand may be `混幺九` instead. It cannot coexist with `十三幺九`, because `十三幺九` requires honors.
- Examples: `111m 999m 111p 999s 11s`; seven pairs such as `11m 99m 11p 99p 11s 99s 11m`.
- Implementation notes: For normal winning shapes, this will always combine with either `对对和` or `七对子`; since those have the same value in this rule set, no special distinction is needed here.

### 十三幺九

- Value: 20 points.
- Definition: The special thirteen-orphans hand: one each of all 13 terminal/honor tile types, plus one duplicate among those 13 tile types as the pair.
- Exclusions / row rule: Same row as `断幺九`, `一气通贯`, `混全带幺`, `混幺九`, `纯全带幺`, and `清幺九`; score only one pattern from this row.
- Edge cases: This is not a standard four-sets-plus-pair hand. It cannot contain exposed melds in normal play because exposing a chi/peng/kong prevents retaining the required singleton structure. No separate 13-sided-wait bonus. It may win by robbing a kong if the claimed tile completes the thirteen-orphans hand, but the hand is already capped at 20 points.
- Examples: `19m 19p 19s 东南西北中发白 + any one duplicate among these 13 types`.
- Implementation notes: Count tile types, not decomposition. The winning hand must contain all 13 terminal/honor types and exactly one duplicate tile among them. Check this as a special hand before `混幺九`, or explicitly exclude this tile-count shape from `混幺九`. It is necessarily closed, but that does not need special scoring treatment because the total is already capped at 20.

### 字一色

- Value: 20 points.
- Definition: A hand made entirely from honor tiles.
- Exclusions / row rule: Same row as `混一色`, `清一色`, and `九莲宝灯`; score only one pattern from this row.
- Edge cases: May be open or closed. May be a standard hand or seven pairs. It cannot coexist with `混一色`, `清一色`, or `九莲宝灯` because it contains no suited tiles.
- Examples: `东东东 南南南 西西西 中中中 白白`; seven pairs such as `东东 南南 西西 北北 中中 发发 白白`.
- Implementation notes: Count all tiles in the final hand, including exposed melds and declared kongs. Every tile must be an honor.

### 九莲宝灯

- Value: 20 points.
- Definition: A closed hand that wins from the true nine-sided wait shape `1112345678999` in one suit. Before the winning tile is added, the 13 concealed tiles must be exactly `1112345678999` in one suit; the winning tile may be any tile from 1 through 9 in that same suit.
- Exclusions / row rule: Same row as `混一色`, `清一色`, and `字一色`; score only one pattern from this row.
- Edge cases: Must be closed; exposed melds and declared kongs disqualify it. A final 14-tile one-suit hand that merely contains the nine-lantern tile counts, but was not won from the specific 13-tile nine-sided wait shape, is only `清一色`, not `九莲宝灯`. No additional score for `清一色` because same row and 20-point cap.
- Examples: Waiting shape `1112345678999m`; winning on any `1m` through `9m` scores `九莲宝灯`.
- Implementation notes: Requires access to the pre-win concealed 13-tile hand and the winning tile, not just the final 14-tile multiset. Do not detect this from final composition alone.

### 天和

- Value: 20 points.
- Definition: Dealer wins immediately with the original starting 14 tiles, before the dealer's first discard and before any player action occurs.
- Exclusions / row rule: Same row as `门前清` and `地和`; score only one pattern from this row.
- Edge cases: Dealer declaring a concealed kong and winning on the supplement tile is not `天和`. Only the original dealt 14-tile hand qualifies.
- Examples: Dealer's initial 14 tiles are already a legal winning hand.
- Implementation notes: Requires an explicit opening-state flag before the dealer discards. Do not infer from turn count alone after kongs or special actions.

### 地和

- Value: 20 points.
- Definition: A non-dealer wins by self-draw on that player's first natural draw, before any chi, pon, kong, or win has interrupted the opening flow.
- Exclusions / row rule: Same row as `门前清` and `天和`; score only one pattern from this row.
- Edge cases: First-round discard win is not `地和`. If any player chi/pons/kongs/wins before the player's first natural draw, `地和` eligibility is lost. Ordinary draw-discard actions before that player's first draw do not break eligibility. A concealed-kong supplement draw is not a first natural draw for `地和`.
- Examples: South self-draws on South's first draw after dealer's first discard, with no intervening call; West or North may also qualify on their own first natural draw if the opening flow remains uninterrupted.
- Implementation notes: Track each non-dealer's first natural draw and a global opening-interrupted flag.

### 四暗刻

- Value: 20 points.
- Definition: A winning hand with four concealed triplets/kongs in the resolved winning structure.
- Exclusions / row rule: Same row as `二暗刻` and `三暗刻`; score only one pattern from this row.
- Edge cases: A discard win can score `四暗刻` only when the winning discard completes the pair; if the discard completes a triplet, that triplet is not concealed and the hand is not `四暗刻`. Self-draw completing the fourth triplet counts. No extra score for single-wait `四暗刻`.
- Examples: `111m 222p 333s 东东东 55m`, winning on `5m` by discard for the pair; self-draw `东` to complete `东东东` also qualifies.
- Implementation notes: `四暗刻` is naturally closed and naturally also `对对和`, but no extra row score is added beyond normal cross-row/cap handling. Requires four concealed triplet/kong structures; do not count undeclared four-of-a-kind as a concealed kong.

### 四连刻

- Value: 20 points.
- Definition: Four consecutive numbers in the same suit each have a triplet or declared kong.
- Exclusions / row rule: Same row as `二连刻` and `三连刻`; replaces lower overlapping consecutive-triplet patterns in the same chain.
- Edge cases: A four-length chain scores only `四连刻`, not internal `三连刻` or `二连刻` patterns. Because it consumes all four melds, it cannot coexist with another meld-based consecutive-triplet score in a legal standard hand. It is naturally also `对对和`, but this usually does not affect the final capped score because `四连刻` is already 20 points.
- Examples: `222m 333m 444m 555m 东东`.
- Implementation notes: Requires all four meld slots to be same-suit consecutive triplets/kongs plus a pair. Use resolved meld structure; do not infer from raw counts alone.

### 一色四同顺

- Value: 20 points.
- Definition: Four identical sequences in the same suit.
- Exclusions / row rule: Same row as `一般高`, `二般高`, and `一色三同顺`; score only one pattern from this row.
- Edge cases: Scores only `一色四同顺` from this row, not `一色三同顺`, `二般高`, or multiple `一般高`. It is naturally also `平和`, but this usually does not affect the final capped score because `一色四同顺` is already 20 points.
- Examples: `234m 234m 234m 234m 东东`.
- Implementation notes: Requires all four meld slots to be the same `(suit, start)` sequence plus one pair. Use resolved decomposition; do not infer from final tile multiset alone.

### 四杠

- Value: 20 points.
- Definition: The winning player has four finalized kongs.
- Exclusions / row rule: Same row as `一杠`, `二杠`, and `三杠`; score only one pattern from this row.
- Edge cases: No four-kong abortive draw. The hand ends on the first win or wall exhaustion. `四杠` is naturally also `对对和`; if all four kongs are concealed, it may also satisfy `四暗刻`, but `四杠` is already 20 points.
- Examples: Player wins after finalizing four kongs of any mix of concealed, exposed, and added kongs.
- Implementation notes: Count finalized kong melds in the winning player's own state. A robbed added kong is not finalized and must not be counted.
