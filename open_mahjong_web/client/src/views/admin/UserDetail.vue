<template>

  <div v-loading="loading">

    <el-page-header @back="backToList" content="用户详情" />

    <template v-if="detail">

      <el-card class="block">

        <template #header>账号</template>

        <el-descriptions :column="2" border>

          <el-descriptions-item label="用户 ID">{{ detail.user.user_id }}</el-descriptions-item>

          <el-descriptions-item label="当前用户名">{{ detail.user.username }}</el-descriptions-item>

          <el-descriptions-item label="绑定邮箱" :span="2">
            <template v-if="detail.user.email">
              <span>{{ detail.user.email }}</span>
              <el-tag
                :type="detail.user.email_verified_at ? 'success' : 'warning'"
                size="small"
                class="email-tag"
              >
                {{ detail.user.email_verified_at ? '已验证' : '未验证' }}
              </el-tag>
            </template>
            <span v-else class="rank-empty">未绑定</span>
          </el-descriptions-item>

          <el-descriptions-item label="类型">{{ detail.user.is_tourist ? '游客' : '注册' }}</el-descriptions-item>

          <el-descriptions-item label="改名卡">{{ detail.user.rename_card_count ?? 0 }} 张（可用 {{ detail.user.rename_card_usable_count ?? 0 }} 张）</el-descriptions-item>

          <el-descriptions-item label="牌谱数">{{ detail.game_record_count }}</el-descriptions-item>

          <el-descriptions-item label="在线状态">
            <el-tag :type="detail.online ? 'success' : 'info'" size="small">
              {{ detail.online ? '在线' : '离线' }}
            </el-tag>
          </el-descriptions-item>

          <el-descriptions-item label="赞助状态" :span="2">

            <el-tag :type="sponsorStatus.tagType" size="small">{{ sponsorStatus.label }}</el-tag>

            <span v-if="sponsorStatus.remaining" class="sponsor-meta">

              {{ sponsorStatus.remaining }}

            </span>

            <span v-if="detail.user.sponsor_expires_at" class="sponsor-meta">

              到期：{{ formatDate(detail.user.sponsor_expires_at) }}

            </span>

          </el-descriptions-item>

          <el-descriptions-item label="赞助到期时间" :span="2">

            <div class="sponsor-edit">

              <el-date-picker

                v-model="edit.sponsor_expires_at"

                type="datetime"

                placeholder="选择到期时间"

                value-format="YYYY-MM-DD HH:mm:ss"

                style="width: 240px"

              />

              <el-button size="small" @click="extendSponsor(30)">+30 天</el-button>

              <el-button size="small" @click="extendSponsor(90)">+90 天</el-button>

              <el-button size="small" @click="extendSponsor(365)">+365 天</el-button>

              <el-button size="small" type="danger" plain @click="clearSponsor">清除到期时间</el-button>

            </div>

          </el-descriptions-item>

          <el-descriptions-item label="赞助金额" :span="2">
            <el-input-number
              v-model="edit.sponsor_amount"
              :min="0"
              :precision="2"
              :step="10"
              controls-position="right"
              style="width: 220px"
            />
            <span class="sponsor-meta">有赞助金额或到期时间的玩家会出现在赞助名单中。</span>
          </el-descriptions-item>

          <el-descriptions-item label="玩家注释" :span="2">
            <el-input
              v-model="edit.admin_note"
              type="textarea"
              :rows="3"
              maxlength="2000"
              show-word-limit
              placeholder="仅管理员可见"
              style="max-width: 520px"
            />
          </el-descriptions-item>

          <el-descriptions-item label="特许入场" :span="2">
            <div class="ladder-pass-list">
              <label v-for="pass in ladderPassOptions" :key="pass.key" class="ladder-pass-item">
                <el-switch v-model="edit[pass.key]" :aria-label="pass.label" />
                <span>{{ pass.label }}</span>
              </label>
            </div>
            <p class="rename-hint ladder-pass-hint">
              初级/中级/高级/MCRPL特许可突破该场次最低段位，与赞助状态无关；中级场七段及以上上限仍生效。
            </p>
          </el-descriptions-item>

          <el-descriptions-item label="特殊设定" :span="2">
            <el-switch v-model="hasSpecialSettings" :disabled="requiresSpecialSettings" aria-label="保留个人入场特许" />
            <span class="sponsor-meta">保留个人入场特许</span>
            <p class="rename-hint ladder-pass-hint">
              {{ requiresSpecialSettings
                ? '非赞助玩家的入场特许属于特殊设定。清空入场特许后可关闭此项。'
                : '开启后会列入特殊设定名单。统一修改赞助权限时，默认保留个人设定。' }}
            </p>
          </el-descriptions-item>

          <el-descriptions-item label="封禁状态" :span="2">

            <el-tag :type="banStatus.tagType" size="small">{{ banStatus.label }}</el-tag>

            <span v-if="detail.user.ban_type" class="sponsor-meta">

              类型：{{ banTypeLabel(detail.user.ban_type) }}

            </span>

            <span v-if="detail.user.ban_expires_at" class="sponsor-meta">

              到期：{{ formatDate(detail.user.ban_expires_at) }}

            </span>

            <span v-else-if="detail.user.ban_type" class="sponsor-meta">永久</span>

          </el-descriptions-item>

          <el-descriptions-item label="封禁类型" :span="2">

            <el-select v-model="edit.ban_type" clearable placeholder="未封禁" style="width: 200px">

              <el-option v-for="(label, key) in BAN_TYPE_LABELS" :key="key" :label="label" :value="key" />

            </el-select>

            <el-button size="small" type="danger" plain style="margin-left: 8px" @click="clearBan">

              解除封禁

            </el-button>

          </el-descriptions-item>

          <el-descriptions-item label="封禁到期" :span="2">

            <div class="sponsor-edit">

              <el-date-picker

                v-model="edit.ban_expires_at"

                type="datetime"

                placeholder="留空表示永久封禁"

                value-format="YYYY-MM-DD HH:mm:ss"

                style="width: 240px"

                :disabled="!edit.ban_type"

              />

              <el-button size="small" :disabled="!edit.ban_type" @click="extendBan(1)">+1 天</el-button>

              <el-button size="small" :disabled="!edit.ban_type" @click="extendBan(7)">+7 天</el-button>

              <el-button size="small" :disabled="!edit.ban_type" @click="extendBan(30)">+30 天</el-button>

              <el-button size="small" :disabled="!edit.ban_type" @click="edit.ban_expires_at = null">设为永久</el-button>

            </div>

          </el-descriptions-item>

          <el-descriptions-item label="封禁原因" :span="2">

            <el-input

              v-model="edit.ban_reason"

              type="textarea"

              :rows="2"

              placeholder="玩家登录被拒时将看到此原因"

              style="max-width: 480px"

              :disabled="!edit.ban_type"

            />

          </el-descriptions-item>

        </el-descriptions>

        <div class="actions">

          <el-input v-model="edit.reason" placeholder="变更原因（必填）" autocomplete="off" name="admin-change-reason" style="max-width: 320px; margin-right: 8px" />

          <el-button type="primary" @click="saveUser" :loading="saving">保存账号设置</el-button>

          <el-button type="danger" plain @click="kickUser" :loading="kicking">踢下线</el-button>

        </div>

      </el-card>



      <UserWallet v-if="!detail.user.is_tourist" :key="detail.user.user_id" :user-id="detail.user.user_id" />

      <el-card v-if="!detail.user.is_tourist" class="block">
        <template #header>改名</template>
        <div class="credential-form">
          <p class="rename-hint">用户名最多16个字符，显示长度2–20（中日韩及全角字符计2，其他字符计1）。</p>
          <div class="credential-row">
            <el-input
              v-model="renameForm.new_username"
              name="om-admin-rename-target"
              autocomplete="off"
              clearable
              placeholder="新用户名"
              style="max-width: 220px"
              data-lpignore="true"
              data-1p-ignore="true"
              data-form-type="other"
            />
            <el-input
              v-model="renameForm.reason"
              name="om-admin-rename-reason"
              autocomplete="off"
              clearable
              placeholder="变更原因（必填）"
              style="max-width: 220px"
              data-lpignore="true"
              data-1p-ignore="true"
              data-form-type="other"
            />
            <el-checkbox v-model="renameForm.sync_history">同步该 UID 的全部历史牌谱名</el-checkbox>
            <el-button type="primary" :loading="renaming" @click="renameUser">保存新用户名</el-button>
          </div>
        </div>
      </el-card>

      <el-card class="block">
        <template #header>重置密码</template>
        <div class="credential-form">
          <div class="credential-row">
            <el-input
              v-model="newPassword"
              type="password"
              name="om-admin-reset-secret"
              autocomplete="off"
              :readonly="passwordReadonly"
              @focus="passwordReadonly = false"
              show-password
              placeholder="新密码（至少6位）"
              style="max-width: 220px"
              data-lpignore="true"
              data-1p-ignore="true"
              data-form-type="other"
            />
            <el-button type="warning" @click="resetPassword">重置密码</el-button>
          </div>
        </div>
      </el-card>



      <el-card class="block">

        <template #header>历史牌谱用户名迁移</template>

        <p class="rename-hint">只修改当前 UID 下，原历史用户名完全匹配的牌谱快照；不会影响其他 UID 的同名玩家，也不会改变当前账户用户名。</p>

        <div class="credential-form">
          <div class="credential-row">
            <el-input v-model="historyRename.oldUsername" name="om-admin-history-old" autocomplete="off" placeholder="原历史用户名" style="max-width: 180px" data-lpignore="true" data-1p-ignore="true" data-form-type="other" />
            <el-input v-model="historyRename.newUsername" name="om-admin-history-new" autocomplete="off" placeholder="新历史用户名" style="max-width: 180px" data-lpignore="true" data-1p-ignore="true" data-form-type="other" />
            <el-input v-model="historyRename.reason" name="om-admin-history-reason" autocomplete="off" placeholder="变更原因（必填）" style="max-width: 220px" data-lpignore="true" data-1p-ignore="true" data-form-type="other" />
            <el-button type="warning" plain :loading="renamingHistory" @click="renameHistoryUsername">迁移历史用户名</el-button>
          </div>
        </div>

      </el-card>

      <el-card class="block">

        <template #header>段位</template>

        <div class="rank-row">

          <span v-if="detail.rank_data">

            {{ detail.rank_data.guobiao_rank }} / {{ detail.rank_data.guobiao_score }} PT

          </span>

          <span v-else class="rank-empty">暂无段位数据</span>

          <div class="rank-actions">

            <el-button @click="$router.push(`/admin/rank?userId=${detail.user.user_id}`)">编辑段位</el-button>

            <el-button type="danger" plain :loading="resettingRank" @click="resetRank">重置段位</el-button>

          </div>

        </div>

      </el-card>



      <el-card v-if="detail.user.is_tourist" class="block">

        <template #header>删除游客</template>

        <el-button type="danger" @click="deleteTourist" :disabled="detail.game_record_count > 0">

          删除游客账号

        </el-button>

        <span v-if="detail.game_record_count > 0" class="warn">有牌谱记录时不可删除</span>

      </el-card>



      <UserTitlesPanel class="block" :user-id="detail.user.user_id" />
      <UserInventoryPanel class="block" :user-id="detail.user.user_id" />

      <el-card class="block">

        <template #header>最近登录 IP（最多 20 条）</template>

        <el-table :data="detail.recent_login_ips || []" size="small" empty-text="暂无登录记录">

          <el-table-column prop="ip_address" label="IP" min-width="140" />
          <el-table-column prop="country_name" label="国家／地区（估算）" min-width="160" />

          <el-table-column label="登录时间" min-width="180">

            <template #default="{ row }">{{ formatDate(row.logged_at) }}</template>

          </el-table-column>

          <el-table-column label="操作" width="90">

            <template #default="{ row }">

              <el-button link type="danger" @click="banLoginIp(row)">封禁 IP</el-button>

            </template>

          </el-table-column>

        </el-table>
        <p>按登录网络估算，不代表国籍或常住地。无法识别时显示未知。<a href="https://db-ip.com" target="_blank" rel="noopener noreferrer">IP Geolocation by DB-IP</a></p>
      </el-card>

      <el-card class="block">
        <template #header>最近对局</template>

        <el-table :data="detail.recent_games" size="small">

          <el-table-column prop="game_id" label="对局 ID" />

          <el-table-column label="对局类型" width="100">
            <template #default="{ row }">{{ formatRoomType(row.room_type) }}</template>
          </el-table-column>

          <el-table-column label="规则" width="80">
            <template #default="{ row }">{{ formatRule(row.rule) }}</template>
          </el-table-column>

          <el-table-column label="子规则" min-width="120">
            <template #default="{ row }">{{ formatSubRule(row.sub_rule) }}</template>
          </el-table-column>

          <el-table-column prop="created_at" label="时间">

            <template #default="{ row }">{{ formatDate(row.created_at) }}</template>

          </el-table-column>

          <el-table-column label="操作" width="80">

            <template #default="{ row }">

              <el-button link @click="$router.push(`/admin/games?game_id=${row.game_id}`)">查看</el-button>

            </template>

          </el-table-column>

        </el-table>

      </el-card>

    </template>

  </div>

