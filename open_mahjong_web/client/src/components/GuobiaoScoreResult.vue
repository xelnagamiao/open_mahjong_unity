<template>
  <div>
    <div :class="['banner', best.canWin ? 'success' : 'fail']">
      <template v-if="best.rule === 'riichi'">
        <div class="banner-num">{{ best.points }}</div>
        <div class="banner-text">点 · {{ best.yakuman ? (best.yakuman > 1 ? `${best.yakuman} 倍役满` : '役满') : `${best.fan} 番 ${best.fu} 符` }}<span v-if="!best.canWin"> · 无役，不能和牌</span></div>
      </template>
      <template v-else>
        <div class="banner-num">{{ preciseFan(best.fan) }}</div>
        <div class="banner-text">{{ tr('番') }}{{ locale === 'fr' && best.fan > 1 ? 's' : '' }}<template v-if="best.rule === 'qingque'"><span> · 底分 {{ best.points }}</span><span v-if="!best.canWin"> · 不足 1 番，不能和牌</span></template><span v-else-if="!best.canWin"> · 不足 8 番，不能和牌</span></div>
      </template>
    </div>
    <div v-if="best.rule === 'riichi'" class="msg-inline">{{ best.payment }}</div>
    <div v-else-if="best.rule !== 'qingque' && !best.canWin" class="msg-inline">牌型成立；花牌不计入 8 番起和条件（不计花牌 {{ best.baseFan }} 番）。</div>
    <div class="fan-block">
      <h4>番种构成（{{ best.fanNames.length }} 项）</h4>
      <div class="fan-tags nowrap-scroll">
        <el-tag v-for="(name, idx) in best.fanNames" :key="idx" type="success" effect="plain" size="small">{{ best.rule ? name : formatGuobiaoFanComposition(name) }}</el-tag>
      </div>
    </div>
    <div v-if="conditions" class="msg-inline">{{ conditions }}</div>
  </div>
</template>
<script setup>
import { formatGuobiaoFanComposition } from '../constants/guobiaoFanDict'
import { locale, tr } from '@/i18n'
import { preciseFan } from '@/utils/calculatorScoreText.js'
defineProps({ best: { type: Object, required: true }, conditions: { type: String, default: '' } })
</script>
<style scoped>
.banner {
  margin: 8px 12px;
  padding: 10px 12px;
  border-radius: 8px;
  display: flex;
  flex-wrap: wrap;
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
