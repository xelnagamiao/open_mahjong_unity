<template>
  <div class="admin-apps">
    <h2 class="page-title">{{ isBase ? '基地申请' : '办赛申请' }}</h2>
    <div class="toolbar">
      <el-radio-group v-model="statusFilter" size="small" @change="onStatusChange">
        <el-radio-button label="">全部</el-radio-button>
        <el-radio-button label="pending">待审</el-radio-button>
        <el-radio-button label="approved">已通过</el-radio-button>
        <el-radio-button label="rejected">已打回</el-radio-button>
      </el-radio-group>
      <el-button size="small" :loading="loading" @click="load">刷新</el-button>
    </div>

    <el-table :data="items" v-loading="loading" stripe>
      <el-table-column prop="application_id" label="ID" width="70" />
      <el-table-column label="申请人" min-width="140">
        <template #default="{ row }">
          {{ row.applicant_username || '—' }} ({{ row.applicant_user_id }})
        </template>
      </el-table-column>
      <el-table-column prop="name" label="名称" min-width="140" />
      <el-table-column label="拟定时间" min-width="180">
        <template #default="{ row }">{{ formatPlannedRange(row) }}</template>
      </el-table-column>
      <el-table-column :label="isBase ? '基地介绍' : '赛事介绍'" min-width="200" show-overflow-tooltip>
        <template #default="{ row }">{{ row.description || row.reason || '—' }}</template>
      </el-table-column>
      <el-table-column prop="remark" label="备注" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ row.remark || '—' }}</template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="提交时间" width="170">
        <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="200" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="previewApplication(row)">预览</el-button>
          <template v-if="row.status === 'pending'">
            <el-button type="success" link @click="openApprove(row)">通过</el-button>
            <el-button type="danger" link @click="openReject(row)">打回</el-button>
          </template>
          <router-link
            v-else-if="row.event_id"
            :to="detailPath(row.event_id, row.kind)"
          >查看{{ isBase ? '基地' : '赛事' }}</router-link>
          <span v-else class="muted">—</span>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination
        background
        layout="total, prev, pager, next"
        :total="total"
        :page-size="pageSize"
        v-model:current-page="page"
        @current-change="load"
      />
    </div>

    <el-dialog v-model="approveVisible" :title="approveTitle" width="420px">
      <el-form label-width="80px">
        <el-form-item :label="approveNameLabel">
          <el-input v-model="approveForm.name" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="approveForm.review_note" type="textarea" rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="approveVisible = false">取消</el-button>
        <el-button type="primary" :loading="acting" @click="doApprove">确认通过</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="rejectVisible" :title="rejectTitle" width="420px">
      <el-form label-width="80px">
        <el-form-item label="打回原因" required>
          <el-input v-model="rejectForm.review_note" type="textarea" rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="rejectVisible = false">取消</el-button>
        <el-button type="danger" :loading="acting" @click="doReject">确认打回</el-button>
      </template>
    </el-dialog>

    <el-divider content-position="left">{{ isBase ? '基地资料修改审核' : '赛事资料修改审核' }}</el-divider>
    <div class="toolbar">
      <el-radio-group v-model="profileStatusFilter" size="small" @change="loadProfileChanges">
        <el-radio-button label="pending">待审</el-radio-button>
        <el-radio-button label="">全部</el-radio-button>
        <el-radio-button label="approved">已通过</el-radio-button>
        <el-radio-button label="rejected">已拒绝</el-radio-button>
      </el-radio-group>
      <el-button size="small" :loading="profileLoading" @click="loadProfileChanges">刷新</el-button>
    </div>
    <el-table :data="profileItems" v-loading="profileLoading" stripe>
      <el-table-column prop="request_id" label="ID" width="70" />
      <el-table-column :label="isBase ? '基地' : '赛事'" min-width="160">
        <template #default="{ row }">
          <router-link :to="detailPath(row.event_id)">{{ row.current_name }}</router-link>
        </template>
      </el-table-column>
      <el-table-column label="申请人" min-width="130">
        <template #default="{ row }">
          {{ row.requester_username || '—' }} ({{ row.requested_by }})
        </template>
      </el-table-column>
      <el-table-column label="拟改名称" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ row.proposed_name }}</template>
      </el-table-column>
      <el-table-column label="拟改简介" min-width="180" show-overflow-tooltip>
        <template #default="{ row }">{{ row.proposed_description || '—' }}</template>
      </el-table-column>
      <el-table-column prop="reason" label="原因" min-width="120" show-overflow-tooltip />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="160" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="previewProfileChange(row)">预览</el-button>
          <template v-if="row.status === 'pending'">
            <el-button type="success" link @click="approveProfile(row)">通过</el-button>
            <el-button type="danger" link @click="rejectProfile(row)">拒绝</el-button>
          </template>
          <router-link v-else :to="detailPath(row.event_id, row.kind)">查看{{ isBase ? '基地' : '赛事' }}</router-link>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="previewVisible" :title="isBase ? '基地页面预览' : '赛事页面预览'" width="640px">
      <EventPreviewCard v-if="previewEvent" :event="previewEvent" :status-label="previewStatusLabel" />
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import EventPreviewCard from '@/components/EventPreviewCard.vue'
import { parseVenueKind, venueAdminDetailPath } from '@/utils/eventMeta'