</template>



<script setup>

import { computed, onMounted, reactive, ref } from 'vue'

import { useRoute, useRouter } from 'vue-router'
import { returnToAdminList } from '@/composables/useAdminListState'

import { ElMessage, ElMessageBox } from 'element-plus'

import adminApi from '@/api/adminClient'
import UserTitlesPanel from './UserTitlesPanel.vue'
import UserInventoryPanel from './UserInventoryPanel.vue'
import UserWallet from '@/components/admin/UserWallet.vue'
import { formatRoomType, formatRule, formatSubRule } from '@/utils/gameMeta'
import { banLabels as BAN_TYPE_LABELS } from '@/utils/accountBans'

import { addDays, getSponsorStatus, ladderPassOptions, toPickerValue } from '@/utils/sponsor'



const route = useRoute()
const router = useRouter()
function backToList() {
  returnToAdminList(router, route.query.from === 'bans' ? '/admin/account-bans' : '/admin/users')
}

const loading = ref(true)

const saving = ref(false)

const kicking = ref(false)

const renaming = ref(false)

const renamingHistory = ref(false)

const resettingRank = ref(false)

const detail = ref(null)

const newPassword = ref('')
const passwordReadonly = ref(true)

const specialSettingsChoice = ref(null)

const edit = reactive({

  sponsor_expires_at: null,

  sponsor_amount: 0,

  admin_note: '',

  is_beginner_qualified: false,

  is_intermediate_qualified: false,

  is_advanced_qualified: false,

  is_mcrpl_qualified: false,

  ban_type: null,

  ban_expires_at: null,

  ban_reason: '',

  reason: '',

})

