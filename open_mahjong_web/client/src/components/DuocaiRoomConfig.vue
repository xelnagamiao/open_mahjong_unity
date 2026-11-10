<template>
  <div class="duocai-room-config">
    <p class="source">{{ DUOCAI_DESCRIPTION }}</p>
    <div class="settings">
      <el-form-item label="子规则" class="room-sub-rule-field">
        <el-select v-model="form.sub_rule" aria-label="夺彩子规则" @change="changeDuocaiProfile(form, $event)">
          <el-option v-for="item in DUOCAI_PROFILES" :key="item.value" :value="item.value" :label="item.label" />
        </el-select>
      </el-form-item>
      <el-form-item label="对局形式"><span>{{ profile?.roundLabel || '请选择子规则' }}</span></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
    <p class="rule-summary">{{ profile?.description }}</p>
    <p class="rule-summary">起始与返点均为40000点，顺位点为0；每副144张，包含8张不同赖子。两副牌组交替使用。</p>
    <a href="/rulebook/duocai" target="_blank" rel="noopener">阅读夺彩麻将规则书与赖子图鉴</a>
  </div>
</template>
<script setup>
import { computed } from 'vue'
import { DUOCAI_DESCRIPTION, DUOCAI_PROFILES, changeDuocaiProfile, duocaiProfile } from '@/utils/duocaiRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
const profile = computed(() => duocaiProfile(props.form.sub_rule))
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.source, .rule-summary { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
