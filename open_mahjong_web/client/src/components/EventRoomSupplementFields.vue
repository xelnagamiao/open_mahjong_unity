<template>
  <div class="supplement-fields">
    <template v-for="key in keys" :key="key">
      <el-form-item v-if="visible(key)" :label="eventFieldDefinitions[key]?.label || '房间密码'">
        <el-input v-if="key === 'password'" v-model="form.password" show-password clearable />
        <el-select v-else-if="key === 'jinluo_weather'" v-model="form[key]" aria-label="金锣赛天气"><el-option v-for="w in jinluoWeathers" :key="w.value" v-bind="w" /></el-select>
        <el-select v-else-if="key === 'cuohe_type'" v-model="form[key]" aria-label="错和形式"><el-option :value="0" label="错和 -30，其余各 +10" /><el-option :value="1" label="错和 -40，其余不加分" /></el-select>
        <el-input-number v-else-if="eventFieldDefinitions[key].min != null" v-model="form[key]" :min="eventFieldDefinitions[key].min" :max="eventFieldDefinitions[key].max" :precision="0" />
        <el-switch v-else v-model="form[key]" :aria-label="eventFieldDefinitions[key].label" :disabled="key === 'allow_spectator' && keyBound" @change="value => changed(key, value)" />
      </el-form-item>
    </template>
    <el-form-item label="场景复现"><el-switch v-model="form.scene_reproduction" aria-label="场景复现" :disabled="keyBound" /><span v-if="keyBound" class="mode-help">复式房间禁用场景复现与观战。</span></el-form-item>
    <el-form-item v-if="form.scene_reproduction" label="随机种子" class="seed-field"><el-input v-model="form.random_seed" maxlength="66" placeholder="64位十六进制随机种子" aria-label="随机种子" /><span>按随机种子复现，允许观战开关独立生效。</span></el-form-item>
  </div>
  <TaiwanRoomDetails v-if="form.room_rule === 'taiwan'" :form="form" />
</template>
<script setup>
import { computed, watch } from 'vue'
import { duplicateRoomActive, eventFieldDefinitions, jinluoWeathers, supplementFields } from '@/utils/eventRoomExtensions.js'
import TaiwanRoomDetails from '@/components/TaiwanRoomDetails.vue'
const props = defineProps({ form: { type: Object, required: true }, showPassword: { type: Boolean, default: true } })
const keys = computed(() => supplementFields(props.form.room_rule))
const keyBound = computed(() => duplicateRoomActive(props.form))
let spectatorBeforeDuplicate = true
watch(keyBound, (active, previous) => {
  if (active) {
    spectatorBeforeDuplicate = props.form.allow_spectator !== false
    props.form.allow_spectator = false
    props.form.scene_reproduction = false
  } else if (previous) props.form.allow_spectator = spectatorBeforeDuplicate
}, { immediate:true, flush:'sync' })
function visible(key) {
  if (key === 'password') return props.showPassword
  if (key === 'jinluo_mode') return props.form.sub_rule === 'guobiao/standard'
  if (key === 'jinluo_weather') return props.form.sub_rule === 'guobiao/standard' && props.form.jinluo_mode
  if (key === 'cuohe_type') return props.form.open_cuohe
  if (key === 'blood_battle') return props.form.sub_rule === 'sichuan/standard'
  if (key === 'hepai_limit') return !props.form.sub_rule.includes('doubles')
  return true
}
function changed(key, value) {
  if (key === 'jinluo_mode' && value) Object.assign(props.form, { hepai_limit:8, open_cuohe:true, tips:false })
  if (key === 'tips' && value) props.form.open_cuohe = false
  if (key === 'open_cuohe' && value) props.form.tips = false
}
</script>
<style scoped>
.supplement-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 0 16px; }
.seed-field { grid-column: 1 / -1; }
.seed-field span { color: var(--el-text-color-secondary); font-size: 12px; line-height: 1.7; }
.mode-help { margin-left:8px; color:var(--el-text-color-secondary); font-size:12px; }
</style>