const renameForm = reactive({

  new_username: '',

  sync_history: false,

  reason: '',

})

const historyRename = reactive({ oldUsername: '', newUsername: '', reason: '' })



const LOGIN_BAN_TYPES = new Set(['login', 'full'])



const banStatus = computed(() => {

  const user = detail.value?.user

  if (!user?.ban_type) {

    return { label: '正常', tagType: 'success' }

  }

  const expires = user.ban_expires_at ? new Date(user.ban_expires_at).getTime() : null

  if (expires !== null && expires <= Date.now()) {

    return { label: '封禁已过期', tagType: 'info' }

  }

  if (LOGIN_BAN_TYPES.has(user.ban_type)) {

    return { label: '禁止登录', tagType: 'danger' }

  }

  return { label: '受限中', tagType: 'warning' }

})



function banTypeLabel(type) {

  return BAN_TYPE_LABELS[type] || type

}



const sponsorStatus = computed(() => getSponsorStatus(detail.value?.user?.sponsor_expires_at, detail.value?.user?.sponsor_amount))

const changedLadderPasses = computed(() => ladderPassOptions.filter(
  ({ key }) => edit[key] !== !!detail.value?.user?.[key]
))

const requiresSpecialSettings = computed(() => !edit.sponsor_expires_at
  && !Number(edit.sponsor_amount)
  && ladderPassOptions.some(({ key }) => edit[key]))

