<template>
  <div>
    <h2 class="page-title">仪表盘</h2>
    <el-row :gutter="16" v-loading="loading">
      <el-col :span="8" v-for="card in cards" :key="card.label">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">{{ card.label }}</div>
          <div class="stat-value">{{ card.value }}</div>
        </el-card>
      </el-col>
    </el-row>
    <el-card class="quick-card" shadow="never">
      <template #header>快捷入口</template>
      <el-space wrap>
        <el-button @click="$router.push('/admin/users')">搜索用户</el-button>
        <el-button @click="$router.push('/admin/rank')">段位管理</el-button>
        <el-button @click="$router.push('/admin/games')">对局查询</el-button>
        <el-button @click="$router.push('/admin/audit')">操作审计</el-button>
        <el-button @click="$router.push('/admin/messages')">消息推送</el-button>
        <el-button @click="$router.push('/admin/ip-bans')">IP 封禁</el-button>
      </el-space>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import adminApi from '@/api/adminClient'

const loading = ref(true)
const summary = ref(null)

const cards = computed(() => {
  const s = summary.value
  if (!s) return []
  return [
    { label: '注册用户', value: s.registered_users },
    { label: '游客账号', value: s.tourist_users },
    { label: '今日对局', value: s.games_today },
    { label: '排行榜人数', value: s.leaderboard_eligible },
    { label: '用户总数', value: s.total_users },
  ]
})

onMounted(async () => {
  try {
    const res = await adminApi.get('/dashboard/summary')
    summary.value = res.data.data
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.stat-card {
  margin-bottom: 16px;
}
.stat-label {
  color: #909399;
  font-size: 14px;
}
.stat-value {
  font-size: 28px;
  font-weight: 600;
  margin-top: 8px;
}
.quick-card {
  margin-top: 8px;
}
</style>
