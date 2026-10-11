<template>
  <div class="riichi-room-config">
    <el-form-item label="起始点数">
      <el-input-number v-model="form.starting_score" :min="1000" :max="1000000" :step="100" step-strictly :precision="0" />
    </el-form-item>
    <el-form-item label="起和番">
      <el-input-number v-model="form.riichi_hepai_limit" :min="1" :max="64" :step="1" :precision="0" aria-label="起和番" />
      <span class="riichi-setting-help">只计实际役，不含宝牌</span>
    </el-form-item>
    <el-form-item label="起和线">
      <el-select v-model="form.minimum_win_score" aria-label="起和线" class="riichi-win-line">
        <el-option v-for="line in riichiWinLines" :key="line.value" :label="line.label" :value="line.value" />
      </el-select>
    </el-form-item>
    <p class="riichi-setting-help">{{ riichiWinLineHelp }}</p>
  </div>
</template>

<script setup>
import { riichiWinLines, riichiWinLineHelp } from '@/utils/riichiWinRequirements.js'
defineProps({ form: { type: Object, required: true } })
</script>

<style scoped>
.riichi-win-line { width: 240px; max-width: 100%; }
.riichi-setting-help { color: var(--el-text-color-secondary); font-size: 13px; line-height: 1.7; }
span.riichi-setting-help { margin-left: 12px; }
p.riichi-setting-help { margin: 0 0 16px; }
</style>