const hasSpecialSettings = computed({
  get() {
    if (requiresSpecialSettings.value) return true
    return specialSettingsChoice.value ?? (changedLadderPasses.value.length > 0 || !!detail.value?.user?.has_special_settings)
  },
  set: value => { specialSettingsChoice.value = value },
})



function formatDate(v) {

  return v ? new Date(v).toLocaleString('zh-CN') : '-'

}



function syncEditFromDetail() {

  if (!detail.value?.user) return

  edit.sponsor_expires_at = detail.value.user.sponsor_expires_at

    ? toPickerValue(detail.value.user.sponsor_expires_at)

    : null

  edit.sponsor_amount = Number(detail.value.user.sponsor_amount || 0)
  edit.admin_note = detail.value.user.admin_note || ''

  for (const { key } of ladderPassOptions) edit[key] = !!detail.value.user[key]
  specialSettingsChoice.value = null

  edit.ban_type = detail.value.user.ban_type || null

  edit.ban_expires_at = detail.value.user.ban_expires_at

    ? toPickerValue(detail.value.user.ban_expires_at)

    : null

  edit.ban_reason = detail.value.user.ban_reason || ''
}



function extendSponsor(days) {

  const base = edit.sponsor_expires_at || detail.value?.user?.sponsor_expires_at

  const activeBase =

    base && new Date(base).getTime() > Date.now() ? base : new Date()

  edit.sponsor_expires_at = toPickerValue(addDays(activeBase, days))

}



