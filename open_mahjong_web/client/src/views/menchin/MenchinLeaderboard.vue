<template>
  <section class="board game-lobby" aria-label="排行榜">
    <div class="sec-h rank-sec-h lobby-section-title">
      <div class="rank-tabs">
        <h2 class="rank-label">{{ hasTabs ? '排行榜' : '挑战排行榜' }}</h2>
        <template v-if="hasTabs">
          <button type="button" class="rank-tab" :class="{ active: !showDuel }"
            :aria-pressed="!showDuel" @click="selected = 'challenge'">挑战</button>
          <button type="button" class="rank-tab" :class="{ active: showDuel }"
            :aria-pressed="showDuel" @click="selected = 'duel'">PvP</button>
        </template>
      </div>
      <el-tooltip placement="left" :show-after="200" :trigger="['hover', 'focus', 'click']">
        <template #content>
          <div class="tip">
            <template v-if="showDuel">
              <div>Elo 积分规则</div>
              <div>初始 R 为 1500，仅系统匹配计分，自建房不计。</div>
              <div>预期胜率 = 1 / (1 + 10^((对手 R − 自己 R) / 2000))。</div>
              <div>变化值 = 32 ×（实际结果 − 预期胜率），结果四舍五入为整数；胜为 1，负为 0。</div>
            </template>
            <template v-else>北京时间今日排名，每人取单局最高分。</template>
          </div>
        </template>
        <button type="button" class="elo-help" :aria-label="showDuel ? '查看 Elo 积分规则' : '查看挑战排名规则'">?</button>
      </el-tooltip>
    </div>
    <div class="panel-box rank-box">
      <div v-if="status === 'error'" class="empty" role="status">
        排行榜读取失败 <button type="button" class="retry" @click="$emit('retry')">重试</button>
      </div>
      <div v-else-if="status === 'loading'" class="empty" role="status">正在读取排行</div>
      <ol v-else-if="entries.length" :key="selected" class="rank-list lobby-rank-list" aria-label="排行榜" tabindex="0">
        <li v-for="(entry, i) in entries" :key="entry.id">
          <span class="rk" :class="'top-' + (i + 1)">{{ i + 1 }}</span>
          <span class="rank-player">
            <span class="nm" data-no-translate>{{ entry.name }}</span>
            <small v-if="showDuel">{{ entry.wins }}胜 {{ entry.matches - entry.wins }}负 · 胜率 {{ Math.round(100 * entry.wins / entry.matches) }}%</small>
            <small v-else>{{ CHALLENGE_LEVELS[entry.level - 1]?.name }}</small>
          </span>
          <span class="rank-score"><b>{{ entry.score }}</b></span>
        </li>
      </ol>
      <div v-else class="empty">{{ showDuel ? '暂无排位成绩' : '今日暂无挑战成绩' }}</div>
    </div>
  </section>
</template>

<script setup>
import { computed, ref } from 'vue'
import { CHALLENGE_LEVELS } from '../../../../shared/menchin.mjs'

const props = defineProps({
  rows: { type: Array, required: true },
  duelRows: Array,
  status: { type: String, required: true },
})
defineEmits(['retry'])
const selected = ref('challenge')
const hasTabs = computed(() => Array.isArray(props.duelRows))
const showDuel = computed(() => hasTabs.value && selected.value === 'duel')
const entries = computed(() => showDuel.value ? props.duelRows : props.rows)
</script>

<style scoped>
.board { min-width: 0; }
.sec-h { margin-bottom: 12px; padding: 6px 12px; background: rgba(0, 0, 0, .75); color: white; font-size: 13px; }
.rank-sec-h, .rank-tabs { display: flex; align-items: center; }
.rank-sec-h { justify-content: space-between; gap: 8px; }
.rank-tabs { gap: 6px; min-width: 0; }
.rank-label { margin: 0 4px 0 0; font: inherit; font-weight: 400; white-space: nowrap; }
.rank-tab { padding: 4px 10px; border: 1px solid rgba(255, 255, 255, .45); border-radius: 14px; background: transparent; color: white; font-family: inherit; font-size: 12px; font-weight: 400; cursor: pointer; white-space: nowrap; }
.elo-help { flex-shrink: 0; width: 20px; height: 20px; padding: 0; border: 1px solid rgba(255, 255, 255, .55); border-radius: 50%; background: transparent; color: white; font-size: 12px; font-weight: 800; line-height: 1; cursor: help; }
.rank-tab:hover, .elo-help:hover { border-color: white; background: rgba(255, 255, 255, .12); }
.rank-tab.active { color: white; background: #e6a23c; border-color: #e6a23c; }
.tip { max-width: min(360px, 75vw); line-height: 1.7; }
.rank-box { padding: 12px 14px; background: white; border: 1px solid #e0e0e0; }
.rank-list { list-style: none; margin: 0; padding: 0; }
.rank-list li { display: grid; grid-template-columns: 28px minmax(0, 1fr) auto; align-items: center; gap: 8px; padding: 9px 0; border-bottom: 1px solid #eee; font-size: 13px; }
.rk { display: grid; place-items: center; width: 24px; height: 24px; border-radius: 50%; background: #f0f2f5; color: #909399; font-weight: 700; }
.rk.top-1 { background: #f5c451; color: #6f4c00; }
.rk.top-2 { background: #d8dde5; color: #4f5968; }
.rk.top-3 { background: #d9a676; color: #6f3818; }
.rank-player, .rank-score { display: flex; flex-direction: column; min-width: 0; }
.nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rank-player small { color: #909399; font-size: 10px; }
.rank-score { align-items: flex-end; color: #409eff; }
.empty { padding: 12px 0; text-align: center; color: #999; font-size: 13px; }
.retry { padding: 3px 8px; border: 1px solid #ccc; background: white; color: #333; font: inherit; cursor: pointer; }
</style>
