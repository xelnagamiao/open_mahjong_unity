<template>
  <div v-loading="loading">
    <el-page-header @back="$router.push('/admin/events')" content="赛事详情" />

    <template v-if="detail">
      <el-card class="block">
        <template #header>基本信息</template>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="赛事名称">{{ detail.name }}</el-descriptions-item>
          <el-descriptions-item label="赛事 ID">{{ detail.event_id }}</el-descriptions-item>
          <el-descriptions-item label="赛事介绍" :span="2">
            <span class="desc-text">{{ detail.description || '—' }}</span>
          </el-descriptions-item>
          <el-descriptions-item label="状态">
            <el-tag :type="eventStatusTagType(detail.status)" size="small">
              {{ eventStatusLabel(detail.status) }}
            </el-tag>
            <el-tag v-if="detail.reopen_requested" type="warning" size="small" style="margin-left: 6px">
              待审核再开
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="牌谱数">{{ detail.record_count ?? 0 }}</el-descriptions-item>
          <el-descriptions-item label="创建者">{{ detail.created_by }}</el-descriptions-item>
          <el-descriptions-item label="创建时间">{{ formatDate(detail.created_at) }}</el-descriptions-item>
          <el-descriptions-item label="关闭时间">{{ formatDate(detail.closed_at) }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ formatDate(detail.updated_at) }}</el-descriptions-item>
        </el-descriptions>

        <div class="actions">
          <el-button
            v-if="detail.status === 'registered'"
            type="primary"
            @click="activateEvent"
          >开启赛事</el-button>
          <el-button
            v-if="detail.status === 'closed' && detail.reopen_requested"
            type="success"
            @click="activateEvent"
          >批准重新开启</el-button>
          <el-button
            v-if="detail.status === 'closed' && detail.reopen_requested"
            type="info"
            plain
            @click="rejectReopen"
          >拒绝再开申请</el-button>
          <el-button
            type="warning"
            :disabled="detail.status !== 'active'"
            @click="closeEvent"
          >关闭赛事</el-button>
          <el-button type="danger" @click="deleteEvent">删除赛事</el-button>
        </div>
      </el-card>

      <el-card v-if="pendingProfile" class="block">
        <template #header>待审资料修改</template>
        <el-descriptions :column="1" border size="small">
          <el-descriptions-item label="申请人">
            {{ pendingProfile.requester_username || '—' }}
            ({{ pendingProfile.requested_by }})
          </el-descriptions-item>
          <el-descriptions-item label="当前名称">{{ pendingProfile.current_name }}</el-descriptions-item>
          <el-descriptions-item label="拟改名称">{{ pendingProfile.proposed_name }}</el-descriptions-item>
          <el-descriptions-item label="当前简介">
            <span class="desc-text">{{ pendingProfile.current_description || '—' }}</span>
          </el-descriptions-item>
          <el-descriptions-item label="拟改简介">
            <span class="desc-text">{{ pendingProfile.proposed_description || '—' }}</span>
          </el-descriptions-item>
          <el-descriptions-item label="修改原因">{{ pendingProfile.reason || '—' }}</el-descriptions-item>
          <el-descriptions-item label="提交时间">{{ formatDate(pendingProfile.created_at) }}</el-descriptions-item>
        </el-descriptions>
        <div class="actions">
          <el-button type="success" @click="approveProfileChange">通过</el-button>
          <el-button type="danger" @click="rejectProfileChange">拒绝</el-button>
        </div>
      </el-card>

      <el-card class="block">
        <template #header>赛事主管理员</template>
        <el-form inline class="admin-inline-form" @submit.prevent="setOwner">
          <el-form-item label="用户 ID">
            <el-input v-model="ownerForm.user_id" clearable style="width: 160px" />
          </el-form-item>
          <el-form-item label="操作原因">
            <el-input v-model="ownerForm.reason" clearable style="width: 180px" placeholder="审计必填" />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="savingOwner" @click="setOwner">
              {{ owner ? '更换赛事主管理员' : '指定赛事主管理员' }}
            </el-button>
          </el-form-item>
        </el-form>
        <p v-if="owner" class="current-owner">
          当前：{{ owner.username || '-' }}
          <span class="uid">({{ owner.user_id }})</span>
        </p>
        <p v-else class="muted">尚未指定赛事主管理员</p>
      </el-card>

      <el-card class="block">
        <template #header>
          赛事子管理员
          <span class="admin-count">（{{ adminList.length }} / 10）</span>
        </template>
        <el-form inline class="admin-inline-form" @submit.prevent="addAdmin">
          <el-form-item label="用户 ID">
            <el-input v-model="adminForm.user_id" clearable style="width: 160px" />
          </el-form-item>
          <el-form-item label="操作原因">
            <el-input v-model="adminForm.reason" clearable style="width: 180px" placeholder="审计必填" />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :loading="savingAdmin" @click="addAdmin">添加</el-button>
          </el-form-item>
        </el-form>

        <el-table :data="adminList" size="small" style="margin-top: 12px" empty-text="暂无赛事子管理员">
          <el-table-column prop="user_id" label="用户 ID" width="120" />
          <el-table-column prop="username" label="用户名" min-width="140" />
          <el-table-column label="添加时间" min-width="160">
            <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="100">
            <template #default="{ row }">
              <el-button link type="danger" @click="removeAdmin(row)">移除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card class="block">
        <template #header>
          空房间
          <el-button
            link
            type="primary"
            style="float: right"
            :loading="loadingRooms"
            @click="loadRooms"
          >刷新</el-button>
        </template>

        <el-form class="admin-inline-form" label-width="88px" @submit.prevent="createRoom">
          <div style="display: flex; flex-wrap: wrap; gap: 0 8px; align-items: flex-start">
            <el-form-item label="规则">
              <el-select v-model="roomForm.room_rule" style="width: 140px">
                <el-option
                  v-for="opt in roomRuleOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="房间名">
              <el-input
                v-model="roomForm.room_name"
                clearable
                style="width: 160px"
                placeholder="可选"
              />
            </el-form-item>
            <el-form-item label="操作原因">
              <el-input
                v-model="roomForm.reason"
                clearable
                style="width: 180px"
                placeholder="审计必填"
              />
            </el-form-item>
            <el-form-item>
              <el-button
                type="primary"
                :loading="creatingRoom"
                :disabled="detail.status !== 'active'"
                @click="createRoom"
              >创建空房间</el-button>
            </el-form-item>
          </div>
          <GuobiaoEmptyRoomConfig v-if="roomForm.room_rule === 'guobiao'" v-model="roomForm" />
          <el-alert
            v-else
            title="当前仅国标空房间提供完整对局配置；其他规则仍按服务端默认参数创建。"
            type="info"
            :closable="false"
            show-icon
            style="margin-bottom: 12px"
          />
        </el-form>

        <el-table
          :data="rooms"
          size="small"
          style="margin-top: 12px"
          v-loading="loadingRooms"
          empty-text="暂无空房间"
        >
          <el-table-column prop="room_id" label="房间 ID" min-width="120" />
          <el-table-column prop="room_name" label="名称" min-width="120" />
          <el-table-column prop="room_rule" label="规则" width="100" />
          <el-table-column label="人数" width="80">
            <template #default="{ row }">
              {{ (row.player_list || []).length }} / {{ row.max_player || 4 }}
            </template>
          </el-table-column>
          <el-table-column label="对局中" width="80">
            <template #default="{ row }">
              <el-tag :type="row.is_game_running ? 'warning' : 'info'" size="small">
                {{ row.is_game_running ? '是' : '否' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="100">
            <template #default="{ row }">
              <el-button
                link
                type="danger"
                :disabled="!!row.is_game_running"
                @click="deleteRoom(row)"
              >删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card class="block">
        <template #header>
          比赛场统计
          <el-button
            link
            type="primary"
            style="float: right"
            :loading="loadingStats"
            @click="loadPlayerStats"
          >刷新</el-button>
        </template>
        <el-alert
          type="info"
          :closable="false"
          show-icon
          style="margin-bottom: 12px"
          title="口径与数据站「比赛场」一致：按牌谱顺位汇总总对局与一位～四位次数。"
        />
        <div class="stats-filters">
          <el-select
            v-model="statsFilter.rule"
            clearable
            placeholder="全部规则"
            size="small"
            style="width: 140px"
            @change="loadPlayerStats"
          >
            <el-option
              v-for="r in statsRuleOptions"
              :key="r"
              :label="ruleLabel(r)"
              :value="r"
            />
          </el-select>
          <el-select
            v-model="statsFilter.game_type"
            clearable
            placeholder="全部局制"
            size="small"
            style="width: 140px"
            @change="loadPlayerStats"
          >
            <el-option
              v-for="opt in GAME_TYPE_OPTIONS"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
          <el-input
            v-model="statsFilter.q"
            clearable
            size="small"
            placeholder="玩家 ID / 用户名"
            style="width: 180px"
            @keyup.enter="loadPlayerStats"
          />
          <el-button type="primary" size="small" :loading="loadingStats" @click="loadPlayerStats">查询</el-button>
          <el-button size="small" @click="resetStatsFilter">重置</el-button>
        </div>
        <div v-loading="loadingStats">
          <div class="stats-totals">
            <div v-for="item in totalsStatsDisplay" :key="item.label" class="stats-cell">
              <span class="stats-label">{{ item.label }}</span>
              <span class="stats-value">{{ item.value }}</span>
            </div>
            <div class="stats-cell">
              <span class="stats-label">参赛人数</span>
              <span class="stats-value">{{ statsTotals.player_count ?? 0 }}</span>
            </div>
          </div>
          <el-table
            :data="statsPlayers"
            size="small"
            empty-text="暂无比赛场对局数据"
            highlight-current-row
            @current-change="onStatsPlayerSelect"
          >
            <el-table-column type="index" label="#" width="48" />
            <el-table-column prop="user_id" label="用户 ID" width="100" />
            <el-table-column prop="username" label="用户名" min-width="120" />
            <el-table-column prop="total_games" label="对局" width="72" sortable />
            <el-table-column label="平均顺位" width="96">
              <template #default="{ row }">{{ avgRank(row) }}</template>
            </el-table-column>
            <el-table-column prop="first_place_count" label="一位" width="72" sortable />
            <el-table-column prop="second_place_count" label="二位" width="72" sortable />
            <el-table-column prop="third_place_count" label="三位" width="72" sortable />
            <el-table-column prop="fourth_place_count" label="四位" width="72" sortable />
            <el-table-column label="一位率" width="88">
              <template #default="{ row }">{{ rankRate(row.first_place_count, row) }}</template>
            </el-table-column>
            <el-table-column prop="total_score" label="总分" width="96" sortable />
          </el-table>
          <div v-if="selectedPlayerStats" class="stats-player-detail">
            <h4 class="stats-heading">
              选中：{{ selectedPlayerStats.username }}
              <span class="uid">ID {{ selectedPlayerStats.user_id }}</span>
            </h4>
            <div class="stats-totals">
              <div v-for="item in selectedPlayerStatsDisplay" :key="item.label" class="stats-cell">
                <span class="stats-label">{{ item.label }}</span>
                <span class="stats-value">{{ item.value }}</span>
              </div>
            </div>
          </div>
        </div>
      </el-card>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'
import {
  buildGuobiaoRoomPayload,
  createDefaultGuobiaoRoomConfig,
} from '@/utils/guobiaoRoomConfig'
import { eventStatusLabel, eventStatusTagType } from '@/utils/eventMeta'
import { avgRank, buildPlayerStatsRows, rankRate } from '@/utils/statsDisplay'

const RULE_LABELS = {
  guobiao: '国标',
  riichi: '立直',
  qingque: '青雀',
  classical: '古典',
  sichuan: '四川',
  changsha: '长沙',
  jiandan: '简单',
}
const GAME_TYPE_OPTIONS = [
  { value: 'quanzhuang', label: '全庄战' },
  { value: 'xifeng', label: '东西战' },
  { value: 'banzhuang', label: '半庄战' },
  { value: 'dongfeng', label: '东风战' },
]
const RANK_STAT_LABELS = new Set([
  '总对局',
  '平均顺位',
  '一位率',
  '二位率',
  '三位率',
  '四位率',
])
function ruleLabel(rule) {
  return RULE_LABELS[rule] || rule
}
function rankOnlyStatsRows(stats) {
  return buildPlayerStatsRows(stats || {}).filter((row) => RANK_STAT_LABELS.has(row.label))
}

const route = useRoute()
const loading = ref(false)
const detail = ref(null)
const savingOwner = ref(false)
const savingAdmin = ref(false)
const ownerForm = reactive({ user_id: '', reason: '' })
const adminForm = reactive({ user_id: '', reason: '' })

const rooms = ref([])
const loadingRooms = ref(false)
const creatingRoom = ref(false)
const roomRuleOptions = [
  { value: 'guobiao', label: '国标' },
  { value: 'riichi', label: '立直' },
  { value: 'qingque', label: '青雀' },
  { value: 'classical', label: '古典' },
  { value: 'sichuan', label: '四川' },
  { value: 'changsha', label: '长沙' },
]
const roomForm = reactive({
  room_rule: 'guobiao',
  reason: '',
  ...createDefaultGuobiaoRoomConfig(),
})

const loadingStats = ref(false)
const statsTotals = ref({
  total_games: 0,
  first_place_count: 0,
  second_place_count: 0,
  third_place_count: 0,
  fourth_place_count: 0,
  player_count: 0,
})
const statsPlayers = ref([])
const statsRuleOptions = ref([])
const statsFilter = reactive({ rule: '', game_type: '', q: '' })
const selectedPlayerId = ref(null)

const owner = computed(() => (detail.value?.admins || []).find((a) => a.role === 'owner') || null)
const adminList = computed(() => (detail.value?.admins || []).filter((a) => a.role === 'admin'))
const pendingProfile = ref(null)
const totalsStatsDisplay = computed(() => rankOnlyStatsRows(statsTotals.value))
const selectedPlayerStats = computed(() => {
  if (selectedPlayerId.value == null) return null
  return statsPlayers.value.find((p) => p.user_id === selectedPlayerId.value) || null
})
const selectedPlayerStatsDisplay = computed(() =>
  selectedPlayerStats.value ? rankOnlyStatsRows(selectedPlayerStats.value) : []
)

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '-'
}

async function loadPendingProfile() {
  try {
    const res = await adminApi.get(`/event-profile-changes/by-event/${route.params.eventId}`)
    pendingProfile.value = res.data.data?.pending || null
  } catch {
    pendingProfile.value = null
  }
}

async function loadRooms() {
  loadingRooms.value = true
  try {
    const res = await adminApi.get(`/events/${route.params.eventId}/rooms`)
    rooms.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载房间失败')
    rooms.value = []
  } finally {
    loadingRooms.value = false
  }
}

async function loadPlayerStats() {
  loadingStats.value = true
  try {
    const params = {}
    if (statsFilter.rule) params.rule = statsFilter.rule
    if (statsFilter.game_type) params.game_type = statsFilter.game_type
    if (statsFilter.q.trim()) params.q = statsFilter.q.trim()
    const res = await adminApi.get(`/events/${route.params.eventId}/player-stats`, { params })
    const data = res.data.data || {}
    statsTotals.value = data.totals || {
      total_games: 0,
      first_place_count: 0,
      second_place_count: 0,
      third_place_count: 0,
      fourth_place_count: 0,
      player_count: 0,
    }
    statsPlayers.value = data.players || []
    statsRuleOptions.value = data.filters?.rules || []
    if (
      selectedPlayerId.value != null
      && !statsPlayers.value.some((p) => p.user_id === selectedPlayerId.value)
    ) {
      selectedPlayerId.value = null
    }
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载统计失败')
    statsPlayers.value = []
  } finally {
    loadingStats.value = false
  }
}

function resetStatsFilter() {
  statsFilter.rule = ''
  statsFilter.game_type = ''
  statsFilter.q = ''
  selectedPlayerId.value = null
  loadPlayerStats()
}

function onStatsPlayerSelect(row) {
  selectedPlayerId.value = row?.user_id ?? null
}

async function load() {
  loading.value = true
  try {
    const res = await adminApi.get(`/events/${route.params.eventId}`)
    detail.value = res.data.data
    if (owner.value) {
      ownerForm.user_id = String(owner.value.user_id)
    }
    await Promise.all([loadRooms(), loadPendingProfile(), loadPlayerStats()])
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
    detail.value = null
  } finally {
    loading.value = false
  }
}

async function promptReason(title) {
  const { value } = await ElMessageBox.prompt('请填写操作原因（审计必填）', title, {
    confirmButtonText: '确定',
    cancelButtonText: '取消',
    inputPattern: /\S+/,
    inputErrorMessage: '原因不能为空',
  })
  return value.trim()
}

async function createRoom() {
  if (!roomForm.reason.trim()) {
    ElMessage.warning('请填写操作原因')
    return
  }
  if (!roomForm.room_rule) {
    ElMessage.warning('请选择规则')
    return
  }
  creatingRoom.value = true
  try {
    let room_config = {}
    let password = ''
    if (roomForm.room_rule === 'guobiao') {
      ;({ room_config, password } = buildGuobiaoRoomPayload(roomForm))
    } else if (roomForm.room_name.trim()) {
      room_config.room_name = roomForm.room_name.trim()
    }
    await adminApi.post(`/events/${route.params.eventId}/rooms`, {
      room_rule: roomForm.room_rule,
      room_config,
      password,
      reason: roomForm.reason.trim(),
    })
    roomForm.reason = ''
    roomForm.room_name = ''
    roomForm.password = ''
    ElMessage.success('空房间已创建')
    await loadRooms()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '创建失败')
  } finally {
    creatingRoom.value = false
  }
}