function clearSponsor() {

  edit.sponsor_expires_at = null

}



function clearBan() {

  edit.ban_type = null

  edit.ban_expires_at = null

  edit.ban_reason = ''

}



function extendBan(days) {

  const base = edit.ban_expires_at || detail.value?.user?.ban_expires_at

  const activeBase =

    base && new Date(base).getTime() > Date.now() ? base : new Date()

  edit.ban_expires_at = toPickerValue(addDays(activeBase, days))

}



async function load() {

  loading.value = true

  try {

    const res = await adminApi.get(`/users/${route.params.userId}`)

    detail.value = res.data.data

    syncEditFromDetail()

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '加载失败')

  } finally {

    loading.value = false

  }

}



async function saveUser() {

  if (!edit.reason.trim()) {

    ElMessage.warning('请填写变更原因')

    return

  }

  saving.value = true

  try {

    const saved = detail.value.user
    const banType = edit.ban_type || null
    const banChanged = banType !== saved.ban_type
      || (edit.ban_expires_at || null) !== toPickerValue(saved.ban_expires_at)
      || edit.ban_reason !== (saved.ban_reason || '')
    const changes = {
      ...Object.fromEntries(changedLadderPasses.value.map(({ key }) => [key, edit[key]])),
      ...(banChanged && {
        ban_type: banType,
        ban_expires_at: edit.ban_expires_at ? new Date(edit.ban_expires_at.replace(' ', 'T')).toISOString() : null,
        ban_reason: edit.ban_reason,
      }),
    }
    if ((edit.sponsor_expires_at || null) !== toPickerValue(saved.sponsor_expires_at)) {
      changes.sponsor_expires_at = edit.sponsor_expires_at || null
    }
    if (Number(edit.sponsor_amount || 0) !== Number(saved.sponsor_amount || 0)) {
      changes.sponsor_amount = edit.sponsor_amount || 0
    }
    if (edit.admin_note !== (saved.admin_note || '')) changes.admin_note = edit.admin_note
    if (specialSettingsChoice.value !== null || changedLadderPasses.value.length) {
      changes.has_special_settings = hasSpecialSettings.value
    }
    if (!Object.keys(changes).length) {
      ElMessage.info('账号设置没有变化')
      return
    }
    await adminApi.patch(`/users/${route.params.userId}`, { ...changes, reason: edit.reason })

    ElMessage.success('已保存')

    edit.reason = ''

    await load()

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '保存失败')

  } finally {

    saving.value = false

  }

}



async function resetPassword() {

  const { value: reason } = await ElMessageBox.prompt('请输入变更原因', '重置密码', {

    confirmButtonText: '确定',

    cancelButtonText: '取消',

  }).catch(() => null)

  if (!reason) return

  if (!newPassword.value || newPassword.value.length < 6) {

    ElMessage.warning('新密码至少 6 位')

    return

  }

  try {

    await adminApi.post(`/users/${route.params.userId}/reset-password`, {

      new_password: newPassword.value,

      reason,

    })

    ElMessage.success('密码已重置')

    newPassword.value = ''
    passwordReadonly.value = true

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '操作失败')

  }

}



async function kickUser() {

  const { value: reason } = await ElMessageBox.prompt('请输入踢下线原因', '踢下线', {

    type: 'warning',

    inputValue: '管理员已将您的账号踢下线',

  }).catch(() => null)

  if (!reason?.trim()) return

  kicking.value = true

  try {

    await adminApi.post(`/users/${route.params.userId}/kick`, { reason: reason.trim() })

    ElMessage.success('已踢下线')

    await load()

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '踢下线失败')

  } finally {

    kicking.value = false

  }

}



async function renameUser() {

  const newName = renameForm.new_username.trim()

  if (!newName) {

    ElMessage.warning('请输入新用户名')

    return

  }

  if (!renameForm.reason.trim()) {

    ElMessage.warning('请填写变更原因')

    return

  }

  if (newName === detail.value?.user?.username) {

    ElMessage.warning('新用户名与当前相同')

    return

  }

  renaming.value = true

  try {

    const res = await adminApi.post(`/users/${route.params.userId}/rename`, {

      new_username: newName,

      sync_history: renameForm.sync_history,

      reason: renameForm.reason.trim(),

    })

    ElMessage.success(res.data.message || '改名成功')

    renameForm.new_username = ''
    renameForm.reason = ''
    renameForm.sync_history = false

    await load()

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '改名失败')

  } finally {

    renaming.value = false

  }

}



