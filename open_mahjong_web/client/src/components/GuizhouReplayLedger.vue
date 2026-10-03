<template>
  <details v-if="rows.length" class="guizhou-replay-ledger">
    <summary>贵州本局账目</summary>
    <p v-if="info.ledger.indicator">翻开 {{ tileName(info.ledger.indicator) }}，和牌鸡为 {{ tileName(info.ledger.chicken_tile) }}</p>
    <p v-else>本局没有和牌鸡指示牌</p>
    <div class="table-scroll">
      <table>
        <thead><tr><th>玩家</th><th>状态</th><th>和牌／查叫</th><th>鸡</th><th>杠</th><th>合计</th></tr></thead>
        <tbody><tr v-for="row in rows" :key="row.seat">
          <th>{{ name(row.seat) }}</th><td>{{ row.ready ? '有叫或和牌' : '没叫' }}</td>
          <td>{{ signed(row.hand) }}</td><td>{{ signed(row.chicken) }}</td><td>{{ signed(row.kong) }}</td><td>{{ signed(row.total) }}</td>
        </tr></tbody>
      </table>
    </div>
    <ol><li v-for="(item, index) in info.ledger.transfers" :key="index">
      {{ name(item.payer) }} 付 {{ name(item.payee) }} {{ item.points }}分：{{ item.reason }}<span v-if="item.tile">（{{ tileName(item.tile) }}）</span>
    </li></ol>
  </details>
</template>

<script setup>
import { computed } from 'vue'
import { guizhouLedgerRows } from '@/utils/guizhouReplay.js'
const props = defineProps({ info: { type: Object, required: true }, players: { type: Array, default: () => [] } })
const rows = computed(() => guizhouLedgerRows(props.info))
const name = seat => props.players.find(p => p.original_player_index === (props.info.seat_to_original?.[seat] ?? seat))?.username || `座位${seat + 1}`
const signed = value => value > 0 ? `+${value}` : String(value)
const tileName = tile => `${tile % 10}${({ 1: '万', 2: '饼', 3: '条' })[Math.floor(tile / 10)] || ''}`
</script>

<style scoped>
.guizhou-replay-ledger { position: absolute; right: 14px; top: 58px; z-index: 80; max-width: min(620px, 90%); max-height: 62%; overflow: auto; padding: 10px 14px; color: #f3eee5; background: #242421f2; border: 1px solid #a99057; border-radius: 6px; font: inherit; }
summary { cursor: pointer; font-weight: 600; }
p, ol { margin: 8px 0; font-size: 13px; line-height: 1.7; }
.table-scroll { overflow-x: auto; }
table { border-collapse: collapse; font-size: 13px; }
th, td { padding: 5px 8px; text-align: right; border-bottom: 1px solid #6e6554; white-space: nowrap; }
th:first-child { text-align: left; }
</style>
