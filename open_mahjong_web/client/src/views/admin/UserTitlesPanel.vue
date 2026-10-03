<template>
  <el-card v-loading="loading">
    <template #header><div class="header"><span>头衔授权</span><el-button link type="primary" :disabled="busy" @click="load">刷新</el-button></div></template>
    <p class="hint">授予后由玩家点击主菜单“背包”，在头衔分类中选择佩戴。撤销当前佩戴的头衔会自动取消佩戴。</p>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <div class="grant-form">
      <el-select v-model="selected" placeholder="选择要授予的头衔" filterable clearable :disabled="busy || loading || !!error" style="min-width: 220px">
        <el-option v-for="title in available" :key="title.title_id" :value="title.title_id" :label="`${title.name} (#${title.title_id})`" />
      </el-select>
      <el-input v-model="reason" placeholder="授予原因（必填）" maxlength="500" :disabled="busy" style="max-width: 360px" />
      <el-button type="primary" :loading="busy" :disabled="!selected || loading || !!error" @click="grant">授予头衔</el-button>
    </div>
    <el-table :data="owned" empty-text="该用户尚未获授任何头衔">
      <el-table-column prop="name" label="头衔" min-width="140" />
      <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="!row.is_enabled ? 'info' : row.equipped ? 'success' : ''">{{ !row.is_enabled ? '已停用' : row.equipped ? '佩戴中' : '未佩戴' }}</el-tag></template></el-table-column>
      <el-table-column label="授予时间" min-width="180"><template #default="{ row }">{{ new Date(row.granted_at).toLocaleString() }}</template></el-table-column>
      <el-table-column prop="granted_by" label="操作人 UID" width="120" />
      <el-table-column prop="grant_reason" label="授予原因" min-width="160" />
      <el-table-column label="操作" width="90"><template #default="{ row }"><el-button link type="danger" :disabled="busy" @click="revoke(row)">撤销</el-button></template></el-table-column>
    </el-table>
  </el-card>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
const props = defineProps({ userId: { type: [String, Number], required: true } })
const owned = ref([])
const catalog = ref([])
const selected = ref(null)
const reason = ref('')
const loading = ref(false)
const busy = ref(false)
const error = ref('')
let loadVersion = 0
const available = computed(() => catalog.value.filter(t => t.is_enabled && !owned.value.some(o => o.title_id === t.title_id)))
async function load() {
  const version = ++loadVersion
  loading.value = true
  error.value = ''
  try {
    const [grants, titles] = await Promise.all([adminApi.get(`/titles/users/${props.userId}`), adminApi.get('/titles')])
    if (version !== loadVersion) return
    owned.value = grants.data.data
    catalog.value = titles.data.data
  } catch (e) { if (version === loadVersion) error.value = e.response?.data?.message || '加载授权失败，请重试' }
  finally { if (version === loadVersion) loading.value = false }
}
function saved(data, label) {
  if (data.synced_online) ElMessage.success(label)
  else ElMessage.warning(`${label}；游戏服暂未同步，重新登录或重新开局后生效`)
}
async function grant() {
  if (busy.value || !selected.value) return
  if (!reason.value.trim()) return ElMessage.warning('请填写授予原因')
  busy.value = true
  try {
    const { data } = await adminApi.post(`/titles/users/${props.userId}/${selected.value}`, { reason: reason.value })
    saved(data, '头衔已授予')
    selected.value = null
    reason.value = ''
    await load()
  } catch (e) { ElMessage.error(e.response?.data?.message || '授予失败，请重试') }
  finally { busy.value = false }
}
async function revoke(title) {
  if (busy.value) return
  const userId = props.userId
  try {
    const { value } = await ElMessageBox.prompt(`撤销“${title.name}”${title.equipped ? '并取消当前佩戴' : ''}，请填写原因：`, '撤销头衔', {
      confirmButtonText: '撤销', cancelButtonText: '取消', inputValidator: v => !!v?.trim() && [...v.trim()].length <= 500 || '请填写 1–500 字的原因',
    })
    if (userId !== props.userId) return
    busy.value = true
    const { data } = await adminApi.delete(`/titles/users/${userId}/${title.title_id}`, { data: { reason: value } })
    saved(data, '头衔已撤销')
    await load()
  } catch (e) { if (e !== 'cancel' && e !== 'close') ElMessage.error(e.response?.data?.message || '撤销失败，请重试') }
  finally { busy.value = false }
}
watch(() => props.userId, () => { owned.value = []; selected.value = null; reason.value = ''; load() }, { immediate: true })
</script>

<style scoped>
.header { display: flex; align-items: center; justify-content: space-between; }
.hint { color: #606266; line-height: 1.6; margin: 0 0 16px; }
.grant-form { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
</style>