async function deleteRoom(row) {
  try {
    const reason = await promptReason('删除空房间')
    await adminApi.delete(`/events/${route.params.eventId}/rooms/${row.room_id}`, {
      data: { reason },
    })
    ElMessage.success('房间已删除')
    await loadRooms()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

async function setOwner() {
  if (!ownerForm.user_id.trim()) {
    ElMessage.warning('请填写赛事主管理员用户 ID')
    return
  }
  if (!ownerForm.reason.trim()) {
    ElMessage.warning('请填写操作原因')
    return
  }
  savingOwner.value = true
  try {
    const res = await adminApi.put(`/events/${route.params.eventId}/owner`, {
      user_id: ownerForm.user_id.trim(),
      reason: ownerForm.reason.trim(),
    })
    detail.value.admins = res.data.data.admins
    ownerForm.reason = ''
    ElMessage.success('赛事主管理员已更新')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '设置失败')
  } finally {
    savingOwner.value = false
  }
}

async function addAdmin() {
  if (!adminForm.user_id.trim()) {
    ElMessage.warning('请填写赛事子管理员用户 ID')
    return
  }
  if (!adminForm.reason.trim()) {
    ElMessage.warning('请填写操作原因')
    return
  }
  savingAdmin.value = true
  try {
    const res = await adminApi.post(`/events/${route.params.eventId}/admins`, {
      user_id: adminForm.user_id.trim(),
      reason: adminForm.reason.trim(),
    })
    detail.value.admins = res.data.data.admins
    adminForm.user_id = ''
    adminForm.reason = ''
    ElMessage.success('赛事子管理员已添加')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '添加失败')
  } finally {
    savingAdmin.value = false
  }
}

