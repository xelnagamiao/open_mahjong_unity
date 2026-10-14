<template>
  <div class="gb-room-config" :class="{ 'gb-room-config--panel': panelLayout }">
    <template v-for="section in sectionOrder" :key="section">
      <div v-if="section === 'header'" class="gb-room-config-header">
        <slot name="before-sub-rule" />
        <el-form-item :label="tr('子规则')" class="gb-room-sub-rule">
          <el-select :model-value="modelValue.sub_rule" style="width: 160px" @update:model-value="patch('sub_rule', $event)">
            <el-option
              v-for="opt in resolvedSubRuleOptions"
              :key="opt.value"
              :label="tr(opt.label)"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <slot name="after-sub-rule" />
        <div v-if="isHuahun" class="huahun-description">
          <p>{{ HUAHUN_DESCRIPTION }}</p>
          <el-button @click="showHuahunRules = true">规则书</el-button>
        </div>
      </div>
      <div v-else-if="section === 'main'" class="gb-room-config-main">
        <h4 v-if="panelLayout" class="gb-room-section-title">{{ tr('参数配置') }}</h4>
        <el-form-item :label="tr('圈数')">
          <span v-if="autoDuplicate || modelValue.duplicate_key?.trim()" class="flower-help">{{ tr('跟随密钥设置') }}</span>
          <el-select v-else :model-value="modelValue.game_round" :aria-label="tr('圈数')" style="width: 100px" @update:model-value="patch('game_round', $event)">
            <el-option :value="1" :label="tr('东风战')" />
            <el-option :value="2" :label="tr('东南战')" />
            <el-option :value="3" :label="tr('东西战')" />
            <el-option :value="4" :label="tr('全庄战')" />
          </el-select>
        </el-form-item>
        <el-form-item :label="tr('局时(秒)')">
          <el-select
            :model-value="modelValue.round_timer"
            style="width: 140px"
            @update:model-value="patch('round_timer', $event)"
          >
            <el-option
              v-for="opt in roundTimerOptions"
              :key="opt.value"
              :label="tr(opt.label)"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="tr('步时(秒)')">
          <el-input-number
            :model-value="modelValue.step_timer"
            :min="0"
            :max="100"
            controls-position="right"
            @update:model-value="patch('step_timer', $event)"
          />
        </el-form-item>
        <el-form-item v-if="!isHuahun" :label="tr(isLanshi ? '起和分' : '起和番')">
          <el-input-number
            :model-value="isLanshi ? 5 : modelValue.hepai_limit"
            :disabled="isLanshi"
            :min="1"
            :max="64"
            controls-position="right"
            @update:model-value="patch('hepai_limit', $event)"
          />
        </el-form-item>
        <el-form-item v-if="showPassword" :label="tr('房间密码')">
          <el-input
            :model-value="modelValue.password"
            clearable
            style="width: 140px"
            :placeholder="tr('可选')"
            show-password
            @update:model-value="patch('password', $event)"
          />
        </el-form-item>
        <el-form-item v-if="panelLayout" :label="tr('错和形式')" class="gb-room-cuohe-type">
          <el-select :model-value="isLanshi ? 1 : modelValue.cuohe_type" :disabled="isLanshi || !modelValue.open_cuohe" @update:model-value="patch('cuohe_type', $event)">
            <el-option :value="0" :label="tr('错和-30，其余各+10')" />
            <el-option :value="1" :label="tr('错和-40，其余不加分')" />
          </el-select>
        </el-form-item>
      </div>
      <DuplicateRoomField v-else-if="section === 'duplicate' && showDuplicateField && !autoDuplicate && !['guobiao/transparent', 'guobiao/transparent_realtime', 'guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'].includes(modelValue.sub_rule)" :model-value="modelValue.duplicate_key || ''" @update:model-value="patch('duplicate_key', $event)" />
      <div v-else-if="section === 'options'" class="gb-room-config-options">
        <h4 v-if="panelLayout" class="gb-room-section-title">{{ tr('开关选项') }}</h4>
        <el-form-item :label="panelLayout ? undefined : tr('花牌')" class="gb-room-flowers">
          <span v-if="modelValue.duplicate_key?.trim()" class="flower-help">{{ panelLayout ? `${tr('花牌')} · ` : '' }}{{ tr('跟随复式设置') }}</span>
          <template v-else><el-switch :model-value="isHuahun || (!isLanshi && modelValue.use_flowers !== false)" :disabled="isLanshi || isHuahun" :active-text="panelLayout ? tr('花牌') : ''" :aria-label="tr('花牌')" @update:model-value="patch('use_flowers', $event)" /><span v-if="modelValue.sub_rule === 'guobiao/lanshi'" class="flower-help">{{ tr('固定无花') }}</span></template>
        </el-form-item>
        <el-form-item :label="panelLayout ? undefined : tr('提示')">
          <el-switch :model-value="!isLanshi && modelValue.tips" :disabled="isLanshi" :active-text="panelLayout ? tr('提示') : ''" :aria-label="tr('提示')" @update:model-value="patch('tips', $event)" />
        </el-form-item>
        <el-form-item v-if="supportsOpeningWins" :label="panelLayout ? undefined : tr('天地人和')">
          <el-tooltip :content="tr('各加计8番：天和为庄家起手自摸；地和为闲家和庄家首张弃牌；人和为闲家首次摸牌自摸。全桌吃碰杠（含暗杠）打断，补花不打断。')">
            <el-switch :model-value="!!modelValue.tian_di_ren_he" :active-text="panelLayout ? tr('天地人和') : ''" :aria-label="tr('天地人和')" @update:model-value="patch('tian_di_ren_he', $event)" />
          </el-tooltip>
        </el-form-item>
        <el-form-item v-if="['guobiao/huahun', 'guobiao/four_jokers'].includes(modelValue.sub_rule)" :label="panelLayout ? undefined : tr('癞子多面听')">
          <el-tooltip content="癞子多面听：国标癞子场的标准设计是先将癞子确定为任意手牌，然后再计算听牌型，因此手持癞子的多面听在癞子确定为符合条件的手牌时，仍可计算独听；开启此设计后，手持癞子的多面听均不计算独听。" :popper-style="{ maxWidth: '440px' }">
            <el-switch :model-value="!!modelValue.joker_multi_wait" :active-text="panelLayout ? tr('癞子多面听') : ''" :aria-label="tr('癞子多面听')" @update:model-value="patch('joker_multi_wait', $event)" />
          </el-tooltip>
        </el-form-item>
        <el-form-item v-if="isHuahun" label="无癞子奖励">
          <el-select :model-value="modelValue.no_joker_bonus || 'add8'" aria-label="无癞子奖励" @update:model-value="patch('no_joker_bonus', $event)">
            <el-option value="add8" label="无癞子+8分" />
            <el-option value="double" label="无癞子翻倍" />
            <el-option value="none" label="不额外加分" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="isHuahun" :label="panelLayout ? undefined : '黑花递增计分'">
          <el-tooltip content="开启时1/2/3/4朵黑花合计4/8/16/32番，关闭时每朵1番；花分和无癞子奖励都不计入起和门槛。">
            <el-switch :model-value="modelValue.black_flower_progressive !== false" :active-text="panelLayout ? '黑花递增计分' : ''" aria-label="黑花递增计分" @update:model-value="patch('black_flower_progressive', $event)" />
          </el-tooltip>
        </el-form-item>
        <el-form-item v-if="isHuahun" label="起和门槛"><span>0/1癞子8番，2癞子16番，3癞子32番，4癞子64番</span></el-form-item>
        <el-form-item :label="panelLayout ? undefined : tr('错和')">
          <el-switch :model-value="isLanshi || modelValue.open_cuohe" :disabled="isLanshi" :active-text="panelLayout ? tr('错和') : ''" :aria-label="tr('错和')" @update:model-value="patch('open_cuohe', $event)" />
        </el-form-item>
        <el-form-item v-if="!panelLayout && (isLanshi || modelValue.open_cuohe)" :label="tr('错和形式')" class="gb-room-cuohe-type">
          <el-select :model-value="isLanshi ? 1 : modelValue.cuohe_type" :disabled="isLanshi" style="width: 220px" @update:model-value="patch('cuohe_type', $event)">
            <el-option :value="0" :label="tr('错和-30，其余各+10')" />
            <el-option :value="1" :label="tr('错和-40，其余不加分')" />
          </el-select>
        </el-form-item>
        <el-form-item :label="panelLayout ? undefined : tr('限制游客')">
          <el-switch :model-value="modelValue.tourist_limit" :active-text="panelLayout ? tr('限制游客') : ''" :aria-label="tr('限制游客')" @update:model-value="patch('tourist_limit', $event)" />
        </el-form-item>
        <el-form-item :label="panelLayout ? undefined : tr('允许观战')">
          <el-switch :model-value="autoDuplicate ? false : modelValue.allow_spectator" :disabled="autoDuplicate" :active-text="panelLayout ? tr('允许观战') : ''" :aria-label="tr('允许观战')" @update:model-value="patch('allow_spectator', $event)" />
        </el-form-item>
        <el-form-item :label="panelLayout ? undefined : tr('战术鸣牌')">
          <el-switch :model-value="modelValue.tactical_call" :active-text="panelLayout ? tr('战术鸣牌') : ''" :aria-label="tr('战术鸣牌')" @update:model-value="patch('tactical_call', $event)" />
        </el-form-item>
        <el-form-item :label="panelLayout ? undefined : tr('鸣牌保护')">
          <el-switch :model-value="modelValue.claim_protection" :active-text="panelLayout ? tr('鸣牌保护') : ''" :aria-label="tr('鸣牌保护')" @update:model-value="patch('claim_protection', $event)" />
        </el-form-item>
      </div>
    </template>
    <el-dialog v-model="showHuahunRules" title="国标（花混癞子）规则书" width="min(760px, 92vw)" append-to-body>
      <p v-for="line in HUAHUN_RULE_TEXT" :key="line" class="huahun-rule-line">{{ line }}</p>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import { tr } from '@/i18n'
