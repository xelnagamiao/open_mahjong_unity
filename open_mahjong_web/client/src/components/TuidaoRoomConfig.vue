<template>
  <div class="tuidao-settings">
    <el-form-item label="子规则">
      <el-select v-model="form.sub_rule"><el-option v-for="profile in guangdongProfiles" :key="profile.value" :label="profile.label" :value="profile.value" /></el-select>
    </el-form-item>
    <p class="source">{{ selectedProfile.description }}</p>
    <div class="common-fields">
      <el-form-item label="局数">
        <el-select v-model="form.game_round"><el-option v-for="value in [1, 2, 3, 4]" :key="value" :label="`${value * 4}局`" :value="value" /></el-select>
      </el-form-item>
      <el-form-item label="局时（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
      <el-form-item label="战术鸣牌"><el-switch v-model="form.tactical_call" /><span class="score-note">抢断5秒，有机器人时自动关闭</span></el-form-item>
      <el-form-item v-if="form.sub_rule === GUANGDONG_MIL_SUB_RULE" label="至少4分起和"><el-switch v-model="form.require_minimum_score" /><span class="score-note">关闭后仍须至少2番</span></el-form-item>
    </div>
    <el-form-item v-if="showPassword" label="房间密码（可选）"><el-input v-model="form.password" type="password" show-password /></el-form-item>
    <a :href="`/rulebooks/mil/${encodeURIComponent(selectedProfile.book)}`" target="_blank" rel="noopener">阅读 MIL 规则书原文</a>
  </div>
</template>

<script setup>
import { computed, watch } from 'vue'
import { GUANGDONG_MIL_SUB_RULE, TUIDAO_SUB_RULE, guangdongProfiles } from '@/utils/guangdongRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: Boolean })
const selectedProfile = computed(() => guangdongProfiles.find(item => item.value === props.form.sub_rule) || guangdongProfiles[0])
watch(() => props.form.sub_rule, value => {
  if (!guangdongProfiles.some(item => item.value === value)) props.form.sub_rule = guangdongProfiles[0].value
  if (typeof props.form.require_minimum_score !== 'boolean') props.form.require_minimum_score = true
  if (typeof props.form.tactical_call !== 'boolean') props.form.tactical_call = true
}, { immediate: true })
</script>

<style scoped>
.common-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 0 16px; }
.source { margin: 0 0 16px; color: #606266; line-height: 1.7; }
.score-note { color: #606266; margin-left: 8px; font-size: 12px; }
.tuidao-settings a { color: #337ecc; }
</style>
