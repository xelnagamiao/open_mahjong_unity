<template>
  <div v-loading="!auth.loaded" class="account-page" :class="{ 'account-page--manage': isManageSection, 'account-page--duplicate': isDuplicateSection }">
    <el-empty v-if="auth.loaded && !auth.isLoggedIn" description="您尚未登录">
      <el-button type="primary" @click="$router.push({ path: '/login', query: { redirect: route.fullPath } })">去登录</el-button>
    </el-empty>

    <template v-else-if="auth.isLoggedIn">
      <ProfilePanel v-if="activeSection === 'sec-profile'" />
      <InventoryPanel v-if="activeSection === 'sec-inventory'" />
      <MailPanel v-if="activeSection === 'sec-mail'" />
      <el-card v-show="activeSection === 'sec-account'" id="sec-account" class="block section" data-sec="sec-account">
        <template #header>账户信息</template>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="用户名">{{ auth.username }}</el-descriptions-item>
          <el-descriptions-item label="用户 ID">{{ auth.userId }}</el-descriptions-item>
          <el-descriptions-item label="改名卡">{{ t('持有 {count} 张', { count: auth.renameCardCount }) }}<span v-if="auth.renameCardUsableCount !== auth.renameCardCount">{{ t('（可用 {count} 张）', { count: auth.renameCardUsableCount }) }}</span></el-descriptions-item>
          <el-descriptions-item label="邮箱" :span="2">
            <template v-if="auth.emailVerified">
              <el-tag type="success" size="small">已绑定</el-tag>
              <span class="email-text">{{ auth.email }}</span>
            </template>
            <template v-else-if="auth.email">
              <el-tag type="info" size="small">注册邮箱，未验证</el-tag>
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
            <el-button @click="$router.push('/forgot-password')">忘记密码</el-button>
          </el-form-item>
        </el-form>

        <el-divider content-position="left">修改用户名</el-divider>
        <el-form label-width="88px" class="bind-form" @submit.prevent="onRename">
          <p class="hint">改名成功时需要消耗 1 张改名卡</p>
          <p class="hint">用户名最多16个字符，显示长度2–20（中日韩及全角字符计2，其他字符计1）。</p>
          <el-form-item label="新用户名">
            <el-input
              v-model="renameForm.newUsername"
              clearable
              placeholder="请输入新用户名"
              :disabled="!canRename"
              style="max-width: 320px"
              autocomplete="off"
            />
          </el-form-item>
          <el-form-item label="牌谱">
            <el-checkbox :model-value="true" disabled>追溯历史牌谱记录</el-checkbox>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="renameLoading" :disabled="!canRename" @click="onRename">
              改名
            </el-button>
            <span v-if="!canRename" class="rename-disabled-hint">{{ renameDisabledHint }}</span>
          </el-form-item>
        </el-form>
      </el-card>

      <RedeemItemsPanel v-if="activeSection === 'sec-redeem'" />
      <TileContentPanel v-show="activeSection === 'sec-uploads'" :active="activeSection === 'sec-uploads'" />
      <section v-if="isDuplicateSection" class="duplicate-workspace">
        <div class="scope-switch" role="group" aria-label="复式创建归属">
          <button type="button" :aria-pressed="activeSection === 'sec-duplicate-personal'" @click="switchDuplicateScope('personal')">为个人创建</button>
          <button type="button" :aria-pressed="activeSection === 'sec-duplicate-event'" @click="switchDuplicateScope('event')">为管理的赛事／基地创建</button>
        </div>
        <KeepAlive>
          <DuplicateWallsPanel :key="activeSection" :scope="activeSection === 'sec-duplicate-event' ? 'event' : 'personal'" :events="myEvents" />
        </KeepAlive>
      </section>

      <section v-show="isManageSection" class="venue-workspace block">
        <div class="venue-heading"><h2>赛事与基地</h2><p>我管理的赛事与基地</p></div>
        <div v-loading="eventsLoading" class="venue-grid">
          <article v-for="row in myEvents" :key="row.event_id" class="venue-card" :class="{ 'venue-card--base': row.kind === 'base', 'venue-card--selected': managingEventId === row.event_id }">
            <button type="button" class="venue-card-open" aria-label="进入管理" :title="row.description || row.name" :aria-describedby="`venue-name-${row.event_id}`" :aria-expanded="managingEventId === row.event_id" :disabled="applyLoading || iconBusy" @click="openVenueManagement(row.event_id)"></button>
            <header class="venue-card-header">
              <span class="venue-kind">{{ row.kind === 'base' ? '基地' : '赛事' }}</span>
              <span class="venue-status" :class="{ 'venue-status--active': row.status === 'active' }">{{ eventStatusLabel(row.status) }}</span>
            </header>
            <div class="venue-card-body">
              <h3 :id="`venue-name-${row.event_id}`" :title="row.name">{{ row.name }}</h3>
              <p class="venue-description" :title="row.description || ''">{{ row.description || '—' }}</p>
              <div v-if="row.reopen_requested || row.profile_change_pending" class="venue-meta">
                <el-tag v-if="row.reopen_requested" type="warning" size="small">待再开</el-tag>
                <el-tag v-if="row.profile_change_pending" type="warning" size="small">资料待审</el-tag>
              </div>
              <footer class="venue-card-footer">
              <div class="venue-card-actions">
                <el-button size="large" plain @click.stop="$router.push(`/events/${row.event_id}`)">公开页</el-button>
                <el-button size="large" type="primary" @click.stop="$router.push('/player-data')">前往数据站</el-button>
              </div>
                <span class="venue-role" :class="{ 'venue-role--owner': row.role === 'owner' }">{{ eventRoleLabel(row.role, row.kind) }}</span>
              </footer>
            </div>
          </article>
          <button type="button" class="venue-create" :disabled="applyLoading || iconBusy" :aria-expanded="applicationPanelVisible && !isBaseApply" aria-controls="venue-application-panel" @click="startApplication('event')"><span class="venue-plus" aria-hidden="true">＋</span><span>创建赛事<small>杯赛、公开赛等赛事活动，也可以创办类似月赛、季赛的无固定时限活动</small></span></button>
          <button type="button" class="venue-create venue-create--base" :disabled="applyLoading || iconBusy" :aria-expanded="applicationPanelVisible && isBaseApply" aria-controls="venue-application-panel" @click="startApplication('base')"><span class="venue-plus" aria-hidden="true">＋</span><span>创建基地<small>日常对局、训练与长期活动的场所</small></span></button>
        </div>
        <el-collapse-transition v-if="!managingEventId" @after-enter="revealApplicationPanel">
          <section v-show="applicationPanelVisible" ref="applicationPanel" id="venue-application-panel" class="application-section" aria-labelledby="venue-application-title">
            <header class="application-header">
              <h3 id="venue-application-title">{{ applyCardTitle }}</h3>
              <el-button :disabled="applyLoading || iconBusy" @click="closeApplicationPanel">收起</el-button>
            </header>
            <div class="application-body">
        <div class="event-application-rules">
          <strong>{{ applicationHelp.rulesTitle }}</strong>
          <ol><li v-for="rule in applicationHelp.rules" :key="rule">{{ rule }}</li></ol>
        </div>
        <el-form label-position="top" class="apply-form" @submit.prevent="submitApplication">
