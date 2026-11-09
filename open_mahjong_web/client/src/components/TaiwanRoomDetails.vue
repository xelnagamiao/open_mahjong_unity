<template>
  <el-collapse class="taiwan-room-details">
    <el-collapse-item v-for="section in sections" :key="section.label" :title="section.label" :name="section.label">
      <div class="detail-grid"><el-form-item v-for="option in section.options" :key="option.key" :label="option.label"><el-select :model-value="form.taiwan_details[option.key] === null ? 'unlimited' : form.taiwan_details[option.key]" :aria-label="option.label" @update:model-value="value => form.taiwan_details[option.key] = value === 'unlimited' ? null : value"><el-option v-for="(value, index) in option.values" :key="String(value)" :value="value === null ? 'unlimited' : value" :label="option.choices[index]" /></el-select></el-form-item></div>
    </el-collapse-item>
    <el-collapse-item title="自定义台表" name="fan-table">
      <el-button @click="form.taiwan_fan_overrides = {}">恢复当前台表预设</el-button>
      <div class="detail-grid"><el-form-item v-for="fan in catalog.fans" :key="fan.key" :label="fan.label"><el-input-number :model-value="form.taiwan_fan_overrides[fan.key] ?? preset[fan.key]" :min="1" :max="64" :precision="0" @update:model-value="value => form.taiwan_fan_overrides[fan.key] = value" /></el-form-item></div>
    </el-collapse-item>
  </el-collapse>
</template>
<script setup>
import { computed } from 'vue'
import catalog from '../../../shared/taiwanRoomOptions.json'
const props = defineProps({ form: { type: Object, required: true } })
const sections = [...new Set(catalog.options.map(o => o.section))].map(label => ({ label, options: catalog.options.filter(o => o.section === label) }))
const preset = computed(() => catalog.fanPresets[props.form.taiwan_details.scoring_preset] || catalog.fanPresets.sml)
</script>
<style scoped>
.detail-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0 16px; }
.detail-grid :deep(.el-select) { width: 100%; }
</style>