async function renameHistoryUsername() {
  const oldName = historyRename.oldUsername.trim()
  const newName = historyRename.newUsername.trim()
  if (!oldName || !newName || !historyRename.reason.trim()) {
    ElMessage.warning('请填写原历史用户名、新历史用户名和变更原因')
    return
  }
  const { value: confirmation } = await ElMessageBox.prompt(
    `只会修改 UID ${detail.value?.user?.user_id} 下的历史用户名“${oldName}”。请输入“${oldName}”确认。`,
    '确认迁移历史用户名',
    { type: 'warning', confirmButtonText: '确认迁移', cancelButtonText: '取消' },
  ).catch(() => null)
  if (confirmation !== oldName) return
  renamingHistory.value = true
  try {
    const res = await adminApi.post(`/users/${route.params.userId}/history-username`, {
      old_username: oldName,
      new_username: newName,
      reason: historyRename.reason.trim(),
    })
    ElMessage.success(`已更新 ${res.data.data.updated_game_player_records} 条历史牌谱记录`)
    historyRename.oldUsername = ''
    historyRename.newUsername = ''
    historyRename.reason = ''
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '历史用户名迁移失败')
  } finally {
    renamingHistory.value = false
  }
}

async function resetRank() {

  const { value: reason } = await ElMessageBox.prompt('重置原因', '重置段位', {

    type: 'warning',

    confirmButtonText: '确定重置',

    cancelButtonText: '取消',

  }).catch(() => null)

  if (!reason?.trim()) return

  resettingRank.value = true

  try {

    const res = await adminApi.post(`/rank/${route.params.userId}/reset`, {

      reason: reason.trim(),

    })

    const data = res.data.data

    if (detail.value) {

      detail.value.rank_data = {

        ...(detail.value.rank_data || {}),

        guobiao_rank: data.guobiao_rank,

        guobiao_score: data.guobiao_score,

      }

    }

    ElMessage.success('段位已重置为 10级')

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '重置失败')

  } finally {

    resettingRank.value = false

  }

}



async function banLoginIp(row) {

  const { value: reason } = await ElMessageBox.prompt(

    `封禁 IP：${row.ip_address}`,

    '封禁 IP',

    {

      type: 'warning',

      inputValue: `封禁用户 ${detail.value?.user?.username} 的登录 IP`,

    },

  ).catch(() => null)

  if (!reason?.trim()) return

  try {

    await adminApi.post('/ip-bans', {

      ip_address: row.ip_address,

      ban_expires_at: null,

      ban_reason: reason.trim(),

      reason: reason.trim(),

    })

    ElMessage.success('IP 已封禁')

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '封禁失败')

  }

}



async function deleteTourist() {

  const { value: reason } = await ElMessageBox.prompt('请输入删除原因', '删除游客', {

    type: 'warning',

  }).catch(() => null)

  if (!reason) return

  try {

    await adminApi.delete(`/users/${route.params.userId}`, { data: { reason } })

    ElMessage.success('已删除')

    backToList()

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '删除失败')

  }

}



onMounted(load)

</script>



<style scoped>

.block {

  margin-top: 16px;

}

.actions {

  margin-top: 16px;

  display: flex;

  flex-wrap: wrap;

  align-items: center;

  gap: 8px;

}

.sponsor-edit {

  display: flex;

  flex-wrap: wrap;

  align-items: center;

  gap: 8px;

}

.sponsor-meta {

  margin-left: 12px;

  color: #606266;

  font-size: 13px;

}

.warn {

  margin-left: 12px;

  color: #e6a23c;

  font-size: 13px;

}

.rename-hint {

  margin: 0 0 12px;

  color: #909399;

  font-size: 13px;

  line-height: 1.5;

}

.email-tag {
  margin-left: 8px;
}

.ladder-pass-list {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px 24px;
}

.ladder-pass-item {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  color: #606266;
  font-size: 13px;
}

.ladder-pass-hint {
  margin-top: 8px;
  margin-bottom: 0;
}

.credential-form {
  position: relative;
}

.credential-row {

  display: flex;

  flex-wrap: wrap;

  align-items: center;

  gap: 8px;

  margin-bottom: 12px;

}

.credential-row:last-child {

  margin-bottom: 0;

}

.rank-row {

  display: flex;

  flex-wrap: wrap;

  align-items: center;

  justify-content: flex-start;

  gap: 12px;

}

.rank-empty {

  color: #909399;

  font-size: 13px;

}

.rank-actions {

  display: flex;

  flex-wrap: wrap;

  gap: 8px;

}

</style>

