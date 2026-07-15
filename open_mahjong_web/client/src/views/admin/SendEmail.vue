<template>
  <div>
    <h2 class="page-title">发送邮件</h2>
    <el-alert
      v-if="statusLoaded && !smtpEnabled"
      class="hint"
      type="error"
      :closable="false"
      title="邮件服务未配置，无法发送。"
    />
    <el-alert
      v-else-if="statusLoaded && smtpEnabled"
      class="hint"
      type="info"
      :closable="false"
      :title="`发件人：${fromLabel}`"
    />

    <el-card class="form-card">
      <el-form label-width="90px" @submit.prevent>
        <el-form-item label="邮件模板">
          <el-select
            v-model="templateId"
            placeholder="选择模板（可选）"
            clearable
            style="width: 100%"
            @change="onTemplateChange"
          >
            <el-option
              v-for="t in TEMPLATES"
              :key="t.id"
              :label="t.label"
              :value="t.id"
            />
          </el-select>
        </el-form-item>

        <el-form-item v-if="activeTemplate" label="模板变量">
          <div class="vars">
            <el-input
              v-if="activeTemplate.vars.includes('eventId')"
              v-model="vars.eventId"
              placeholder="比赛 ID"
              clearable
            />
            <el-input
              v-if="activeTemplate.vars.includes('eventName')"
              v-model="vars.eventName"
              placeholder="比赛名称"
              clearable
            />
            <el-input
              v-if="activeTemplate.vars.includes('username')"
              v-model="vars.username"
              placeholder="用户名（可选）"
              clearable
            />
            <el-input
              v-if="activeTemplate.vars.includes('reason')"
              v-model="vars.reason"
              type="textarea"
              :rows="2"
              placeholder="驳回/说明原因"
            />
            <el-button type="primary" link @click="applyTemplate">填入主题与正文</el-button>
          </div>
        </el-form-item>

        <el-form-item label="收件方式">
          <el-radio-group v-model="mode">
            <el-radio-button value="email">邮箱地址</el-radio-button>
            <el-radio-button value="user">用户 ID</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <el-form-item v-if="mode === 'email'" label="收件邮箱">
          <el-input
            v-model="form.to"
            placeholder="例如 user@example.com"
            maxlength="255"
            clearable
          />
        </el-form-item>
        <el-form-item v-else label="用户 ID">
          <el-input
            v-model="form.user_id"
            placeholder="需已绑定并验证邮箱"
            clearable
          />
        </el-form-item>

        <el-form-item label="主题">
          <el-input v-model="form.subject" maxlength="200" show-word-limit clearable />
        </el-form-item>
        <el-form-item label="正文">
          <el-input
            v-model="form.body"
            type="textarea"
            :rows="12"
            maxlength="10000"
            show-word-limit
            placeholder="支持换行，将以纯文本 + 简单 HTML 发送"
          />
        </el-form-item>
        <el-form-item>
          <el-button
            type="primary"
            :loading="loading"
            :disabled="!smtpEnabled"
            @click="send"
          >
            发送邮件
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const CONTACT_FOOTER = [
  '',
  '如有疑问请勿回复本邮箱，请联系 salasasa 开发者 QQ：1448826180。',
  '',
  '— salasasa.cn',
].join('\n')

const TEMPLATES = [
  {
    id: 'event_approved',
    label: '办赛申请已通过',
    vars: ['eventId', 'eventName', 'username'],
    subject: '【salasasa】您的比赛已通过审核',
    body: ({ eventId, eventName, username }) => {
      const nameLine = eventName ? `（${eventName}）` : ''
      const hello = username ? `${username} 您好，` : '您好，'
      return [
        hello,
        '',
        `您的比赛 ID ${eventId || 'xxxx'}${nameLine} 已通过审核。`,
        '您可登录网站赛事管理后台查看并继续配置比赛信息。',
        CONTACT_FOOTER,
      ].join('\n')
    },
  },
  {
    id: 'event_rejected',
    label: '办赛申请未通过',
    vars: ['eventId', 'eventName', 'username', 'reason'],
    subject: '【salasasa】您的办赛申请未通过',
    body: ({ eventId, eventName, username, reason }) => {
      const nameLine = eventName ? `（${eventName}）` : ''
      const hello = username ? `${username} 您好，` : '您好，'
      const idPart = eventId ? `比赛 ID ${eventId}${nameLine}` : `您的办赛申请${nameLine}`
      return [
        hello,
        '',
        `${idPart} 未能通过审核。`,
        reason ? `原因：${reason}` : '原因：暂无补充说明。',
        '您可根据说明修改后重新提交申请。',
        CONTACT_FOOTER,
      ].join('\n')
    },
  },
  {
    id: 'event_notice',
    label: '比赛通用通知',
    vars: ['eventId', 'eventName', 'username'],
    subject: '【salasasa】比赛通知',
    body: ({ eventId, eventName, username }) => {
      const hello = username ? `${username} 您好，` : '您好，'
      const about = eventId
        ? `关于比赛 ID ${eventId}${eventName ? `（${eventName}）` : ''}：`
        : eventName
          ? `关于比赛「${eventName}」：`
          : '关于您的比赛：'
      return [
        hello,
        '',
        about,
        '（请在此处补充通知内容）',
        CONTACT_FOOTER,
      ].join('\n')
    },
  },
  {
    id: 'general_notice',
    label: '通用通知',
    vars: ['username'],
    subject: '【salasasa】通知',
    body: ({ username }) => {
      const hello = username ? `${username} 您好，` : '您好，'
      return [
        hello,
        '',
        '（请在此处补充通知内容）',
        CONTACT_FOOTER,
      ].join('\n')
    },
  },
]

