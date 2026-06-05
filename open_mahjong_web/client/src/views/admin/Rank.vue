<template>
  <div>
    <h2 class="page-title">段位管理</h2>
    <el-row :gutter="16">
      <el-col :span="10">
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
              <el-select v-model="form.guobiao_rank" style="width: 100%">
                <el-option v-for="r in rankNames" :key="r" :label="r" :value="r" />
              </el-select>
            </el-form-item>
            <el-form-item label="分数 PT">
              <el-input-number v-model="form.guobiao_score" :step="0.1" style="width: 100%" />
            </el-form-item>
            <el-form-item label="变更原因">
              <el-input v-model="form.reason" type="textarea" />
            </el-form-item>
            <el-button type="primary" @click="saveRank" :loading="saving">保存</el-button>
            <el-button type="danger" @click="resetRank">重置为 10级</el-button>
          </el-form>
        </el-card>
      </el-col>
      <el-col :span="14">
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
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const RANK_NAMES = [
  '10级', '9级', '8级', '7级', '6级', '5级', '4级', '3级', '2级', '1级',
  '初段', '二段', '三段', '四段', '五段', '六段', '七段', '八段', '九段',
]

const route = useRoute()
const userId = ref('10000001')
const rankNames = RANK_NAMES
const form = reactive({
  guobiao_rank: '10级',
  guobiao_score: 0,
  reason: '',
})
const saving = ref(false)
const leaderboard = ref([])
const lbLoading = ref(false)

async function loadRank() {
  const id = parseInt(userId.value, 10)
  if (Number.isNaN(id)) return
  try {
    const res = await adminApi.get(`/rank/${id}`)
    form.guobiao_rank = res.data.data.guobiao_rank
    form.guobiao_score = res.data.data.guobiao_score
  } catch (e) {
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
  saving.value = true
  try {
    await adminApi.put(`/rank/${id}`, {
      guobiao_rank: form.guobiao_rank,
      guobiao_score: form.guobiao_score,
      reason: form.reason,
    })
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
    await adminApi.post(`/rank/${id}/reset`, { reason })
    form.guobiao_rank = '10级'
    form.guobiao_score = 0
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
</style>
