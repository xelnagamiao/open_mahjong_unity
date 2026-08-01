<template>
  <div
    class="scoreboard-layer"
    role="dialog"
    aria-modal="true"
    aria-label="计分板"
    @click.self="$emit('close')"
  >
    <section class="scoreboard-panel">
      <header class="scoreboard-panel__header">
        <h2>计分板</h2>
        <button type="button" aria-label="关闭计分板" @click="$emit('close')">×</button>
      </header>

      <div class="scoreboard-panel__scroll">
        <table class="scoreboard-table">
          <thead>
            <tr>
              <th rowspan="2" class="scoreboard-table__round">局数</th>
              <th rowspan="2" class="scoreboard-table__fan">主番</th>
              <th v-for="player in orderedPlayers" :key="player.player_index" colspan="2">
                {{ player.username || `#${player.user_id}` }}
              </th>
            </tr>
            <tr>
              <template v-for="player in orderedPlayers" :key="`sub-${player.player_index}`">
                <th>本局</th>
                <th>分值</th>
              </template>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in rows"
              :key="row.key"
              :class="{ 'is-predicted': row.predicted, 'is-selectable': selectable && !row.predicted }"
              :tabindex="selectable && !row.predicted ? 0 : undefined"
              @click="selectable && !row.predicted && $emit('select-row', row.sourceIndex)"
              @keydown.enter="selectable && !row.predicted && $emit('select-row', row.sourceIndex)"
            >
              <th>{{ row.roundLabel }}</th>
              <td>{{ row.predicted ? '' : (row.mainFan || '—') }}</td>
              <template v-for="cell in row.players" :key="`${row.key}-${cell.seat}`">
                <td :class="scoreClass(cell.deltaValue)">{{ cell.delta }}</td>
                <td>{{ cell.total }}</td>
              </template>
            </tr>
            <tr v-if="!rows.length">
              <td class="scoreboard-table__empty" :colspan="2 + orderedPlayers.length * 2">
                尚无结算记录
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { locale, roundLabelKey, tr } from '@/i18n'

const props = defineProps({
  players: { type: Array, default: () => [] },
  settlements: { type: Array, default: () => [] },
  selectable: { type: Boolean, default: false },
  roundLabelFormat: { type: String, default: 'wind-seat' },
})

defineEmits(['close', 'select-row'])

const orderedPlayers = computed(() => [...props.players]
  .sort((left, right) => (
    Number(left.original_player_index ?? left.player_index)
    - Number(right.original_player_index ?? right.player_index)
  )))

function parseDelta(value) {
  const parsed = Number.parseInt(String(value ?? '0'), 10)
  return Number.isFinite(parsed) ? parsed : 0
}

function roundLabel(round) {
  const number = Number(round)
  return tr(roundLabelKey(number, props.roundLabelFormat, locale.value))
}

function scoreClass(value) {
  if (value > 0) return 'is-gain'
  if (value < 0) return 'is-loss'
  return 'is-zero'
}

