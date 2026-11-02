<template>
  <div class="shape-options" role="group" aria-label="和牌牌型">
    <span class="row-label">和牌牌型</span>
    <div class="switches">
      <el-switch v-for="item in items" :key="item.key" :model-value="options[item.key]" size="small"
        :active-text="item.label" :aria-label="item.label" :title="item.title"
        :disabled="loading || (rule === 'riichi' && item.key === 'mcrSevenPairs') || (rule !== 'guobiao' && ['unrelatedTiles', 'combinationDragon'].includes(item.key))"
        @change="value => $emit('change', item.key, value)" />
    </div>
  </div>
</template>
<script setup>
defineProps({ options: { type: Object, required: true }, rule: { type: String, default: 'guobiao' }, loading: Boolean })
defineEmits(['change'])
const items = [
  { key: 'mcrSevenPairs', label: '国标七对', title: '允许出现相同对子（青雀同样适用）' },
  { key: 'riichiSevenPairs', label: '日麻七对', title: '不许出现相同对子' },
  { key: 'thirteenOrphans', label: '十三幺' },
  { key: 'unrelatedTiles', label: '全不靠' },
  { key: 'combinationDragon', label: '组合龙' },
]
</script>
<style scoped>
.shape-options { padding: 8px 12px; display: flex; align-items: center; flex-wrap: wrap; gap: 8px 14px; border-top: 1px dashed var(--omu-border, #ebeef5); }
.row-label { font-size: 12.5px; font-weight: 600; color: var(--omu-text-soft, #475569); }
.switches { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 14px; }
</style>
