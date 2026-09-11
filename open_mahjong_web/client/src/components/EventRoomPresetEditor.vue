<template>
  <div class="event-room-preset-editor" data-testid="room-preset-editor">
    <aside class="preset-list" aria-label="对局设置列表">
      <button type="button" class="preset-item" :class="{ active: selectedKey === 'default' }" @click="selectEntry('default')">默认配置</button>
      <button
        v-for="preset in settingsDoc.presets || []"
        :key="preset.preset_id"
        type="button"
        class="preset-item"
        :class="{ active: selectedKey === preset.preset_id }"
        @click="selectEntry(preset.preset_id)"
      >{{ preset.name }}</button>
      <el-button class="new-preset-button" text type="primary" :disabled="busy || !loaded" @click="selectEntry('new')">＋ 新建预设</el-button>
    </aside>
    <el-form label-position="top" class="preset-form" :disabled="busy || !loaded" @submit.prevent="save">
      <div v-if="selectedKey !== 'default' || dirty" class="preset-form-heading">
        <strong v-if="selectedKey !== 'default'">{{ selectedKey === 'new' ? '新建预设' : '编辑预设' }}</strong>
        <span v-if="dirty" class="preset-unsaved">有未保存的修改</span>
      </div>
      <p class="preset-help">{{ selectedKey === 'default' ? '用于手动组桌和自动匹配。' : '保存后可在组桌和自动匹配中选择。' }}修改仅影响后续新桌。</p>
      <el-alert v-if="remoteChanged || conflict" title="已保存的设置发生更新，当前草稿仍保留。请核对后再保存，或取消修改以载入最新设置。" type="warning" :closable="false" class="preset-notice" />
      <el-alert v-if="sourceMissing" title="该预设已被删除，草稿仍保留。请选择其他配置，或复制内容后新建预设。" type="warning" :closable="false" class="preset-notice" />
      <div class="preset-top-fields">
        <el-form-item v-if="selectedKey !== 'default'" label="预设名称" required class="preset-name-field">
          <el-input v-model="draftName" maxlength="40" show-word-limit placeholder="例如：小组赛 · 东南战" />
        </el-form-item>
        <el-form-item label="规则">
          <el-select v-model="form.room_rule">
            <el-option v-for="rule in ruleOptions" :key="rule.value" :label="rule.label" :value="rule.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="房间名" class="preset-room-name-field">
          <el-input v-model="form.room_name" clearable placeholder="可选，留空自动命名" />
        </el-form-item>
      </div>
      <GuobiaoEmptyRoomConfig v-if="form.room_rule === 'guobiao'" :model-value="form" :show-password="false" panel-layout />
      <el-alert v-else title="当前仅国标提供完整对局配置，其他规则使用服务端默认参数。" type="info" :closable="false" class="preset-notice" />
      <div class="preset-form-actions">
        <el-button type="primary" :loading="busy" :disabled="!loaded || sourceMissing || (!dirty && selectedKey !== 'new')" @click="save">保存配置</el-button>
        <el-button :disabled="busy" @click="cancel">取消修改</el-button>
        <el-button v-if="selectedKey !== 'default' && selectedKey !== 'new'" type="danger" plain :disabled="busy || usedByAutoMatch" @click="remove">删除预设</el-button>
        <span v-if="usedByAutoMatch" class="preset-help">自动匹配已选择此预设，请先切换匹配预设再删除。</span>
      </div>
    </el-form>
  </div>
</template>

<script setup>
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'
import { buildEventRoomSettings, createEventRoomForm } from '@/utils/eventRoomSettings'

const props = defineProps({
  settingsDoc: { type: Object, required: true },
  loaded: Boolean,
  busy: Boolean,
  ruleOptions: { type: Array, required: true },
  saveChange: { type: Function, required: true },
  removePreset: { type: Function, required: true },
})
const selectedKey = ref('default')
const draftName = ref('')
const form = reactive(createEventRoomForm())
const initialSignature = ref('')
const draftRevision = ref(0)
const conflict = ref(false)
const signature = computed(() => JSON.stringify({ name: draftName.value, form }))
const dirty = computed(() => signature.value !== initialSignature.value)
const namedSource = computed(() => (props.settingsDoc.presets || []).find(item => item.preset_id === selectedKey.value))
const sourceMissing = computed(() => selectedKey.value !== 'default' && selectedKey.value !== 'new' && !namedSource.value)
const remoteChanged = computed(() => dirty.value && props.settingsDoc.revision !== draftRevision.value)
const usedByAutoMatch = computed(() => selectedKey.value !== 'default' && selectedKey.value !== 'new' && props.settingsDoc.auto_match?.preset_id === selectedKey.value)

