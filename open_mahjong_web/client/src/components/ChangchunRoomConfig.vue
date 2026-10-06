<template>
  <div class="changchun-room-config">
    <p class="source">MIL 2024：136张、十三张手牌，三门带幺九，可吃碰杠和报听。一条仅可在特殊杠中代牌；报听后看宝，六番封顶。三张特殊杠首次不补牌，以后每加一张补一张。</p>
    <div class="settings">
      <el-form-item label="局数">
        <el-select v-model="form.game_round">
          <el-option v-for="round in [1, 2, 3, 4]" :key="round" :value="round" :label="`${round * 4}局`" />
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
      <el-form-item label="战术鸣牌"><el-switch v-model="form.tactical_call" title="有效申请后可在5秒内以更高优先级抢断；机器人入座自动关闭" /></el-form-item>
    </div>
    <a href="/rulebooks/mil/长春麻将（推广）竞赛规则（试行2024版）.pdf" target="_blank" rel="noopener">阅读 MIL 长春规则书</a>
  </div>
</template>

<script setup>
import { watch } from 'vue'
import { CHANGCHUN_SUB_RULE, loadChangchunForm } from '@/utils/changchunRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
watch(() => props.form.room_rule, value => {
  if (value === 'changchun' && props.form.sub_rule !== CHANGCHUN_SUB_RULE) loadChangchunForm(props.form)
}, { immediate: true, flush: 'sync' })
</script>

<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
.source { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
