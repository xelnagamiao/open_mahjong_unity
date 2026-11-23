<template>
  <section class="sponsors-page">
    <h2 class="page-title">赞助玩家和特殊设定</h2>
    <el-radio-group v-model="group" class="group-selector" aria-label="玩家分组" @change="changeGroup">
      <el-radio-button value="sponsor">赞助玩家<span v-if="totals">（{{ totals.sponsor }}）</span></el-radio-button>
      <el-radio-button value="special">特殊设定玩家<span v-if="totals">（{{ totals.special }}）</span></el-radio-button>
    </el-radio-group>
    <el-card v-if="group === 'sponsor'" shadow="never" class="permission-card">
      <div class="permission-heading">
        <div>
          <h3>统一设置入场特许</h3>
          <p>设置作用于整个赞助名单。拥有特殊设定的赞助玩家默认保留个人特许。</p>
        </div>
        <el-button type="primary" :loading="permissionSaving" :disabled="loading || !targetCount" @click="savePermissions">
          应用到赞助玩家<span v-if="totals">（{{ targetCount }} 名）</span>
        </el-button>
      </div>
      <div class="permission-list">
        <label v-for="item in permissionOptions" :key="item.key" class="permission-item">
          <el-switch v-model="permissionForm[item.key]" :aria-label="item.label" />
          <span>{{ item.label }}</span>
        </label>
      </div>
      <el-checkbox
        v-if="totals?.special"
        v-model="syncSpecial"
        :disabled="!totals.overlap || permissionSaving"
        class="sync-option"
      >同步特殊设定玩家（{{ totals.overlap }} 名同时有赞助记录）</el-checkbox>
    </el-card>
    <el-card shadow="never" class="list-card">
      <el-form inline class="admin-inline-form" @submit.prevent="search">
        <el-form-item label="用户">
          <el-input
            v-model="query"
            clearable
            placeholder="用户 ID、用户名或邮箱"
            style="width: 280px"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" native-type="submit">查询</el-button>
          <el-button @click="$router.push('/admin/users')">用户管理</el-button>
        </el-form-item>
      </el-form>

      <el-alert v-if="listError" type="error" :title="listError" :closable="false" />
      <el-table
        row-key="user_id"
        :data="items"
        v-loading="loading"
        class="sponsor-table"
        :empty-text="listError ? '请重新查询' : group === 'sponsor' ? '未找到赞助玩家' : '未找到特殊设定玩家'"
      >
        <el-table-column
          prop="user_id"
          label="ID"
          width="125"
          class-name="sponsor-id-cell"
        />
        <el-table-column prop="username" label="用户名" min-width="150">
          <template #default="{ row }">
            {{ row.username }}
            <el-tag v-if="row.has_sponsorship && row.has_special_settings" type="info" size="small">
              {{ group === 'sponsor' ? '特殊设定' : '赞助玩家' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="赞助金额" width="180" class-name="sponsor-amount-cell">
          <template #default="{ row }">
            <el-input
              v-model="row.edit.amount"
              inputmode="decimal"
              size="small"
              class="amount-input"
              aria-label="赞助金额"
              placeholder="0.00"
              @blur="normalizeAmount(row)"
            />
          </template>
        </el-table-column>
        <el-table-column
          label="赞助到期时间"
          width="215"
          class-name="sponsor-time-cell"
          label-class-name="sponsor-time-header"
        >
          <template #default="{ row }">
            <span class="sponsor-time-value">
              {{ row.sponsor_expires_at ? formatDate(row.sponsor_expires_at) : '未设置' }}
            </span>
          </template>
        </el-table-column>
        <el-table-column label="入场特许" min-width="240">
          <template #default="{ row }">
            <el-tag v-for="pass in passesOf(row)" :key="pass" size="small" class="pass-tag">
              {{ pass }}
            </el-tag>
            <span v-if="passesOf(row).length === 0" class="empty-value">暂无</span>
          </template>
        </el-table-column>
        <el-table-column label="玩家注释" min-width="260">
          <template #default="{ row }">
            <el-input
              v-model="row.edit.note"
              type="textarea"
              :rows="2"
              maxlength="2000"
              show-word-limit
              placeholder="仅管理员可见"
              size="small"
              class="note-input"
            />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" :fixed="isMobile ? false : 'right'">
          <template #default="{ row }">
            <el-button
              type="primary"
              size="small"
              :loading="savingId === row.user_id"
              @click="saveRow(row)"
            >保存</el-button>
            <el-button link type="primary" @click="$router.push(`/admin/users/${row.user_id}`)">详情</el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-pagination
        v-if="total > limit"
        :current-page="page"
        :page-size="limit"
        :total="total"
        layout="total, prev, pager, next"
        class="pagination"
        @current-change="changePage"
      />
    </el-card>
  </section>
</template>

<script setup>
import { useAdminListState } from '@/composables/useAdminListState'
import { useMobile } from '@/composables/useMobile'
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import { ladderPassOptions as permissionOptions } from '@/utils/sponsor'

const { saved, remember } = useAdminListState()
const { isMobile } = useMobile()
const group = ref(saved.group === 'special' ? 'special' : 'sponsor')
const query = ref(saved.q ?? '')
let appliedQuery = query.value
let latest = 0
const items = ref([])
const loading = ref(false)
const page = ref(saved.page ?? 1)
const limit = 20
const total = ref(0)
const totals = ref(null)
const listError = ref('')
const savingId = ref(null)
const permissionSaving = ref(false)
const syncSpecial = ref(false)
const targetCount = computed(() => totals.value
  ? totals.value.sponsor - (syncSpecial.value ? 0 : totals.value.overlap)
  : 0)
const permissionForm = ref(Object.fromEntries(permissionOptions.map(({ key }) => [key, false])))

function formatDate(value) {
  return value
    ? new Date(value).toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
      })
    : '未设置'
}

