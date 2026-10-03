<template>
    <section id="gb-decompose-section" class="decompose-section">
      <div class="decompose-head">
        <h2>全部拆解</h2>
        <span class="decompose-count">
          {{ `共 ${decompositions.length} 种拆解` }}
        </span>
      </div>
      <div v-if="decompositions.length" class="decomp-list">
        <div
          v-for="(item, idx) in decompositions"
          :key="idx"
          class="decomp-item"
        >
          <div class="decomp-header">
            <span class="decomp-rank">#{{ idx + 1 }}</span>
            <span class="decomp-score">{{ item.fan }} 番</span>
          </div>
          <div class="decomp-tiles">
            <div
              v-for="(group, gIdx) in item.groups"
              :key="gIdx"
              class="decomp-group"
            >
              <div class="decomp-group-label">{{ group.label }}</div>
              <div class="decomp-group-tiles nowrap-scroll">
                <TileMiniGlyph v-for="(t, tIdx) in group.tiles" :key="tIdx" :tile-id="t" />
              </div>
            </div>
          </div>
          <div class="decomp-fans">
            <el-tag v-for="(name, fIdx) in item.fanNames" :key="fIdx" size="small" effect="plain">
              {{ formatGuobiaoFanComposition(name) }}
            </el-tag>
          </div>
        </div>
      </div>
      <div v-else class="msg-inline">该牌型不能和牌。</div>
    </section>
</template>
<script setup>
import TileMiniGlyph from './TileMiniGlyph.vue'
import { formatGuobiaoFanComposition } from '../constants/guobiaoFanDict'
defineProps({ decompositions: { type: Array, required: true } })
</script>
<style scoped>
.nowrap-scroll {
  white-space: nowrap;
  overflow-x: auto;
  max-width: 100%;
  padding-bottom: 2px;
}

.decomp-group-tiles { display: inline-flex; gap: 1px; align-items: center; }

.decomp-list {
  padding: 8px 12px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.decomp-summary {
  font-size: 12.5px;
  color: var(--omu-text-soft, #475569);
}
.decomp-item {
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 8px;
  padding: 8px 10px;
  background: var(--omu-surface-soft, #f5f7fa);
}
.decomp-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}
.decomp-rank {
  font-family: var(--omu-mono, 'Consolas', monospace);
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12px;
}
.decomp-score {
  font-size: 1rem;
  font-weight: 700;
  color: var(--omu-accent, #409eff);
}
.decomp-tiles {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 6px;
}
.decomp-group {
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  background: #ffffff;
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 6px;
  padding: 4px 6px;
}
.decomp-group-label {
  font-size: 10.5px;
  color: var(--omu-text-muted, #94a3b8);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 2px;
}
.decomp-fans { display: flex; flex-wrap: wrap; gap: 4px; }

.decompose-section {
  margin-top: 16px;
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid var(--omu-border, #ebeef5);
  border-radius: 12px;
  padding: 12px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
  scroll-margin-top: 16px;
}

.decompose-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.decompose-head h2 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: #1e40af;
}

.decompose-count {
  font-size: 12.5px;
  color: #475569;
  font-weight: 600;
}

</style>
