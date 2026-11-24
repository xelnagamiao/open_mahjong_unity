<template>
  <div class="duel-table">
    <div class="scoreline">
      <span>第 {{ game.round }} / {{ game.rounds }} 局</span>
      <span>我方 {{ game.scores[seat] }} ∶ {{ game.scores[other] }} 对方</span>
      <span v-if="active" class="timer" role="timer">剩余 {{ seconds }} 秒</span>
    </div>
    <section class="side">
      <h3>对方 · <span data-no-translate>{{ name(other) }}</span>
        <small v-if="game.dealer === other">先手</small>
        <small v-if="!room.players[other]?.online">离线</small>
      </h3>
      <div class="opponent-hand" :aria-label="'对方暗手 ' + game.handCounts[other] + ' 张'">
        <img v-for="n in game.handCounts[other]" :key="n" :src="back" alt="" />
      </div>
      <div v-if="game.melds[other].length" class="melds" aria-label="对方副露">
        <div v-for="(meld, i) in game.melds[other]" :key="i" class="meld">
          <span>{{ moveNames[meld.kind] }}</span>
          <template v-for="(tile, j) in meld.tiles" :key="j">
            <TileChip v-if="tile" :tile-id="tile" size="sm" />
            <img v-else class="meld-back" :src="back" alt="暗杠" />
          </template>
        </div>
      </div>
    </section>

    <div class="wall-row" aria-label="牌山">
      <span>牌山 {{ game.wallCount }} 张</span>
      <div class="wall" aria-hidden="true"><img v-for="n in game.wallCount" :key="n" :src="back" alt="" /></div>
    </div>
    <div v-for="who in [other, seat]" :key="who" class="river-row">
      <span>{{ who === seat ? '我方牌河' : '对方牌河' }}</span>
      <div class="river" :aria-label="who === seat ? '我方牌河' : '对方牌河'">
        <div v-for="(entry, i) in game.rivers[who]" :key="i"
          :class="{ claimed: entry.claimed, latest: isLastDiscard(who, i) }"
          :title="entry.claimed ? '已被副露' : TILE_NAME[entry.tile]">
          <TileChip :tile-id="entry.tile" size="sm" />
        </div>
        <span v-if="!game.rivers[who].length" class="empty">—</span>
      </div>
    </div>

    <section class="side self">
      <h3>我方 · <span data-no-translate>{{ name(seat) }}</span><small v-if="game.dealer === seat">先手</small></h3>
      <div v-if="game.melds[seat].length" class="melds" aria-label="我方副露">
        <div v-for="(meld, i) in game.melds[seat]" :key="i" class="meld">
          <span>{{ moveNames[meld.kind] }}</span>
          <TileChip v-for="(tile, j) in meld.tiles" :key="j" :tile-id="tile" size="sm" />
        </div>
      </div>
      <div class="my-hand" role="group" aria-label="我方手牌">
        <button v-for="(tile, i) in game.hand" :key="i" type="button"
          :aria-label="'打出' + TILE_NAME[tile]" :class="{ drawn: game.drawn && i === game.hand.length - 1 }"
          :disabled="!canAct || !discardable.has(tile)" @click="act({ type: 'discard', tile })">
          <TileChip :tile-id="tile" size="lg" aria-hidden="true" />
        </button>
      </div>
    </section>
    <div v-if="active && game.phase !== 'between'" class="turn">
      <span role="status">{{ turnText }}</span>
      <template v-if="game.actions.length">
        <button v-for="move in controls" :key="move.type + ':' + (move.tile || '')" class="move" type="button"
          :class="{ win: move.type === 'win' }" :disabled="!canAct" @click="act(move)">{{ moveLabel(move) }}</button>
      </template>
    </div>
    <div v-if="game.result" class="result" role="status">
      <span>{{ resultText }}</span>
      <span v-if="active && game.phase === 'between'">下一局即将开始</span>
      <div v-if="game.result.hand" class="open-hand" aria-label="宣和手牌">
        <span>{{ game.result.actor === seat ? '我方亮牌' : '对方亮牌' }}</span>
        <TileChip v-for="(tile, i) in game.result.hand" :key="i" :tile-id="tile" size="sm"
          :class="{ winning: i === game.result.hand.length - 1 }" />
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { TILE_NAME } from '@/composables/useMahjongTiles'
import TileChip from '@/components/TileChip.vue'

