<template>
  <div>
    <div :class="['banner', best.canWin ? 'success' : 'fail']">
      <div class="banner-num">{{ best.fan }}</div>
      <div class="banner-text">{{ tr('番') }}{{ locale === 'fr' && best.fan > 1 ? 's' : '' }}<span v-if="!best.canWin"> · 不足 8 番，不能和牌</span></div>
    </div>
    <div v-if="!best.canWin" class="msg-inline">牌型成立；花牌不计入 8 番起和条件（不计花牌 {{ best.baseFan }} 番）。</div>
    <div class="fan-block">
      <h4>番种构成（{{ best.fanNames.length }} 项）</h4>
      <div class="fan-tags nowrap-scroll">
        <el-tag v-for="(name, idx) in best.fanNames" :key="idx" type="success" effect="plain" size="small">{{ formatGuobiaoFanComposition(name) }}</el-tag>
      </div>
    </div>
    <div v-if="conditions" class="msg-inline">{{ conditions }}</div>
  </div>
</template>
<script setup>
import { formatGuobiaoFanComposition } from '../constants/guobiaoFanDict'
import { locale, tr } from '@/i18n'
defineProps({ best: { type: Object, required: true }, conditions: { type: String, default: '' } })
</script>
<style scoped>
.banner {
  margin: 8px 12px;
  padding: 10px 12px;
  border-radius: 8px;
  display: flex;
  align-items: baseline;
  gap: 8px;
  border: 1px solid;
}
.banner.success {
  background: #ecfdf5;
  border-color: #6ee7b7;
  color: #065f46;
}
.banner.fail {
  background: #fef2f2;
  border-color: #fca5a5;
  color: #991b1b;
}
.banner-num { font-size: 2rem; font-weight: 700; line-height: 1; }
.banner-text { font-size: 13px; }

.fan-block { padding: 0 12px 10px; }
.fan-block h4 {
  margin: 0 0 8px;
  font-size: 12.5px;
  color: var(--omu-text-soft, #606266);
  letter-spacing: 0.5px;
}
.fan-tags {
  display: flex;
  flex-wrap: nowrap;
  gap: 4px;
}

.nowrap-scroll {
  white-space: nowrap;
  overflow-x: auto;
  max-width: 100%;
  padding-bottom: 2px;
}


.msg-inline {
  padding: 10px 12px 12px;
  font-size: 12.5px;
  color: var(--omu-text-muted, #94a3b8);
  text-align: center;
}


</style>
