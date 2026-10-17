<template>
  <div class="fuzhou-room-config">
    <div class="settings">
      <el-form-item label="子规则"><el-select v-model="form.sub_rule"><el-option :value="FUZHOU_SUB_RULE" label="福州十六张" /></el-select></el-form-item>
      <el-form-item label="局数"><el-select v-model="form.game_round"><el-option v-for="[round,label] in [[1,'一圈（4局）'],[2,'半庄（8局）'],[4,'一雀（16局）']]" :key="round" :value="round" :label="label" /></el-select></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
      <el-form-item label="底分"><el-select v-model="form.fuzhou_base_score"><el-option v-for="score in [10,15,20]" :key="score" :value="score" :label="`${score}分`" /></el-select></el-form-item>
    </div>
    <p class="source">{{ FUZHOU_DESCRIPTION }}</p>
    <a href="/rulebooks/fuzhou.html" target="_blank" rel="noopener">阅读规则书及平台补则</a>
  </div>
</template>
<script setup>
import { watch } from 'vue'
import { FUZHOU_SUB_RULE, FUZHOU_DESCRIPTION, loadFuzhouForm } from '@/utils/fuzhouRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
watch(() => props.form.room_rule, value => {
  if (value === 'fuzhou' && props.form.sub_rule !== FUZHOU_SUB_RULE) loadFuzhouForm(props.form)
}, { immediate: true, flush: 'sync' })
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; font: inherit; }
.source { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
