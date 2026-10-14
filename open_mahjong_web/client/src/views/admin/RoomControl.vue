<template>
  <div>
    <h2 class="page-title">房间管理</h2>
    <el-card>
      <div class="toolbar">
        <el-button type="primary" @click="openCreate">创建房间</el-button>
        <el-button :loading="loading" @click="fetchList">刷新列表</el-button>
        <el-input v-model="keyword" placeholder="房间号、名称、房主或玩家" clearable class="search" />
        <el-select v-model="status" class="status" aria-label="房间状态">
          <el-option label="全部状态" value="all" />
          <el-option label="等待中" value="waiting" />
          <el-option label="对局中" value="playing" />
          <el-option label="无人在线" value="offline" />
        </el-select>
        <el-checkbox v-model="autoRefresh">自动刷新（5s）</el-checkbox>
      </div>
      <el-table :data="filteredItems" v-loading="loading" class="room-table" empty-text="没有符合条件的房间">
        <el-table-column prop="room_id" label="房间号" width="100" />
        <el-table-column prop="room_name" label="房间名称" min-width="160" />
        <el-table-column label="所属" min-width="150">
          <template #default="{ row }">{{ row.event_id ? `${row.venue_kind === 'base' ? '基地' : '赛事'} · ${row.event_name || row.event_id}` : '普通房间' }}</template>
        </el-table-column>
        <el-table-column label="玩法" min-width="180">
          <template #default="{ row }">{{ row.sub_rule ? formatSubRule(row.sub_rule) : formatRule(row.room_rule) }}</template>
        </el-table-column>
        <el-table-column label="房主" min-width="140">
          <template #default="{ row }">{{ row.host_user_id ? `${row.host_name} (${row.host_user_id})` : '待入座' }}</template>
        </el-table-column>
        <el-table-column label="玩家" min-width="230">
          <template #default="{ row }">
            <div v-for="p in row.players" :key="p.seat_index" class="player">
              {{ p.username }}<span class="muted"> ({{ p.user_id }})</span>
              <el-tag :type="p.is_bot ? 'info' : p.online ? 'success' : 'warning'" size="small">{{ p.is_bot ? '机器人' : p.online ? '在线' : '离线' }}</el-tag>
            </div>
            <span v-if="!row.players.length" class="muted">空房间</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="145">
          <template #default="{ row }">
            <el-tag :type="row.is_game_running ? 'success' : row.online_count ? 'info' : 'warning'">{{ row.is_game_running ? '对局中' : '等待中' }}</el-tag>
            <div class="muted">{{ row.players.length }}/{{ row.max_player }} 座 · {{ row.online_count }} 人在线</div>
            <div v-if="row.duplicate_match_id" class="muted">{{ row.duplicate_table === 'open' ? '开室' : '闭室' }} · 配对房间</div>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="danger" :loading="closing === row.room_id" :disabled="Boolean(closing) || row.event_seating_pending" @click="closeRoom(row)">关闭房间</el-button>
          </template>
        </el-table-column>
      </el-table>
      <p class="muted">共 {{ filteredItems.length }} 个房间。赛事、基地及管理员创建的房间允许保留空房，玩家创建的普通房间无人后自动解散。</p>
    </el-card>
    <VenueRoomDialog v-model="createVisible" :form="form" :loading="creating" :room-rule-options="roomRuleOptions" :show-auto-duplicate="selectedVenue?.kind === 'event'" :load-duplicate-quota="loadDuplicateQuota" show-reason @confirm="createRoom">
      <template #before-settings>
        <el-form-item label="房间所属">
          <el-select v-model="venueId" filterable placeholder="普通房间" style="width: 100%" aria-label="房间所属">
            <el-option label="普通房间" value="" />
            <el-option v-for="venue in venues" :key="venue.event_id" :value="venue.event_id" :label="`${venue.kind === 'base' ? '基地' : '赛事'} · ${venue.name} (${venue.event_id})`" />
          </el-select>
        </el-form-item>
      </template>
    </VenueRoomDialog>
  </div>
</template>

