<template>
  <div class="emp-games">
    <div class="emp-tab-bar emp-games-bar">
      <span class="emp-games-hint">{{ showFilters ? '显示当前进行中或已暂停的对局' : `仅显示本${venueNoun}当前进行中或已暂停的对局，每 5 秒自动刷新。` }}</span>
      <div class="emp-games-tools">
        <el-checkbox v-if="showFilters" v-model="autoRefresh">自动刷新（5s）</el-checkbox>
        <el-button text type="primary" :loading="loadingGames" :disabled="Boolean(controllingGameId)" @click="loadGames">刷新列表</el-button>
      </div>
    </div>
    <div v-if="showFilters" class="emp-games-filters">
      <el-select v-model="scope" class="scope-filter" popper-class="game-filter-popper" aria-label="对局范围">
        <el-option label="大厅对局" value="lobby" />
        <el-option label="赛事对局" value="event" />
        <el-option label="基地对局" value="base" />
        <el-option label="全部对局" value="all" />
      </el-select>
      <el-select v-if="scope === 'event' || scope === 'base'" v-model="filterVenueId" filterable clearable class="venue-filter" popper-class="game-filter-popper"
        :placeholder="scope === 'base' ? '全部基地（可搜索名称或 ID）' : '全部赛事（可搜索名称或 ID）'"
        :aria-label="scope === 'base' ? '筛选基地' : '筛选赛事'">
        <el-option v-for="venue in venueFilterOptions" :key="venue.event_id" :value="venue.event_id" :label="`${venue.name} (${venue.event_id})`" />
      </el-select>
      <el-input v-model="keyword" placeholder="对局 ID、房间号、玩家或 UID" clearable class="keyword-filter" aria-label="搜索对局" />
      <el-select v-model="status" class="status-filter" popper-class="game-filter-popper" aria-label="对局状态筛选">
        <el-option label="全部状态" value="all" />
        <el-option label="进行中" value="running" />
        <el-option label="暂停／待暂停" value="paused" />
        <el-option label="投票中" value="voting" />
        <el-option label="结束中" value="ending" />
      </el-select>
    </div>
    <el-table
      :data="filteredGames"
      row-key="gamestate_id"
      size="small"
      scrollbar-always-on
      v-loading="loadingGames"
      :empty-text="showFilters ? '没有符合条件的对局' : `本${venueNoun}当前没有进行中的对局`"
    >
      <el-table-column prop="room_id" label="房间号" width="100" />
      <el-table-column prop="gamestate_id" label="对局 ID" min-width="180" show-overflow-tooltip />
      <el-table-column v-if="showFilters" label="所属" min-width="160" show-overflow-tooltip>
        <template #default="{ row }">{{ row.event_id ? `${row.venue_kind === 'base' ? '基地' : '赛事'} · ${row.event_name || row.event_id}` : '大厅对局' }}</template>
      </el-table-column>
      <el-table-column label="规则" min-width="160">
        <template #default="{ row }">{{ row.sub_rule ? formatSubRule(row.sub_rule) : formatRule(row.room_rule) }}</template>
      </el-table-column>
      <el-table-column label="玩家" min-width="240">
        <template #default="{ row }">
          <span v-for="player in row.players" :key="player.user_id" class="emp-game-player">
            {{ player.username || player.user_id }}（{{ player.user_id }}）{{ player.is_bot ? '· 机器人' : '' }}
          </span>
        </template>
      </el-table-column>
      <el-table-column label="对局状态" width="130">
        <template #default="{ row }">
          <el-tag :type="isResumeDisabled(row) ? 'info' : 'warning'" size="small">{{ phaseLabel(row.vote_phase) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="240" fixed="right">
        <template #default="{ row }">
          <el-button size="small" :disabled="Boolean(controllingGameId) || isPauseDisabled(row)" @click="onPause(row)">暂停</el-button>
          <el-button size="small" type="success" :disabled="Boolean(controllingGameId) || isResumeDisabled(row)" @click="onResume(row)">恢复</el-button>
          <el-button size="small" type="danger" :disabled="Boolean(controllingGameId) || row.vote_phase === 'end_countdown'" @click="onEnd(row)">结束对局</el-button>
        </template>
      </el-table-column>
    </el-table>
    <p v-if="showFilters" class="emp-games-hint">当前筛选共 {{ filteredGames.length }} 局，总计 {{ games.length }} 局。默认显示大厅对局，可切换范围查看赛事或基地。</p>
  </div>
</template>

<script setup>
import { computed, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import eventAdminApi from '@/api/eventAdminClient'
import adminApi from '@/api/adminClient'
import { formatRule, formatSubRule } from '@/utils/gameMeta'
import { filterAdminGames, gameVenueOptions } from '@/utils/adminGameFilters'

const props = defineProps({
  eventId: { type: String, default: '' },
  kind: { type: String, default: 'event' },
  platformAdmin: { type: Boolean, default: false },
  active: { type: Boolean, default: true },
  showFilters: { type: Boolean, default: false },
})
const emit = defineEmits(['ended'])
const venueNoun = computed(() => props.kind === 'base' ? '基地' : '赛事')
const games = ref([])
const loadingGames = ref(false)
const controllingGameId = ref('')
let gameRequestVersion = 0
let gamePanelMounted = true
const scope = ref('lobby')
const filterVenueId = ref('')
const keyword = ref('')
const status = ref('all')
const autoRefresh = ref(!props.showFilters)
const venueFilterOptions = computed(() => gameVenueOptions(games.value, scope.value))
const filteredGames = computed(() => props.showFilters
  ? filterAdminGames(games.value, { scope: scope.value, venueId: filterVenueId.value, keyword: keyword.value, status: status.value })
  : games.value)
watch(scope, () => { filterVenueId.value = '' })

const PHASE_LABELS = {
  idle: '进行中',
  voting: '投票中',
  rejected: '投票未通过',
  pause_pending: '待暂停',
  paused: '已暂停',
  resume_voting: '解除投票',
  resume_countdown: '解除倒计时',
  end_countdown: '结束倒计时',
}

function phaseLabel(p) {
  return PHASE_LABELS[p] || p || '进行中'
}

function isPauseDisabled(row) {
  return ['paused', 'pause_pending', 'resume_voting', 'resume_countdown', 'end_countdown'].includes(row.vote_phase)
}

function isResumeDisabled(row) {
  return !['paused', 'pause_pending', 'resume_voting', 'resume_countdown'].includes(row.vote_phase)
}

async function loadGames() {
  const eventId = props.eventId
  const version = ++gameRequestVersion
  loadingGames.value = true
  try {
    const res = props.platformAdmin
      ? await adminApi.get('/game-control/list')
      : await eventAdminApi.get(`/events/${encodeURIComponent(eventId)}/games`)
    if (version !== gameRequestVersion || eventId !== props.eventId) return
    if (res.data.success === false) throw new Error(res.data.message || '加载对局失败')
    const items = res.data.data?.items || []
    games.value = props.platformAdmin && eventId ? items.filter(game => game.event_id === eventId) : items
  } catch (e) {
    if (version !== gameRequestVersion || eventId !== props.eventId) return
    ElMessage.error(e.response?.data?.message || e.message || '加载对局失败')
    games.value = []
  } finally {
    if (version === gameRequestVersion) loadingGames.value = false
  }
}

async function callControl(action, row, confirmText) {
  if (!gamePanelMounted || controllingGameId.value || !row.gamestate_id) return
  const eventId = props.eventId
  const gameId = row.gamestate_id
  if (props.platformAdmin && eventId && row.event_id !== eventId) return
  controllingGameId.value = gameId
  try {
    await ElMessageBox.confirm(`对局 ${gameId}\n${confirmText}`, action === 'end' ? '结束对局' : '确认操作', {
      type: action === 'end' ? 'error' : 'warning',
      confirmButtonText: action === 'end' ? '结束对局' : '确认',
      cancelButtonText: '取消',
      customClass: 'emp-game-confirm',
    })
    if (!gamePanelMounted || eventId !== props.eventId) return
    const res = props.platformAdmin
      ? await adminApi.post(`/game-control/${action}`, { gamestate_id: gameId })
      : await eventAdminApi.post(`/events/${encodeURIComponent(eventId)}/games/${encodeURIComponent(gameId)}/${action}`)
    if (!gamePanelMounted || eventId !== props.eventId) return
    if (res.data.success === false) throw new Error(res.data.message || '操作失败')
    ElMessage.success(res.data.message || '操作成功')
    await loadGames()
    if (action === 'end' && gamePanelMounted && eventId === props.eventId) emit('ended')
  } catch (e) {
    if (e !== 'cancel' && e !== 'close' && gamePanelMounted && eventId === props.eventId) {
      ElMessage.error(e.response?.data?.message || e.message || '操作失败')
      await loadGames()
    }
  } finally {
    controllingGameId.value = ''
  }
}

function onPause(row) {
  return callControl('pause', row, '确认强制暂停该对局？将在下一安全节点暂停。')
}

function onResume(row) {
  return callControl('resume', row, '确认恢复该对局？若尚在等待暂停，将取消暂停请求。')
}

function onEnd(row) {
  return callControl('end', row, '确认强制结束该对局？玩家将返回大厅，结束后无法继续，也不会保存为完整牌谱记录。')
}

watch(() => [props.eventId, props.platformAdmin, props.active], () => {
  gameRequestVersion++
  games.value = []
  loadingGames.value = false
  if (props.active && (props.eventId || props.platformAdmin)) loadGames()
}, { immediate: true })

const gamesPollTimer = setInterval(() => {
  if (props.active && autoRefresh.value && (props.eventId || props.platformAdmin) && !document.hidden && !loadingGames.value && !controllingGameId.value) loadGames()
}, 5000)
onUnmounted(() => {
  clearInterval(gamesPollTimer)
  gameRequestVersion++
  gamePanelMounted = false
})
</script>

<style scoped>
.emp-games-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}
.emp-games-tools, .emp-games-filters { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; }
.emp-games-filters { margin: 12px 0; }
.scope-filter { width: 140px; }
.venue-filter { width: 280px; max-width: 100%; }
.keyword-filter { width: 260px; max-width: 100%; }
.status-filter { width: 150px; }
:global(.game-filter-popper .el-select-dropdown__item.is-selected) { font-weight: 400; }
.emp-games :deep(.el-table th.el-table__cell),
.emp-games :deep(.el-button),
:global(.emp-game-confirm .el-message-box__title),
:global(.emp-game-confirm .el-button) {
  font-weight: 400;
  font-synthesis: none;
}
.emp-games-hint {
  color: #606266;
  font-size: 13px;
  font-weight: 400;
}
.emp-game-player {
  display: inline-block;
  margin-right: 8px;
  font-weight: 400;
}
</style>
