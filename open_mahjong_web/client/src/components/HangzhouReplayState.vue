<template>
  <details v-if="info" class="hangzhou-state">
    <summary>白板财神 · {{ info.dealer_streak || 1 }}老庄 · {{ info.dealer_multiplier || 2 }}倍</summary>
    <table><thead><tr><th>座位</th><th>财飘</th><th>十风</th><th>吃牌</th><th>状态</th></tr></thead>
      <tbody><tr v-for="seat in [0,1,2,3]" :key="seat"><td>{{ seatName(seat) }}</td><td>{{ info.cai_piao_counts?.[seat] || 0 }}</td><td>{{ info.ten_winds_counts?.[seat] || 0 }}/10</td><td>{{ info.chi_counts?.[seat] || 0 }}</td><td>{{ info.forced_draw_discard?.[seat] ? '仅摸切' : '' }}<span v-if="(info.chi_counts?.[seat] || 0) >= 3"> · {{ seatName(seat) }}和：{{ seatName((seat+3)%4) }}全包；{{ seatName((seat+3)%4) }}和：{{ seatName(seat) }}双倍全包</span></td></tr></tbody>
    </table>
    <p>杠后暗弃{{ info.burned_count || 0 }}张</p>
    <p v-if="info.phase === 'waiting_hangzhou_ten_winds'">十风和牌选择</p>
    <ul v-if="info.ledger?.payment?.transfers?.length"><li v-for="(item,index) in info.ledger.payment.transfers" :key="index">{{ seatName(item.payer) }}付{{ seatName(item.winner) }} {{ item.points }}分 · {{ hangzhouPaymentReason(info, item.reason) }}</li></ul>
  </details>
</template>
<script setup>
import { hangzhouPaymentReason } from '@/utils/hangzhouReplay.js'
const props = defineProps({ info: { type: Object, default: null } })
const seats = ['东起','南起','西起','北起']
const seatName = seat => seats[props.info?.seat_to_original?.[seat] ?? seat] ?? '?' 
</script>
<style scoped>
.hangzhou-state {
  position: absolute;
  right: 14px;
  top: 58px;
  z-index: 45; /* Below the wall, result and score overlays. */
  max-width: min(420px, calc(100% - 28px));
  max-height: 62%;
  overflow: auto;
  box-sizing: border-box;
  padding: 10px 14px;
  color: #f3eee5;
  background: #242421f2;
  border: 1px solid #a99057;
  border-radius: 6px;
  font: inherit;
}
.hangzhou-state[open] { width: min(420px, calc(100% - 28px)); }
p, ul { margin: 8px 0; font-size: 13px; line-height: 1.7; }
summary { cursor: pointer; font-weight: 600; }
table { width: 100%; border-collapse: collapse; margin-top: 10px; }
th,td { padding: 5px; text-align: left; font-size: 13px; vertical-align: top; }
th:not(:last-child), td:not(:last-child) { white-space: nowrap; }
ul { margin-bottom: 0; padding-left: 20px; }
</style>
