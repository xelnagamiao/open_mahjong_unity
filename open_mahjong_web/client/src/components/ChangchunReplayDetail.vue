<template>
  <div class="cc-replay-detail">
    <strong>长春 · {{ info.exhausted ? '无宝' : `宝：${changchunTileName(info.tile)}` }}</strong>
    <p v-if="info.bao">{{ info.bao === 'chong' ? '冲宝' : '摸宝' }}</p>
    <p v-for="item in info.tailTiles" :key="`tail-${item.player}`">座位{{ item.player + 1 }}末张单放：{{ item.tile ? changchunTileName(item.tile) : '扣放' }}</p>
    <p v-for="(item, index) in info.ledger" :key="index">座位{{ item.player + 1 }} {{ names[item.kind] || '杠' }}：{{ deltas(item.delta) }}</p>
    <p v-for="item in info.payments" :key="item.payer">座位{{ item.payer + 1 }}应付 {{ item.score }}分（{{ item.fan }}番）<span v-if="item.responsible !== item.payer">，由座位{{ item.responsible + 1 }}包庄</span></p>
  </div>
</template>
<script setup>
import { changchunTileName } from '@/utils/changchunReplay.js'
defineProps({ info: { type: Object, required: true } })
const names = { direct: '明杠', added: '加杠', concealed: '暗杠', special: '特殊杠', special_added: '特殊加杠' }
const deltas = delta => [0, 1, 2, 3].map(i => `${Number(delta?.[i]) > 0 ? '+' : ''}${delta?.[i] ?? 0}`).join(' / ')
</script>
<style scoped>
.cc-replay-detail { padding: 12px; border: 1px solid var(--el-border-color); border-radius: 8px; line-height: 1.7; }
p { margin: 4px 0 0; }
</style>