function normalizeAmount(row) {
  const value = Number(row.edit.amount)
  row.edit.amount = Number.isFinite(value) && value > 0
    ? Math.round(value * 100) / 100
    : 0
}

function passesOf(row) {
  return permissionOptions.filter(({ key }) => row[key]).map(({ label }) => label)
}

function changeGroup() {
  syncSpecial.value = false
  items.value = []
  total.value = 0
  page.value = 1
  load()
}

function changePage(value) {
  if (value === page.value) return
  page.value = value
  load()
}

function search() {
  appliedQuery = query.value.trim()
  page.value = 1
  load()
}

async function load() {
  const request = ++latest
  remember({ group: group.value, q: appliedQuery, page: page.value })
  listError.value = ''
  loading.value = true
  try {
    const res = await adminApi.get('/users/sponsors', {
      params: { group: group.value, q: appliedQuery, page: page.value, limit },
    })
    if (request !== latest) return
    const data = res.data.data
    items.value = data.items.map((item) => ({
      ...item,
      edit: { amount: Number(item.sponsor_amount || 0), note: item.admin_note || '' },
    }))
    total.value = data.total
    totals.value = data.totals
    if (!data.totals.overlap) syncSpecial.value = false
    page.value = data.page
  } catch (error) {
    if (request !== latest) return
    items.value = []
    total.value = 0
    totals.value = null
    syncSpecial.value = false
    listError.value = error.response?.data?.message || '读取玩家名单失败'
  } finally {
    if (request === latest) loading.value = false
  }
}

async function saveRow(row) {
  if (savingId.value !== null) return
  normalizeAmount(row)
  const changes = {}
  if (row.edit.amount !== Number(row.sponsor_amount || 0)) changes.sponsor_amount = row.edit.amount
  if (row.edit.note !== (row.admin_note || '')) changes.admin_note = row.edit.note
  if (!Object.keys(changes).length) {
    ElMessage.info('没有需要保存的修改')
    return
  }
  const result = await ElMessageBox.prompt('请输入变更原因', '保存玩家信息', {
    confirmButtonText: '保存',
    cancelButtonText: '取消',
    inputPlaceholder: '用于后台审计',
    inputValidator: (value) => Boolean(value?.trim()) || '请填写变更原因',
  }).catch(() => null)
  if (!result) return

  savingId.value = row.user_id
  try {
    await adminApi.patch(`/users/${row.user_id}`, {
      ...changes,
      reason: result.value.trim(),
    })
    ElMessage.success('玩家信息已保存')
    await load()
  } catch (error) {
    ElMessage.error(error.response?.data?.message || '保存玩家信息失败')
  } finally {
    savingId.value = null
  }
}

