<template>
  <div class="custom-room">
    <div class="section-title">
      <h2>自定义房间</h2>
      <el-space wrap>
        <el-button :disabled="!online || Boolean(currentRoom)" @click="openCreate">创建房间</el-button>
        <el-button :icon="Refresh" :disabled="!online" :loading="listBusy" @click="refreshRoomList">刷新列表</el-button>
      </el-space>
    </div>

    <el-card v-if="currentRoom" class="room-panel" shadow="never">
      <div class="room-panel__head">
        <div>
          <strong>{{ currentRoom.room_name || `房间 ${currentRoom.room_id}` }}</strong>
          <el-tag size="small" type="info">#{{ currentRoom.room_id }}</el-tag>
          <el-tag size="small">{{ subRuleLabel(currentRoom.sub_rule) }}</el-tag>
        </div>
        <el-button type="danger" plain @click="leaveRoom">离开房间</el-button>
      </div>

      <div class="seat-grid">
        <div v-for="(seat, idx) in seats" :key="`${seat.userId}-${idx}`" class="seat-card">
          <div class="seat-card__head">
            <span class="seat-idx">座位 {{ idx + 1 }}{{ idx === 0 ? ' · 房主' : '' }}</span>
            <el-button
              v-if="isHost && seat.userId !== null && Number(seat.userId) !== Number(myUserId)"
              class="seat-kick"
              size="small"
              text
              type="danger"
              @click="kickPlayer(seat.userId)"
            >移出</el-button>
          </div>
          <strong>{{ seat.username || '空位' }}</strong>
          <small v-if="seat.userId !== null">
            {{ seat.isBot ? '机器人' : (seat.ready || idx === 0 ? '已准备' : '未准备') }}
          </small>
        </div>
      </div>

      <el-space wrap class="room-actions">
        <template v-if="isHost">
          <el-button :disabled="!canAddBot" @click="addBot(false)">添加机器人</el-button>
          <el-button :disabled="!canAddBot" @click="addBot(true)">添加牌效机器人</el-button>
          <el-button type="primary" :disabled="!canStart" @click="startGame">开始对局</el-button>
        </template>
        <el-button v-else type="primary" @click="toggleReady">
          {{ selfReady ? '取消准备' : '准备' }}
        </el-button>
      </el-space>
    </el-card>

    <el-card v-else class="room-list-card" shadow="never">
      <el-table v-loading="listBusy" :data="visibleRooms" size="small" empty-text="暂无公开自定义房间">
        <el-table-column prop="room_id" label="房号" width="72" />
        <el-table-column prop="room_name" label="房间名" min-width="120" show-overflow-tooltip />
        <el-table-column label="人数" width="72">
          <template #default="{ row }">{{ (row.player_list || []).length }}/{{ row.max_player || 4 }}</template>
        </el-table-column>
        <el-table-column label="规则" min-width="100">
          <template #default="{ row }">
            <el-tag :type="isSupportedRoom(row) ? 'success' : 'info'" size="small">
              {{ ruleLabel(row) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="密码" width="64">
          <template #default="{ row }">{{ row.has_password ? '有' : '无' }}</template>
        </el-table-column>
        <el-table-column label="" width="88" align="right">
          <template #default="{ row }">
            <el-button
              size="small"
              type="primary"
              :disabled="!canJoinRoom(row)"
              @click="joinRoom(row)"
            >{{ isSupportedRoom(row) ? '加入' : '仅展示' }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="createOpen" title="创建国标房间" width="min(96vw, 720px)" destroy-on-close>
      <el-form label-position="top" @submit.prevent="submitCreate">
        <el-form-item label="房间名" required>
          <el-input v-model="createForm.room_name" maxlength="24" placeholder="例如：周末国标桌" />
        </el-form-item>
        <GuobiaoEmptyRoomConfig v-model="createForm" :sub-rule-options="STANDARD_SUB_RULE" />
        <el-button type="primary" class="full-btn" :loading="createBusy" @click="submitCreate">创建并进入</el-button>
      </el-form>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'
import { createDefaultGuobiaoRoomConfig } from '@/utils/guobiaoRoomConfig'
import { salasasaClient } from '@/game2d/salasasa/client'

const props = defineProps({
  online: { type: Boolean, default: false },
  myUserId: { type: [Number, String], default: null },
  joinedQueue: { type: [String, Object], default: null },
})

const emit = defineEmits(['occupy-changed'])

const SUB_RULE_LABELS = {
  'guobiao/standard': '国标标准',
  'guobiao/xiaolin': '小林',
  'guobiao/kshen': 'K神',
  'guobiao/lanshi': '蓝氏',
}

const RULE_LABELS = {
  guobiao: '国标',
  riichi: '立直',
  qingque: '青雀',
  changsha: '长沙',
  sichuan: '四川',
  jiandan: '简单麻将',
  classical: '古典麻将',
}

const STANDARD_SUB_RULE = [{ value: 'guobiao/standard', label: '国标标准' }]

const roomList = ref([])
const listBusy = ref(false)
const currentRoom = ref(null)
const createOpen = ref(false)
const createBusy = ref(false)
const createForm = reactive(createDefaultGuobiaoRoomConfig())

const visibleRooms = computed(() =>
  (roomList.value || []).filter((room) => !room.is_game_running),
)
let refreshTimer = null

const seats = computed(() => {
  const room = currentRoom.value
  if (!room) return []
  const list = room.player_list || []
  const settings = room.player_settings || {}
  const readyList = room.ready_list || []
  const max = room.max_player || 4
  return Array.from({ length: max }, (_, idx) => {
    const userId = list[idx]
    if (userId == null) return { userId: null, username: '', ready: false, isBot: false }
    const meta = settings[userId] || settings[String(userId)] || {}
    return {
      userId,
      username: meta.username || `玩家 ${userId}`,
      ready: readyList.includes(userId) || userId <= 10,
      isBot: userId <= 10,
    }
  })
})

const isHost = computed(() => {
  const room = currentRoom.value
  if (!room || props.myUserId == null) return false
  return Number(room.host_user_id ?? room.player_list?.[0]) === Number(props.myUserId)
})

const selfReady = computed(() => {
  const room = currentRoom.value
  if (!room || props.myUserId == null) return false
  return (room.ready_list || []).includes(Number(props.myUserId))
})

const canAddBot = computed(() => {
  const room = currentRoom.value
  if (!room) return false
  return (room.player_list || []).length < (room.max_player || 4)
})

const canStart = computed(() => {
  const room = currentRoom.value
  if (!room || !isHost.value) return false
  const list = room.player_list || []
  if (list.length < 4) return false
  const readyList = room.ready_list || []
  return list.slice(1).every((uid) => uid <= 10 || readyList.includes(uid))
})

function subRuleLabel(rule) {
  return SUB_RULE_LABELS[rule] || rule || '国标'
}

function isSupportedRoom(room) {
  return room?.room_rule === 'guobiao' && (room.sub_rule || 'guobiao/standard') === 'guobiao/standard'
}

function ruleLabel(room) {
  if (room?.sub_rule) return subRuleLabel(room.sub_rule)
  return RULE_LABELS[room?.room_rule] || room?.room_rule || '未知规则'
}

function canJoinRoom(room) {
  if (!props.online || props.joinedQueue || !isSupportedRoom(room)) return false
  return (room.player_list || []).length < (room.max_player || 4)
}

function setCurrentRoom(room) {
  currentRoom.value = room
  emit('occupy-changed', Boolean(room))
}

function handleResponse(response) {
  if (response.type === 'room/get_room_list') {
    listBusy.value = false
    roomList.value = Array.isArray(response.room_list) ? response.room_list : []
  }
  if (response.type === 'room/create_room_done' && response.success && response.room_info) {
    createBusy.value = false
    createOpen.value = false
    setCurrentRoom(response.room_info)
    ElMessage.success(response.message || '房间创建成功')
  }
  if (response.type === 'room/join_room_done' && response.success) {
    ElMessage.success(response.message || '已加入房间')
    // 完整房间信息由随后的 room/refresh_room_info 推送
  }
  if (response.type === 'error_message' && response.success === false) {
    createBusy.value = false
    listBusy.value = false
  }
  if (response.type === 'room/refresh_room_info' && response.room_info) {
    const info = response.room_info
    const mine = Number(props.myUserId)
    if (mine && (info.player_list || []).some((id) => Number(id) === mine)) {
      setCurrentRoom(info)
    } else if (currentRoom.value && String(currentRoom.value.room_id) === String(info.room_id)) {
      setCurrentRoom(null)
    }
  }
  if (response.type === 'room/sync_not_in_room') {
    setCurrentRoom(null)
  }
  if (response.type === 'room/leave_room_done') {
    if (response.success) {
      setCurrentRoom(null)
      refreshRoomList()
    }
  }
  if (response.type === 'tips' && response.message && response.success === false) {
    createBusy.value = false
  }
}

function refreshRoomList(showLoading = true) {
  if (!props.online) return
  if (showLoading) listBusy.value = true
  if (!salasasaClient.send({ type: 'room/get_room_list', show_tip: false })) {
    listBusy.value = false
    ElMessage.error('游戏连接尚未就绪')
  }
}

function openCreate() {
  if (!props.online) {
    ElMessage.warning('请先登录并连接游戏服务')
    return
  }
  if (props.joinedQueue) {
    ElMessage.warning('请先取消匹配再创建房间')
    return
  }
  Object.assign(createForm, createDefaultGuobiaoRoomConfig(), {
    room_name: `${props.myUserId || '玩家'}的房间`,
    sub_rule: 'guobiao/standard',
    tips: true,
    tactical_call: true,
  })
  createOpen.value = true
}

function submitCreate() {
  const roomname = String(createForm.room_name || '').trim()
  if (!roomname) {
    ElMessage.warning('请填写房间名')
    return
  }
  createBusy.value = true
  const ok = salasasaClient.send({
    type: 'room/create_GB_room',
    rule: 'guobiao',
    sub_rule: 'guobiao/standard',
    roomname,
    gameround: Number(createForm.game_round) || 4,
    roundTimerValue: Number(createForm.round_timer) || 0,
    stepTimerValue: Number(createForm.step_timer) || 0,
    tips: !!createForm.tips,
    password: String(createForm.password || ''),
    random_seed: 0,
    open_cuohe: !!createForm.open_cuohe,
    cuohe_type: createForm.open_cuohe ? Number(createForm.cuohe_type) || 0 : 0,
    hepai_limit: Math.max(1, Math.min(64, Number(createForm.hepai_limit) || 8)),
    tourist_limit: !!createForm.tourist_limit,
    allow_spectator: createForm.allow_spectator !== false,
    tactical_call: !!createForm.tactical_call,
    claim_protection: createForm.claim_protection !== false,
  })
  if (!ok) {
    createBusy.value = false
    ElMessage.error('游戏连接尚未就绪')
  }
}

async function joinRoom(row) {
  if (!props.online) return
  if (!isSupportedRoom(row)) {
    ElMessage.info('2D 测试版目前仅支持进入国标标准房间')
    return
  }
  if (props.joinedQueue) {
    ElMessage.warning('请先取消匹配再加入房间')
    return
  }
  let password = ''
  if (row.has_password) {
    try {
      const { value } = await ElMessageBox.prompt('请输入房间密码', `加入房间 #${row.room_id}`, {
        inputType: 'password',
        confirmButtonText: '加入',
        cancelButtonText: '取消',
      })
      password = value || ''
    } catch {
      return
    }
  }
  if (!salasasaClient.send({ type: 'room/join_room', room_id: String(row.room_id), password })) {
    ElMessage.error('游戏连接尚未就绪')
  }
}

function leaveRoom() {
  const room = currentRoom.value
  if (!room) return
  if (!salasasaClient.send({ type: 'room/leave_room', room_id: String(room.room_id) })) {
    ElMessage.error('游戏连接尚未就绪')
  }
}

function addBot(smart) {
  const room = currentRoom.value
  if (!room) return
  const type = smart ? 'room/add_smart_bot' : 'room/add_bot'
  if (!salasasaClient.send({ type, room_id: String(room.room_id) })) {
    ElMessage.error('游戏连接尚未就绪')
  }
}

function toggleReady() {
  const room = currentRoom.value
  if (!room) return
  if (!salasasaClient.send({
    type: 'room/set_ready',
    room_id: String(room.room_id),
    ready: !selfReady.value,
  })) {
    ElMessage.error('游戏连接尚未就绪')
  }
}

function startGame() {
  const room = currentRoom.value
  if (!room) return
  if (!salasasaClient.send({ type: 'room/start_game', room_id: String(room.room_id) })) {
    ElMessage.error('游戏连接尚未就绪')
  }
}

function kickPlayer(targetUserId) {
  const room = currentRoom.value
  if (!room) return
  if (!salasasaClient.send({
    type: 'room/kick_player',
    room_id: String(room.room_id),
    target_user_id: Number(targetUserId),
  })) {
    ElMessage.error('游戏连接尚未就绪')
  }
}

function syncMyRoom() {
  salasasaClient.send({ type: 'room/sync_my_room' })
}

watch(() => props.online, (online) => {
  if (refreshTimer) window.clearInterval(refreshTimer)
  refreshTimer = null
  if (online) {
    refreshRoomList()
    syncMyRoom()
    refreshTimer = window.setInterval(() => refreshRoomList(false), 5_000)
  } else {
    setCurrentRoom(null)
    roomList.value = []
  }
}, { immediate: true })

onBeforeUnmount(() => {
  if (refreshTimer) window.clearInterval(refreshTimer)
})

defineExpose({ handleResponse, refreshRoomList, hasRoom: () => Boolean(currentRoom.value) })
</script>

<style scoped>
.custom-room { width: 100%; }
.section-title { min-height: 52px; display: flex; align-items: flex-start; justify-content: space-between; gap: 18px; margin-bottom: 14px; }
.section-title h2 { margin: 0; color: #262626; font-size: 21px; font-weight: 600; }
.room-panel, .room-list-card { background: #fff; border-color: #e8e8e8; }
.room-panel__head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 16px; }
.room-panel__head > div { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.seat-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; margin-bottom: 16px; }
.seat-card { min-height: 92px; padding: 12px; display: flex; flex-direction: column; gap: 4px; border: 1px solid #e8e8e8; border-radius: 8px; background: #fafafa; }
.seat-card__head { min-height: 24px; display: flex; align-items: center; justify-content: space-between; gap: 6px; }
.seat-idx { font-size: 12px; color: #8c8c8c; }
.seat-kick { flex: none; margin: -5px -6px -5px 0; }
.seat-card strong { font-size: 15px; }
.seat-card small { color: #595959; }
.room-actions { width: 100%; }
.full-btn { width: 100%; margin-top: 8px; }
@media (max-width: 860px) {
  .seat-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .section-title { flex-direction: column; }
}
</style>
