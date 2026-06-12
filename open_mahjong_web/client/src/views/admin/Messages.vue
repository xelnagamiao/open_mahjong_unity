<template>
  <div>
    <h2 class="page-title">消息推送</h2>
    <el-row :gutter="16">
      <el-col :span="12">
        <el-card>
          <template #header>全服广播</template>
          <el-form label-width="80px">
            <el-form-item label="标题">
              <el-input v-model="broadcastForm.title" maxlength="64" show-word-limit />
            </el-form-item>
            <el-form-item label="内容">
              <el-input
                v-model="broadcastForm.content"
                type="textarea"
                :rows="6"
                maxlength="2000"
                show-word-limit
              />
            </el-form-item>
            <el-button type="primary" :loading="broadcastLoading" @click="sendBroadcast">
              发送全服广播
            </el-button>
          </el-form>
        </el-card>
      </el-col>
      <el-col :span="12">
        <el-card>
          <template #header>指定用户</template>
          <el-form label-width="80px">
            <el-form-item label="用户 ID">
              <el-input v-model="userForm.user_id" placeholder="例如 10000001" />
            </el-form-item>
            <el-form-item label="标题">
              <el-input v-model="userForm.title" maxlength="64" show-word-limit />
            </el-form-item>
            <el-form-item label="内容">
              <el-input
                v-model="userForm.content"
                type="textarea"
                :rows="6"
                maxlength="2000"
                show-word-limit
              />
            </el-form-item>
            <el-button type="primary" :loading="userLoading" @click="sendToUser">
              发送给用户
            </el-button>
          </el-form>
        </el-card>
      </el-col>
    </el-row>
    <el-alert
      class="hint"
      type="info"
      :closable="false"
      title="消息将推送给当前在线且已登录的玩家，并在客户端以弹窗形式展示。"
    />
  </div>
</template>

<script setup>
import { reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const broadcastForm = reactive({
  title: '',
  content: '',
})

const userForm = reactive({
  user_id: '',
  title: '',
  content: '',
})

const broadcastLoading = ref(false)
const userLoading = ref(false)

function validateForm(form) {
  const title = form.title.trim()
  const content = form.content.trim()
  if (!title) {
    ElMessage.warning('请输入消息标题')
    return null
  }
  if (!content) {
    ElMessage.warning('请输入消息内容')
    return null
  }
  return { title, content }
}

async function confirmSend(summary) {
  try {
    await ElMessageBox.confirm(summary, '确认发送', {
      confirmButtonText: '确认发送',
      cancelButtonText: '取消',
      type: 'warning',
      dangerouslyUseHTMLString: true,
    })
    return true
  } catch {
    return false
  }
}

function buildSummary(targetLabel, title, content) {
  const safeTitle = title.replace(/</g, '&lt;').replace(/>/g, '&gt;')
  const safeContent = content.replace(/</g, '&lt;').replace(/>/g, '&gt;')
  return `<p><strong>发送对象：</strong>${targetLabel}</p>
<p><strong>标题：</strong>${safeTitle}</p>
<p><strong>内容：</strong></p>
<p style="white-space: pre-wrap; margin: 0;">${safeContent}</p>`
}

async function sendBroadcast() {
  const parsed = validateForm(broadcastForm)
  if (!parsed) return

  const confirmed = await confirmSend(
    buildSummary('全服所有在线玩家', parsed.title, parsed.content)
  )
  if (!confirmed) return

  broadcastLoading.value = true
  try {
    const res = await adminApi.post('/messages/broadcast', parsed)
    ElMessage.success(res.data.message || '广播已发送')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '广播发送失败')
  } finally {
    broadcastLoading.value = false
  }
}

async function sendToUser() {
  const userId = parseInt(userForm.user_id, 10)
  if (Number.isNaN(userId) || userId <= 0) {
    ElMessage.warning('请输入有效的用户 ID')
    return
  }

  const parsed = validateForm(userForm)
  if (!parsed) return

  const confirmed = await confirmSend(
    buildSummary(`用户 ID ${userId}`, parsed.title, parsed.content)
  )
  if (!confirmed) return

  userLoading.value = true
  try {
    const res = await adminApi.post('/messages/user', {
      user_id: userId,
      ...parsed,
    })
    const username = res.data.data?.username
    const suffix = username ? `（${username}）` : ''
    ElMessage.success(res.data.message || `已向用户 ${userId}${suffix} 发送消息`)
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '消息发送失败')
  } finally {
    userLoading.value = false
  }
}
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.hint {
  margin-top: 16px;
}
</style>
