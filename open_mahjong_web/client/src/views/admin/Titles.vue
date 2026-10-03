<template>
  <section v-loading="loading">
    <div class="toolbar">
      <h2>头衔管理</h2>
      <el-button type="primary" @click="edit()">新增头衔</el-button>
    </div>
    <p class="hint">在用户详情中授予或撤销头衔。每人最多佩戴一个，也可以不佩戴；停用会取消所有用户当前的佩戴，保留授权记录。</p>
    <div class="toolbar">
      <el-input v-model="keyword" placeholder="搜索名称或 ID" clearable style="max-width: 320px" />
      <el-button @click="load">刷新</el-button>
    </div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-table :data="filtered" empty-text="暂无头衔">
      <el-table-column prop="title_id" label="ID" width="80" />
      <el-table-column prop="name" label="头衔名称" min-width="150" />
      <el-table-column prop="description" label="说明" min-width="220" />
      <el-table-column label="状态" width="90">
        <template #default="{ row }"><el-tag :type="row.is_enabled ? 'success' : 'info'">{{ row.is_enabled ? '启用' : '停用' }}</el-tag></template>
      </el-table-column>
      <el-table-column prop="owner_count" label="获授人数" width="100" />
      <el-table-column prop="sort_order" label="排序" width="80" />
      <el-table-column label="操作" width="100"><template #default="{ row }"><el-button link type="primary" @click="edit(row)">编辑</el-button></template></el-table-column>
    </el-table>
    <el-dialog v-model="dialog" :title="editingId ? '编辑头衔' : '新增头衔'" width="min(560px, 94vw)" :close-on-click-modal="false" :before-close="closeDialog">
      <el-form label-width="88px" @submit.prevent="save">
        <el-form-item label="名称" required><el-input v-model="form.name" maxlength="24" show-word-limit /></el-form-item>
        <el-form-item label="说明"><el-input v-model="form.description" maxlength="200" show-word-limit /></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="form.sort_order" :min="-1000000" :max="1000000" :precision="0" /></el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.is_enabled" /></el-form-item>
        <el-alert v-if="editingId && !form.is_enabled" type="warning" title="保存后将取消此头衔的所有佩戴；重新启用后需由玩家重新选择佩戴。" :closable="false" />
        <el-form-item label="变更原因" required class="reason"><el-input v-model="form.reason" maxlength="500" placeholder="记录到管理审计" /></el-form-item>
      </el-form>
      <template #footer><el-button :disabled="saving" @click="dialog = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">保存</el-button></template>
    </el-dialog>
  </section>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'

const titles = ref([])
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const keyword = ref('')
const dialog = ref(false)
const editingId = ref(null)
const form = reactive({ name: '', description: '', is_enabled: true, sort_order: 0, reason: '' })
const filtered = computed(() => titles.value.filter(t => `${t.title_id} ${t.name}`.toLowerCase().includes(keyword.value.trim().toLowerCase())))

async function load() {
  loading.value = true
  error.value = ''
  try { titles.value = (await adminApi.get('/titles')).data.data }
  catch (e) { error.value = e.response?.data?.message || '加载头衔失败，请重试' }
  finally { loading.value = false }
}
function edit(row) {
  editingId.value = row?.title_id || null
  Object.assign(form, { name: row?.name || '', description: row?.description || '', is_enabled: row?.is_enabled ?? true, sort_order: row?.sort_order ?? 0, reason: '' })
  dialog.value = true
}
function closeDialog(done) { if (!saving.value) done() }
async function save() {
  if (saving.value) return
  if (!form.name.trim() || !form.reason.trim()) return ElMessage.warning('请填写名称和变更原因')
  saving.value = true
  try {
    const { data } = editingId.value
      ? await adminApi.patch(`/titles/${editingId.value}`, { ...form })
      : await adminApi.post('/titles', { ...form })
    dialog.value = false
    if (data.synced_online) ElMessage.success('头衔已保存')
    else ElMessage.warning('已保存；游戏服暂未同步，重新登录或重新开局后生效')
    await load()
  } catch (e) { ElMessage.error(e.response?.data?.message || '保存失败，请重试') }
  finally { saving.value = false }
}
onMounted(load)
</script>

<style scoped>
.toolbar { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 16px; }
h2 { font-size: 22px; margin: 0; }
.hint { color: #606266; line-height: 1.7; margin: 0 0 20px; }
.reason { margin-top: 18px; }
</style>
