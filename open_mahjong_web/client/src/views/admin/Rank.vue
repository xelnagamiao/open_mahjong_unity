<template>
  <div>
    <h2 class="page-title">段位管理</h2>
    <el-row :gutter="16">
      <el-col :xs="12" :sm="10" :span="10">
        <el-card>
          <template #header>编辑段位</template>
          <el-form label-width="100px">
            <el-form-item label="用户 ID">
              <el-input
                v-model="userId"
                @keyup.enter="loadRank"
              />
            </el-form-item>
            <el-form-item label="段位">
              <el-select v-model="form.guobiao_rank" style="width: 100%" @change="onRankChange">
                <el-option v-for="r in rankNames" :key="r" :label="r" :value="r" />
              </el-select>
            </el-form-item>

            <div v-if="selectedBounds" class="rank-info">
              <div class="rank-info-row">
                <span class="label">升段进度</span>
                <span v-if="formProgress.isMaxRank" class="value">
                  {{ form.guobiao_score }} PT（最高段位，无升段上限）
                </span>
                <span v-else class="value">
                  {{ form.guobiao_score }} / {{ formProgress.target }} PT
                  （{{ formProgress.percent }}%，还差 {{ formProgress.remaining }} PT）
                </span>
              </div>
              <el-progress
                v-if="!formProgress.isMaxRank"
                :percentage="formProgress.percent"
                :stroke-width="10"
                :format="() => ''"
              />
              <div class="rank-info-row bounds-row">
                <span class="label">有效 PT 范围</span>
                <span class="value range-value">{{ validPtRangeText }}</span>
              </div>
              <div class="rank-meta">
                <span>起始分 {{ selectedBounds.startScore }}</span>
                <span>升段分 {{ selectedBounds.promoteScore }}</span>
                <span>{{ selectedBounds.canDemote ? '可掉段' : '不可掉段' }}</span>
              </div>
              <p v-if="scoreOutOfRange" class="range-warn">当前 PT 超出该段位有效范围，保存将被拒绝</p>
            </div>

            <el-form-item label="分数 PT">
              <el-input-number
                v-model="form.guobiao_score"
                :min="ptInputMin"
                :max="ptInputMax"
                :step="0.1"
                :precision="2"
                style="width: 100%"
              />
            </el-form-item>
            <el-form-item label="变更原因">
              <el-input v-model="form.reason" type="textarea" />
            </el-form-item>
            <el-button type="primary" @click="saveRank" :loading="saving" :disabled="scoreOutOfRange">
              保存
            </el-button>
            <el-button type="danger" @click="resetRank">重置为 10级</el-button>
          </el-form>
        </el-card>

        <el-card v-if="loadedRank" class="loaded-card">
          <template #header>当前加载数据</template>
          <p class="loaded-line">
            数据库：{{ loadedRank.guobiao_rank }} / {{ loadedRank.guobiao_score }} PT
          </p>
          <p v-if="loadedRank.updated_at" class="loaded-sub">更新于 {{ formatTime(loadedRank.updated_at) }}</p>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="14" :span="14">
        <el-card v-loading="lbLoading">
          <template #header>排行榜预览 Top 100</template>
          <el-table :data="leaderboard" size="small" max-height="480">
            <el-table-column prop="rank_position" label="#" width="50" />
            <el-table-column prop="user_id" label="ID" width="110" />
            <el-table-column prop="username" label="用户名" />
            <el-table-column prop="guobiao_rank" label="段位" width="80" />
            <el-table-column prop="guobiao_score" label="PT" width="80" />
          </el-table>
        </el-card>

        <el-card class="table-card">
          <template #header>段位分数对照表</template>
          <el-table :data="rankTableRows" size="small" max-height="360">
            <el-table-column prop="name" label="段位" width="72" />
            <el-table-column prop="start_score" label="起始分" width="72" />
            <el-table-column prop="promote_score" label="升段分" width="72" />
            <el-table-column label="可设 PT 上限" width="110">
              <template #default="{ row }">
                {{ row.bounds.isTopRank ? '无上限' : row.bounds.maxScore }}
              </template>
            </el-table-column>
            <el-table-column label="掉段" width="64">
              <template #default="{ row }">
                {{ row.can_demote ? '是' : '否' }}
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import {
  RANK_NAMES,
  RANK_TABLE,
  getScoreBounds,
  getPromotionProgress,
  validateRankScore,
  clampScoreToRank,
} from '@/constants/rankTable'

const route = useRoute()
const userId = ref('10000001')
const rankNames = RANK_NAMES
const form = reactive({
  guobiao_rank: '10级',
  guobiao_score: 0,
  reason: '',
})
const loadedRank = ref(null)
const saving = ref(false)
const leaderboard = ref([])
const lbLoading = ref(false)