<el-form-item label="队徽（可选）"><TeamIconPicker @busy="iconBusy = $event" v-model="applyForm.team_icon_url" :disabled="applyLoading" /></el-form-item>
          <el-form-item :label="isBaseApply ? '基地名称' : '赛事名称'" required>
            <el-input v-model="applyForm.name" maxlength="128" show-word-limit />
            <p class="field-note">{{ applicationHelp.name }}</p>
          </el-form-item>

          <el-form-item :label="isBaseApply ? '拟定时间（可选）' : '拟定开始时间 / 拟定结束时间'" :required="!isBaseApply">
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
            <p class="field-note">{{ applicationHelp.dates }}</p>
          </el-form-item>

          <el-form-item :label="isBaseApply ? '基地介绍' : '赛事介绍'" required>
            <el-input
              v-model="applyForm.description"
              type="textarea"
              :rows="5"
              maxlength="2000"
              show-word-limit
            />
            <p class="field-note">{{ applicationHelp.description }}</p>
          </el-form-item>

          <el-form-item :label="isBaseApply ? '基地负责人' : '赛事负责人'" required>
            <div class="organizer-row">
              <el-input
                v-model="applyForm.organizer_name"
                maxlength="32"
                show-word-limit
                placeholder="姓名"
              />
              <el-input
                v-model="applyForm.organizer_phone"
                maxlength="20"
                placeholder="手机号"
              />
            </div>
            <p class="field-note">{{ applicationHelpData.organizer }}</p>
          </el-form-item>

          <el-form-item :label="editingApplicationId ? '追加备注' : '备注'">
            <ApplicationRemarkThread
              v-if="editingApplicationId"
              :items="editingRemarkHistory"
              class="remark-history"
            />
            <el-input
              v-model="applyForm.remark"
              type="textarea"
              :rows="3"
              maxlength="2000"
              show-word-limit
              :placeholder="editingApplicationId ? '可追加一条新备注，将写入双方往来记录' : '可选'"
            />
            <p class="field-note">{{ applicationHelpData.remark }}</p>
          </el-form-item>

        </el-form>
            </div>
            <footer class="application-footer">
          <el-button :disabled="applyLoading || iconBusy" @click="closeApplicationPanel()">取消</el-button>
          <el-button type="primary" :loading="applyLoading" :disabled="iconBusy" @click="submitApplication">{{ applySubmitLabel }}</el-button>
            </footer>
          </section>
        </el-collapse-transition>
        <EventManagePanel
          v-if="managingEventId"
          :profile-revision="profileRevision"
          :event-id="managingEventId"
          @close="managingEventId = null"
          @updated="loadMyEvents"
        />
        <details class="application-history" :open="!!route.query.application">
          <summary>申请记录 · {{ myApplications.length }}</summary>
        <div class="fit-table-wrap">
          <el-table :data="myApplications" size="small" empty-text="暂无申请记录" class="fit-table" scrollbar-always-on>
            <el-table-column label="类型" width="80"><template #default="{ row }">{{ row.kind === 'base' ? '基地' : '赛事' }}</template></el-table-column>
            <el-table-column prop="name" label="名称" min-width="140" />
            <el-table-column label="拟定时间" min-width="150">
              <template #default="{ row }">{{ formatPlannedRange(row) }}</template>
            </el-table-column>
            <el-table-column label="介绍" min-width="160" show-overflow-tooltip>
              <template #default="{ row }">{{ row.description || row.reason || '—' }}</template>
            </el-table-column>
            <el-table-column label="负责人" min-width="140">
              <template #default="{ row }">{{ organizerText(row) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="appStatusType(row.status)" size="small">{{ appStatusLabel(row.status) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="提交时间" width="160">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="230">
              <template #default="{ row }">
                <el-button link type="primary" @click="previewApplication(row)">预览</el-button>
                <el-button
                  v-if="row.status === 'pending' || row.status === 'rejected'"
                  link
                  type="warning"
                  @click="editApplication(row)"
                >{{ row.status === 'rejected' ? '修改重提' : '修改申请' }}</el-button>
                <el-button v-if="row.status === 'approved' && ownedEvents.has(row.event_id)"
                  link type="warning" @click="openProfileEditor(ownedEvents.get(row.event_id))">修改资料</el-button>
                <el-button
                  v-if="row.event_id"
                  link
                  type="primary"
                  @click="$router.push(`/events/${row.event_id}`)"
                >公开页</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>

        </details>

      </section>



      <VenueProfileDialog v-model="profileDialogVisible" :venue="profileEditingEvent" @updated="onProfileUpdated" />

      <el-dialog v-model="applicationPreviewVisible" :title="previewedApplication?.kind === 'base' ? '基地页面预览' : '赛事页面预览'" width="720px">
        <EventPreviewCard v-if="previewedApplication" :event="previewedApplication" />
      </el-dialog>
    </template>
  </div>