function resetDraft(key = selectedKey.value) {
  selectedKey.value = key
  const source = key === 'default' || key === 'new' ? props.settingsDoc.manual : namedSource.value
  Object.assign(form, createEventRoomForm(source || {}))
  draftName.value = key === 'default' || key === 'new' ? '' : source?.name || ''
  draftRevision.value = props.settingsDoc.revision
  conflict.value = false
  initialSignature.value = signature.value
}

async function canDiscard() {
  if (props.busy) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm('当前配置有未保存的修改，是否放弃这些修改？', '未保存的修改', { type: 'warning', confirmButtonText: '放弃修改', cancelButtonText: '继续编辑' })
    return true
  } catch (_) { return false }
}

async function selectEntry(key) {
  if (!props.loaded || props.busy || key === selectedKey.value) return
  if (await canDiscard()) resetDraft(key)
}

async function cancel() {
  if (await canDiscard()) resetDraft(sourceMissing.value || selectedKey.value === 'new' ? 'default' : selectedKey.value)
}

async function save() {
  if (props.busy || !props.loaded || sourceMissing.value) return
  const name = draftName.value.trim()
  if (selectedKey.value !== 'default' && !name) { ElMessage.warning('请填写预设名称'); return }
  const { room_rule, room_config } = buildEventRoomSettings(form)
  const result = await props.saveChange({
    kind: selectedKey.value === 'default' ? 'default' : selectedKey.value === 'new' ? 'create' : 'update',
    presetId: selectedKey.value,
    revision: draftRevision.value,
    name,
    room_rule,
    room_config,
  })
  if (result?.saved) { await nextTick(); resetDraft(result.presetId || selectedKey.value) }
  else if (props.settingsDoc.revision !== draftRevision.value) {
    draftRevision.value = props.settingsDoc.revision
    conflict.value = true
  }
}

async function remove() {
  if (props.busy || usedByAutoMatch.value || !namedSource.value) return
  if (!await canDiscard()) return
  try {
    await ElMessageBox.confirm(`确认删除「${namedSource.value.name}」？`, '删除对局预设', { type: 'warning' })
    if (await props.removePreset(selectedKey.value, draftRevision.value)) resetDraft('default')
  } catch (_) { /* Closing the confirmation keeps the current draft. */ }
}

watch(() => props.settingsDoc, () => {
  if (!initialSignature.value || !dirty.value) resetDraft(sourceMissing.value ? 'default' : selectedKey.value)
}, { immediate: true })

defineExpose({ canDiscard, resetDraft, dirty })
</script>

<style scoped>
.event-room-preset-editor { display: grid; grid-template-columns: 170px minmax(0, 1fr); gap: 20px; padding: 14px 0 18px; border-top: 1px solid #ebeef5; }
.preset-list { display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding-right: 14px; border-right: 1px solid #ebeef5; }
.preset-item { padding: 9px 10px; border: 0; border-radius: 4px; background: transparent; color: #606266; font: inherit; font-size: 13px; line-height: 1.5; text-align: left; overflow-wrap: anywhere; cursor: pointer; }
.preset-item:hover { background: #f5f7fa; }
.preset-item.active { background: #ecf5ff; color: #409eff; }
.new-preset-button { margin: 4px 0 0; justify-content: flex-start; }
.preset-form { min-width: 0; }
.preset-form-heading { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; font-size: 14px; color: #303133; }
.preset-unsaved { color: #b88230; font-size: 12px; }
.preset-help { margin: 0 0 14px; color: #909399; font-size: 12px; line-height: 1.6; }
.preset-form-heading + .preset-help { margin-top: 5px; }
.preset-notice { margin-bottom: 12px; }
.preset-top-fields { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 0 14px; }
.preset-name-field { grid-column: 1 / -1; }
.preset-room-name-field { grid-column: span 2; }
.preset-form :deep(.el-form-item) { margin-bottom: 12px; }
.preset-form :deep(.el-select), .preset-form :deep(.el-input) { width: 100% !important; }
.preset-form-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 16px; padding-top: 14px; border-top: 1px solid #ebeef5; }
.preset-form-actions :deep(.el-button) { margin-left: 0; }
.preset-form-actions .preset-help { margin: 0; }
@media (max-width: 960px) {
  .event-room-preset-editor { grid-template-columns: minmax(0, 1fr); gap: 14px; }
  .preset-list { flex-direction: row; flex-wrap: wrap; padding-right: 0; padding-bottom: 10px; border-right: 0; border-bottom: 1px solid #ebeef5; }
  .preset-item { max-width: 100%; }
  .new-preset-button { margin-top: 0; }
}
@media (max-width: 640px) {
  .preset-top-fields { grid-template-columns: minmax(0, 1fr); }
  .preset-room-name-field { grid-column: auto; }
}
</style>
