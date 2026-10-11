<template>
  <div class="jiujiang-room-config">
    <p class="source">九江麻将是九江地区一个没落的麻将规则，目前九江地区通行红中麻将，本规则是芋蔗凭借小时候的记忆和一定程度上的改良复现的一种失传规则，如发现任何问题或者能够提供九江麻将的资料的玩家可以联系Q2264033554</p>
    <div class="settings">
      <el-form-item label="副数">
        <el-select v-model="form.game_round">
          <el-option v-for="round in [1, 2, 3, 4]" :key="round" :value="round" :label="`${round * 4}副`" />
        </el-select>
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
    <a href="/rulebooks/jiujiang.docx" download="九江麻将规则书.docx">下载九江麻将规则书</a>
  </div>
</template>

<script setup>
import { watch } from 'vue'
import { JIUJIANG_SUB_RULE, loadJiujiangForm } from '@/utils/jiujiangRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
watch(() => props.form.room_rule, value => {
  if (value === 'jiujiang' && props.form.sub_rule !== JIUJIANG_SUB_RULE) loadJiujiangForm(props.form)
}, { immediate: true, flush: 'sync' })
</script>

<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.source { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