</template>

<script setup>
import applicationHelpData from '../../../../open_mahjong_unity/Assets/Resources/VenueApplicationHelp.json'
import TeamIconPicker from '@/components/TeamIconPicker.vue'
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import playerApi from '@/api/playerClient'
import RedeemItemsPanel from '@/components/RedeemItemsPanel.vue'
import ProfilePanel from '@/views/account/ProfilePanel.vue'
import InventoryPanel from '@/views/account/InventoryPanel.vue'
import MailPanel from '@/views/account/MailPanel.vue'
import { RenameResultUnconfirmedError } from '@/api/playerRename'
import EventManagePanel from '@/components/EventManagePanel.vue'
import VenueProfileDialog from '@/components/VenueProfileDialog.vue'
import EventPreviewCard from '@/components/EventPreviewCard.vue'
import ApplicationRemarkThread from '@/components/ApplicationRemarkThread.vue'
import TileContentPanel from '@/views/account/TileContentPanel.vue'
import DuplicateWallsPanel from '@/views/account/DuplicateWallsPanel.vue'
import { eventRoleLabel, eventStatusLabel, venueManageHash } from '@/utils/eventMeta'
import { useI18n } from '@/i18n'

const auth = usePlayerAuthStore()
const eventAuth = useEventAdminAuthStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const SECTION_IDS = ['sec-profile', 'sec-inventory', 'sec-mail', 'sec-account', 'sec-redeem', 'sec-uploads', 'sec-duplicate-personal', 'sec-duplicate-event', 'sec-manage-event', 'sec-manage-base']
const LEGACY_HASH = {
  'sec-apply': 'sec-manage-event',
  'sec-apply-event': 'sec-manage-event',
  'sec-apply-base': 'sec-manage-base',
  'sec-manage': 'sec-manage-event',
}
let cooldownTimer = null

