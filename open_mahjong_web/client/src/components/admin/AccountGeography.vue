<template>
  <el-button @click="open">账号国家／地区分布</el-button>
  <el-dialog v-model="visible" title="注册账号最近登录分布" width="min(680px, 94vw)" append-to-body>
    <p>按每个注册账号最后一次成功登录的 IP 估算国家／地区，每个账号只计一次，游客不计入。</p>
    <p>这是最近登录的网络位置，并非国籍或常住地；代理网络可能影响结果。</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <template v-if="data">
      <el-alert v-if="!data.database_available" title="尚未配置可用的本地 IP 地址库，当前归属地显示为未知。" type="warning" :closable="false" />
      <p>注册账号 {{ data.total_accounts }} 个 · 统计时间 {{ formatDate(data.generated_at) }} · 缓存 {{ data.cache_seconds / 60 }} 分钟</p>
      <el-table :data="data.countries" v-loading="loading" empty-text="暂无注册账号">
        <el-table-column prop="country_name" label="国家／地区" />
        <el-table-column prop="accounts" label="账号数" align="right" />
        <el-table-column label="占比" align="right">
          <template #default="{ row }">{{ data.total_accounts ? (row.accounts / data.total_accounts * 100).toFixed(1) : '0.0' }}%</template>
        </el-table-column>
      </el-table>
    </template>
    <p>无登录记录、内网地址及无法识别的地址归入“未知”。只在管理员打开此窗口时查询，不采集页面访问或对局事件。</p>
    <a href="https://db-ip.com" target="_blank" rel="noopener noreferrer">IP Geolocation by DB-IP</a>
    <template #footer><el-button :loading="loading" @click="load">刷新</el-button><el-button @click="visible = false">关闭</el-button></template>
  </el-dialog>
</template>

<script setup>
import { ref } from 'vue'
import adminApi from '@/api/adminClient'
const visible = ref(false)
const loading = ref(false)
const data = ref(null)
const error = ref('')
const formatDate = value => new Date(value).toLocaleString('zh-CN')
async function load() {
  if (loading.value) return
  loading.value = true
  error.value = ''
  try { data.value = (await adminApi.get('/users/geography')).data.data }
  catch (e) { error.value = e.response?.data?.message || '读取账号分布失败' }
  finally { loading.value = false }
}
function open() { visible.value = true; void load() }
</script>
