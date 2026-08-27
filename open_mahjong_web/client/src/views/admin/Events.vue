<template>
  <div>
    <h2 class="page-title">{{ isBase ? '基地管理' : '赛事管理' }}</h2>

    <el-card>
      <template #header>
        <div class="list-header">
          <span>{{ isBase ? '基地列表' : '赛事列表' }}</span>
          <div class="list-header-actions">
            <el-radio-group v-model="statusFilter" size="small" @change="load">
              <el-radio-button value="">全部</el-radio-button>
              <el-radio-button value="registered">已注册</el-radio-button>
              <el-radio-button value="active">已开启</el-radio-button>
              <el-radio-button value="closed">已关闭</el-radio-button>
              <el-radio-button value="reopen">待审再开</el-radio-button>
            </el-radio-group>
            <el-button type="primary" size="small" @click="createDialogVisible = true">
              {{ isBase ? '创建基地' : '创建赛事' }}
            </el-button>
          </div>
        </div>
      </template>

      <el-table :data="items" v-loading="loading" size="small">
        <el-table-column prop="name" label="名称" min-width="160" show-overflow-tooltip />
        <el-table-column prop="event_id" :label="isBase ? '基地 ID' : '赛事 ID'" min-width="140" />
        <el-table-column label="状态" width="140">
          <template #default="{ row }">
            <el-tag :type="eventStatusTagType(row.status)" size="small">
              {{ eventStatusLabel(row.status) }}
            </el-tag>
            <el-tag v-if="row.reopen_requested" type="warning" size="small" style="margin-left: 4px">
              待再开
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="isBase ? '基地主管理员' : '赛事主管理员'" min-width="140">
          <template #default="{ row }">
            <template v-if="row.owner_user_id">
              {{ row.owner_username || '-' }}
              <span class="uid">({{ row.owner_user_id }})</span>
            </template>
            <span v-else class="muted">未指定</span>
          </template>
        </el-table-column>
        <el-table-column prop="admin_count" label="子管理员" width="90" />
        <el-table-column prop="record_count" label="牌谱数" width="80" />
        <el-table-column label="创建时间" min-width="160">
          <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="$router.push(detailPath(row.event_id))">
              详情
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog
      v-model="createDialogVisible"
      :title="isBase ? '创建基地' : '创建赛事'"
      width="480px"
      destroy-on-close
    >
      <p class="hint">
        {{ isBase
          ? '创建后为「已注册」状态，需基地主管理员自行开启后方可建房。'
          : '创建后为「已注册」状态，需赛事主管理员自行开启后方可建房。' }}
      </p>
      <el-form label-position="top" @submit.prevent="createEvent">
        <el-form-item :label="isBase ? '基地名称' : '赛事名称'" required>
          <el-input
            v-model="createForm.name"
            clearable
            :placeholder="isBase ? '如 周末活动室' : '如 2026春季赛'"
          />
        </el-form-item>
        <el-form-item label="主管理员 ID">
          <el-input
            v-model="createForm.owner_user_id"
            clearable
            placeholder="可选，稍后指定"
          />
        </el-form-item>
        <el-form-item label="操作原因" required>
          <el-input v-model="createForm.reason" clearable placeholder="审计必填" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="creating" @click="createEvent">创建</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'
import { eventStatusLabel, eventStatusTagType, parseVenueKind, venueAdminDetailPath } from '@/utils/eventMeta'

const route = useRoute()
const router = useRouter()
const items = ref([])
const loading = ref(false)
const creating = ref(false)
const createDialogVisible = ref(false)
const statusFilter = ref('')
const createForm = reactive({
  name: '',
  owner_user_id: '',
  reason: '',
})
const isBase = computed(() => parseVenueKind(route.meta.venueKind) === 'base')
const venueKind = computed(() => (isBase.value ? 'base' : 'event'))

function detailPath(eventId) {
  return venueAdminDetailPath(venueKind.value, eventId)
}

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '-'
}

async function load() {
  loading.value = true
  try {
    const params = { kind: venueKind.value }
    if (statusFilter.value === 'reopen') {
      params.reopen_requested = '1'
      params.status = 'closed'
    } else if (statusFilter.value) {
      params.status = statusFilter.value
    }
    const res = await adminApi.get('/events', { params })
    items.value = res.data.data.items
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
  } finally {
    loading.value = false
  }
}

async function createEvent() {
  const noun = isBase.value ? '基地' : '赛事'
  if (!createForm.name.trim()) {
    ElMessage.warning(`请填写${noun}名称`)
    return
  }
  if (!createForm.reason.trim()) {
    ElMessage.warning('请填写操作原因')
    return
  }
  creating.value = true
  try {
    const body = {
      name: createForm.name.trim(),
      reason: createForm.reason.trim(),
      kind: venueKind.value,
    }
    if (createForm.owner_user_id.trim()) {
      body.owner_user_id = createForm.owner_user_id.trim()
    }
    const res = await adminApi.post('/events', body)
    ElMessage.success(`${noun}已创建（已注册，待开启）`)
    createForm.name = ''
    createForm.owner_user_id = ''
    createForm.reason = ''
    createDialogVisible.value = false
    const id = res.data.data.event_id
    if (id) {
      router.push(detailPath(id))
    } else {
      await load()
    }
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '创建失败')
  } finally {
    creating.value = false
  }
}

watch(() => route.meta.venueKind, () => {
  statusFilter.value = ''
  createDialogVisible.value = false
  load()
})

onMounted(load)
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
  font-size: 18px;
  font-weight: 600;
}
.list-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.list-header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.hint {
  margin: 0 0 12px;
  font-size: 13px;
  color: #909399;
}
.uid {
  color: #909399;
  font-size: 12px;
}
.muted {
  color: #909399;
}
</style>
