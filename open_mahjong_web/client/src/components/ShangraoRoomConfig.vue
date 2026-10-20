<template>
  <div class="shangrao-room-config">
    <p class="source">{{ SHANGRAO_DESCRIPTION }}</p>
    <div class="settings">
      <el-form-item label="副数"><el-select v-model="form.game_round"><el-option v-for="round in [1, 2, 3, 4]" :key="round" :value="round" :label="`${round * 4}副`" /></el-select></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
      <el-form-item v-for="(label,key) in SHANGRAO_OPTIONS" :key="key" :label="label"><el-switch v-model="form['shangrao_'+key]" /></el-form-item>
    </div>
    <a href="/rulebook/shangrao" target="_blank" rel="noopener">阅读上饶麻将 规则书</a>
  </div>
</template>
<script setup>
import { watch } from 'vue'
import { SHANGRAO_SUB_RULE, SHANGRAO_DESCRIPTION, SHANGRAO_OPTIONS, loadShangraoForm } from '@/utils/shangraoRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
watch(() => props.form.room_rule, value => {
  if (value === 'shangrao' && props.form.sub_rule !== SHANGRAO_SUB_RULE) loadShangraoForm(props.form)
}, { immediate: true, flush: 'sync' })
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.source { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
