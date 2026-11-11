<template>
  <div>
    <h2 class="page-title">经典牌谱</h2>
    <el-alert
      class="hint"
      type="info"
      :closable="false"
      title="添加到数据站「其他数据」页。粘贴 2D 或 3D 链接可自动识别牌谱、局数、节点和主角；未指定主角时按目标节点自动选择。"
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
        <el-form-item label="主角">
          <el-select v-model="form.protagonist_player_index" :loading="resolving" placeholder="粘贴牌谱链接后自动填充">
            <el-option v-for="player in players" :key="player.original_player_index"
              :value="player.original_player_index" :label="`${player.username}（${player.user_id}）`" />
          </el-select>
          <span class="field-hint protagonist-hint">回放将锁定此玩家，可手动改选</span>
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
        <el-table-column label="主角" min-width="130">
          <template #default="{ row }">{{ row.protagonist?.username || '-' }}</template>
        </el-table-column>
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
const resolving = ref(false)
const players = ref([])
let resolvedUrl = ''
let resolveVersion = 0
const form = reactive(emptyForm())

function emptyForm() {
  return {
    id: '',
    url: '',
    description: '',
    round: undefined,
    node: undefined,
    protagonist_player_index: undefined,
    sort: 0,
  }
}

function resetForm() {
  resolveVersion += 1
  resolving.value = false
  resolvedUrl = ''
  players.value = []
  Object.assign(form, emptyForm())
}

function positionText(row) {
  const parts = []
  if (row.round) parts.push(`第${row.round}局`)
  if (row.node != null) parts.push(`节点 ${row.node}`)
  return parts.join(' · ') || '-'
}

async function fillFromUrl() {
  const url = form.url.trim()
  if (url === resolvedUrl) return true
  const parsed = parseRecordShareInput(form.url)
  if (!parsed) {
    ElMessage.warning('请粘贴有效的 2D / 3D 牌谱链接或牌谱 ID')
    return false
  }
  const version = ++resolveVersion
  resolving.value = true
  players.value = []
  form.protagonist_player_index = undefined
  form.round = parsed.round
  form.node = parsed.node
  try {
    const res = await adminApi.post('/classic-records/resolve', { url })
    if (version !== resolveVersion || form.url.trim() !== url) return false
    const item = res.data.data
    players.value = item.players || []
    form.protagonist_player_index = item.protagonist_player_index ?? undefined
    resolvedUrl = url
    return true
  } catch (err) {
    if (version === resolveVersion) ElMessage.error(err.response?.data?.message || '牌谱读取失败')
    return false
  } finally {
    if (version === resolveVersion) resolving.value = false
  }
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
  resolveVersion += 1
  resolving.value = false
  form.id = row.id
  form.url = row.url_2d || row.game_id
  form.description = row.description
  form.round = row.round ?? undefined
  form.node = row.node ?? undefined
  form.sort = Number(row.sort) || 0
  form.protagonist_player_index = row.protagonist_player_index ?? undefined
  players.value = row.players || []
  resolvedUrl = form.url.trim()
}

async function submit() {
  if (!form.url.trim() || !form.description.trim()) {
    ElMessage.warning('请填写牌谱链接和描述')
    return
  }
  saving.value = true
  try {
    if (!await fillFromUrl()) return
    const payload = {
      url: form.url,
      description: form.description,
      sort: form.sort,
      protagonist_player_index: form.protagonist_player_index,
      protagonist_user_id: players.value.find(player => player.original_player_index === form.protagonist_player_index)?.user_id,
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
.protagonist-hint { margin-left: 12px; font-weight: 400; }
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
