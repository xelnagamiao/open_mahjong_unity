<template>
  <div class="new-chinese-room-config">
    <p class="description">{{ NEW_CHINESE_DESCRIPTION }}</p>
    <div class="settings">
      <el-form-item label="圈数"><el-select v-model="form.game_round" aria-label="新中式麻将圈数"><el-option v-for="n in [4,8,12,16]" :key="n" :value="n" :label="`${n}圈`" /></el-select></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
      <el-form-item label="换三张"><el-switch v-model="form.exchange_three" /></el-form-item>
    </div>
  </div>
</template>
<script setup>
import { NEW_CHINESE_DESCRIPTION } from '@/constants/newChineseRulebooks.js'
defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.description { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
