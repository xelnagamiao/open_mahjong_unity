<template>
  <div class="player-login-page">
    <div class="login-card">
      <h1>玩家登录</h1>
      <p class="hint">使用已注册的游戏账号登录（与对战平台同一账户）</p>
      <form @submit.prevent="onSubmit">
        <label>
          <span>用户名</span>
          <input v-model="form.username" autocomplete="username" />
        </label>
        <label>
          <span>密码</span>
          <input v-model="form.password" type="password" autocomplete="current-password" />
        </label>
        <button type="submit" :disabled="loading">{{ loading ? '登录中…' : '登录' }}</button>
      </form>
      <p v-if="error" class="err">{{ error }}</p>
    </div>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'

const router = useRouter()
const route = useRoute()
const auth = usePlayerAuthStore()
const loading = ref(false)
const error = ref('')
const form = reactive({ username: '', password: '' })

function goAfterLogin() {
  const redirect = route.query.redirect
  if (typeof redirect === 'string' && redirect.startsWith('/') && redirect !== '/login') {
    router.replace(redirect)
  } else {
    router.replace('/')
  }
}

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
  if (auth.isLoggedIn) goAfterLogin()
})

async function onSubmit() {
  loading.value = true
  error.value = ''
  try {
    await auth.login(form.username, form.password)
    goAfterLogin()
  } catch (e) {
    error.value = e.response?.data?.message || '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.player-login-page {
  max-width: 420px;
  margin: 24px auto;
}
.login-card {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 24px 22px;
}
.login-card h1 {
  margin: 0 0 8px;
  font-size: 1.3rem;
}
.hint {
  color: #888;
  font-size: 13px;
  margin-bottom: 18px;
}
label {
  display: block;
  margin-bottom: 12px;
  font-size: 13px;
  color: #555;
}
label span {
  display: block;
  margin-bottom: 4px;
}
input {
  width: 100%;
  box-sizing: border-box;
  padding: 8px 10px;
  border: 1px solid #ddd;
  font: inherit;
}
button {
  width: 100%;
  margin-top: 8px;
  padding: 10px;
  border: 0;
  background: #409eff;
  color: #fff;
  font-weight: 700;
  cursor: pointer;
}
button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.err {
  color: #c00;
  font-size: 13px;
  margin-top: 12px;
}
</style>