import DuplicateRoomField from '@/components/DuplicateRoomField.vue'
import { HUAHUN_DESCRIPTION, HUAHUN_RULE_TEXT } from '@/utils/guobiaoHuahun.js'
import { supportsGuobiaoOpeningWins, updateGuobiaoRoomConfig } from '@/utils/guobiaoRoomConfig.js'

const showHuahunRules = ref(false)

const props = defineProps({
  modelValue: { type: Object, required: true },
  showPassword: { type: Boolean, default: true },
  autoDuplicate: { type: Boolean, default: false },
  showDuplicateField: { type: Boolean, default: true },
  panelLayout: { type: Boolean, default: false },
  subRuleOptions: { type: Array, default: null },
})
defineEmits(['update:modelValue'])

// Keep keyboard navigation in the same order as each layout's visible sections.
const sectionOrder = computed(() => props.panelLayout
  ? ['header', 'options', 'main', 'duplicate']
  : ['header', 'main', 'duplicate', 'options'])

const roundTimerOptions = [
  { value: 0, label: '0（不限时）' },
  { value: 5, label: '5' },
  { value: 10, label: '10' },
  { value: 20, label: '20（标准）' },
  { value: 40, label: '40' },
  { value: 60, label: '60' },
]

const defaultSubRuleOptions = [
  { value: 'guobiao/sixteen', label: '国标麻将（十六张）' },
  { value: 'guobiao/standard', label: '国标标准' },
  { value: 'guobiao/xiaolin', label: '小林' },
  { value: 'guobiao/kshen', label: 'K神' },
  { value: 'guobiao/lanshi', label: '蓝十' },
  { value: 'guobiao/transparent', label: '透明国标' },
  { value: 'guobiao/transparent_realtime', label: '国标透明（实时手牌）' },
  { value: 'guobiao/huahun', label: '国标（花混癞子）' },
  { value: 'guobiao/four_jokers', label: '国标麻将(四癞子)' },
]

