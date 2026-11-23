<template>
  <div class="new-chinese-room-config">
    <el-form-item label="子规则"><el-select v-model="form.sub_rule"><el-option v-for="p in customRulebooks" :key="p.subRule" :value="p.subRule" :label="p.label" /></el-select></el-form-item>
    <p class="description">{{ (customRulebooks.find(p => p.subRule === form.sub_rule) || customRulebooks[0]).description }}</p>
    <div class="settings">
      <el-form-item :label="form.sub_rule === 'new_chinese/sanhua' ? '副数' : '圈数'"><el-select v-model="form.game_round" aria-label="新中式麻将圈数"><el-option v-for="n in [4,8,12,16]" :key="n" :value="n" :label="`${n}${form.sub_rule === 'new_chinese/sanhua' ? '副' : '圈'}`" /></el-select></el-form-item>
      <el-form-item v-if="form.sub_rule !== 'new_chinese/sanhua'" label="换三张"><el-switch v-model="form.exchange_three" aria-label="换三张" /></el-form-item>
      <el-form-item v-if="form.sub_rule !== 'new_chinese/sanhua'" label="起和番">
        <el-input-number :model-value="newChineseWinLimit(form)" :disabled="form.exchange_three" :min="0" :max="MAX_NEW_CHINESE_FAN"
          :precision="0" aria-label="起和番" @update:model-value="form.hepai_limit = $event" />
      </el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="剩余张数提示"><el-switch v-model="form.count_tips" /></el-form-item>
      <el-form-item label="指针提示"><el-switch v-model="form.pointer_tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
  </div>
</template>
<script setup>
import { customRulebooks } from '@/constants/newChineseRulebooks.js'
import { MAX_NEW_CHINESE_FAN, newChineseWinLimit } from '@/utils/newChineseRoomConfig.js'
defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.description { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
