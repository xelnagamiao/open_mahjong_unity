<template>
  <div>
    <h2 class="page-title">对局记录管理</h2>
    <el-card>
      <el-form inline class="admin-inline-form" @submit.prevent="search">
        <el-form-item label="对局 ID">
          <el-input v-model="filters.game_id" clearable placeholder="对局 ID" style="width: 140px" />
        </el-form-item>
        <el-form-item label="用户 ID">
          <el-input
            v-model="filters.user_id"
            clearable
            style="width: 140px"
          />
        </el-form-item>
        <el-form-item label="规则">
          <el-select v-model="filters.rule" clearable placeholder="规则" style="width: 120px">
            <el-option label="国标" value="guobiao" />
            <el-option label="立直" value="riichi" />
            <el-option label="青雀" value="qingque" />
            <el-option label="古典" value="classical" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">查询</el-button>
          <el-button @click="fetchAll" :loading="loadingAll">获取全部</el-button>
        </el-form-item>
      </el-form>

      <el-table :data="items" v-loading="loading || loadingAll" style="margin-top: 16px">
        <el-table-column prop="game_id" label="对局 ID" width="140" />
        <el-table-column label="对局类型" width="100">
          <template #default="{ row }">{{ formatRoomType(row.room_type) }}</template>
        </el-table-column>
        <el-table-column label="规则" width="80">
          <template #default="{ row }">{{ formatRule(row.rule) }}</template>
        </el-table-column>
        <el-table-column label="子规则" min-width="140">
          <template #default="{ row }">{{ formatSubRule(row.sub_rule) }}</template>
        </el-table-column>
        <el-table-column prop="created_at" label="时间" width="170">
          <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
        </el-table-column>
        <el-table-column label="玩家" min-width="260">
          <template #default="{ row }">
            <div class="player-list">
              <div v-for="p in row.players" :key="p.user_id" class="player-row">
                <el-tag class="rank-tag" :type="rankTagType(p.rank)" size="small" effect="dark">
                  {{ p.rank }}位
                </el-tag>
                <span class="player-name">{{ p.username }}</span>
                <el-tag class="score-tag" :type="scoreTagType(p.score)" size="small">
                  {{ p.score }}
                </el-tag>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="160">
          <template #default="{ row }">
            <el-button link type="primary" @click="showDetail(row.game_id)">详情</el-button>
            <el-button link type="danger" @click="removeGame(row.game_id)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" title="对局详情" width="640px">
      <template v-if="current">
        <p><b>ID:</b> {{ current.game_id }}</p>
        <p><b>对局类型:</b> {{ formatRoomType(currentMeta.room_type) }}</p>
        <p><b>规则:</b> {{ formatRule(currentMeta.rule) }}</p>
        <p><b>子规则:</b> {{ formatSubRule(currentMeta.sub_rule) }}</p>
        <p><b>时间:</b> {{ formatDate(current.created_at) }}</p>
        <el-table :data="current.players" size="small">
          <el-table-column prop="rank" label="顺位" width="60" />
          <el-table-column prop="user_id" label="用户 ID" width="100" />
          <el-table-column prop="username" label="用户名" />
          <el-table-column prop="score" label="分数" width="80" />
        </el-table>
        <el-checkbox v-model="includeRecord" style="margin-top: 12px" @change="reloadDetail">
          包含完整牌谱 JSON
        </el-checkbox>
        <pre v-if="current.record" class="record-json">{{ recordPreview }}</pre>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import { formatRoomType, formatRule, formatSubRule, pickGameMeta } from '@/utils/gameMeta'

const route = useRoute()
const filters = reactive({
  game_id: '',
  user_id: '10000001',
  rule: '',
})
const items = ref([])
const loading = ref(false)
const loadingAll = ref(false)
const dialogVisible = ref(false)
const current = ref(null)
const includeRecord = ref(false)
const detailGameId = ref('')

const currentMeta = computed(() => pickGameMeta(current.value?.players))

const recordPreview = computed(() => {
  if (!current.value?.record) return ''
  const r = current.value.record
  const str = typeof r === 'string' ? r : JSON.stringify(r, null, 2)
  return str.length > 8000 ? str.slice(0, 8000) + '\n... (已截断)' : str
})

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '-'
}

function rankTagType(rank) {
  if (rank === 1) return 'success'
  if (rank === 2) return 'primary'
  if (rank === 3) return 'info'
  return 'danger'
}

function scoreTagType(score) {
  const n = Number(score)
  if (Number.isNaN(n)) return 'info'
  if (n > 0) return 'success'
  if (n < 0) return 'danger'
  return 'info'
}

function hasFilter() {
  return !!(filters.game_id?.trim() || filters.user_id?.trim() || filters.rule)
}

function buildFilterParams() {
  const params = {}
  if (filters.game_id?.trim()) params.game_id = filters.game_id.trim()
  if (filters.user_id?.trim()) params.user_id = filters.user_id.trim()
  if (filters.rule) params.rule = filters.rule
  return params
}

async function search() {
  if (!hasFilter()) {
    ElMessage.warning('请至少填写一项筛选条件，或使用「获取全部」')
    return
  }
  loading.value = true
  try {
    const res = await adminApi.get('/games/search', { params: buildFilterParams() })
    items.value = res.data.data.items
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '查询失败')
  } finally {
    loading.value = false
  }
}

async function fetchAll() {
  loadingAll.value = true
  try {
    const res = await adminApi.get('/games/search', { params: { all: true } })
    items.value = res.data.data.items
    ElMessage.success(`已加载最近 ${items.value.length} 条对局`)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
  } finally {
    loadingAll.value = false
  }
}

async function showDetail(gameId) {
  detailGameId.value = gameId
  includeRecord.value = false
  await reloadDetail()
  dialogVisible.value = true
}

async function reloadDetail() {
  if (!detailGameId.value) return
  try {
    const res = await adminApi.get(`/games/${detailGameId.value}`, {
      params: { include_record: includeRecord.value },
    })
    current.value = res.data.data
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载详情失败')
  }
}

async function removeGame(gameId) {
  const { value: reason } = await ElMessageBox.prompt('删除原因', '删除对局', { type: 'warning' }).catch(
    () => null
  )
  if (!reason) return
  try {
    await adminApi.delete(`/games/${gameId}`, { data: { reason } })
    ElMessage.success('已删除')
    if (hasFilter()) {
      await search()
    } else if (items.value.length) {
      await fetchAll()
    }
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '删除失败')
  }
}

onMounted(() => {
  if (route.query.game_id) {
    filters.game_id = String(route.query.game_id)
    search()
  }
})
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.player-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.player-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.rank-tag {
  min-width: 40px;
  text-align: center;
  font-weight: 600;
}
.player-name {
  flex: 1;
  font-size: 13px;
  color: #303133;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.score-tag {
  min-width: 56px;
  text-align: center;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}
.player-chip {
  display: inline-block;
  margin-right: 8px;
  font-size: 12px;
}
.record-json {
  margin-top: 12px;
  max-height: 320px;
  overflow: auto;
  font-size: 11px;
  background: #f5f7fa;
  padding: 8px;
  border-radius: 4px;
}
</style>
