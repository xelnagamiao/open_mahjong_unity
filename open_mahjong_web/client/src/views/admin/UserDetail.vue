<template>

  <div v-loading="loading">

    <el-page-header @back="$router.push('/admin/users')" content="用户详情" />

    <template v-if="detail">

      <el-card class="block">

        <template #header>账号</template>

        <el-descriptions :column="2" border>

          <el-descriptions-item label="用户 ID">{{ detail.user.user_id }}</el-descriptions-item>

          <el-descriptions-item label="用户名">{{ detail.user.username }}</el-descriptions-item>

          <el-descriptions-item label="类型">{{ detail.user.is_tourist ? '游客' : '注册' }}</el-descriptions-item>

          <el-descriptions-item label="牌谱数">{{ detail.game_record_count }}</el-descriptions-item>

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

              <el-button size="small" type="danger" plain @click="clearSponsor">清除赞助</el-button>

            </div>

          </el-descriptions-item>

          <el-descriptions-item label="MCRPL 资格">

            <el-switch v-model="edit.is_mcrpl_qualified" />

          </el-descriptions-item>

        </el-descriptions>

        <div class="actions">

          <el-input v-model="edit.reason" placeholder="变更原因（必填）" style="max-width: 320px; margin-right: 8px" />

          <el-button type="primary" @click="saveUser" :loading="saving">保存账号设置</el-button>

          <el-button @click="$router.push(`/admin/rank?userId=${detail.user.user_id}`)">编辑段位</el-button>

        </div>

      </el-card>



      <el-card v-if="detail.rank_data" class="block">

        <template #header>段位</template>

        {{ detail.rank_data.guobiao_rank }} / {{ detail.rank_data.guobiao_score }} PT

      </el-card>



      <el-card class="block">

        <template #header>重置密码</template>

        <el-input v-model="newPassword" type="password" show-password placeholder="新密码（至少6位）" style="max-width: 240px" />

        <el-button type="warning" style="margin-left: 8px" @click="resetPassword">重置</el-button>

      </el-card>



      <el-card v-if="detail.user.is_tourist" class="block">

        <template #header>删除游客</template>

        <el-button type="danger" @click="deleteTourist" :disabled="detail.game_record_count > 0">

          删除游客账号

        </el-button>

        <span v-if="detail.game_record_count > 0" class="warn">有牌谱记录时不可删除</span>

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

import { useRoute } from 'vue-router'

import { ElMessage, ElMessageBox } from 'element-plus'

import adminApi from '@/api/adminClient'
import { formatRoomType, formatRule, formatSubRule } from '@/utils/gameMeta'

import { addDays, getSponsorStatus, toPickerValue } from '@/utils/sponsor'



const route = useRoute()

const loading = ref(true)

const saving = ref(false)

const detail = ref(null)

const newPassword = ref('')

const edit = reactive({

  sponsor_expires_at: null,

  is_mcrpl_qualified: false,

  reason: '',

})



const sponsorStatus = computed(() => getSponsorStatus(detail.value?.user?.sponsor_expires_at))



function formatDate(v) {

  return v ? new Date(v).toLocaleString('zh-CN') : '-'

}



function syncEditFromDetail() {

  if (!detail.value?.user) return

  edit.sponsor_expires_at = detail.value.user.sponsor_expires_at

    ? toPickerValue(detail.value.user.sponsor_expires_at)

    : null

  edit.is_mcrpl_qualified = detail.value.user.is_mcrpl_qualified

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

    await adminApi.patch(`/users/${route.params.userId}`, {

      sponsor_expires_at: edit.sponsor_expires_at,

      is_mcrpl_qualified: edit.is_mcrpl_qualified,

      reason: edit.reason,

    })

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

  } catch (e) {

    ElMessage.error(e.response?.data?.message || '操作失败')

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

    history.back()

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

</style>