<script setup>
import { computed, onMounted, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import VenueRoomDialog from '@/components/VenueRoomDialog.vue'
import { buildEventRoomCreation, createEventRoomCreationForm, eventRoomCreationError } from '@/utils/eventRoomCreation'
import { formatRule, formatSubRule } from '@/utils/gameMeta'

const items = ref([])
const loading = ref(false)
const keyword = ref('')
const status = ref('all')
const autoRefresh = ref(false)
const closing = ref('')
const createVisible = ref(false)
const creating = ref(false)
const venueId = ref('')
const venues = ref([])
const form = reactive({ ...createEventRoomCreationForm(), reason: '' })
const selectedVenue = computed(() => venues.value.find(venue => venue.event_id === venueId.value))
const loadDuplicateQuota = computed(() => {
  const id = venueId.value
  return async () => (await adminApi.get(`/events/${encodeURIComponent(id)}/duplicate-quota`)).data.data
})
watch(venueId, () => {
  form.auto_duplicate = false
  if (selectedVenue.value?.kind !== 'event' && ['sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate'].includes(form.sub_rule)) {
    form.sub_rule = form.sub_rule.replace('_duplicate', '')
  }
})
const roomRuleOptions = [
  ['guobiao', '国标'], ['riichi', '立直'], ['qingque', '青雀'], ['classical', '古典'], ['sichuan', '四川'],
  ['changsha', '长沙麻将'], ['taiwan', '台湾麻将'], ['hongkong', '香港麻将'], ['guizhou', '贵州麻将'],
  ['yixing', '宜兴麻将'], ['shenyang', '沈阳麻将'], ['nanjing', '南京麻将'], ['fuzhou', '福州麻将'], ['wuhan', '武汉麻将'],
  ['nanchang', '南昌麻将'], ['wenzhou', '温州麻将'], ['hangzhou', '杭州麻将'], ['jiujiang', '九江麻将'],
  ['hongzhong', '红中麻将'], ['changchun', '长春麻将'], ['guangdong', '广东麻将'], ['shanxi', '山西麻将'],
].map(([value, label]) => ({ value, label }))
const filteredItems = computed(() => items.value.filter(room => {
  if (status.value === 'playing' && !room.is_game_running) return false
  if (status.value === 'waiting' && room.is_game_running) return false
  if (status.value === 'offline' && room.online_count) return false
  const query = keyword.value.trim().toLowerCase()
  return !query || [room.room_id, room.room_name, room.host_name, room.host_user_id,
    ...room.players.flatMap(p => [p.username, p.user_id])].some(value => String(value ?? '').toLowerCase().includes(query))
}))
let timer
let disposed = false

async function fetchList() {
  if (loading.value) return
  loading.value = true
  try {
    const response = await adminApi.get('/room-control')
    if (!disposed) items.value = response.data.data.items || []
  } catch (error) {
    if (!disposed) ElMessage.error(error.response?.data?.message || '获取房间列表失败')
  } finally { loading.value = false }
}

async function openCreate() {
  createVisible.value = true
  try {
    const response = await adminApi.get('/room-control/venues')
    venues.value = response.data.data.items || []
  } catch { ElMessage.error('获取赛事和基地列表失败，可稍后重新打开') }
}

async function createRoom() {
  if (!form.reason.trim()) { ElMessage.warning('请填写操作原因'); return }
  const paired = ['sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate'].includes(form.sub_rule)
  if (paired && venues.value.find(venue => venue.event_id === venueId.value)?.kind !== 'event') {
    ElMessage.warning('复式双打请选择比赛场创建'); return
  }
  creating.value = true
  try {
    const response = await adminApi.post('/room-control/create', {
      ...buildEventRoomCreation(form), event_id: venueId.value || undefined, reason: form.reason.trim(),
    })
    ElMessage.success(`已创建 ${response.data.data.created_count} ${paired ? '组开闭室' : '个房间'}`)
    createVisible.value = false
    form.reason = ''
    form.password = ''
  } catch (error) { ElMessage.error(eventRoomCreationError(error)) }
  finally { creating.value = false; await fetchList() }
}

async function closeRoom(room) {
  const impact = room.duplicate_match_id ? '开室、闭室将整组关闭，进行中的对局也会结束。' : room.is_game_running ? '正在进行的对局将结束，玩家将离开房间。' : '房间内的玩家将离开房间。'
  let reason
  try {
    const result = await ElMessageBox.prompt(`确认关闭房间 ${room.room_id}「${room.room_name}」？${impact}请填写关闭原因。`, '关闭房间', {
      type: 'warning', confirmButtonText: '关闭房间', cancelButtonText: '取消', inputPattern: /\S+/, inputErrorMessage: '请填写关闭原因',
    })
    reason = result.value.trim()
  } catch { return }
  closing.value = room.room_id
  try {
    await adminApi.post(`/room-control/${encodeURIComponent(room.room_id)}/close`, { instance_id: room.instance_id, reason })
    ElMessage.success('房间已关闭')
  } catch (error) { ElMessage.error(error.response?.data?.message || '关闭失败，请刷新列表确认房间状态') }
  finally { closing.value = ''; await fetchList() }
}

onMounted(() => {
  fetchList()
  timer = setInterval(() => { if (autoRefresh.value && !document.hidden) fetchList() }, 5000)
})
onBeforeUnmount(() => { disposed = true; clearInterval(timer) })
</script>

<style scoped>
.page-title { margin: 0 0 16px; }
.toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
.search { width: 240px; max-width: 100%; }
.status { width: 125px; }
.room-table { margin-top: 16px; }
.muted { color: #909399; font-size: 12px; line-height: 1.8; }
.player { margin: 3px 0; font-size: 13px; }
.player .el-tag { margin-left: 6px; }
</style>