const resolvedSubRuleOptions = computed(() =>
  props.subRuleOptions?.length ? props.subRuleOptions : defaultSubRuleOptions,
)

const isHuahun = computed(() => props.modelValue.sub_rule === 'guobiao/huahun')
const isLanshi = computed(() => props.modelValue.sub_rule === 'guobiao/lanshi')

const supportsOpeningWins = computed(() => supportsGuobiaoOpeningWins(props.modelValue.sub_rule))

function patch(key, value) {
  updateGuobiaoRoomConfig(props.modelValue, key, value)
}
</script>

<style scoped>
.huahun-description {
  grid-column: 1 / -1;
  line-height: 1.7;
  margin-bottom: 12px;
}
.huahun-description p { margin: 0 0 10px; }
.huahun-rule-line { line-height: 1.8; margin: 0 0 16px; }
.gb-room-config {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 16px;
  width: 100%;
}
.flower-help { font-size: 12px; color: #737d88; margin-left: 6px; }
.gb-room-config-header,
.gb-room-config-main,
.gb-room-config-options {
  display: contents;
}
.gb-room-config :deep(.el-form-item) {
  margin-bottom: 12px;
}
.gb-room-config :deep(.el-select),
.gb-room-config :deep(.el-input),
.gb-room-config :deep(.el-input-number) {
  width: 100%;
}
.gb-room-config--panel {
  display: block;
}
.gb-room-config--panel .gb-room-config-header {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 2fr);
  gap: 0 12px;
  margin-bottom: 6px;
}
.gb-room-section-title {
  grid-column: 1 / -1;
  margin: 0 0 8px;
  color: #303133;
  font-size: 13px;
  line-height: 1.5;
  font-weight: 500;
}
.gb-room-config--panel .gb-room-config-main {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  column-gap: 12px;
  padding-top: 16px;
  border-top: 1px solid #ebeef5;
}
.gb-room-config--panel .gb-room-config-options {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0 24px;
  margin-bottom: 16px;
}
.gb-room-config--panel .gb-room-config-options :deep(.el-form-item) {
  display: flex;
  align-items: center;
  min-width: 0;
  min-height: 40px;
  margin-bottom: 0;
}
.gb-room-config--panel .gb-room-config-options :deep(.el-switch__label) {
  height: auto;
  margin-left: 8px;
  color: #606266;
  line-height: 20px;
}
.gb-room-config--panel .gb-room-config-options :deep(.el-form-item__content) {
  min-width: 0;
  line-height: 32px;
}
.gb-room-config--panel .gb-room-flowers :deep(.el-form-item__content) {
  line-height: 20px;
}
.gb-room-config--panel .gb-room-flowers :deep(.el-switch) { flex-shrink: 0; }
.gb-room-config--panel .gb-room-config-main .gb-room-cuohe-type {
  grid-column: span 2;
}
@media (max-width: 640px) {
  .gb-room-config {
    grid-template-columns: 1fr;
  }
}
@container (max-width: 620px) {
  .gb-room-config--panel .gb-room-config-header,
  .gb-room-config--panel .gb-room-config-main {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    column-gap: 12px;
  }
  .gb-room-config--panel .gb-room-config-options {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    column-gap: 16px;
  }
  .gb-room-config--panel .gb-room-config-options :deep(.el-switch__label) {
    font-size: 13px;
  }
  .gb-room-config--panel .gb-room-config-main .gb-room-cuohe-type {
    grid-column: 1 / -1;
  }
}

</style>
