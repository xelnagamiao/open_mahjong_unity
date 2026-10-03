<template>
  <details class="guangdong-replay-ledger">
    <summary>广东花鬼本局账目</summary>
    <p>打鬼：<span v-for="(count, seat) in info.ghost_discard_counts" :key="seat">{{ name(seat) }} {{ count }}张　</span></p>
    <template v-if="info.horses">
      <p>奖马顺序：{{ info.horses.tiles?.length ? info.horses.tiles.map(guangdongTileName).join('、') : '无剩余牌' }}</p>
      <p>中马 {{ (info.horses.hits || []).reduce((sum, count) => sum + Number(count), 0) }}张<span v-if="info.horses.payer != null">；责任支付：{{ name(info.horses.payer) }}</span></p>
    </template>
    <template v-if="info.result">
      <p>{{ info.result.fan }}番；基本分 {{ info.result.base_score }}分；系数 ×{{ info.result.coefficient }}</p>
      <p v-if="info.result.substitutions?.length">鬼牌解释：{{ info.result.substitutions.map(item => `${guangdongTileName(item.physical)}代${guangdongTileName(item.logical)}`).join('、') }}</p>
    </template>
    <ol v-if="info.kongs.length"><li v-for="(kong, index) in info.kongs" :key="index">{{ kongName(kong.kind) }} {{ guangdongTileName(kong.tile) }}：{{ changes(kong.changes) }}</li></ol>
    <p v-if="info.refund">流局退杠：{{ changes(info.refund) }}</p>
    <p v-if="info.result?.win_changes">和牌（含奖马）：{{ changes(info.result.win_changes) }}</p>
    <p v-if="info.horses?.changes">奖马分变：{{ changes(info.horses.changes) }}</p>
  </details>
</template>

<script setup>
import { guangdongTileName } from '@/utils/guangdongReplay.js'
const props = defineProps({ info: { type: Object, required: true }, players: { type: Array, default: () => [] }, seats: { type: Array, default: () => [0, 1, 2, 3] } })
const name = seat => props.players.find(p => p.original_player_index === props.seats.indexOf(Number(seat)))?.username || `座位${Number(seat) + 1}`
const changes = values => (values || []).map((value, seat) => `${name(seat)} ${value > 0 ? '+' : ''}${value}`).join('；')
const kongName = kind => ({ concealed: '暗杠', added: '加杠', exposed: '直杠', direct: '直杠' })[kind] || kind
</script>

<style scoped>
.guangdong-replay-ledger { position: absolute; right: 14px; top: 58px; z-index: 80; max-width: min(620px, 90%); max-height: 62%; overflow: auto; padding: 10px 14px; color: #f3eee5; background: #242421f2; border: 1px solid #a99057; border-radius: 6px; font: inherit; }
summary { cursor: pointer; font-weight: 600; }
p, ol { margin: 8px 0; font-size: 13px; line-height: 1.7; }
</style>