const props = defineProps({
  room: { type: Object, required: true },
  seconds: { type: Number, required: true },
  enabled: Boolean,
})
const emit = defineEmits(['move'])
const back = '/tiles/faces/back.png'
const moveNames = { win: '和牌', pass: '过', chi: '吃', peng: '碰', mingGang: '明杠', anGang: '暗杠', jiaGang: '加杠' }
const resultNames = { tsumo: '自摸', ron: '荣和', rob: '抢杠和', falseWin: '诈和' }
const game = computed(() => props.room.game)
const seat = computed(() => props.room.seat)
const other = computed(() => 1 - seat.value)
const active = computed(() => props.room.phase === 'playing')
const canAct = computed(() => active.value && props.enabled && props.seconds > 0)
const discardable = computed(() => new Set(game.value.actions.filter(a => a.type === 'discard').map(a => a.tile)))
const controls = computed(() => {
  const actions = game.value.actions.filter(a => a.type !== 'discard')
  return [...actions.filter(a => a.type === 'win'), ...actions.filter(a => a.type !== 'win' && a.type !== 'pass'), ...actions.filter(a => a.type === 'pass')]
})
const turnText = computed(() => {
  const g = game.value
  if (g.phase === 'discard') return g.turn === seat.value ? '点击手牌打出' : '对方出牌中'
  if (!g.actions.length) return '等待对方响应'
  const tile = TILE_NAME[g.lastDiscard?.tile] || ''
  return (g.phase === 'rob' ? '对方加杠' : '对方打出') + tile
})
const resultText = computed(() => {
  const r = game.value.result
  if (!r) return ''
  if (r.reason === 'draw') return '流局 · 双方 0 分'
  const owner = r.actor === seat.value ? '我方' : '对方'
  const winner = r.winner === seat.value ? '我方' : '对方'
  return owner + resultNames[r.reason] + ' · ' + winner + ' +1 分'
})
function name(i) {
  return props.room.players[i]?.name || ''
}
function act(move) {
  if (canAct.value) emit('move', move)
}
function isLastDiscard(who, i) {
  const g = game.value
  return g.phase === 'claim' && who === g.lastDiscard?.seat && i === g.rivers[who].length - 1
}
function moveLabel(move) {
  if (move.type === 'chi') return '吃 ' + [move.tile, move.tile + 1, move.tile + 2].map(t => t % 10).join('') + '万'
  return moveNames[move.type] + (move.tile ? ' ' + TILE_NAME[move.tile] : '')
}
</script>

<style scoped>
.duel-table { --line: #d9d9d9; background: #fff; border: 1px solid var(--line); }
.duel-table :deep(.tile-chip) { pointer-events: none; }
.scoreline, .turn, .result, .wall-row, .river-row, .side { padding: 9px 14px; }
.scoreline, .turn, .result, .wall-row, .river-row { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 14px; }
.scoreline { background: #444; color: white; }
.timer { margin-left: auto; font-variant-numeric: tabular-nums; }
.side h3 { display: flex; align-items: baseline; flex-wrap: wrap; gap: 4px; font-size: 15px; font-weight: 400; margin: 0 0 8px; overflow-wrap: anywhere; }
.side small, .meld > span { color: #686868; font-size: 13px; }
.opponent-hand, .wall { display: flex; gap: 1px; align-items: center; }
.opponent-hand img { width: min(17px, 4.2vw); height: auto; aspect-ratio: 43/60; }
.wall-row { min-height: 30px; background: #f4f4f4; }
.wall-row > span, .river-row > span { min-width: 5.4em; }
.wall img { width: 14px; height: auto; aspect-ratio: 43/60; }
.river-row { border-bottom: 1px solid #eee; flex-wrap: nowrap; }
.river, .melds, .meld { display: flex; flex-wrap: wrap; align-items: center; min-width: 0; gap: 2px; }
.river { min-height: 36px; }
.river > div { display: flex; }
.river .claimed { opacity: .45; }
.river .latest { outline: 2px solid #2672a8; }
.empty { color: #777; }
.melds { gap: 6px 12px; }
.meld { gap: 1px; }
.melds :deep(.tile-chip.sm), .meld-back { width: 23px; height: 32px; }
.self .melds { margin-bottom: 8px; }
.my-hand { display: flex; flex-wrap: wrap; align-items: center; gap: 3px 2px; }
.my-hand button { display: block; box-sizing: border-box; width: 41px; padding: 0; border: 0; background: white; cursor: pointer; }
.my-hand button:disabled { opacity: 1; cursor: default; }
.my-hand :deep(.tile-chip.lg) { display: flex; width: 100%; max-width: 100%; height: auto; aspect-ratio: 43/60; }
.my-hand .drawn { margin-left: 5px; }
.my-hand button:hover:enabled, .my-hand button:focus-visible { outline: 2px solid #2672a8; }
.turn, .result { border-top: 1px solid var(--line); min-height: 40px; }
.turn > span { color: #505050; }
.move { padding: 7px 14px; border: 1px solid #aaa; color: #333; background: #fff; font: inherit; cursor: pointer; min-height: 40px; }
.move.win { background: #b8392c; border-color: #b8392c; color: white; }
.move:disabled { opacity: .55; cursor: default; }
.result { background: #f5f5f5; }
.open-hand { display: flex; align-items: center; flex-wrap: wrap; gap: 2px; flex-basis: 100%; }
.open-hand > span { margin-right: 6px; }
.open-hand .winning { margin-left: 5px; outline: 1px solid #757575; }
@media (max-width: 620px) {
  .scoreline, .turn, .result, .wall-row, .river-row, .side { padding: 7px 8px; }
  .scoreline, .turn, .wall-row, .river-row { gap: 5px 8px; }
  .river-row > span { min-width: 4.2em; }
  .river :deep(.tile-chip.sm) { width: 22px; height: 30px; }
  .my-hand button { width: min(41px, calc((100% - 18px) / 7)); }
}
</style>