async function savePermissions() {
  if (permissionSaving.value || loading.value || !targetCount.value) return
  const permissions = { ...permissionForm.value }
  const sync = syncSpecial.value
  const passes = passesOf(permissions)
  const changes = passes.length ? `仅开启${passes.join('、')}特许` : '关闭全部入场特许'
  const special = sync ? `，包含 ${totals.value.overlap} 名有特殊设定的赞助玩家` : ''
  const message = `将为 ${targetCount.value} 名赞助玩家${changes}${special}。请输入变更原因。`
  const result = await ElMessageBox.prompt(message, '统一设置赞助玩家入场特许', {
    confirmButtonText: '应用',
    cancelButtonText: '取消',
    inputPlaceholder: '用于后台审计',
    inputValidator: (value) => Boolean(value?.trim()) || '请填写变更原因',
  }).catch(() => null)
  if (!result) return

  permissionSaving.value = true
  try {
    const response = await adminApi.patch('/users/sponsors/permissions', {
      ...permissions,
      sync_special_settings: sync,
      reason: result.value.trim(),
    })
    ElMessage.success(`已更新 ${response.data.data.affected_count} 名赞助玩家`)
    syncSpecial.value = false
    await load()
  } catch (error) {
    ElMessage.error(error.response?.data?.message || '统一更新赞助玩家权限失败')
  } finally {
    permissionSaving.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.sponsors-page {
  max-width: 1800px;
}
.page-title {
  margin: 0 0 16px;
  font-size: 22px;
  font-weight: 400;
  letter-spacing: 0.01em;
}
.group-selector {
  margin-bottom: 16px;
}
.group-selector :deep(.el-radio-button__inner),
.sync-option :deep(.el-checkbox__label) {
  font-weight: 400;
}
.sync-option {
  margin-top: 12px;
  white-space: normal;
}
.permission-card {
  margin-bottom: 16px;
}
.permission-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.permission-heading h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 400;
}
.permission-heading p {
  margin: 6px 0 0;
  color: #909399;
  font-size: 13px;
}
.permission-list {
  display: flex;
  flex-wrap: wrap;
  gap: 18px 28px;
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid #ebeef5;
}
.permission-item {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border: 1px solid #ebeef5;
  border-radius: 6px;
  background: #fafbfc;
}
.list-card :deep(.el-card__body) {
  padding-top: 20px;
}
.admin-inline-form {
  margin-bottom: 0;
}
.sponsor-table {
  margin-top: 18px;
}
.sponsor-table :deep(.el-table__header th) {
  background: #f8fafc;
  color: #606266;
  font-weight: 400;
}
.sponsor-table :deep(.el-table__cell) {
  padding: 11px 12px;
}
.sponsor-table :deep(.sponsor-amount-cell) {
  padding-right: 22px;
}
.sponsor-table :deep(.sponsor-amount-cell .cell) {
  padding-left: 0;
  padding-right: 0;
}
.sponsor-table :deep(.sponsor-id-cell) {
  white-space: nowrap;
}
.sponsor-table :deep(.sponsor-time-cell) {
  padding-left: 28px;
  white-space: nowrap;
}
.sponsor-time-value {
  color: #606266;
  font-variant-numeric: tabular-nums;
}
.pass-tag {
  margin: 2px 6px 2px 0;
}
.amount-input {
  width: 100%;
  min-width: 128px;
}
.note-input :deep(.el-textarea__inner) {
  min-height: 52px !important;
  line-height: 1.45;
  resize: vertical;
}
.note-input :deep(.el-input__count) {
  opacity: 0;
  transition: opacity 0.15s ease;
}
.note-input:focus-within :deep(.el-input__count) {
  opacity: 1;
}
.empty-value {
  color: #909399;
}
.pagination {
  justify-content: flex-end;
  margin-top: 16px;
}
@media (max-width: 900px) {
  .permission-heading {
    align-items: flex-start;
    flex-direction: column;
  }
  .permission-heading .el-button {
    width: 100%;
  }
  .admin-inline-form :deep(.el-form-item) {
    margin-right: 0;
  }
}
@media (max-width: 768px) {
  .group-selector {
    display: grid;
    grid-template-columns: 4fr 5fr;
  }
  .group-selector :deep(.el-radio-button__inner) {
    width: 100%;
    padding: 8px 2px;
    font-size: 13px;
    white-space: normal;
  }
  .permission-list {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(85px, 1fr));
    gap: 8px;
  }
  .permission-item {
    gap: 4px;
    padding: 2px 0;
    border: 0;
    background: transparent;
  }
  .admin-inline-form.el-form--inline :deep(.el-form-item) {
    width: 100%;
  }
}
</style>
