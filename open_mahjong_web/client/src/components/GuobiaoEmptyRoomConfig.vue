<template>
  <div class="gb-room-config" :class="{ 'gb-room-config--panel': panelLayout }">
    <div class="gb-room-config-main">
      <el-form-item label="子规则" class="gb-room-sub-rule">
        <el-select :model-value="modelValue.sub_rule" style="width: 160px" @update:model-value="patch('sub_rule', $event)">
          <el-option
            v-for="opt in resolvedSubRuleOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="圈数">
        <el-select :model-value="modelValue.game_round" style="width: 100px" @update:model-value="patch('game_round', $event)">
          <el-option :value="1" label="东风战" />
          <el-option :value="2" label="东南战" />
          <el-option :value="4" label="全庄战" />
        </el-select>
      </el-form-item>
      <el-form-item label="局时(秒)">
        <el-select
          :model-value="modelValue.round_timer"
          style="width: 140px"
          @update:model-value="patch('round_timer', $event)"
        >
          <el-option
            v-for="opt in roundTimerOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="步时(秒)">
        <el-input-number
          :model-value="modelValue.step_timer"
          :min="0"
          :max="100"
          controls-position="right"
          @update:model-value="patch('step_timer', $event)"
        />
      </el-form-item>
      <el-form-item label="起和番">
        <el-input-number
          :model-value="modelValue.hepai_limit"
          :min="1"
          :max="64"
          controls-position="right"
          @update:model-value="patch('hepai_limit', $event)"
        />
      </el-form-item>
      <el-form-item v-if="showPassword" label="房间密码">
        <el-input
          :model-value="modelValue.password"
          clearable
          style="width: 140px"
          placeholder="可选"
          show-password
          @update:model-value="patch('password', $event)"
        />
      </el-form-item>
    </div>
    <div class="gb-room-config-options">
      <el-form-item label="提示">
        <el-switch :model-value="modelValue.tips" @update:model-value="patch('tips', $event)" />
      </el-form-item>
      <el-form-item label="错和">
        <el-switch :model-value="modelValue.open_cuohe" @update:model-value="patch('open_cuohe', $event)" />
      </el-form-item>
      <el-form-item v-if="modelValue.open_cuohe" label="错和形式" class="gb-room-cuohe-type">
        <el-select :model-value="modelValue.cuohe_type" style="width: 220px" @update:model-value="patch('cuohe_type', $event)">
          <el-option :value="0" label="错和-30，其余各+10" />
          <el-option :value="1" label="错和-40，其余不加分" />
        </el-select>
      </el-form-item>
      <el-form-item label="限制游客">
        <el-switch :model-value="modelValue.tourist_limit" @update:model-value="patch('tourist_limit', $event)" />
      </el-form-item>
      <el-form-item label="允许观战">
        <el-switch :model-value="modelValue.allow_spectator" @update:model-value="patch('allow_spectator', $event)" />
      </el-form-item>
      <el-form-item label="战术鸣牌">
        <el-switch :model-value="modelValue.tactical_call" @update:model-value="patch('tactical_call', $event)" />
      </el-form-item>
      <el-form-item label="鸣牌保护">
        <el-switch :model-value="modelValue.claim_protection" @update:model-value="patch('claim_protection', $event)" />
      </el-form-item>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  modelValue: { type: Object, required: true },
  showPassword: { type: Boolean, default: true },
  panelLayout: { type: Boolean, default: false },
  subRuleOptions: { type: Array, default: null },
})
const emit = defineEmits(['update:modelValue'])

const roundTimerOptions = [
  { value: 0, label: '0（不限时）' },
  { value: 5, label: '5' },
  { value: 10, label: '10' },
  { value: 20, label: '20（标准）' },
  { value: 40, label: '40' },
  { value: 60, label: '60' },
]

const defaultSubRuleOptions = [
  { value: 'guobiao/standard', label: '国标标准' },
  { value: 'guobiao/xiaolin', label: '小林' },
  { value: 'guobiao/kshen', label: 'K神' },
  { value: 'guobiao/lanshi', label: '蓝氏' },
]

const resolvedSubRuleOptions = computed(() =>
  props.subRuleOptions?.length ? props.subRuleOptions : defaultSubRuleOptions,
)

const defaultHepai = {
  'guobiao/standard': 8,
  'guobiao/xiaolin': 1,
  'guobiao/kshen': 8,
  'guobiao/lanshi': 5,
}

function patch(key, value) {
  props.modelValue[key] = value
  if (key === 'sub_rule' && defaultHepai[value] != null) {
    props.modelValue.hepai_limit = defaultHepai[value]
  }
}
</script>

<style scoped>
.gb-room-config {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 16px;
  width: 100%;
}
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
.gb-room-config--panel .gb-room-config-main {
  display: grid;
  grid-template-columns: minmax(144px, 1.2fr) repeat(4, minmax(104px, 1fr));
  column-gap: 16px;
}
.gb-room-config--panel .gb-room-config-main :deep(.el-input-number) {
  max-width: 176px;
}
.gb-room-config--panel .gb-room-config-options {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0 20px;
  margin-top: 2px;
  padding-top: 10px;
  border-top: 1px solid #ebeef5;
}
.gb-room-config--panel .gb-room-config-options :deep(.el-form-item) {
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  max-width: 180px;
  min-height: 40px;
  margin-bottom: 0;
}
.gb-room-config--panel .gb-room-config-options :deep(.el-form-item__label) {
  height: auto;
  padding: 0;
  margin: 0;
  line-height: 20px;
}
.gb-room-config--panel .gb-room-config-options :deep(.el-form-item__content) {
  flex: none;
  min-width: 0;
  line-height: 32px;
}
.gb-room-config--panel .gb-room-config-options .gb-room-cuohe-type {
  order: 1;
  grid-column: 1 / -1;
  max-width: none;
  justify-content: flex-start;
  gap: 16px;
  margin: 4px 0 8px;
}
.gb-room-config--panel .gb-room-cuohe-type :deep(.el-form-item__content) {
  flex: 1;
  max-width: 320px;
}
@media (max-width: 1279px) {
  .gb-room-config--panel .gb-room-config-main {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
  .gb-room-config--panel .gb-room-config-options {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@media (max-width: 640px) {
  .gb-room-config {
    grid-template-columns: 1fr;
  }
  .gb-room-config--panel .gb-room-config-main {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    column-gap: 12px;
  }
  .gb-room-config--panel .gb-room-sub-rule {
    grid-column: 1 / -1;
  }
  .gb-room-config--panel .gb-room-config-options {
    column-gap: 14px;
  }
  .gb-room-config--panel .gb-room-config-options :deep(.el-form-item__label) {
    font-size: 13px;
  }
  .gb-room-config--panel .gb-room-config-options .gb-room-cuohe-type {
    display: block;
  }
  .gb-room-config--panel .gb-room-cuohe-type :deep(.el-form-item__label) {
    margin-bottom: 6px;
  }
}
@media (max-width: 359px) {
  .gb-room-config--panel .gb-room-config-options {
    grid-template-columns: minmax(0, 1fr);
  }
}

</style>
