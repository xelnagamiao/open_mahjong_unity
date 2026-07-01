<template>
  <div>
    <h2 class="page-title">对局管理</h2>
    <el-card>
      <div class="toolbar">
        <el-button type="primary" @click="fetchList" :loading="loading">刷新列表</el-button>
        <span class="hint">仅显示当前进行中的对局（实时来自游戏服内存）</span>
        <el-checkbox v-model="autoRefresh" @change="toggleAuto">自动刷新（5s）</el-checkbox>
      </div>

      <el-table :data="items" v-loading="loading" style="margin-top: 16px" empty-text="当前没有进行中的对局">
        <el-table-column prop="gamestate_id" label="对局 ID" min-width="200" />
        <el-table-column label="房间类型" width="110">
          <template #default="{ row }">{{ formatRoomType(row.room_type) }}</template>
        </el-table-column>
        <el-table-column label="规则" width="80">
          <template #default="{ row }">{{ formatRule(row.room_rule) }}</template>
        </el-table-column>
        <el-table-column label="子规则" min-width="160">
          <template #default="{ row }">{{ formatSubRule(row.sub_rule) }}</template>
        </el-table-column>
        <el-table-column prop="game_status" label="状态机" width="160" />
        <el-table-column label="投票/暂停" width="130">
          <template #default="{ row }">
            <el-tag :type="phaseTagType(row.vote_phase)" size="small">{{ phaseLabel(row.vote_phase) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="玩家" min-width="220">
          <template #default="{ row }">
            <span v-for="p in row.players" :key="p.user_id" class="player-chip">
              {{ p.username }}<span class="uid">({{ p.user_id }})</span>
              <span v-if="p.is_bot" class="bot">机</span>
            </span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="240" fixed="right">
          <template #default="{ row }">
            <el-button
              size="small"
              :disabled="isPauseDisabled(row)"
              @click="onPause(row)"
            >暂停</el-button>
            <el-button
              size="small"
              type="success"
              :disabled="isResumeDisabled(row)"
              @click="onResume(row)"
            >解除</el-button>
            <el-button
              size="small"
              type="danger"
              @click="onEnd(row)"
            >结束对局</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onBeforeUnmount, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import { formatRoomType, formatRule, formatSubRule } from '@/utils/gameMeta'

const items = ref([])
const loading = ref(false)
const autoRefresh = ref(false)
let timer = null

const PHASE_LABELS = {
  idle: '无',
  voting: '投票中',
  pause_pending: '待暂停',
  paused: '已暂停',
  resume_voting: '解除投票',
  resume_countdown: '解除倒计时',
  end_countdown: '结束倒计时',
}

function phaseLabel(p) {
  return PHASE_LABELS[p] || p || '无'
}

function phaseTagType(p) {
  if (p === 'paused' || p === 'pause_pending') return 'warning'
  if (p && p !== 'idle') return 'info'
  return ''
}

async function fetchList() {
  loading.value = true
  try {
    const res = await adminApi.get('/game-control/list')
    items.value = res.data.data.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '获取对局列表失败')
  } finally {
    loading.value = false
  }
}

function isPauseDisabled(row) {
  return row.vote_phase === 'paused' || row.vote_phase === 'pause_pending'
}
function isResumeDisabled(row) {
  return row.vote_phase !== 'paused' && row.vote_phase !== 'pause_pending'
}

async function callControl(action, row, confirmText) {
  try {
    await ElMessageBox.confirm(confirmText, '确认操作', { type: 'warning' })
  } catch (_) {
    return
  }
  try {
    const res = await adminApi.post(`/game-control/${action}`, { gamestate_id: row.gamestate_id })
    ElMessage.success(res.data.message || '操作成功')
    await fetchList()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

function onPause(row) {
  callControl('pause', row, `确认强制暂停该对局？\n将在下一安全节点挂起。`)
}
function onResume(row) {
  callControl('resume', row, `确认强制解除暂停？`)
}
async function onEnd(row) {
  try {
    await ElMessageBox.confirm(
      `确认强制结束该对局？\n所有玩家将被踢回大厅，且不会保存为完整牌谱记录。`,
      '危险操作',
      { type: 'error', confirmButtonText: '结束对局', cancelButtonText: '取消' }
    )
  } catch (_) {
    return
  }
  try {
    const res = await adminApi.post('/game-control/end', { gamestate_id: row.gamestate_id })
    ElMessage.success(res.data.message || '已结束')
    await fetchList()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  }
}

function toggleAuto(val) {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
  if (val) {
    timer = setInterval(fetchList, 5000)
  }
}

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

fetchList()
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
}
.hint {
  color: #909399;
  font-size: 12px;
}
.player-chip {
  display: inline-block;
  margin-right: 8px;
  font-size: 12px;
}
.uid {
  color: #909399;
}
.bot {
  color: #e6a23c;
  margin-left: 2px;
}
</style>
