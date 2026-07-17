<template>
  <div class="admin-login-page">
    <el-card class="login-card">
      <h1>OMU管理后台登录</h1>
      <p class="hint">使用已配置的管理员游戏账号登录</p>
      <el-form @submit.prevent="onSubmit">
        <el-form-item label="用户名">
          <el-input v-model="form.username" autocomplete="username" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="form.password" type="password" show-password autocomplete="current-password" />
        </el-form-item>
        <el-button type="primary" native-type="submit" :loading="loading" style="width: 100%">
          登录
        </el-button>
      </el-form>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useAdminAuthStore } from '@/stores/adminAuth'

const router = useRouter()
const route = useRoute()
const auth = useAdminAuthStore()
const loading = ref(false)
const form = reactive({ username: '', password: '' })

function resolveAdminRedirect() {
  const redirect = route.query.redirect
  if (
    typeof redirect === 'string' &&
    redirect.startsWith('/admin') &&
    !redirect.startsWith('/admin/login')
  ) {
    return redirect
  }
  return '/admin'
}

async function goAdmin() {
  const target = resolveAdminRedirect()
  await router.replace(target)
}

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
  if (auth.isLoggedIn) await goAdmin()
})

async function onSubmit() {
  loading.value = true
  try {
    await auth.login(form.username, form.password)
    ElMessage.success('登录成功')
    await goAdmin()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '登录失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.admin-login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}
.login-card {
  width: 100%;
  max-width: 400px;
  margin: 0 16px;
  padding: 8px;
  box-sizing: border-box;
}
.login-card h1 {
  margin: 0 0 8px;
  font-size: 22px;
}
.hint {
  color: #909399;
  font-size: 13px;
  margin-bottom: 20px;
}
</style>
