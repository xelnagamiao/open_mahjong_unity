<template>
  <div v-loading="!auth.loaded" class="account-page">
    <el-empty v-if="auth.loaded && !auth.isLoggedIn" description="您尚未登录">
      <el-button type="primary" @click="$router.push('/login?redirect=/account')">去登录</el-button>
    </el-empty>

    <template v-else-if="auth.isLoggedIn">
      <el-card v-show="activeSection === 'sec-account'" id="sec-account" class="block section" data-sec="sec-account">
        <template #header>账户信息</template>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="用户名">{{ auth.username }}</el-descriptions-item>
          <el-descriptions-item label="用户 ID">{{ auth.userId }}</el-descriptions-item>
          <el-descriptions-item label="邮箱" :span="2">
            <template v-if="auth.emailVerified">
              <el-tag type="success" size="small">已绑定</el-tag>
              <span class="email-text">{{ auth.email }}</span>
            </template>
            <el-tag v-else type="info" size="small">未绑定</el-tag>
          </el-descriptions-item>
        </el-descriptions>
        <div class="actions">
          <el-button
            type="primary"
            @click="$router.push(`/player-data?q=${encodeURIComponent(auth.username)}`)"
          >我的战绩</el-button>
          <el-button @click="onLogout">退出登录</el-button>
        </div>

        <el-divider content-position="left">绑定邮箱</el-divider>
        <el-form label-width="88px" class="bind-form" @submit.prevent>
          <el-form-item label="邮箱">
            <el-input
              v-model="emailForm.email"
              clearable
              placeholder="name@example.com"
              style="max-width: 320px"
            />
          </el-form-item>
          <el-form-item label="验证码">
            <div class="code-row">
              <el-input
                v-model="emailForm.code"
                clearable
                maxlength="6"
                placeholder="6 位验证码"
                style="width: 160px"
              />
              <el-button :loading="emailSending" :disabled="sendCooldown > 0" @click="sendEmailCode">
                {{ sendCooldown > 0 ? `${sendCooldown}s` : '发送验证码' }}
              </el-button>
              <el-button type="primary" :loading="emailVerifying" @click="verifyEmail">
                确认绑定
              </el-button>
              <el-button
                v-if="auth.emailVerified"
                type="danger"
                plain
                :loading="emailUnbinding"
                @click="unbindEmail"
              >解除绑定</el-button>
            </div>
          </el-form-item>
        </el-form>

        <el-divider content-position="left">修改密码</el-divider>
        <el-form label-width="88px" style="max-width: 420px" @submit.prevent="onChangePassword">
          <el-form-item label="旧密码">
            <el-input v-model="pwd.old" type="password" show-password autocomplete="current-password" />
          </el-form-item>
          <el-form-item label="新密码">
            <el-input v-model="pwd.next" type="password" show-password autocomplete="new-password" />
          </el-form-item>
          <el-form-item label="确认密码">
            <el-input v-model="pwd.confirm" type="password" show-password autocomplete="new-password" />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="pwdLoading" @click="onChangePassword">更新密码</el-button>
          </el-form-item>
        </el-form>
      </el-card>

      <el-card v-show="activeSection === 'sec-apply'" id="sec-apply" class="block section" data-sec="sec-apply">
        <template #header>提交办赛申请</template>
        <el-form label-position="top" class="apply-form" @submit.prevent="submitApplication">
          <el-form-item label="赛事名称" required>
            <el-input v-model="applyForm.name" maxlength="128" show-word-limit />
          </el-form-item>

          <el-form-item label="拟定开始时间 / 拟定结束时间" required>
            <div class="date-row">
              <el-date-picker
                v-model="applyForm.planned_start_at"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="拟定开始时间"
                class="date-picker"
              />
              <span class="date-sep">至</span>
              <el-date-picker
                v-model="applyForm.planned_end_at"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="拟定结束时间（可选）"
                class="date-picker"
                clearable
              />
            </div>
            <p class="field-note">
              拟定开始时间和结束时间只是申请的开启赛事和关闭赛事的大致时间范围，赛事的开启与关闭将由比赛管理员自行决定；如果是长期的月赛或者季度赛，可以不设截止时间或连续申报比赛，在确定拟定日期以后也可以随时进行更改。
            </p>
          </el-form-item>

          <el-form-item label="赛事介绍" required>
            <el-input
              v-model="applyForm.description"
              type="textarea"
              :rows="5"
              maxlength="2000"
              show-word-limit
            />
            <p class="field-note">
              赛事介绍中必须包含明确的报名联系方式，对于实际赛程这里可以不予规定、即使予以规定，后期也可以进行更改。但是，如果赛事在实际赛程中规定了某些赛事规则或者奖励方式但未达成，或者在临时更改赛制以后出现了争议或争端，平台也会一定程度上对赛事进行一些介入监管；包括且不限于，批评、取消办赛资格、封禁个别账户等惩罚，请务必注意这一点。
            </p>
          </el-form-item>

          <el-form-item label="备注">
            <el-input
              v-model="applyForm.remark"
              type="textarea"
              :rows="3"
              maxlength="1000"
              show-word-limit
            />
            <p class="field-note">
              给予管理员的审核意见，或者不希望在赛事介绍中展示、但是需要预先告知的特殊声明。
            </p>
          </el-form-item>

          <el-form-item>
            <el-button type="primary" :loading="applyLoading" @click="submitApplication">
              提交办赛申请
            </el-button>
          </el-form-item>
        </el-form>
      </el-card>

      <el-card v-show="activeSection === 'sec-manage'" id="sec-manage" class="block section" data-sec="sec-manage">
        <template #header>赛事管理</template>
        <p class="hint">查看办赛申请与已注册赛事；点击「管理赛事」在下方展开管理面板。</p>

        <el-divider content-position="left">赛事申请</el-divider>
        <div class="fit-table-wrap">
          <el-table :data="myApplications" size="small" empty-text="暂无申请记录" class="fit-table">
            <el-table-column prop="name" label="赛事名称" min-width="120" />
            <el-table-column label="拟定时间" min-width="150">
              <template #default="{ row }">{{ formatPlannedRange(row) }}</template>
            </el-table-column>
            <el-table-column label="介绍" min-width="160" show-overflow-tooltip>
              <template #default="{ row }">{{ row.description || row.reason || '—' }}</template>
            </el-table-column>
            <el-table-column label="备注" min-width="100" show-overflow-tooltip>
              <template #default="{ row }">{{ row.remark || '—' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="appStatusType(row.status)" size="small">{{ appStatusLabel(row.status) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="提交时间" width="160">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="100">
              <template #default="{ row }">
                <el-button
                  v-if="row.event_id"
                  link
                  type="primary"
                  @click="$router.push(`/events/${row.event_id}`)"
                >公开页</el-button>
                <span v-else class="muted">—</span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <el-divider content-position="left">已注册赛事</el-divider>
        <div class="fit-table-wrap">
          <el-table
            v-loading="eventsLoading"
            :data="myEvents"
            size="small"
            empty-text="暂无管理中的赛事"
            class="fit-table"
          >
            <el-table-column prop="name" label="赛事名称" min-width="140" />
            <el-table-column label="介绍" min-width="160" show-overflow-tooltip>
              <template #default="{ row }">{{ row.description || '—' }}</template>
            </el-table-column>
            <el-table-column label="角色" width="120">
              <template #default="{ row }">{{ eventRoleLabel(row.role) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="110">
              <template #default="{ row }">
                <el-tag :type="eventStatusTagType(row.status)" size="small">
                  {{ eventStatusLabel(row.status) }}
                </el-tag>
                <el-tag v-if="row.reopen_requested" type="warning" size="small" style="margin-left: 4px">
                  待再开
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="牌谱" width="70">
              <template #default="{ row }">{{ row.record_count || 0 }}</template>
            </el-table-column>
            <el-table-column label="操作" width="180">
              <template #default="{ row }">
                <el-button
                  link
                  :type="managingEventId === row.event_id ? 'warning' : 'danger'"
                  @click="toggleManage(row.event_id)"
                >{{ managingEventId === row.event_id ? '收起' : '管理赛事' }}</el-button>
                <el-button link type="primary" @click="$router.push(`/events/${row.event_id}`)">赛事页面</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <EventManagePanel
          v-if="managingEventId"
          :event-id="managingEventId"
          @close="managingEventId = null"
          @updated="loadMyEvents"
        />
      </el-card>
    </template>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import playerApi from '@/api/playerClient'
