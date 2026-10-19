<template>
  <div class="changsha-room-config">
    <p v-if="form.sub_rule !== exchangeSixRule">开杠后按骰子位置公开补牌；卡摸仅限制小胡点和。封顶按每名和牌者、每名付款者计算，同批多次和牌累计计入封顶。</p>
    <p v-else>开局有2秒可选择全桌不换；换完一轮后可选择再换一轮，最多两轮。每轮选六张、最多两种花色；三种花色失去和牌资格。固定四张开杠、双鸟、小胡2分、大胡8分起、不封顶。</p>
    <div class="settings">
      <el-form-item label="子规则"><el-select v-model="form.sub_rule"><el-option label="经典双鸟" value="changsha/classic_double_bird" /><el-option label="换六张" :value="exchangeSixRule" /></el-select></el-form-item>
      <el-form-item v-if="form.sub_rule === exchangeSixRule" label="换牌时间（秒）"><el-input-number v-model="form.exchange_selection_seconds" :min="10" :max="60" :precision="0" /></el-form-item>
      <el-form-item label="局数"><el-select v-model="form.game_round"><el-option v-for="round in [1,2,4]" :key="round" :value="round" :label="`${round * 4}局`" /></el-select></el-form-item>
      <el-form-item label="局时储备（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item v-if="showPassword" label="房间密码"><el-input v-model="form.password" type="password" show-password clearable /></el-form-item>
      <template v-for="(option,key) in changshaOptions" :key="key">
        <el-form-item v-if="form.sub_rule !== exchangeSixRule && (key !== 'score_cap_value' || form.score_cap_type !== 'none')" :label="option.label">
          <el-select v-if="option.values" v-model="form[key]"><el-option v-for="(value,index) in option.values" :key="value" :value="value" :label="option.choices?.[index] || String(value)" /></el-select>
          <el-switch v-else-if="typeof option.default === 'boolean'" v-model="form[key]" />
          <el-input-number v-else v-model="form[key]" :min="option.min" :max="option.max" :precision="0" />
        </el-form-item>
      </template>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
  </div>
</template>
<script setup>
import { watch } from 'vue'
import { changshaOptions, exchangeSixRule } from '@/utils/changshaRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
watch(() => props.form.score_cap_type, value => {
  if (value !== 'none' && !(props.form.score_cap_value > 0)) props.form.score_cap_value = value === 'big_hu' ? 4 : 64
})
</script>
<style scoped>
.settings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.settings :deep(.el-select), .settings :deep(.el-input-number) { width: 100%; }
p { color: var(--el-text-color-regular); line-height: 1.7; margin: 0 0 14px; }
@media (max-width: 640px) { .settings { grid-template-columns: 1fr; } }
</style>