const selectedBounds = computed(() => getScoreBounds(form.guobiao_rank))
const formProgress = computed(() =>
  getPromotionProgress(form.guobiao_rank, form.guobiao_score) || {
    current: 0,
    target: 0,
    percent: 0,
    remaining: 0,
    isMaxRank: false,
  }
)
const ptInputMin = computed(() => selectedBounds.value?.minScore ?? 0)
const ptInputMax = computed(() =>
  selectedBounds.value?.isTopRank ? undefined : selectedBounds.value?.maxScore
)
const scoreOutOfRange = computed(() => !validateRankScore(form.guobiao_rank, form.guobiao_score).valid)

const validPtRangeText = computed(() => {
  const b = selectedBounds.value
  if (!b) return ''
  const max = b.isTopRank ? '无上限' : b.maxScore
  return `${b.minScore} ~ ${max} PT`
})

const rankTableRows = computed(() =>
  RANK_TABLE.map((row) => ({
    name: row.name,
    start_score: row.startScore,
    promote_score: row.promoteScore,
    can_demote: row.canDemote,
    bounds: getScoreBounds(row.name),
  }))
)

function formatTime(val) {
  if (!val) return ''
  return String(val).replace('T', ' ').slice(0, 19)
}

function onRankChange() {
  form.guobiao_score = clampScoreToRank(form.guobiao_rank, form.guobiao_score)
}

async function loadRank() {
  const id = parseInt(userId.value, 10)
  if (Number.isNaN(id)) return
  try {
    const res = await adminApi.get(`/rank/${id}`)
    const data = res.data.data
    form.guobiao_rank = data.guobiao_rank
    form.guobiao_score = data.guobiao_score
    loadedRank.value = data
  } catch (e) {
    loadedRank.value = null
    ElMessage.error(e.response?.data?.message || '加载段位失败')
  }
}

async function loadLeaderboard() {
  lbLoading.value = true
  try {
    const res = await adminApi.get('/rank/leaderboard', { params: { limit: 100 } })
    leaderboard.value = res.data.data
  } finally {
    lbLoading.value = false
  }
}

async function saveRank() {
  const id = parseInt(userId.value, 10)
  if (Number.isNaN(id)) {
    ElMessage.warning('请输入有效用户 ID')
    return
  }
  if (!form.reason.trim()) {
    ElMessage.warning('请填写变更原因')
    return
  }
  const validation = validateRankScore(form.guobiao_rank, form.guobiao_score)
  if (!validation.valid) {
    ElMessage.warning(validation.message)
    return
  }
  saving.value = true
  try {
    const res = await adminApi.put(`/rank/${id}`, {
      guobiao_rank: form.guobiao_rank,
      guobiao_score: validation.normalizedScore,
      reason: form.reason,
    })
    const data = res.data.data
    form.guobiao_score = data.guobiao_score
    loadedRank.value = { ...loadedRank.value, ...data }
    ElMessage.success('段位已更新')
    form.reason = ''
    await loadLeaderboard()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

async function resetRank() {
  const id = parseInt(userId.value, 10)
  if (Number.isNaN(id)) return
  const { value: reason } = await ElMessageBox.prompt('重置原因', '重置段位').catch(() => null)
  if (!reason) return
  try {
    const res = await adminApi.post(`/rank/${id}/reset`, { reason })
    const data = res.data.data
    form.guobiao_rank = data.guobiao_rank
    form.guobiao_score = data.guobiao_score
    loadedRank.value = data
    ElMessage.success('已重置')
    await loadLeaderboard()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '重置失败')
  }
}

watch(
  () => route.query.userId,
  (v) => {
    if (v) {
      userId.value = String(v)
      loadRank()
    }
  },
  { immediate: true }
)

onMounted(() => {
  loadLeaderboard()
  if (userId.value.trim()) loadRank()
})
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.rank-info {
  margin: 0 0 16px 100px;
  padding: 12px;
  background: #f5f7fa;
  border-radius: 6px;
  font-size: 13px;
}
.rank-info-row {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 8px;
  line-height: 1.5;
}
.rank-info-row .label {
  flex-shrink: 0;
  color: #909399;
  white-space: nowrap;
}
.rank-info-row .value {
  color: #303133;
  min-width: 0;
}
.range-value {
  white-space: nowrap;
}
.bounds-row { margin-bottom: 4px; }
.rank-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 8px;
  font-size: 12px;
  color: #606266;
}
.range-warn {
  margin: 8px 0 0;
  color: #f56c6c;
  font-size: 12px;
}
.loaded-card,
.table-card {
  margin-top: 16px;
}
.loaded-line {
  margin: 0;
  font-size: 14px;
}
.loaded-sub {
  margin: 6px 0 0;
  font-size: 12px;
  color: #909399;
}
</style>