const mode = ref('email')
const templateId = ref('')
const form = reactive({
  to: '',
  user_id: '',
  subject: '',
  body: '',
})
const vars = reactive({
  eventId: '',
  eventName: '',
  username: '',
  reason: '',
})

const loading = ref(false)
const statusLoaded = ref(false)
const smtpEnabled = ref(false)
const fromEmail = ref('')
const fromName = ref('')

const activeTemplate = computed(() => TEMPLATES.find((t) => t.id === templateId.value) || null)

const fromLabel = computed(() => {
  if (fromName.value && fromEmail.value) return `${fromName.value} <${fromEmail.value}>`
  return fromEmail.value || '—'
})

function onTemplateChange() {
  if (!templateId.value) return
  applyTemplate()
}

function applyTemplate() {
  const t = activeTemplate.value
  if (!t) {
    ElMessage.warning('请先选择模板')
    return
  }
  form.subject = t.subject
  form.body = t.body({
    eventId: vars.eventId.trim(),
    eventName: vars.eventName.trim(),
    username: vars.username.trim(),
    reason: vars.reason.trim(),
  })
  ElMessage.success('已填入模板内容，可继续修改后发送')
}

async function loadStatus() {
  try {
    const res = await adminApi.get('/mail/status')
    const data = res.data?.data || {}
    smtpEnabled.value = !!data.enabled
    fromEmail.value = data.fromEmail || ''
    fromName.value = data.fromName || ''
  } catch {
    smtpEnabled.value = false
  } finally {
    statusLoaded.value = true
  }
}

function escapeHtml(s) {
  return String(s).replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

async function confirmSend(summary) {
  try {
    await ElMessageBox.confirm(summary, '确认发送邮件', {
      confirmButtonText: '确认发送',
      cancelButtonText: '取消',
      type: 'warning',
      dangerouslyUseHTMLString: true,
    })
    return true
  } catch {
    return false
  }
}

async function send() {
  const subject = form.subject.trim()
  const body = form.body.trim()
  if (!subject) {
    ElMessage.warning('请输入邮件主题')
    return
  }
  if (!body) {
    ElMessage.warning('请输入邮件正文')
    return
  }

  const payload = { subject, body }
  let targetLabel = ''

  if (mode.value === 'email') {
    const to = form.to.trim()
    if (!to) {
      ElMessage.warning('请填写收件邮箱')
      return
    }
    payload.to = to
    targetLabel = escapeHtml(to)
  } else {
    const userId = parseInt(form.user_id, 10)
    if (Number.isNaN(userId) || userId <= 0) {
      ElMessage.warning('请输入有效的用户 ID')
      return
    }
    payload.user_id = userId
    targetLabel = `用户 ID ${userId}（其已验证邮箱）`
  }

  const confirmed = await confirmSend(
    `<p><strong>收件对象：</strong>${targetLabel}</p>
<p><strong>主题：</strong>${escapeHtml(subject)}</p>
<p><strong>正文：</strong></p>
<p style="white-space: pre-wrap; margin: 0;">${escapeHtml(body)}</p>`
  )
  if (!confirmed) return

  loading.value = true
  try {
    const res = await adminApi.post('/mail/send', payload)
    ElMessage.success(res.data.message || '邮件已发送')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '邮件发送失败')
  } finally {
    loading.value = false
  }
}

onMounted(loadStatus)
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.hint {
  margin-bottom: 16px;
}
.form-card {
  max-width: 720px;
}
.vars {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}
</style>
