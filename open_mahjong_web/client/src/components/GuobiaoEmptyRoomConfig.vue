<template>
  <div class="gb-room-config">
    <el-form-item label="子规则">
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
    <el-form-item label="局时(分)">
      <el-input-number
        :model-value="modelValue.round_timer"
        :min="0"
        :max="120"
        controls-position="right"
        @update:model-value="patch('round_timer', $event)"
      />
    </el-form-item>
    <el-form-item label="步时(秒)">
      <el-input-number
        :model-value="modelValue.step_timer"
        :min="0"
        :max="120"
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
    <el-form-item label="房间密码">
      <el-input
        :model-value="modelValue.password"
        clearable
        style="width: 140px"
        placeholder="可选"
        show-password
        @update:model-value="patch('password', $event)"
      />
    </el-form-item>
    <el-form-item label="提示">
      <el-switch :model-value="modelValue.tips" @update:model-value="patch('tips', $event)" />
    </el-form-item>
    <el-form-item label="错和">
      <el-switch :model-value="modelValue.open_cuohe" @update:model-value="patch('open_cuohe', $event)" />
    </el-form-item>
    <el-form-item v-if="modelValue.open_cuohe" label="错和形式">
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
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  modelValue: { type: Object, required: true },
  subRuleOptions: { type: Array, default: null },
})
const emit = defineEmits(['update:modelValue'])

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
  display: flex;
  flex-wrap: wrap;
  gap: 0 8px;
  width: 100%;
  margin-bottom: 4px;
}
</style>
