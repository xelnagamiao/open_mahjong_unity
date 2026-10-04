<template>
  <section v-if="info || ledger.length" class="hongzhong-detail" aria-label="红中结算说明">
    <p v-for="(event, index) in ledger" :key="index">{{ event.kind }}：{{ event.changes.map((value, seat) => `第${seat + 1}席 ${value > 0 ? '+' : ''}${value}`).join('、') }}</p>
    <p v-if="info">扎鸟：{{ (info.tiles || []).map(hongzhongTileName).join('、') || '无剩余牌' }} · 中 {{ info.hits ?? 0 }} 鸟（每家另付 {{ info.hits ?? 0 }} 分）</p>
    <p v-if="info?.detail?.logical_hand?.length">计分牌型：{{ info.detail.logical_hand.map(hongzhongTileName).join('、') }}</p>
    <p v-if="info?.detail?.winning_logical_tile">和牌张按 {{ hongzhongTileName(info.detail.winning_logical_tile) }} 计分</p>
    <p v-if="substitutions.length">红中替代：{{ substitutions.join('、') }}</p>
  </section>
</template>

<script setup>
import { computed } from 'vue'
import { hongzhongTileName, hongzhongSubstitutionName } from '@/utils/hongzhongReplay.js'
const props = defineProps({ info: { type: Object, default: null }, ledger: { type: Array, default: () => [] } })
const substitutions = computed(() => (props.info?.detail?.joker_substitutions || [])
  .map(hongzhongSubstitutionName).filter(Boolean))
</script>

<style scoped>
.hongzhong-detail { padding: 10px 14px; background: var(--el-fill-color-light); border-radius: 6px; }
.hongzhong-detail p { margin: 4px 0; line-height: 1.7; overflow-wrap: anywhere; }
</style>