const route = useRoute()
const isBase = computed(() => parseVenueKind(route.meta.venueKind) === 'base')
const venueKind = computed(() => (isBase.value ? 'base' : 'event'))

function detailPath(eventId, kind) {
  return venueAdminDetailPath(kind || venueKind.value, eventId)
}

const items = ref([])
const loading = ref(false)
const acting = ref(false)
const statusFilter = ref('pending')
const page = ref(1)
const pageSize = 20
const total = ref(0)

const approveVisible = ref(false)
const rejectVisible = ref(false)
const currentId = ref(null)
const currentKind = ref('event')
const approveForm = reactive({ name: '', review_note: '' })
const rejectForm = reactive({ review_note: '' })
const previewVisible = ref(false)
const previewEvent = ref(null)
const previewStatusLabel = ref('申请中的赛事页面预览')
const isCurrentBase = computed(() => currentKind.value === 'base')
const approveTitle = computed(() => (isCurrentBase.value ? '通过基地申请' : '通过办赛申请'))
const approveNameLabel = computed(() => (isCurrentBase.value ? '基地名称' : '赛事名称'))
const rejectTitle = computed(() => (isCurrentBase.value ? '打回基地申请' : '打回办赛申请'))

const profileItems = ref([])
const profileLoading = ref(false)
const profileStatusFilter = ref('pending')

function statusLabel(s) {
  return ({ pending: '待审', approved: '已通过', rejected: '已打回', cancelled: '已取消' })[s] || s
}

function statusType(s) {
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

function onStatusChange() {
  page.value = 1
  load()
}

async function load() {
  loading.value = true
  try {
    const res = await adminApi.get('/event-applications', {
      params: {
        status: statusFilter.value || undefined,
        kind: venueKind.value,
        page: page.value,
        page_size: pageSize,
      },
    })
    items.value = res.data?.data?.items || []
    total.value = res.data?.data?.total || 0
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
  } finally {
    loading.value = false
  }
}

function openApprove(row) {
  currentId.value = row.application_id
  currentKind.value = row.kind === 'base' ? 'base' : 'event'
  approveForm.name = row.name
  approveForm.review_note = ''
  approveVisible.value = true
}

function openReject(row) {
  currentId.value = row.application_id
  currentKind.value = row.kind === 'base' ? 'base' : 'event'
  rejectForm.review_note = ''
  rejectVisible.value = true
}

function previewApplication(row) {
  previewEvent.value = row
  previewStatusLabel.value = row.kind === 'base' ? '基地申请预览' : '办赛申请预览'
  previewVisible.value = true
}

function previewProfileChange(row) {
  previewEvent.value = {
    name: row.proposed_name,
    description: row.proposed_description,
    requester_username: row.requester_username,
  }
  previewStatusLabel.value = isBase.value ? '基地资料修改后预览' : '赛事资料修改后预览'
  previewVisible.value = true
}

async function doApprove() {
  acting.value = true
  try {
    await adminApi.post(`/event-applications/${currentId.value}/approve`, {
      name: approveForm.name,
      review_note: approveForm.review_note,
    })
    ElMessage.success(isBase.value ? '已通过并创建基地' : '已通过并创建赛事')
    approveVisible.value = false
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  } finally {
    acting.value = false
  }
}

async function doReject() {
  if (!rejectForm.review_note.trim()) {
    ElMessage.warning('请填写打回原因')
    return
  }
  acting.value = true
  try {
    await adminApi.post(`/event-applications/${currentId.value}/reject`, {
      review_note: rejectForm.review_note,
    })
    ElMessage.success('已打回')
    rejectVisible.value = false
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  } finally {
    acting.value = false
  }
}

async function loadProfileChanges() {
  profileLoading.value = true
  try {
    const res = await adminApi.get('/event-profile-changes', {
      params: {
        status: profileStatusFilter.value || undefined,
        kind: venueKind.value,
        limit: 50,
      },
    })
    profileItems.value = res.data?.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载资料修改失败')
  } finally {
    profileLoading.value = false
  }
}

async function approveProfile(row) {
  try {
    const { value } = await ElMessageBox.prompt('请填写操作原因', '通过资料修改', {
      confirmButtonText: '通过',
      cancelButtonText: '取消',
      inputPattern: /\S+/,
      inputErrorMessage: '原因不能为空',
    })
    await adminApi.post(`/event-profile-changes/${row.request_id}/approve`, { reason: value.trim() })
    ElMessage.success('已通过')
    await loadProfileChanges()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

async function rejectProfile(row) {
  try {
    const { value } = await ElMessageBox.prompt('请填写拒绝原因', '拒绝资料修改', {
      confirmButtonText: '拒绝',
      cancelButtonText: '取消',
      inputPattern: /\S+/,
      inputErrorMessage: '原因不能为空',
    })
    await adminApi.post(`/event-profile-changes/${row.request_id}/reject`, { reason: value.trim() })
    ElMessage.success('已拒绝')
    await loadProfileChanges()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

onMounted(() => {
  load()
  loadProfileChanges()
})

watch(() => route.meta.venueKind, () => {
  page.value = 1
  load()
  loadProfileChanges()
})
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
  font-size: 18px;
  font-weight: 600;
}
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
  gap: 12px;
  flex-wrap: wrap;
}
.pager {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}
.muted { color: #999; }
</style>