import EventManagePanel from '@/components/EventManagePanel.vue'
import { eventRoleLabel, eventStatusLabel, eventStatusTagType } from '@/utils/eventMeta'

const auth = usePlayerAuthStore()
const eventAuth = useEventAdminAuthStore()
const router = useRouter()
const route = useRoute()

const sectionIds = ['sec-account', 'sec-apply', 'sec-manage']
let cooldownTimer = null

const activeSection = computed(() => {
  const hash = (route.hash || '').replace(/^#/, '')
  if (sectionIds.includes(hash)) return hash
  return 'sec-account'
})

const pwd = reactive({ old: '', next: '', confirm: '' })
const pwdLoading = ref(false)

const emailForm = reactive({ email: '', code: '' })
const emailSending = ref(false)
const emailVerifying = ref(false)
const emailUnbinding = ref(false)
const sendCooldown = ref(0)

const applyForm = reactive({
  name: '',
  planned_start_at: null,
  planned_end_at: null,
  description: '',
  remark: '',
})
const applyLoading = ref(false)
const myApplications = ref([])
const myEvents = ref([])
const eventsLoading = ref(false)
const managingEventId = ref(null)

function toggleManage(eventId) {
  managingEventId.value = managingEventId.value === eventId ? null : eventId
}

function appStatusLabel(s) {
  return ({ pending: '待审', approved: '已通过', rejected: '已拒绝', cancelled: '已取消' })[s] || s
}

function appStatusType(s) {
  return ({ pending: 'warning', approved: 'success', rejected: 'danger', cancelled: 'info' })[s] || 'info'
}

function formatDate(v) {
  if (!v) return ''
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  return d.toLocaleString('zh-CN', { hour12: false })
}

function formatDay(v) {
  if (!v) return ''
  const s = String(v)
  if (/^\d{4}-\d{2}-\d{2}/.test(s)) return s.slice(0, 10)
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function formatPlannedRange(row) {
  const start = formatDay(row.planned_start_at)
  const end = formatDay(row.planned_end_at)
  if (!start && !end) return '—'
  if (start && end) return `${start} ~ ${end}`
  if (start) return `${start} 起`
  return `至 ${end}`
}

async function loadMyApplications() {
  if (!auth.isLoggedIn) {
    myApplications.value = []
    return
  }
  try {
    const res = await playerApi.get('/event-applications/mine')
    myApplications.value = res.data?.data?.items || []
  } catch {
    myApplications.value = []
  }
}

async function loadMyEvents() {
  if (!auth.isLoggedIn) {
    myEvents.value = []
    return
  }
  eventsLoading.value = true
  try {
    const res = await playerApi.get('/my-events')
    myEvents.value = res.data?.data?.items || []
    auth.isEventAdmin = myEvents.value.length > 0
  } catch {
    myEvents.value = []
  } finally {
    eventsLoading.value = false
  }
}

function startCooldown(sec = 60) {
  sendCooldown.value = sec
  if (cooldownTimer) clearInterval(cooldownTimer)
  cooldownTimer = setInterval(() => {
    sendCooldown.value -= 1
    if (sendCooldown.value <= 0) {
      clearInterval(cooldownTimer)
      cooldownTimer = null
      sendCooldown.value = 0
    }
  }, 1000)
}

function openManageFromQuery() {
  const manageId = typeof route.query.manage === 'string' ? route.query.manage : ''
  if (!manageId) return
  managingEventId.value = manageId
  if (activeSection.value !== 'sec-manage') {
    router.replace({ path: '/account', hash: '#sec-manage', query: route.query })
  }
}

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
  if (!eventAuth.loaded) await eventAuth.fetchMe()
  if (auth.email && !emailForm.email) emailForm.email = auth.email
  await Promise.all([loadMyApplications(), loadMyEvents()])
  await nextTick()
  openManageFromQuery()
})