const activeSection = computed(() => {
  const hash = (route.hash || '').replace(/^#/, '')
  const mapped = LEGACY_HASH[hash] || hash
  if (SECTION_IDS.includes(mapped)) return mapped
  return 'sec-profile'
})
const isManageSection = computed(() => activeSection.value === 'sec-manage-event' || activeSection.value === 'sec-manage-base')
const isDuplicateSection = computed(() => activeSection.value === 'sec-duplicate-personal' || activeSection.value === 'sec-duplicate-event')
const applicationPanelVisible = ref(false)
const applicationPanel = ref(null)

const pwd = reactive({ old: '', next: '', confirm: '' })
const pwdLoading = ref(false)
const renameForm = reactive({ newUsername: '' })
const renameLoading = ref(false)
const canRename = computed(() => Number(auth.renameCardUsableCount) > 0)
const renameDisabledHint = computed(() => auth.renameCardCount > 0 ? '改名卡暂不可用' : '暂无改名卡')

const emailForm = reactive({ email: '', code: '' })
const emailSending = ref(false)
const emailVerifying = ref(false)
const emailUnbinding = ref(false)
const sendCooldown = ref(0)

const applyForm = reactive({
  team_icon_url: '',
  kind: 'event',
  name: '',
  planned_start_at: null,
  planned_end_at: null,
  description: '',
  remark: '',
  organizer_name: '',
  organizer_phone: '',
})
const applyLoading = ref(false)
const iconBusy = ref(false)
const editingApplicationId = ref(null)
const editingApplicationStatus = ref('')
const editingRemarkHistory = ref([])
const applicationPreviewVisible = ref(false)
const previewedApplication = ref(null)
const myApplications = ref([])
const myEvents = ref([])
const eventsLoading = ref(false)
const managingEventId = ref(null)
const profileDialogVisible = ref(false)
const profileEditingEvent = ref(null)
const profileRevision = ref(0)
const ownedEvents = computed(() => new Map(myEvents.value.filter((row) => row.role === 'owner').map((row) => [row.event_id, row])))
const isBaseApply = computed(() => applyForm.kind === 'base')
const applicationHelp = computed(() => applicationHelpData[isBaseApply.value ? 'base' : 'event'])
const applyCardTitle = computed(() => {
  if (editingApplicationId.value) {
    return isBaseApply.value ? '修改基地申请' : '修改办赛申请'
  }
  return isBaseApply.value ? '提交基地申请' : '提交办赛申请'
})
const applySubmitLabel = computed(() => {
  if (editingApplicationId.value && editingApplicationStatus.value === 'rejected') return '修改并重新提交'
  if (editingApplicationId.value) return '保存申请修改'
  return isBaseApply.value ? '提交基地申请' : '提交办赛申请'
})

function switchDuplicateScope(scope) {
  router.replace({ path: '/account', hash: `#sec-duplicate-${scope}` })
}

function startApplication(kind) {
  if (applyLoading.value || iconBusy.value) return
  cancelApplicationEdit()
  applyForm.kind = kind
  openApplicationForm(kind)
}

function openVenueManagement(eventId) {
  if (applyLoading.value || iconBusy.value) return
  applicationPanelVisible.value = false
  managingEventId.value = eventId
}

function openProfileEditor(row) {
  profileEditingEvent.value = row
  profileDialogVisible.value = true
}

function onProfileUpdated() {
  profileRevision.value += 1
  loadMyEvents()
}

function organizerText(row) {
  const name = String(row?.organizer_name || '').trim()
  const phone = String(row?.organizer_phone || '').trim()
  if (name && phone) return `${name} ${phone}`
  return name || phone || '—'
}

function applyApplicationToForm(row) {
  applyForm.kind = row.kind === 'base' ? 'base' : 'event'
  applyForm.name = row.name || ''
  applyForm.planned_start_at = formatDay(row.planned_start_at) || null
  applyForm.planned_end_at = formatDay(row.planned_end_at) || null
  applyForm.description = row.description || row.reason || ''
  applyForm.remark = ''
  applyForm.organizer_name = row.organizer_name || ''
  applyForm.team_icon_url = row.team_icon_url || ''
  applyForm.organizer_phone = row.organizer_phone || ''
  editingRemarkHistory.value = Array.isArray(row.remark_history) ? row.remark_history : []
}

function editApplication(row) {
  if (applyLoading.value || iconBusy.value) return
  editingApplicationId.value = row.application_id
  editingApplicationStatus.value = row.status
  applyApplicationToForm(row)
  openApplicationForm(row.kind)
}

function openApplicationForm(kind = 'event') {
  managingEventId.value = null
  applyForm.kind = kind === 'base' ? 'base' : 'event'
  if (applicationPanelVisible.value) nextTick(revealApplicationPanel)
  applicationPanelVisible.value = true
}

function closeApplicationPanel() {
  if (applyLoading.value || iconBusy.value) return
  applicationPanelVisible.value = false
}

function revealApplicationPanel() {
  if (!applicationPanelVisible.value) return
  applicationPanel.value?.querySelector('.application-header')?.scrollIntoView({
    block: 'nearest',
    behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
  })
}

async function scrollToSection() {
  await nextTick()
  if (/^#sec-apply(?:-event|-base)?$/.test(route.hash)) return
  const main = document.querySelector('.account-main')
  if (main) main.scrollTop = 0
}

function cancelApplicationEdit() {
  editingApplicationId.value = null
  editingApplicationStatus.value = ''
  editingRemarkHistory.value = []
  applyForm.name = ''
  applyForm.planned_start_at = null
  applyForm.planned_end_at = null
  applyForm.description = ''
  applyForm.remark = ''
  applyForm.organizer_name = ''
  applyForm.team_icon_url = ''
  applyForm.organizer_phone = ''
}

function previewApplication(row) {
  previewedApplication.value = row
  applicationPreviewVisible.value = true
}

function appStatusLabel(s) {
  return ({ pending: '待审', approved: '已通过', rejected: '已打回', cancelled: '已取消' })[s] || s
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
  const owner = auth.userId
  try {
    const res = await playerApi.get('/my-events')
    if (owner !== auth.userId) return
    myEvents.value = res.data?.data?.items || []
    auth.isEventAdmin = myEvents.value.length > 0
    // A newly approved applicant may still have the session issued before approval.
    if (myEvents.value.length && !eventAuth.isLoggedIn) await auth.fetchMe()
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
  applicationPanelVisible.value = false
  managingEventId.value = manageId
  const row = myEvents.value.find((item) => item.event_id === manageId)
  const hash = venueManageHash(row?.kind)
  if (activeSection.value !== hash.slice(1)) {
    router.replace({ path: '/account', hash, query: route.query })
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
  () => [route.query.manage, route.query.application],
  async ([manageId, applicationId], _previous, onCleanup) => {
    if (!manageId && !applicationId) return
    let current = true
    const owner = auth.userId
    onCleanup(() => { current = false })
    await Promise.all([loadMyApplications(), loadMyEvents()])
    if (current && owner === auth.userId) openManageFromQuery()
  }
)

watch(() => route.hash, (hash) => {
  if (/^#sec-apply(?:-event|-base)?$/.test(hash)) {
    startApplication(hash === '#sec-apply-base' ? 'base' : 'event')
  } else {
    applicationPanelVisible.value = false
    scrollToSection()
  }
}, { immediate: true })

onBeforeUnmount(() => {
  if (cooldownTimer) clearInterval(cooldownTimer)
})

function onLogout() {
  auth.logout()
  eventAuth.logout()
  router.replace('/')
}

async function onRename() {
  if (renameLoading.value) return
  const newName = renameForm.newUsername.trim()
  if (!canRename.value) {
    ElMessage.warning(renameDisabledHint.value)
    return
  }
  if (!newName) {
    ElMessage.warning('请输入新用户名')
    return
  }
  if (newName === auth.username) {
    ElMessage.warning('新用户名与当前相同')
    return
  }
  renameLoading.value = true
  try {
    try {
      await ElMessageBox.confirm(
        `确定将用户名改为「${newName}」？改名成功时将消耗 1 张改名卡，并追溯更新该账号全部历史牌谱中的名字。此操作不可撤销。`,
        '确认改名',
        { type: 'warning', confirmButtonText: '确认改名', cancelButtonText: '取消' }
      )
    } catch {
      return
    }
    const res = await auth.rename(newName)
    renameForm.newUsername = ''
    ElMessage.success(res.message || '改名成功，已同步历史牌谱')
  } catch (e) {
    if (e instanceof RenameResultUnconfirmedError) {
      ElMessage.warning(e.message)
    } else {
      ElMessage.error(e.response?.data?.message || '改名失败')
    }
  } finally {
    renameLoading.value = false
  }
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
  if (iconBusy.value || applyLoading.value) return
  if (!applyForm.name.trim()) {
    ElMessage.warning(isBaseApply.value ? '请填写基地名称' : '请填写赛事名称')
    return
  }
  if (!isBaseApply.value && !applyForm.planned_start_at) {
    ElMessage.warning('请填写拟定开始时间')
    return
  }
  if (!applyForm.description.trim()) {
    ElMessage.warning(isBaseApply.value ? '请填写基地介绍' : '请填写赛事介绍')
    return
  }
  if (!applyForm.organizer_name.trim()) {
    ElMessage.warning(isBaseApply.value ? '请填写基地负责人姓名' : '请填写赛事负责人姓名')
    return
  }
  const phone = applyForm.organizer_phone.replace(/[\s-]/g, '')
  if (!phone) {
    ElMessage.warning('请填写负责人手机号')
    return
  }
  if (!/^1\d{10}$/.test(phone) && !/^\+?\d{7,15}$/.test(phone)) {
    ElMessage.warning('负责人手机号格式不正确')
    return
  }
  applyLoading.value = true
  try {
    const payload = {
      kind: applyForm.kind === 'base' ? 'base' : 'event',
      name: applyForm.name,
      planned_start_at: applyForm.planned_start_at,
      planned_end_at: applyForm.planned_end_at || null,
      description: applyForm.description,
      remark: applyForm.remark,
      organizer_name: applyForm.organizer_name,
      organizer_phone: phone,
      team_icon_url: applyForm.team_icon_url,
    }
    if (editingApplicationId.value) {
      if (editingApplicationStatus.value === 'rejected') {
        await playerApi.post(`/event-applications/${editingApplicationId.value}/resubmit`, payload)
        ElMessage.success('已重新提交，请等待管理员审核')
      } else {
        await playerApi.put(`/event-applications/${editingApplicationId.value}`, payload)
        ElMessage.success('申请修改已保存')
      }
    } else {
      await playerApi.post('/event-applications', payload)
      ElMessage.success('申请已提交，请等待管理员审核')
    }
    applicationPanelVisible.value = false
    cancelApplicationEdit()
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
  margin-left: max(0px, calc((100% - 1100px) / 3));
  margin-right: auto;
}
.account-page--manage, .account-page--duplicate {
  max-width: 1100px;
}
.venue-workspace { --venue-event: #2563b0; --venue-base: #16715b; container-type: inline-size; }
.venue-heading { margin-bottom: 18px; }
.venue-heading h2 { margin: 0 0 6px; font-size: 22px; font-weight: 400; }
.venue-heading p { margin: 0; color: var(--el-text-color-secondary); font-size: 13px; }
.venue-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; align-items: start; }
.venue-card { position: relative; grid-column: span 2; display: flex; flex-direction: column; height: calc((100cqw - 48px) / 4); box-sizing: border-box; border: 1px solid var(--el-border-color); border-radius: 10px; overflow: hidden; background: var(--el-bg-color); min-width: 0; }
.venue-card-open { position: absolute; inset: 0; z-index: 1; width: 100%; height: 100%; border: 0; border-radius: inherit; background: transparent; cursor: pointer; }
.venue-card-open:disabled { cursor: not-allowed; }
.venue-card:has(.venue-card-open:focus-visible) { outline: 2px solid var(--el-color-primary); outline-offset: 2px; }
.venue-card:has(.venue-card-open:hover:not(:disabled)) { border-color: var(--el-color-primary); }
.venue-card-actions { position: relative; z-index: 2; }
.venue-card--selected { outline: 2px solid var(--el-text-color-primary); outline-offset: 2px; }
.venue-card-header { display: flex; flex-shrink: 0; justify-content: space-between; align-items: center; gap: 12px; background: var(--venue-event); color: #fff; padding: 8px 16px; }
.venue-card--base .venue-card-header { background: var(--venue-base); }
.venue-status { padding: 2px 8px; border-radius: 4px; background: #fff; color: #525866; font-size: 12px; line-height: 18px; }
.venue-status--active { color: #15803d; }
.venue-card-body { display: flex; flex-direction: column; flex: 1; min-height: 0; gap: 8px; padding: 12px 16px; }
.venue-card h3 { margin: 0; font-size: 18px; line-height: 24px; font-weight: 400; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; flex-shrink: 0; }
.venue-description { margin: 0; color: var(--el-text-color-regular); font-size: 13px; line-height: 21px; white-space: pre-wrap; overflow-wrap: anywhere; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; height: 42px; max-height: 42px; flex-shrink: 0; }
.venue-meta, .venue-card-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.venue-meta { color: var(--el-text-color-secondary); font-size: 12px; }
.venue-card-footer { display: flex; flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: 8px 12px; margin-top: auto; }
.venue-role { margin-left: auto; padding: 3px 8px; border: 1px solid currentColor; border-radius: 4px; color: #b45309; font-size: 12px; line-height: 18px; white-space: nowrap; }
.venue-role--owner { color: #c62828; }
.venue-card-actions .el-button + .el-button { margin-left: 0; }
.venue-create { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 16px; width: 100%; aspect-ratio: 1; box-sizing: border-box; padding: 16px; text-align: center; background: var(--el-bg-color); color: var(--el-text-color-primary); border: 1px solid var(--el-border-color); border-top: 4px solid var(--venue-event); border-radius: 10px; font: inherit; cursor: pointer; }
.venue-create--base { border-top-color: var(--venue-base); }
.venue-create:hover { border-color: var(--venue-event); }
.venue-create--base:hover { border-color: var(--venue-base); }
.venue-create:focus-visible, .scope-switch button:focus-visible { outline: 2px solid var(--el-color-primary); outline-offset: 3px; }
.venue-plus { color: var(--venue-event); font-size: 36px; line-height: 1; }
.venue-create--base .venue-plus { color: var(--venue-base); }
.venue-create small { display: block; margin-top: 5px; color: var(--el-text-color-secondary); font-size: 12px; line-height: 1.6; overflow-wrap: anywhere; }
@media (max-width: 1200px) { .venue-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .venue-card { height: 240px; } }
@media (max-width: 480px) { .venue-grid { grid-template-columns: minmax(0, 1fr); } .venue-card { grid-column: span 1; height: 260px; } }
.application-history { margin: 18px 0; background: var(--el-bg-color); border: 1px solid var(--el-border-color); border-radius: 8px; padding: 14px; }
.application-history summary { cursor: pointer; font-size: 14px; }
.application-history[open] summary { margin-bottom: 14px; }
.scope-switch { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 20px; }
.scope-switch button { padding: 10px 16px; font: inherit; background: var(--el-bg-color); color: var(--el-text-color-primary); border: 1px solid var(--el-border-color); border-radius: 7px; cursor: pointer; }
.scope-switch button[aria-pressed="true"] { background: #2563b0; border-color: #2563b0; color: #fff; }
:global(html[lang^="zh"] .venue-workspace *), :global(html[lang^="zh"] .duplicate-workspace *), :global(html[lang^="zh"] .application-section *) { font-weight: 400 !important; font-synthesis: none; }
.block {
  margin-bottom: 16px;
  min-width: 0;
}
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
}
.application-section { margin-top: 16px; border: 1px solid var(--el-border-color); border-radius: 10px; background: var(--el-bg-color); }
.application-header { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 16px 20px; border-bottom: 1px solid var(--el-border-color-lighter); }
.application-header h3 { margin: 0; font-size: 18px; font-weight: 400; }
.application-body { max-width: 760px; box-sizing: border-box; padding: 20px; }
.application-footer { display: flex; justify-content: flex-end; gap: 12px; padding: 16px 20px; border-top: 1px solid var(--el-border-color-lighter); }
.application-footer .el-button + .el-button { margin-left: 0; }
.venue-create:disabled { cursor: not-allowed; opacity: .6; }
@media (max-width: 480px) { .application-header, .application-body, .application-footer { padding: 16px; } }
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
.event-application-rules {
  margin-bottom: 18px;
  padding: 12px 16px;
  border: 1px solid #e6a23c;
  border-radius: 4px;
  background: #fdf6ec;
  color: #7a4b00;
  font-size: 13px;
  line-height: 1.7;
}
.event-application-rules ol {
  margin: 6px 0 0;
  padding-left: 20px;
}
.apply-form :deep(.el-form-item__content) {
  max-width: 100%;
  flex-wrap: wrap;
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
  width: 100%;
  flex: 1 0 100%;
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
.organizer-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  width: 100%;
  flex: 1 0 100%;
}
.organizer-row :deep(.el-input) {
  flex: 1;
  min-width: 180px;
}
.remark-history {
  margin-bottom: 10px;
}
.field-note {
  margin: 8px 0 0;
  width: 100%;
  flex: 1 0 100%;
  color: #606266;
  font-size: 12px;
  line-height: 1.65;
  background: #f5f7fa;
  border-left: 3px solid #409eff;
  padding: 8px 10px;
  white-space: pre-wrap;
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
.bind-form > .hint {
  margin: 0 0 12px;
  padding-left: 88px;
  box-sizing: border-box;
  line-height: 1.5;
}
.rename-disabled-hint {
  margin-left: 8px;
  color: #909399;
  font-size: 13px;
}
.bind-form :deep(.el-checkbox.is-disabled .el-checkbox__label) {
  color: #c0c4cc;
}
</style>
