<template>
  <div v-loading="loading" class="emp">
    <header class="emp-head">
      <div class="emp-head-main">
        <h2 class="emp-name">{{ detail?.name || '加载中…' }}</h2>
        <div class="emp-meta" v-if="detail">
          <el-tag :type="eventStatusTagType(detail.status)" size="small" effect="dark">
            {{ eventStatusLabel(detail.status) }}
          </el-tag>
          <el-tag v-if="detail.reopen_requested" type="warning" size="small">待审核再开</el-tag>
          <el-tag type="info" size="small" effect="plain">{{ eventRoleLabel(detail.my_role) }}</el-tag>
          <span class="emp-id">ID {{ detail.event_id }}</span>
          <span class="emp-stat">牌谱 {{ detail.record_count ?? 0 }}</span>
        </div>
      </div>
      <div class="emp-head-actions">
        <router-link
          v-if="detail"
          class="emp-public-link"
          :to="`/events/${detail.event_id}`"
          target="_blank"
        >公开详情</router-link>
        <el-button text type="primary" @click="$emit('close')">收起</el-button>
      </div>
    </header>

    <template v-if="detail">
      <section class="emp-lifecycle" :class="detail.status">
        <p class="emp-lifecycle-text">{{ lifecycleHint }}</p>
        <div v-if="isOwner" class="emp-lifecycle-actions">
          <el-button
            v-if="detail.status === 'registered'"
            type="primary"
            @click="openEvent"
          >开始赛事</el-button>
          <el-button
            v-if="detail.status === 'active'"
            type="warning"
            @click="closeEvent"
          >关闭赛事</el-button>
          <el-button
            v-if="detail.status === 'closed' && !detail.reopen_requested"
            type="primary"
            plain
            @click="requestReopen"
          >申请重新开启</el-button>
          <el-tag v-if="detail.status === 'closed' && detail.reopen_requested" type="warning">
            已提交申请，等待平台管理员审核
          </el-tag>
        </div>
      </section>

      <section class="emp-desc">
        <h3 class="emp-sec-title">赛事介绍</h3>
        <p class="emp-desc-body">{{ detail.description?.trim() || '暂无介绍' }}</p>
        <dl class="emp-desc-meta">
          <div><dt>创建时间</dt><dd>{{ formatDate(detail.created_at) }}</dd></div>
          <div v-if="detail.closed_at"><dt>关闭时间</dt><dd>{{ formatDate(detail.closed_at) }}</dd></div>
        </dl>
      </section>

      <el-tabs v-model="activeTab" class="emp-tabs">
        <el-tab-pane v-if="isOwner" label="赛事资料" name="profile">
          <el-alert
            title="修改赛事名或赛事简介需提交平台管理员审核，通过后才会在公开页生效。"
            type="info"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <el-alert
            v-if="pendingProfile"
            :title="`待审核：拟改为「${pendingProfile.proposed_name}」`"
            type="warning"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <el-form label-position="top" class="emp-profile-form" @submit.prevent="submitProfileChange">
            <el-form-item label="赛事名称">
              <el-input v-model="profileForm.name" maxlength="128" show-word-limit />
            </el-form-item>
            <el-form-item label="赛事简介">
              <el-input
                v-model="profileForm.description"
                type="textarea"
                :rows="5"
                maxlength="2000"
                show-word-limit
              />
            </el-form-item>
            <el-form-item label="修改说明">
              <el-input v-model="profileForm.reason" placeholder="可选，供审核参考" />
            </el-form-item>
            <el-form-item>
              <el-button
                type="primary"
                :loading="savingProfile"
                :disabled="!!pendingProfile"
                @click="submitProfileChange"
              >提交审核</el-button>
              <el-button
                v-if="pendingProfile"
                :loading="cancellingProfile"
                @click="cancelProfileChange"
              >撤销申请</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="比赛公告" name="announcements">
          <el-alert type="info" :closable="false" show-icon class="emp-alert emp-announce-tip">
            <template #title>发布比赛公告说明</template>
            <p class="tip-lead">
              您在包括且不限于以下情形时都可以发布比赛公告，并且您应该在1、4条所属情形发生时发布比赛公告，本平台对此不做严格规定。
            </p>
            <ol class="tip-list">
              <li>赛事规则的最新变更或者活动通知</li>
              <li>传达赛事中出现的精彩片段</li>
              <li>发布比赛的中途对阵情况，帮助玩家更好的观看享受比赛</li>
              <li>公布比赛中途或最终的排名以及奖励获得者</li>
              <li>对比赛中的违规行为进行公布</li>
            </ol>
          </el-alert>
          <el-form label-position="top" class="emp-profile-form" @submit.prevent="publishAnnouncement">
            <el-form-item label="标题">
              <el-input v-model="announceForm.title" maxlength="200" show-word-limit />
            </el-form-item>
            <el-form-item label="内容">
              <el-input
                v-model="announceForm.body"
                type="textarea"
                :rows="5"
                maxlength="10000"
                show-word-limit
              />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="publishingAnnounce" @click="publishAnnouncement">
                发布公告
              </el-button>
              <el-button text type="primary" :loading="loadingAnnouncements" @click="loadAnnouncements">
                刷新
              </el-button>
            </el-form-item>
          </el-form>
          <el-table
            :data="announcements"
            size="small"
            v-loading="loadingAnnouncements"
            empty-text="暂无公告"
          >
            <el-table-column prop="title" label="标题" min-width="140" />
            <el-table-column label="内容" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">{{ row.body }}</template>
            </el-table-column>
            <el-table-column label="作者" width="110">
              <template #default="{ row }">{{ row.author_username || row.created_by }}</template>
            </el-table-column>
            <el-table-column label="时间" min-width="150">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ row }">
                <el-button
                  v-if="canDeleteAnnouncement(row)"
                  link
                  type="danger"
                  @click="deleteAnnouncement(row)"
                >删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane v-if="isOwner" label="子管理员" name="admins">
          <div class="emp-tab-bar">
            <el-form inline class="emp-form" @submit.prevent="addAdmin">
              <el-form-item label="用户 ID">
                <el-input v-model="adminForm.user_id" clearable style="width: 140px" />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" :loading="savingAdmin" @click="addAdmin">添加子管理员</el-button>
              </el-form-item>
            </el-form>
            <span class="emp-count">{{ adminList.length }} / 10</span>
          </div>
          <el-table :data="adminList" size="small" empty-text="暂无赛事子管理员">
            <el-table-column prop="username" label="用户名" min-width="120" />
            <el-table-column prop="user_id" label="用户 ID" width="120" />
            <el-table-column label="添加时间" min-width="150">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="90">
              <template #default="{ row }">
                <el-button link type="danger" @click="removeAdmin(row)">移除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="房间" name="rooms">
          <el-alert
            v-if="detail.status !== 'active'"
            :title="detail.status === 'registered' ? '赛事尚未开启，无法创建房间' : '赛事已关闭，无法创建房间'"
            type="info"
            :closable="false"
            show-icon
            class="emp-alert"
          />
          <el-form class="emp-form emp-room-form" label-width="88px" @submit.prevent="createRoom">
            <div class="emp-tab-bar">
              <el-form-item label="规则">
                <el-select v-model="roomForm.room_rule" style="width: 120px">
                  <el-option
                    v-for="opt in roomRuleOptions"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>
              <el-form-item label="房间名">
                <el-input v-model="roomForm.room_name" clearable style="width: 160px" placeholder="可选" />
              </el-form-item>
              <el-form-item>
                <el-button
                  type="primary"
                  :loading="creatingRoom"
                  :disabled="detail.status !== 'active'"
                  @click="createRoom"
                >创建空房间</el-button>
              </el-form-item>
              <el-button text type="primary" :loading="loadingRooms" @click="loadRooms">刷新</el-button>
            </div>
            <GuobiaoEmptyRoomConfig v-if="roomForm.room_rule === 'guobiao'" v-model="roomForm" />
            <el-alert
              v-else
              title="当前仅国标空房间提供完整对局配置；其他规则仍按服务端默认参数创建。"
              type="info"
              :closable="false"
              show-icon
              class="emp-alert"
            />
          </el-form>
          <el-table :data="rooms" size="small" v-loading="loadingRooms" empty-text="暂无房间">
            <el-table-column prop="room_name" label="名称" min-width="100" />
            <el-table-column prop="room_id" label="房间 ID" min-width="100" />
            <el-table-column prop="room_rule" label="规则" width="90" />
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
            <el-table-column label="操作" width="90">
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
        </el-tab-pane>

        <el-tab-pane label="进行中对局" name="games">
          <div class="emp-tab-bar emp-tab-bar--end">
            <el-button text type="primary" :loading="loadingGames" @click="loadGames">刷新</el-button>
          </div>
          <el-table :data="games" size="small" v-loading="loadingGames" empty-text="当前没有本赛事进行中的对局">
            <el-table-column prop="gamestate_id" label="对局 ID" min-width="150" />
            <el-table-column prop="room_rule" label="规则" width="90" />
            <el-table-column prop="game_status" label="状态机" width="110" />
            <el-table-column label="投票/暂停" width="110">
              <template #default="{ row }">
                <el-tag size="small">{{ phaseLabel(row.vote_phase) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="玩家" min-width="160">
              <template #default="{ row }">
                <span v-for="p in row.players" :key="p.user_id" class="player-chip">
                  {{ p.username }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="220" fixed="right">
              <template #default="{ row }">
                <el-button size="small" :disabled="isPauseDisabled(row)" @click="onPause(row)">暂停</el-button>
                <el-button
                  size="small"
                  type="success"
                  :disabled="isResumeDisabled(row)"
                  @click="onResume(row)"
                >解除</el-button>
                <el-button size="small" type="danger" @click="onEnd(row)">结束对局</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>

        <el-tab-pane label="牌谱" name="records">
          <div class="emp-tab-bar emp-tab-bar--end">
            <el-button text type="primary" :loading="loadingRecords" @click="loadRecords">刷新</el-button>
          </div>
          <el-table :data="records" size="small" v-loading="loadingRecords" empty-text="暂无牌谱">
            <el-table-column prop="game_id" label="对局 ID" min-width="120" />
            <el-table-column prop="rule" label="规则" width="90" />
            <el-table-column prop="sub_rule" label="子规则" min-width="120" />
            <el-table-column label="时间" min-width="150">
              <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
            </el-table-column>
            <el-table-column label="玩家" min-width="200">
              <template #default="{ row }">
                <span v-for="p in row.players" :key="p.user_id" class="player-chip">
                  {{ p.rank }}位 {{ p.username }}({{ p.score }})
                </span>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </template>
  </div>
</template>

<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import eventAdminApi from '@/api/eventAdminClient'
import { useEventAdminAuthStore } from '@/stores/eventAdminAuth'
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'
import {
  buildGuobiaoRoomPayload,
  createDefaultGuobiaoRoomConfig,
} from '@/utils/guobiaoRoomConfig'
import { eventRoleLabel, eventStatusLabel, eventStatusTagType } from '@/utils/eventMeta'

const props = defineProps({
  eventId: { type: String, required: true },
})
const emit = defineEmits(['close', 'updated'])
const eventAuth = useEventAdminAuthStore()

const loading = ref(false)
const detail = ref(null)
const activeTab = ref('rooms')
const savingAdmin = ref(false)
const adminForm = reactive({ user_id: '' })

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
  ...createDefaultGuobiaoRoomConfig(),
})

const games = ref([])
const loadingGames = ref(false)
const records = ref([])
const loadingRecords = ref(false)

const profileForm = reactive({ name: '', description: '', reason: '' })
const pendingProfile = ref(null)
const savingProfile = ref(false)
const cancellingProfile = ref(false)

const announcements = ref([])
const loadingAnnouncements = ref(false)
const publishingAnnounce = ref(false)
const announceForm = reactive({ title: '', body: '' })

const isOwner = computed(() => detail.value?.my_role === 'owner')
const adminList = computed(() => (detail.value?.admins || []).filter((a) => a.role === 'admin'))

function canDeleteAnnouncement(row) {
  if (isOwner.value) return true
  return Number(row.created_by) === Number(eventAuth.userId)
}

const lifecycleHint = computed(() => {
  const s = detail.value?.status
  if (s === 'registered') {
    return '赛事注册成功，在比赛开赛时可点击开始赛事按钮，开启赛事以后可开始创建比赛房间'
  }
  if (s === 'active') {
    return '赛事已开启，在比赛全程结束后可点击关闭赛事按钮结束赛事，在关闭结束后仍可以查看赛事数据对赛事内容进行统计或查询。'
  }
  if (s === 'closed') {
    return detail.value?.reopen_requested
      ? '赛事已关闭，重新开启申请审核中。'
      : '赛事已关闭，无法创建房间。如需再次开启，请提交申请由平台管理员审核。'
  }
  return ''
})

const PHASE_LABELS = {
  idle: '无',
  voting: '投票中',
  pause_pending: '待暂停',
  paused: '已暂停',
  resume_voting: '解除投票',
  resume_countdown: '解除倒计时',
  end_countdown: '结束倒计时',
}

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '—'
}
function phaseLabel(p) {
  return PHASE_LABELS[p] || p || '无'
}
function isPauseDisabled(row) {
  return row.vote_phase === 'paused' || row.vote_phase === 'pause_pending'
}
function isResumeDisabled(row) {
  return row.vote_phase !== 'paused' && row.vote_phase !== 'pause_pending'
}

