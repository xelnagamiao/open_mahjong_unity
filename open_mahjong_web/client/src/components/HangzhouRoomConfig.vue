<template>
  <div class="hangzhou-room-config">
    <div class="settings">
      <el-form-item label="子规则"><el-select v-model="form.sub_rule"><el-option :value="HANGZHOU_SUB_RULE" label="MIL 杭州（2025）" /></el-select></el-form-item>
      <el-form-item label="局数"><el-select v-model="form.game_round"><el-option v-for="round in [1,2,3,4]" :key="round" :value="round" :label="`${round * 4}局`" /></el-select></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item label="战术鸣牌"><el-switch v-model="form.tactical_call" /><span>抢断再询问5秒；机器人参与时自动关闭</span></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
    <p class="source">{{ HANGZHOU_DESCRIPTION }}</p>
    <a href="/rulebooks/mil/杭州麻将（推广）竞赛规则（试行2025版）.pdf" target="_blank" rel="noopener">阅读 MIL 规则书原文</a>
  </div>
</template>
<script setup>
import { watch } from 'vue'
import { HANGZHOU_SUB_RULE, HANGZHOU_DESCRIPTION, loadHangzhouForm } from '@/utils/hangzhouRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
watch(() => props.form.room_rule, value => {
  if (value === 'hangzhou' && props.form.sub_rule !== HANGZHOU_SUB_RULE) loadHangzhouForm(props.form)
}, { immediate: true, flush: 'sync' })
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; font: inherit; }
.source { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
