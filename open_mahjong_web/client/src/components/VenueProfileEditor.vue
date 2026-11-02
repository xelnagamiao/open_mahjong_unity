<template>
  <div v-loading="loading" class="venue-profile-editor">
    <el-alert
      :title="admin ? '修改后直接生效，请填写操作原因。' : '修改名称或介绍后提交审核，通过后才会在公开页生效。'"
      type="info" :closable="false" show-icon
    />
    <template v-if="loadError">
      <el-alert :title="loadError" type="error" :closable="false" />
      <el-button @click="load">重新加载</el-button>
    </template>
    <template v-else>
      <el-alert v-if="pending" title="资料修改申请待审核" type="warning" :closable="false" show-icon>
        下方为已提交的内容，如需继续修改，请先撤销申请。
      </el-alert>
      <el-alert v-else-if="lastRejected" title="上次资料修改申请已打回" type="error" :closable="false" show-icon>
        {{ lastRejected.review_note || '请修改后重新提交。' }}
      </el-alert>
      <el-form label-position="top" @submit.prevent="submit">
        <el-form-item :label="`${venueNoun}名称`" required>
          <el-input v-model="form.name" maxlength="128" show-word-limit :disabled="formDisabled" />
        </el-form-item>
        <el-form-item :label="`${venueNoun}介绍`">
          <el-input v-model="form.description" type="textarea" :rows="6" maxlength="2000" show-word-limit :disabled="formDisabled" />
        </el-form-item>
        <el-form-item :label="admin ? '操作原因' : '修改说明'" :required="admin">
          <el-input v-model="form.reason" type="textarea" :rows="2" maxlength="2000" show-word-limit
            :placeholder="admin ? '审计必填' : '可选，供审核参考'" :disabled="formDisabled" />
        </el-form-item>
        <el-form-item class="profile-actions">
          <el-button type="primary" :loading="saving" :disabled="loading || cancelling || !!pending" @click="submit">
            {{ admin ? '保存修改' : lastRejected ? '修改并重新提交' : '提交修改申请' }}
          </el-button>
          <el-button v-if="pending" :loading="cancelling" :disabled="loading || saving" @click="cancel">撤销申请</el-button>
        </el-form-item>
      </el-form>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'
import eventAdminApi from '@/api/eventAdminClient'
import { venueKindLabel } from '@/utils/eventMeta'

const props = defineProps({
  eventId: { type: String, required: true },
  kind: { type: String, default: 'event' },
  admin: { type: Boolean, default: false },
})
const emit = defineEmits(['updated'])
const venueNoun = computed(() => venueKindLabel(props.kind))
const form = reactive({ name: '', description: '', reason: '' })
const current = reactive({ name: '', description: '' })
const pending = ref(null)
const lastRejected = ref(null)
const loading = ref(true)
const saving = ref(false)
const cancelling = ref(false)
const loadError = ref('')
const formDisabled = computed(() => loading.value || saving.value || cancelling.value || !!pending.value)

async function load() {
  loading.value = true
  loadError.value = ''
  try {
    const res = props.admin
      ? await adminApi.get(`/events/${props.eventId}`)
      : await eventAdminApi.get(`/events/${props.eventId}/profile-change`)
    const data = res.data.data
    current.name = (props.admin ? data.name : data.current_name) || ''
    current.description = (props.admin ? data.description : data.current_description) || ''
    pending.value = props.admin ? null : data.pending || null
    lastRejected.value = props.admin ? null : data.last_rejected || null
    const draft = pending.value || lastRejected.value
    form.name = draft?.proposed_name ?? current.name
    form.description = draft?.proposed_description ?? current.description
    form.reason = draft?.reason || ''
  } catch (e) {
    loadError.value = e.response?.data?.message || '加载资料失败'
  } finally {
    loading.value = false
  }
}

async function submit() {
  if (formDisabled.value || loadError.value) return
  const payload = { name: form.name.trim(), description: form.description.trim(), reason: form.reason.trim() }
  if (!payload.name) return ElMessage.warning(`请填写${venueNoun.value}名称`)
  if (props.admin && !payload.reason) return ElMessage.warning('请填写操作原因')
  if (payload.name === current.name.trim() && payload.description === current.description.trim()) {
    return ElMessage.warning('名称与介绍均未变更')
  }
  saving.value = true
  try {
    const res = props.admin
      ? await adminApi.put(`/events/${props.eventId}/profile`, payload)
      : await eventAdminApi.post(`/events/${props.eventId}/profile-change`, payload)
    if (props.admin) {
      current.name = res.data.data.event.name
      current.description = res.data.data.event.description || ''
    } else {
      pending.value = res.data.data.pending
      lastRejected.value = null
    }
    ElMessage.success(res.data.message || (props.admin ? '资料已更新' : '已提交审核'))
    emit('updated', res.data.data)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '提交失败')
  } finally {
    saving.value = false
  }
}

async function cancel() {
  if (cancelling.value || !pending.value) return
  cancelling.value = true
  try {
    await eventAdminApi.post(`/events/${props.eventId}/profile-change/cancel`)
    pending.value = null
    lastRejected.value = null
    ElMessage.success('已撤销申请，可继续修改后提交')
    emit('updated', { pending: null })
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '撤销失败')
  } finally {
    cancelling.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.venue-profile-editor { min-width: 0; }
.venue-profile-editor > .el-alert { margin-bottom: 16px; }
.profile-actions { margin-bottom: 0; }
.profile-actions :deep(.el-form-item__content) { gap: 8px; }
.profile-actions :deep(.el-button + .el-button) { margin-left: 0; }
</style>
