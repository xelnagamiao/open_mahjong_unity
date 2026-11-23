<template>
  <div class="note-editor">
    <el-input v-model="draft" type="textarea" :rows="2" maxlength="2000" show-word-limit
      :aria-label="label" placeholder="仅管理员可见" size="small" :disabled="saving" />
    <el-button link type="primary" size="small" :loading="saving"
      :disabled="draft === (modelValue || '')" @click="save">保存备注</el-button>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const props = defineProps({
  modelValue: String,
  endpoint: { type: String, required: true },
  label: { type: String, required: true },
})
const emit = defineEmits(['update:modelValue'])
const draft = ref(props.modelValue || '')
const saving = ref(false)
watch(() => props.modelValue, value => { draft.value = value || '' })

async function save() {
  if (saving.value) return
  saving.value = true
  try {
    const result = await ElMessageBox.prompt('请输入变更原因', props.label, {
      confirmButtonText: '保存', cancelButtonText: '取消', inputPlaceholder: '用于后台审计',
      inputValidator: value => Boolean(value?.trim()) || '请填写变更原因',
    }).catch(() => null)
    if (!result) return
    const { data } = await adminApi.patch(props.endpoint, { admin_note: draft.value, reason: result.value.trim() })
    emit('update:modelValue', data.data.admin_note)
    ElMessage.success('备注已保存')
  } catch (error) {
    ElMessage.error(error.response?.data?.message || '备注保存失败，请重试')
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.note-editor { display: grid; justify-items: start; gap: 4px; }
.note-editor :deep(.el-input__count) { visibility: hidden; }
.note-editor:focus-within :deep(.el-input__count) { visibility: visible; }
</style>
