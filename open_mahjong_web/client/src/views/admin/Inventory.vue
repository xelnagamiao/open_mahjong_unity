<template>
  <div v-loading="loading">
    <div class="heading"><h2>道具管理</h2><el-button type="primary" @click="editItem()">新增物品</el-button></div>
    <p class="hint">当前仅内置改名卡。在用户详情发放或回收，玩家从主菜单点击“背包”后使用。停用后保留持有数量。</p>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <div class="filters"><el-input v-model="search" placeholder="搜索名称、编码或 ID" clearable /><el-select v-model="category"><el-option label="全部分类" value="" /><el-option v-for="(label, key) in categories" :key="key" :label="label" :value="key" /></el-select><el-button @click="load">刷新</el-button></div>
    <el-table :data="filtered" empty-text="暂无物品">
      <el-table-column prop="item_id" label="ID" width="80" />
      <el-table-column prop="name" label="名称" min-width="130" />
      <el-table-column label="分类" width="80"><template #default="{ row }">{{ categories[row.category] }}</template></el-table-column>
      <el-table-column prop="code" label="编码" min-width="160" />
      <el-table-column prop="description" label="说明" min-width="220" />
      <el-table-column label="状态" width="150"><template #default="{ row }"><el-tag v-if="row.is_default">默认可用</el-tag><el-tag v-else :type="row.use_enabled ? 'success' : 'info'">{{ row.use_enabled ? '可使用' : '已停用' }}</el-tag><div v-if="!row.grant_enabled" class="hint">停止发放</div></template></el-table-column>
      <el-table-column label="操作" width="85"><template #default="{ row }"><el-button link type="primary" @click="editItem(row)">编辑</el-button></template></el-table-column>
    </el-table>
    <el-dialog v-model="visible" class="inventory-catalog-dialog" top="6vh" :title="editingId ? '编辑物品' : '新增物品'" width="640px" :close-on-click-modal="!saving" :before-close="closeDialog">
      <el-form label-width="100px" :disabled="saving">
        <el-form-item label="名称" required><el-input v-model="form.name" maxlength="32" show-word-limit /></el-form-item>
        <el-form-item label="编码" required><el-input v-model="form.code" :disabled="!!editingId" placeholder="小写字母、数字和下划线，创建后固定" maxlength="64" /></el-form-item>
        <el-form-item label="资源 / 效果" required><el-select v-model="form.asset_key" :disabled="!!editingId" filterable style="width:100%"><el-option v-for="asset in assets" :key="asset.asset_key" :value="asset.asset_key" :label="`${categories[asset.category]} · ${asset.label}`" /></el-select></el-form-item>
        <el-form-item label="说明"><el-input v-model="form.description" type="textarea" :rows="2" maxlength="300" show-word-limit /></el-form-item>
        <el-form-item label="允许发放"><el-switch v-model="form.grant_enabled" /></el-form-item>
        <el-form-item label="允许使用"><el-switch v-model="form.use_enabled" :disabled="form.is_default" /></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="form.sort_order" :min="-1000000" :max="1000000" /></el-form-item>
        <el-form-item label="变更原因" required><el-input v-model="form.reason" maxlength="500" /></el-form-item>
      </el-form>
      <template #footer><el-button :disabled="saving" @click="visible = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">保存</el-button></template>
    </el-dialog>
  </div>
</template>
<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'
const categories = { item: '道具' }
const loading = ref(false), saving = ref(false), visible = ref(false), error = ref('')
const items = ref([]), assets = ref([]), search = ref(''), category = ref(''), editingId = ref(null)
const form = reactive({ config: {} })
const filtered = computed(() => items.value.filter(i => (!category.value || i.category === category.value) && `${i.name} ${i.code} ${i.item_id}`.toLowerCase().includes(search.value.trim().toLowerCase())))
async function load() {
  loading.value = true; error.value = ''
  try { const { data } = await adminApi.get('/inventory/catalog'); items.value = data.data.items; assets.value = data.data.assets }
  catch (e) { error.value = e.response?.data?.message || '物品目录加载失败' }
  finally { loading.value = false }
}
function editItem(item) {
  editingId.value = item?.item_id || null
  Object.assign(form, item ? JSON.parse(JSON.stringify(item)) : { name: '', code: '', description: '', asset_key: '', is_default: false, grant_enabled: true, use_enabled: true, sort_order: 0 })
  form.config = {}; form.reason = ''; visible.value = true
}
function closeDialog(done) { if (!saving.value) done() }
async function save() {
  if (!form.name?.trim() || !form.code?.trim() || !form.asset_key || !form.reason.trim()) return ElMessage.warning('请填写名称、编码、资源和变更原因')
  if (saving.value) return
  saving.value = true
  try {
    const body = { ...form }
    const { data } = editingId.value ? await adminApi.patch(`/inventory/catalog/${editingId.value}`, body) : await adminApi.post('/inventory/catalog', body)
    if (data.synced_online) ElMessage.success('物品已保存'); else ElMessage.warning('已保存，游戏服在线通知未完成，请刷新背包')
    visible.value = false; await load()
  } catch (e) { ElMessage.error(e.response?.data?.message || '保存失败') }
  finally { saving.value = false }
}
onMounted(load)
</script>
<style scoped>
.heading { display:flex; align-items:center; justify-content:space-between; gap:16px; }
.hint { color:#606266; line-height:1.6; font-size:13px; }
.filters { display:flex; gap:12px; margin:20px 0; }.filters .el-input { max-width:320px; }.filters .el-select { width:140px; }
</style>
<style>
.inventory-catalog-dialog { display:flex; flex-direction:column; max-height:88vh; }
.inventory-catalog-dialog .el-dialog__body { overflow-y:auto; }
.inventory-catalog-dialog .el-dialog__header, .inventory-catalog-dialog .el-dialog__footer { flex-shrink:0; }
</style>
