<template>
  <div>
    <h2 class="page-title">经典牌谱</h2>
    <el-alert
      class="hint"
      type="info"
      :closable="false"
      title="添加到数据站「其他数据」页。可粘贴 2D 或 3D 牌谱链接，会自动识别牌谱 ID、局数和节点。"
    />

    <el-card shadow="never">
      <el-form :model="form" label-width="88px" @submit.prevent="submit">
        <el-form-item label="牌谱链接">
          <el-input
            v-model="form.url"
            placeholder="https://salasasa.cn/2d/record/… 或 /game-unity?recordId=…"
            @blur="fillFromUrl"
          />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" maxlength="80" show-word-limit placeholder="例如：人和不够番" />
        </el-form-item>
        <el-form-item label="局 / 节点">
          <el-space wrap>
            <el-input-number v-model="form.round" :min="1" :max="64" controls-position="right" placeholder="局" />
            <el-input-number v-model="form.node" :min="0" :max="9999" controls-position="right" placeholder="节点" />
            <el-input-number v-model="form.sort" :min="-9999" :max="9999" controls-position="right" />
            <span class="field-hint">排序数字越大越靠前</span>
          </el-space>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="submit">
            {{ form.id ? '保存修改' : '添加经典牌谱' }}
          </el-button>
          <el-button v-if="form.id" @click="resetForm">取消编辑</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never" style="margin-top: 16px">
      <el-table :data="items" v-loading="loading" size="small">
        <el-table-column prop="description" label="描述" min-width="160" />
        <el-table-column prop="game_id" label="牌谱 ID" width="130" />
        <el-table-column label="定位" width="110">
          <template #default="{ row }">
            {{ positionText(row) }}
          </template>
        </el-table-column>
        <el-table-column prop="sort" label="排序" width="70" />
        <el-table-column label="对局" min-width="220">
          <template #default="{ row }">
            <span v-if="row.missing" class="missing">牌谱已失效</span>
            <span v-else>{{ (row.players || []).map((p) => p.username).join(' / ') || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="220">
          <template #default="{ row }">
            <el-button link type="primary" @click="edit(row)">编辑</el-button>
            <a class="table-link" :href="row.url_2d" target="_blank" rel="noopener">2D</a>
            <a class="table-link" :href="row.url_3d" target="_blank" rel="noopener">3D</a>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import { parseRecordShareInput } from '@/utils/recordShareLink'

const items = ref([])
const loading = ref(false)
const saving = ref(false)
const form = reactive(emptyForm())

function emptyForm() {
  return {
    id: '',
    url: '',
    description: '',
    round: undefined,
    node: undefined,
    sort: 0,
  }
}

function resetForm() {
  Object.assign(form, emptyForm())
}

function positionText(row) {
  const parts = []
  if (row.round) parts.push(`第${row.round}局`)
  if (row.node != null) parts.push(`节点 ${row.node}`)
  return parts.join(' · ') || '-'
}

function fillFromUrl() {
  const parsed = parseRecordShareInput(form.url)
  if (!parsed) return
  if (parsed.round != null) form.round = parsed.round
  if (parsed.node != null) form.node = parsed.node
}

async function loadItems() {
  loading.value = true
  try {
    const res = await adminApi.get('/classic-records')
    items.value = res.data?.data?.items || []
  } catch (err) {
    ElMessage.error(err.response?.data?.message || '加载失败')
  } finally {
    loading.value = false
  }
}

function edit(row) {
  form.id = row.id
  form.url = row.url_2d || row.game_id
  form.description = row.description
  form.round = row.round ?? undefined
  form.node = row.node ?? undefined
  form.sort = Number(row.sort) || 0
}

async function submit() {
  fillFromUrl()
  if (!form.url.trim() || !form.description.trim()) {
    ElMessage.warning('请填写牌谱链接和描述')
    return
  }
  saving.value = true
  try {
    const payload = {
      url: form.url,
      description: form.description,
      sort: form.sort,
    }
    if (Number.isFinite(form.round)) payload.round = form.round
    if (Number.isFinite(form.node)) payload.node = form.node
    if (form.id) {
      await adminApi.put(`/classic-records/${form.id}`, payload)
      ElMessage.success('已保存')
    } else {
      await adminApi.post('/classic-records', payload)
      ElMessage.success('已添加经典牌谱')
    }
    resetForm()
    await loadItems()
  } catch (err) {
    ElMessage.error(err.response?.data?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

async function remove(row) {
  try {
    await ElMessageBox.confirm(`确认删除「${row.description}」？`, '删除经典牌谱', {
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    await adminApi.delete(`/classic-records/${row.id}`)
    ElMessage.success('已删除')
    if (form.id === row.id) resetForm()
    await loadItems()
  } catch (err) {
    ElMessage.error(err.response?.data?.message || '删除失败')
  }
}

onMounted(loadItems)
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.hint {
  margin-bottom: 16px;
}
.field-hint {
  font-size: 12px;
  color: #909399;
}
.missing {
  color: #e6a23c;
}
.table-link {
  margin: 0 8px;
  font-size: 13px;
  color: #409eff;
  text-decoration: none;
}
.table-link:hover {
  text-decoration: underline;
}
</style>