async function loadRooms() {
  loadingRooms.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/rooms`)
    rooms.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载房间失败')
    rooms.value = []
  } finally {
    loadingRooms.value = false
  }
}

async function loadGames() {
  loadingGames.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/games`)
    games.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载对局失败')
    games.value = []
  } finally {
    loadingGames.value = false
  }
}

async function loadRecords() {
  loadingRecords.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/records`)
    records.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载牌谱失败')
    records.value = []
  } finally {
    loadingRecords.value = false
  }
}

async function loadProfileChange() {
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/profile-change`)
    const data = res.data.data || {}
    profileForm.name = data.current_name || detail.value?.name || ''
    profileForm.description = data.current_description || detail.value?.description || ''
    pendingProfile.value = data.pending || null
  } catch {
    profileForm.name = detail.value?.name || ''
    profileForm.description = detail.value?.description || ''
    pendingProfile.value = null
  }
}

async function loadAnnouncements() {
  loadingAnnouncements.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}/announcements`)
    announcements.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载公告失败')
    announcements.value = []
  } finally {
    loadingAnnouncements.value = false
  }
}

async function submitProfileChange() {
  if (!profileForm.name.trim()) {
    ElMessage.warning('请填写赛事名称')
    return
  }
  savingProfile.value = true
  try {
    const res = await eventAdminApi.post(`/events/${props.eventId}/profile-change`, {
      name: profileForm.name.trim(),
      description: profileForm.description,
      reason: profileForm.reason.trim(),
    })
    pendingProfile.value = res.data.data?.pending || null
    profileForm.reason = ''
    ElMessage.success(res.data.message || '已提交审核')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '提交失败')
  } finally {
    savingProfile.value = false
  }
}

async function cancelProfileChange() {
  cancellingProfile.value = true
  try {
    await eventAdminApi.post(`/events/${props.eventId}/profile-change/cancel`)
    pendingProfile.value = null
    ElMessage.success('已撤销申请')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '撤销失败')
  } finally {
    cancellingProfile.value = false
  }
}

async function publishAnnouncement() {
  if (!announceForm.title.trim() || !announceForm.body.trim()) {
    ElMessage.warning('请填写公告标题与内容')
    return
  }
  publishingAnnounce.value = true
  try {
    const res = await eventAdminApi.post(`/events/${props.eventId}/announcements`, {
      title: announceForm.title.trim(),
      body: announceForm.body.trim(),
    })
    announcements.value = res.data.data?.items || []
    announceForm.title = ''
    announceForm.body = ''
    ElMessage.success(res.data.message || '公告已发布')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '发布失败')
  } finally {
    publishingAnnounce.value = false
  }
}

async function deleteAnnouncement(row) {
  try {
    await ElMessageBox.confirm(`确认删除公告「${row.title}」？`, '删除公告', { type: 'warning' })
    const res = await eventAdminApi.delete(
      `/events/${props.eventId}/announcements/${row.announcement_id}`
    )
    announcements.value = res.data.data?.items || []
    ElMessage.success('公告已删除')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

async function load() {
  if (!props.eventId) return
  loading.value = true
  try {
    const res = await eventAdminApi.get(`/events/${props.eventId}`)
    detail.value = res.data.data
    activeTab.value = isOwner.value ? 'profile' : 'announcements'
    if (detail.value?.status === 'active') activeTab.value = 'rooms'
    await Promise.all([
      loadRooms(),
      loadGames(),
      loadRecords(),
      loadAnnouncements(),
      isOwner.value ? loadProfileChange() : Promise.resolve(),
    ])
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
    detail.value = null
  } finally {
    loading.value = false
  }
}

function applyEventPatch(data) {
  Object.assign(detail.value, data)
  emit('updated')
}

async function openEvent() {
  try {
    await ElMessageBox.confirm(
      `确认开启赛事「${detail.value.name}」？`,
      '开启赛事',
      { type: 'info' }
    )
    const res = await eventAdminApi.post(`/events/${props.eventId}/open`, {})
    applyEventPatch(res.data.data)
    ElMessage.success('赛事已开启')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '开启失败')
  }
}

async function closeEvent() {
  try {
    await ElMessageBox.confirm(
      `确认关闭赛事「${detail.value.name}」？关闭后该赛事将被封存，再次开启需平台管理员进行审核`,
      '关闭赛事',
      { type: 'warning' }
    )
    const res = await eventAdminApi.post(`/events/${props.eventId}/close`, {})
    applyEventPatch(res.data.data)
    ElMessage.success('赛事已关闭')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '关闭失败')
  }
}

async function requestReopen() {
  try {
    await ElMessageBox.confirm(
      `确认申请重新开启赛事「${detail.value.name}」？`,
      '申请重新开启',
      { type: 'info' }
    )
    const res = await eventAdminApi.post(`/events/${props.eventId}/request-reopen`, {})
    applyEventPatch(res.data.data)
    ElMessage.success(res.data.message || '已提交重新开启申请')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '申请失败')
  }
}

async function addAdmin() {
  if (!adminForm.user_id.trim()) {
    ElMessage.warning('请填写用户 ID')
    return
  }
  savingAdmin.value = true
  try {
    const res = await eventAdminApi.post(`/events/${props.eventId}/admins`, {
      user_id: adminForm.user_id.trim(),
    })
    detail.value.admins = res.data.data.admins
    adminForm.user_id = ''
    ElMessage.success('赛事子管理员已添加')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '添加失败')
  } finally {
    savingAdmin.value = false
  }
}

async function removeAdmin(row) {
  try {
    await ElMessageBox.confirm(
      `确认移除赛事子管理员「${row.username || row.user_id}」？`,
      '移除赛事子管理员',
      { type: 'warning' }
    )
    const res = await eventAdminApi.delete(`/events/${props.eventId}/admins/${row.user_id}`)
    detail.value.admins = res.data.data.admins
    ElMessage.success('已移除')
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '移除失败')
  }
}

async function createRoom() {
  creatingRoom.value = true
  try {
    let room_config = {}
    let password = ''
    if (roomForm.room_rule === 'guobiao') {
      ;({ room_config, password } = buildGuobiaoRoomPayload(roomForm))
    } else if (roomForm.room_name.trim()) {
      room_config.room_name = roomForm.room_name.trim()
    }
    await eventAdminApi.post(`/events/${props.eventId}/rooms`, {
      room_rule: roomForm.room_rule,
      room_config,
      password,
    })
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
    await ElMessageBox.confirm(
      `确认删除房间 ${row.room_id}？`,
      '删除房间',
      { type: 'warning' }
    )
    await eventAdminApi.delete(`/events/${props.eventId}/rooms/${row.room_id}`)
    ElMessage.success('房间已删除')
    await loadRooms()
  } catch (e) {
    if (e === 'cancel' || e === 'close') return
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

async function callControl(action, row, confirmText) {
  try {
    await ElMessageBox.confirm(confirmText, '确认操作', { type: 'warning' })
  } catch (_) {
    return
  }
  try {
    const res = await eventAdminApi.post(
      `/events/${props.eventId}/games/${row.gamestate_id}/${action}`
    )
    ElMessage.success(res.data.message || '操作成功')
    await loadGames()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

function onPause(row) {
  callControl('pause', row, '确认强制暂停该对局？')
}
function onResume(row) {
  callControl('resume', row, '确认强制解除暂停？')
}
async function onEnd(row) {
  try {
    await ElMessageBox.confirm('确认强制结束该对局？玩家将被踢回大厅。', '危险操作', {
      type: 'error',
      confirmButtonText: '结束对局',
    })
  } catch (_) {
    return
  }
  try {
    const res = await eventAdminApi.post(
      `/events/${props.eventId}/games/${row.gamestate_id}/end`
    )
    ElMessage.success(res.data.message || '已结束')
    await loadGames()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

watch(
  () => props.eventId,
  () => {
    load()
  },
  { immediate: true }
)
</script>

<style scoped>
.emp {
  margin-top: 20px;
  padding: 20px 20px 8px;
  border: 1px solid #e4e7ed;
  border-radius: 10px;
  background: #fafbfc;
}
.emp-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}
.emp-head-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.emp-public-link {
  font-size: 13px;
  color: #409eff;
  text-decoration: none;
  padding: 0 8px;
  white-space: nowrap;
}
.emp-public-link:hover {
  text-decoration: underline;
}
.emp-name {
  margin: 0 0 8px;
  font-size: 22px;
  font-weight: 700;
  color: #1f2329;
  line-height: 1.3;
}
.emp-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.emp-id,
.emp-stat {
  font-size: 12px;
  color: #909399;
  font-family: ui-monospace, Consolas, monospace;
}
.emp-lifecycle {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border-radius: 8px;
  margin-bottom: 16px;
}
.emp-lifecycle.registered {
  background: #f4f4f5;
  border: 1px solid #e9e9eb;
}
.emp-lifecycle.active {
  background: #f0f9eb;
  border: 1px solid #e1f3d8;
}
.emp-lifecycle.closed {
  background: #fef0f0;
  border: 1px solid #fde2e2;
}
.emp-lifecycle-text {
  margin: 0;
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: #606266;
  line-height: 1.5;
}
.emp-lifecycle-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
.emp-desc {
  margin-bottom: 16px;
  padding: 16px;
  background: #fff;
  border-radius: 8px;
  border: 1px solid #ebeef5;
}
.emp-sec-title {
  margin: 0 0 10px;
  font-size: 13px;
  font-weight: 600;
  color: #909399;
  letter-spacing: 0.04em;
}
.emp-desc-body {
  margin: 0;
  white-space: pre-wrap;
  line-height: 1.7;
  font-size: 14px;
  color: #303133;
  min-height: 2.4em;
}
.emp-desc-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 16px 28px;
  margin: 14px 0 0;
  padding-top: 12px;
  border-top: 1px dashed #ebeef5;
}
.emp-desc-meta div {
  display: flex;
  gap: 8px;
  font-size: 12px;
}
.emp-desc-meta dt {
  color: #909399;
  margin: 0;
}
.emp-desc-meta dd {
  margin: 0;
  color: #606266;
}
.emp-tabs {
  background: #fff;
  border-radius: 8px;
  border: 1px solid #ebeef5;
  padding: 0 12px 12px;
}
.emp-tab-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}
.emp-tab-bar--end {
  justify-content: flex-end;
}
.emp-form {
  flex-wrap: wrap;
}
.emp-count {
  color: #909399;
  font-size: 13px;
  line-height: 32px;
}
.emp-room-form :deep(.el-form-item) {
  margin-bottom: 10px;
}
.emp-alert {
  margin-bottom: 12px;
}
.emp-profile-form {
  max-width: 640px;
}
.emp-announce-tip :deep(.el-alert__content) {
  width: 100%;
}
.tip-lead {
  margin: 0 0 8px;
  font-size: 13px;
  line-height: 1.6;
  color: #606266;
}
.tip-list {
  margin: 0;
  padding-left: 1.2em;
  font-size: 13px;
  line-height: 1.7;
  color: #606266;
}
.player-chip {
  display: inline-block;
  margin-right: 8px;
  font-size: 12px;
}
</style>