watch(
  () => route.query.manage,
  () => {
    openManageFromQuery()
  }
)

watch(activeSection, async () => {
  await nextTick()
  const main = document.querySelector('.account-main')
  if (main) main.scrollTop = 0
})

onBeforeUnmount(() => {
  if (cooldownTimer) clearInterval(cooldownTimer)
})

function onLogout() {
  auth.logout()
  eventAuth.logout()
  router.push('/login?redirect=/account')
}

async function onChangePassword() {
  if (pwd.next.length < 6) {
    ElMessage.warning('新密码至少 6 位')
    return
  }
  if (pwd.next !== pwd.confirm) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }
  pwdLoading.value = true
  try {
    await auth.changePassword(pwd.old, pwd.next)
    ElMessage.success('密码已更新')
    pwd.old = ''
    pwd.next = ''
    pwd.confirm = ''
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '更新失败')
  } finally {
    pwdLoading.value = false
  }
}

async function sendEmailCode() {
  emailSending.value = true
  try {
    await playerApi.post('/auth/email/send-code', { email: emailForm.email })
    ElMessage.success('验证码已发送')
    startCooldown(60)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '发送失败')
  } finally {
    emailSending.value = false
  }
}

async function verifyEmail() {
  emailVerifying.value = true
  try {
    const res = await playerApi.post('/auth/email/verify', {
      email: emailForm.email,
      code: emailForm.code,
    })
    auth.email = res.data?.data?.email || emailForm.email
    auth.emailVerified = true
    emailForm.code = ''
    ElMessage.success('邮箱绑定成功')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '绑定失败')
  } finally {
    emailVerifying.value = false
  }
}

