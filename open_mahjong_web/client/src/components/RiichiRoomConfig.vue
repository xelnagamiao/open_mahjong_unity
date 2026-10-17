<template>
  <div class="riichi-room-config">
    <el-form-item label="子规则">
      <el-select :model-value="form.sub_rule" aria-label="立直子规则" @change="changeRiichiMode(form, $event)">
        <el-option v-for="mode in riichiModes" :key="mode.value" :label="mode.label" :value="mode.value" />
      </el-select>
    </el-form-item>
    <p v-if="isCaptain" class="riichi-setting-help" role="note">鹫巢麻将规则变体，四位玩家分为A、B两队，助手坐上家，主将坐下家，副将无击飞，最终两队只比对主将的分数判断胜负，鹫巢双打没有连庄。</p>
    <el-form-item v-if="!isCaptain" label="规则预设">
      <el-select :model-value="selectedRiichiPreset(form)?.id || ''" placeholder="自定义规则" aria-label="日麻规则预设" @change="applyRiichiPreset(form, $event)">
        <el-option v-for="preset in riichiPresets" :key="preset.id" :label="preset.label" :value="preset.id" />
      </el-select>
      <span class="riichi-setting-help">{{ form.sub_rule === 'riichi/sanma' ? '三人立直' : '四人立直' }}；选择预设同时应用局制、起始点和高级规则。</span>
    </el-form-item>
    <el-form-item label="圈数">
      <el-select v-model="form.game_round" aria-label="圈数">
        <el-option v-for="(label, index) in ['东风', '东南', '东南西', '全庄']" :key="index" :label="label" :value="index + 1" />
      </el-select>
    </el-form-item>
    <el-form-item label="起始点数">
      <el-input-number v-model="form.starting_score" :min="1000" :max="1000000" :step="100" step-strictly :precision="0" />
    </el-form-item>
    <el-form-item label="起和番">
      <el-input-number v-model="form.riichi_hepai_limit" :min="1" :max="64" :step="1" :precision="0" aria-label="起和番" />
      <span class="riichi-setting-help">只计实际役，不含宝牌</span>
    </el-form-item>
    <el-form-item label="起和线">
      <el-select v-model="form.minimum_win_score" aria-label="起和线" class="riichi-win-line">
        <el-option v-for="line in riichiWinLines" :key="line.value" :label="line.label" :value="line.value" />
      </el-select>
    </el-form-item>
    <p class="riichi-setting-help">{{ riichiWinLineHelp }}</p>
    <div class="riichi-switches">
      <el-checkbox v-model="form.red_dora" :disabled="isCaptain">赤宝牌</el-checkbox>
      <el-checkbox v-model="form.allow_kuikae">允许食替</el-checkbox>
      <el-checkbox v-model="form.open_xiru" :disabled="isCaptain">允许延长战</el-checkbox>
      <el-checkbox v-model="form.open_tobi" :disabled="isCaptain">{{ isCaptain ? '主将负分结束' : '击飞结束' }}</el-checkbox>
      <el-checkbox v-model="form.open_cuohe" @change="value => { if (value) form.tips = false }">允许错和宣告</el-checkbox>
    </div>
    <el-form-item label="多家荣和">
      <el-select v-model="form.hepai_way" :disabled="isCaptain" aria-label="多家荣和">
        <el-option label="多响" value="multi_ron" />
        <el-option label="三家荣和流局" value="three_ron_abort" />
        <el-option label="头跳" value="head_bump" />
      </el-select>
    </el-form-item>
    <el-collapse>
      <el-collapse-item v-for="section in sections" :key="section.name" :title="section.name" :name="section.name">
        <div class="riichi-options">
          <el-form-item v-for="option in section.options" :key="option.key" :label="option.label">
            <el-select v-model="form.riichi_detailed_config[option.key]" :aria-label="option.label">
              <el-option v-for="(value, index) in option.values" :key="String(value)" :value="value" :label="option.choices[index]" />
            </el-select>
            <p class="riichi-setting-help">{{ option.help }}</p>
          </el-form-item>
        </div>
      </el-collapse-item>
    </el-collapse>
    <div class="riichi-options riichi-common">
      <el-form-item label="局时（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
    </div>
    <div class="riichi-switches">
      <el-checkbox v-model="form.tips" @change="value => { if (value) form.open_cuohe = false }">和牌提示</el-checkbox>
      <el-checkbox v-model="form.tourist_limit">禁止游客</el-checkbox>
      <el-checkbox v-model="form.allow_spectator">允许观战</el-checkbox>
    </div>
  </div>
</template>

<script setup>
import { riichiWinLines, riichiWinLineHelp } from '@/utils/riichiWinRequirements.js'
import { computed } from 'vue'
import { riichiPresets, riichiOptions, applyRiichiPreset, selectedRiichiPreset, riichiModes, changeRiichiMode, isWashizuCaptain, captainFixedKeys } from '@/utils/riichiRoomConfig.js'

const props = defineProps({ form: { type: Object, required: true } })
const isCaptain = computed(() => isWashizuCaptain(props.form))
const sections = computed(() => {
  const groups = new Map()
  for (const option of riichiOptions) {
    if (isCaptain.value && captainFixedKeys.includes(option.key)) continue
    if (option.key === 'minimum_win_score' || (option.section === '三人麻将' && props.form.sub_rule !== 'riichi/sanma')) continue
    if (!groups.has(option.section)) groups.set(option.section, { name: option.section, options: [] })
    groups.get(option.section).options.push(option)
  }
  return [...groups.values()]
})
</script>

<style scoped>
.riichi-win-line { width: 240px; max-width: 100%; }
.riichi-setting-help { color: var(--el-text-color-secondary); font-size: 13px; line-height: 1.7; }
span.riichi-setting-help { margin-left: 12px; }
p.riichi-setting-help { margin: 0 0 16px; }
.riichi-switches { display: flex; flex-wrap: wrap; gap: 4px 16px; margin: 0 0 16px; }
.riichi-switches :deep(.el-checkbox) { margin-right: 0; }
.riichi-options { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 260px), 1fr)); gap: 0 20px; }
.riichi-options :deep(.el-select) { width: 100%; }
.riichi-options p { margin: 4px 0 0; }
.riichi-common { margin-top: 20px; }
</style>
