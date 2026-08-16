<template>
  <div class="redirect-tip">正在跳转到账户面板赛事管理…</div>
</template>

<script setup>
import { onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

onMounted(() => {
  const eventId = route.params.eventId
  router.replace({
    path: '/account',
    hash: '#sec-manage-event',
    query: eventId ? { manage: String(eventId) } : undefined,
  })
})
</script>

<style scoped>
.redirect-tip {
  padding: 24px;
  color: #909399;
}
</style>
