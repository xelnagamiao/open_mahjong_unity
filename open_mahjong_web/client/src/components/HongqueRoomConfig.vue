<template>
  <div class="hongque-room-config">
    <el-form-item label="子规则">
      <el-select v-model="form.sub_rule" aria-label="虹雀子规则">
        <el-option v-for="profile in hongqueProfiles" :key="profile.value" :value="profile.value" :label="profile.label" />
      </el-select>
    </el-form-item>
    <p v-if="book.description" class="description">{{ book.description }}</p>
    <div class="settings">
      <el-form-item label="局数"><el-select v-model="form.game_round" aria-label="虹雀局数"><el-option v-for="n in [4, 8, 12, 16]" :key="n" :value="n" :label="`${n}局`" /></el-select></el-form-item>
      <el-form-item label="点和方式"><el-select v-model="form.hepai_way" aria-label="虹雀点和方式"><el-option value="multi_ron" label="一炮多响" /><el-option value="head_bump" label="截和" /></el-select></el-form-item>
      <template v-if="isHongqueW(form)">
        <el-form-item label="加入无色牌"><el-switch v-model="form.hongque_colourless" aria-label="加入无色牌" /></el-form-item>
        <el-form-item label="牌库"><span>{{ form.hongque_colourless ? '144张' : '126张' }}</span></el-form-item>
        <el-form-item label="起和番数"><el-input-number v-model="form.hongque_min_fan" :min="0" :max="100" :precision="0" aria-label="起和番数" /></el-form-item>
        <el-form-item label="起和点数"><el-input-number v-model="form.hongque_min_points" :min="0" :max="10000" :precision="0" aria-label="起和点数" /></el-form-item>
      </template>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
    <p v-if="isHongqueW(form)" class="description">只允许碰彩虹，不允许普通碰牌。每位玩家每局最多补牌两次。无色牌可组成对子或同色顺子，不能组成刻子或彩虹。</p>
    <a :href="`/rulebook/hongque?sub_rule=${encodeURIComponent(form.sub_rule)}`" target="_blank" rel="noopener">阅读完整规则书</a>
  </div>
</template>
<script setup>
import { computed } from 'vue'
import { hongqueRulebook } from '@/constants/hongqueRulebooks.js'
import { hongqueProfiles, isHongqueW } from '@/utils/hongqueRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
const book = computed(() => hongqueRulebook(props.form.sub_rule))
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.hongque-room-config :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.description { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
a { display: inline-block; margin-top: 16px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
