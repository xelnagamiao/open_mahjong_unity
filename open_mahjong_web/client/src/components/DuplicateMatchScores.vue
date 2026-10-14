<template>
  <section aria-label="复式成绩">
    <div class="totals">
      <span>副分 · 蓝队 {{ smallScores?.[0] ?? '—' }} : {{ smallScores?.[1] ?? '—' }} 红队</span>
      <span>IMP · 蓝队 {{ impScores?.[0] ?? '—' }} : {{ impScores?.[1] ?? '—' }} 红队</span>
      <span class="basis">按{{ match.scoring === 'imp' ? ' IMP ' : '副分' }}判定胜负</span>
    </div>
    <el-table :data="match.boards" aria-label="逐副复式分数">
      <el-table-column prop="number" label="副号" width="70" />
      <el-table-column label="开室蓝队副分" min-width="130"><template #default="{ row }">{{ row.tables.open.delta[0] }}</template></el-table-column>
      <el-table-column label="闭室蓝队副分" min-width="130"><template #default="{ row }">{{ row.tables.closed.delta[0] }}</template></el-table-column>
      <el-table-column label="蓝队原始分差" min-width="130"><template #default="{ row }">{{ row.raw_difference[0] }}</template></el-table-column>
      <el-table-column label="蓝 : 红 副分" min-width="120"><template #default="{ row }">{{ scoreText(row.small_scores ?? (match.scoring === 'raw_difference' ? row.scores : null)) }}</template></el-table-column>
      <el-table-column label="蓝 : 红 IMP" min-width="120"><template #default="{ row }">{{ scoreText(row.imp_scores ?? (match.scoring === 'imp' ? row.scores : null)) }}</template></el-table-column>
    </el-table>
  </section>
</template>

<script setup>
import { computed } from 'vue'
const props = defineProps({ match: { type: Object, required: true } })
const smallScores = computed(() => props.match.small_scores ?? (props.match.scoring === 'raw_difference' ? props.match.scores : null))
const impScores = computed(() => props.match.imp_scores ?? (props.match.scoring === 'imp' ? props.match.scores : null))
const scoreText = scores => Array.isArray(scores) && scores.length === 2 ? `${scores[0]} : ${scores[1]}` : '—'
</script>

<style scoped>
.totals { display: flex; flex-wrap: wrap; align-items: baseline; gap: 12px 24px; margin: 16px 0; }
.basis { color: #737d88; font-size: 13px; }
</style>