async function removeAdmin(row) {
  try {
    const reason = await promptReason('移除赛事子管理员')
    const res = await adminApi.delete(
      `/events/${route.params.eventId}/admins/${row.user_id}`,
      { data: { reason } }
    )
    detail.value.admins = res.data.data.admins
    ElMessage.success('已移除')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '移除失败')
  }
}

async function activateEvent() {
  const isReopen = detail.value.status === 'closed'
  try {
    await ElMessageBox.confirm(
      isReopen
        ? `确认批准重新开启赛事「${detail.value.name}」？`
        : `确认开启赛事「${detail.value.name}」？`,
      isReopen ? '批准重新开启' : '开启赛事',
      { type: 'info' }
    )
    const reason = await promptReason(isReopen ? '批准重新开启' : '开启赛事')
    const res = await adminApi.post(`/events/${route.params.eventId}/activate`, { reason })
    Object.assign(detail.value, res.data.data)
    ElMessage.success(isReopen ? '已批准重新开启' : '赛事已开启')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

async function rejectReopen() {
  try {
    const reason = await promptReason('拒绝重新开启申请')
    const res = await adminApi.post(`/events/${route.params.eventId}/reject-reopen`, { reason })
    Object.assign(detail.value, res.data.data)
    ElMessage.success('已拒绝重新开启申请')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

async function approveProfileChange() {
  try {
    const reason = await promptReason('通过资料修改')
    await adminApi.post(`/event-profile-changes/${pendingProfile.value.request_id}/approve`, {
      reason,
    })
    ElMessage.success('已通过资料修改')
    await load()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

async function rejectProfileChange() {
  try {
    const reason = await promptReason('拒绝资料修改')
    await adminApi.post(`/event-profile-changes/${pendingProfile.value.request_id}/reject`, {
      reason,
    })
    ElMessage.success('已拒绝资料修改')
    pendingProfile.value = null
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

async function closeEvent() {
  try {
    await ElMessageBox.confirm(
      `确认关闭赛事「${detail.value.name}」？关闭后该赛事将被封存，再次开启需平台管理员进行审核`,
      '关闭赛事',
      { type: 'warning' }
    )
    const reason = await promptReason('关闭赛事')
    const res = await adminApi.post(`/events/${route.params.eventId}/close`, { reason })
    Object.assign(detail.value, res.data.data)
    ElMessage.success('赛事已关闭')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '关闭失败')
  }
}

async function deleteEvent() {
  try {
    await ElMessageBox.confirm(
      `删除赛事将级联删除该赛事下全部牌谱（当前 ${detail.value.record_count ?? 0} 局），且不可恢复。`,
      '删除赛事',
      { type: 'error', confirmButtonText: '继续' }
    )
    const { value: confirmName } = await ElMessageBox.prompt(
      '请输入完整赛事名称以确认删除',
      '确认删除',
      {
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        inputPattern: /\S+/,
        inputErrorMessage: '请输入赛事名称',
      }
    )
    const reason = await promptReason('删除赛事')
    const res = await adminApi.delete(`/events/${route.params.eventId}`, {
      data: { reason, confirm_name: confirmName.trim() },
    })
    ElMessage.success(res.data.message || '赛事已删除')
    window.location.href = '/admin/events'
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

onMounted(load)
</script>

<style scoped>
.block {
  margin-top: 16px;
}
.actions {
  margin-top: 16px;
  display: flex;
  gap: 8px;
}
.current-owner {
  margin: 8px 0 0;
}
.uid {
  color: #909399;
  font-size: 12px;
}
.muted {
  color: #909399;
  margin: 8px 0 0;
}
.admin-count {
  color: #909399;
  font-weight: normal;
  font-size: 13px;
}
.desc-text {
  white-space: pre-wrap;
  line-height: 1.5;
}
.stats-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 12px;
}
.stats-totals {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
  gap: 8px 12px;
  margin-bottom: 12px;
  padding: 10px 12px;
  background: #fafbfc;
  border: 1px solid #ebeef5;
  border-radius: 6px;
}
.stats-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.stats-label {
  font-size: 12px;
  color: #909399;
}
.stats-value {
  font-size: 16px;
  font-weight: 600;
  color: #303133;
  font-variant-numeric: tabular-nums;
}
.stats-player-detail {
  margin-top: 12px;
  padding-top: 8px;
  border-top: 1px dashed #ebeef5;
}
.stats-heading {
  margin: 0 0 8px;
  font-size: 13px;
  color: #606266;
}
</style>
