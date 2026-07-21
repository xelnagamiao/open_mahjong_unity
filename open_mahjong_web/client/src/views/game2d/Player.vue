<template>
  <div v-if="loading" class="profile-loading"><el-icon class="is-loading" :size="34"><Loading /></el-icon></div>
  <div v-else-if="!profile" class="profile-loading">
    <el-empty description="未找到玩家"><el-button @click="router.push('/2d')">返回大厅</el-button></el-empty>
  </div>
  <div v-else class="profile-page">
    <div class="profile-wrap">
      <el-button class="back-button" :icon="ArrowLeft" @click="router.push('/2d')">返回匹配大厅</el-button>
      <el-card class="profile-identity" shadow="never">
        <div class="identity-body">
          <div class="profile-avatar"><el-icon><Postcard /></el-icon></div>
          <div><span>Salasasa 玩家资料</span><h1>{{ username }}</h1><p>用户 ID：{{ profile.user_id }}</p></div>
        </div>
      </el-card>
      <div class="profile-grid">
        <el-card shadow="never"><div class="profile-stat-title">国标段位</div><div class="profile-stat-value">{{ rank }}</div></el-card>
        <el-card shadow="never"><el-statistic title="国标分数" :value="score" :precision="1" /></el-card>
        <el-card shadow="never"><template #header>段位进度</template><el-progress :percentage="progress" /></el-card>
      </div>
      <el-card shadow="never">
        <template #header>公开资料</template>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="用户名">{{ username }}</el-descriptions-item>
          <el-descriptions-item label="用户 ID">{{ profile.user_id }}</el-descriptions-item>
          <el-descriptions-item label="国标段位">{{ rank }}</el-descriptions-item>
          <el-descriptions-item label="国标分数">{{ score }}</el-descriptions-item>
        </el-descriptions>
      </el-card>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Loading, Postcard } from '@element-plus/icons-vue'
import { playerProfileUrl, publicApiGet } from '@/game2d/salasasa/api'

const route = useRoute()
const router = useRouter()
const profile = ref(null)
const loading = ref(true)

const username = computed(() => profile.value?.user_settings?.username ?? profile.value?.username ?? `玩家 ${profile.value?.user_id}`)
const rank = computed(() => profile.value?.rank?.guobiao_rank ?? profile.value?.guobiao_rank ?? '未定级')
const score = computed(() => profile.value?.rank?.guobiao_score ?? profile.value?.guobiao_score ?? 0)
const progress = computed(() => {
  const raw = profile.value?.rank?.progress
  const value = typeof raw === 'number' ? raw : raw?.percent
  return Math.max(0, Math.min(100, Number(value ?? 0)))
})

async function loadProfile(key) {
  loading.value = true
  profile.value = null
  try {
    profile.value = await publicApiGet(playerProfileUrl(key))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '玩家资料加载失败')
  } finally {
    loading.value = false
  }
}

watch(() => route.params.id, (id) => loadProfile(id), { immediate: true })
</script>

<style scoped src="./Player.css"></style>