async function unbindEmail() {
  try {
    await ElMessageBox.confirm('确定解除当前邮箱绑定？', '解除绑定', { type: 'warning' })
  } catch {
    return
  }
  emailUnbinding.value = true
  try {
    await playerApi.post('/auth/email/unbind')
    auth.email = ''
    auth.emailVerified = false
    ElMessage.success('已解除绑定')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  } finally {
    emailUnbinding.value = false
  }
}

async function submitApplication() {
  if (!applyForm.name.trim()) {
    ElMessage.warning('请填写赛事名称')
    return
  }
  if (!applyForm.planned_start_at) {
    ElMessage.warning('请填写拟定开始时间')
    return
  }
  if (!applyForm.description.trim()) {
    ElMessage.warning('请填写赛事介绍')
    return
  }
  applyLoading.value = true
  try {
    await playerApi.post('/event-applications', {
      name: applyForm.name,
      planned_start_at: applyForm.planned_start_at,
      planned_end_at: applyForm.planned_end_at || null,
      description: applyForm.description,
      remark: applyForm.remark,
    })
    ElMessage.success('申请已提交，请等待管理员审核')
    applyForm.name = ''
    applyForm.planned_start_at = null
    applyForm.planned_end_at = null
    applyForm.description = ''
    applyForm.remark = ''
    await loadMyApplications()
    await auth.fetchMe()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '提交失败')
  } finally {
    applyLoading.value = false
  }
}
</script>

<style scoped>
.account-page {
  max-width: 1100px;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}
.block {
  margin-bottom: 16px;
  min-width: 0;
}
.actions {
  margin-top: 16px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.hint {
  margin: 0 0 12px;
  color: #909399;
  font-size: 13px;
}
.apply-form {
  max-width: 100%;
  min-width: 0;
}
.apply-form :deep(.el-form-item__content) {
  max-width: 100%;
}
.apply-form :deep(.el-input),
.apply-form :deep(.el-textarea) {
  max-width: 100%;
}
.date-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  max-width: 100%;
}
.date-picker {
  width: 180px;
  max-width: 100%;
}
@media (max-width: 768px) {
  .date-picker {
    width: 100%;
  }
}
.date-sep {
  color: #909399;
  font-size: 13px;
}
.field-note {
  margin: 8px 0 0;
  color: #606266;
  font-size: 12px;
  line-height: 1.65;
  background: #f5f7fa;
  border-left: 3px solid #409eff;
  padding: 8px 10px;
  word-break: break-word;
  overflow-wrap: anywhere;
}
.fit-table {
  width: 100%;
}
.fit-table-wrap {
  width: 100%;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}
.fit-table :deep(.el-table__body-wrapper) {
  overflow-x: auto;
}
.email-text {
  margin-left: 8px;
  color: #303133;
}
.code-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
.muted {
  color: #c0c4cc;
}
.bind-form {
  margin-bottom: 8px;
}
</style>