const rows = computed(() => {
  const players = orderedPlayers.value
  const playedCount = players.reduce(
    (count, player) => Math.max(count, player.score_history?.length ?? 0),
    0,
  )
  const roundHistory = players.find((player) => player.round_number_history?.length)
    ?.round_number_history ?? []

  const startingScores = new Map(players.map((player) => {
    const historyTotal = (player.score_history ?? []).reduce(
      (total, value) => total + parseDelta(value),
      0,
    )
    return [player.player_index, Number(player.score ?? 0) - historyTotal]
  }))

  const result = []
  for (let index = 0; index < playedCount; index += 1) {
    const round = Number(roundHistory[index] ?? index + 1)
    result.push({
      key: `played-${index}`,
      sourceIndex: index,
      predicted: false,
      roundLabel: roundLabel(round),
      mainFan: props.settlements[index] ?? '—',
      players: players.map((player) => {
        const history = player.score_history ?? []
        const delta = history[index] ?? ''
        const deltaValue = delta === '' ? null : parseDelta(delta)
        const cumulative = history
          .slice(0, index + 1)
          .reduce((total, value) => total + parseDelta(value), 0)
        return {
          seat: player.player_index,
          delta: delta === ''
            ? ''
            : deltaValue > 0 ? `+${deltaValue}` : String(deltaValue),
          deltaValue,
          total: delta === ''
            ? ''
            : Number(startingScores.get(player.player_index) ?? 0) + cumulative,
        }
      }),
    })
  }

  const maxPlayedRound = roundHistory.length
    ? Math.max(...roundHistory.map(Number).filter(Number.isFinite))
    : playedCount
  // 国标计分板固定铺满四圈 16 局；错和形成的重复局行保留在前面，
  // 因而会自然把总行数延伸到 16 行以上。
  for (let round = maxPlayedRound + 1; round <= 16; round += 1) {
    result.push({
      key: `predicted-${round}`,
      sourceIndex: -1,
      predicted: true,
      roundLabel: roundLabel(round),
      mainFan: '',
      players: players.map((player) => ({
        seat: player.player_index,
        delta: '',
        deltaValue: null,
        total: '',
      })),
    })
  }
  return result
})
</script>

<style scoped>
.scoreboard-layer {
  position: absolute;
  inset: 0;
  z-index: 65;
  display: grid;
  place-items: center;
  padding: 14px;
  box-sizing: border-box;
  background: rgba(0, 0, 0, 0.46);
}

.scoreboard-panel {
  width: min(960px, 96%);
  max-height: min(94dvh, 660px);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid #757575;
  border-radius: 8px;
  background: #fff;
  color: #111;
  box-shadow: 0 16px 42px rgba(0, 0, 0, 0.34);
}

.scoreboard-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 44px;
  padding: 0 10px 0 16px;
  border-bottom: 1px solid #bdbdbd;
  background: #f2f2f2;
}

.scoreboard-panel__header h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 650;
  letter-spacing: 0.04em;
}

.scoreboard-panel__header button {
  width: 34px;
  height: 34px;
  border: 1px solid #b8b8b8;
  border-radius: 5px;
  background: #fff;
  color: #222;
  font-size: 24px;
  line-height: 1;
  cursor: pointer;
}

.scoreboard-panel__scroll {
  min-height: 0;
  /* 两层表头 + 16 局 + 最多两条错和附加行；再多才滚动。 */
  max-height: min(585px, calc(94dvh - 44px));
  overflow: auto;
}

.scoreboard-table {
  width: 100%;
  min-width: 720px;
  border-collapse: collapse;
  table-layout: fixed;
  font-size: 14px;
}

.scoreboard-table th,
.scoreboard-table td {
  box-sizing: border-box;
  height: 29px;
  padding: 0 6px;
  border-right: 1px solid #aaa;
  border-bottom: 1px solid #aaa;
  line-height: 29px;
  text-align: center;
  vertical-align: middle;
  white-space: nowrap;
}

.scoreboard-table thead th {
  background: #d8d8d8;
  color: #080808;
  font-weight: 700;
}

.scoreboard-table thead tr:first-child th {
  height: 34px;
  line-height: 34px;
}
.scoreboard-table__round { width: 68px; }
.scoreboard-table__fan { width: 84px; }
.scoreboard-table tbody tr:nth-child(odd) > * { background: #fff; }
.scoreboard-table tbody tr:nth-child(even) > * { background: #e9e9e9; }
.scoreboard-table tbody th { font-weight: 650; }
.scoreboard-table .is-gain { color: #08752f; font-weight: 700; }
.scoreboard-table .is-loss { color: #ae1427; font-weight: 700; }
.scoreboard-table .is-zero { color: #303030; }
.scoreboard-table tr.is-predicted { color: #555; }
.scoreboard-table tbody tr.is-selectable { cursor: pointer; }
.scoreboard-table tbody tr.is-selectable:hover > *,
.scoreboard-table tbody tr.is-selectable:focus > * {
  background: #d8e6f5;
  outline: none;
}
.scoreboard-table__empty { height: 88px !important; color: #444; }
</style>
