<template>
  <div>
    <h2 class="page-title">我的赛事</h2>
    <el-card>
      <el-table :data="items" v-loading="loading" size="small" empty-text="暂无管理的赛事">
        <el-table-column prop="name" label="名称" min-width="160" show-overflow-tooltip />
        <el-table-column label="类型" width="80">
          <template #default="{ row }">
            <el-tag :type="row.kind === 'base' ? 'warning' : 'primary'" size="small" effect="plain">
              {{ row.kind === 'base' ? '基地' : '赛事' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="event_id" label="赛事 ID" min-width="140" />
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-tag :type="eventStatusTagType(row.status)" size="small">
              {{ eventStatusLabel(row.status) }}
            </el-tag>
            <el-tag v-if="row.reopen_requested" type="warning" size="small" style="margin-left: 4px">
              待再开
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="我的角色" width="130">
          <template #default="{ row }">
            {{ eventRoleLabel(row.role, row.kind) }}
          </template>
        </el-table-column>
        <el-table-column prop="admin_count" label="子管理员" width="90" />
        <el-table-column prop="record_count" label="牌谱数" width="80" />
        <el-table-column label="创建时间" min-width="160">
          <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button
              link
              type="primary"
              @click="$router.push(`/event-admin/events/${row.event_id}`)"
            >管理</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import eventAdminApi from '@/api/eventAdminClient'
import { eventRoleLabel, eventStatusLabel, eventStatusTagType } from '@/utils/eventMeta'

const items = ref([])
const loading = ref(false)

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '-'
}

async function load() {
  loading.value = true
  try {
    const res = await eventAdminApi.get('/events')
    items.value = res.data.data?.items || []
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>
