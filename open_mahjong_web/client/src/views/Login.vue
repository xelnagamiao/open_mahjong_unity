<template>
  <div class="player-login-page">
    <div class="login-card">
      <h1>玩家登录</h1>
      <p class="hint">使用已注册的游戏账号登录（与对战平台同一账户）</p>
      <form @submit.prevent="onSubmit">
        <label>
          <span>用户名 / 邮箱</span>
          <input v-model="form.username" name="username" autocomplete="username" />
        </label>
        <label>
          <span>密码</span>
          <input v-model="form.password" name="password" type="password" autocomplete="current-password" />
        </label>
        <div class="login-options">
          <label class="option-checkbox">
            <input v-model="form.rememberPassword" type="checkbox" @change="saveLoginPreferences(form)" />
            <span>记住密码</span>
          </label>
          <label class="option-checkbox">
            <input v-model="form.keepLoggedIn" type="checkbox" @change="saveLoginPreferences(form)" />
            <span>保持登录状态 30 天</span>
          </label>
        </div>
        <button type="submit" :disabled="loading">{{ loading ? '登录中…' : '登录' }}</button>
      </form>
      <p v-if="error" class="err">{{ error }}</p>
      <p class="switch-page"><router-link to="/forgot-password">忘记密码？</router-link></p>
      <p class="switch-page">
        还没有账号？
        <router-link :to="registerTarget">立即注册</router-link>
      </p>
    </div>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { loadLoginPreferences, saveLoginPreferences, saveBrowserPassword, getBrowserPassword } from '@/utils/loginPreferences'

const router = useRouter()
const route = useRoute()
const auth = usePlayerAuthStore()
const loading = ref(false)
const error = ref('')
const form = reactive({ ...loadLoginPreferences(), password: '' })
const registerTarget = {
  path: '/register',
  query: typeof route.query.redirect === 'string' ? { redirect: route.query.redirect } : {},
}

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
  if (auth.isLoggedIn) {
    goAfterLogin()
    return
  }
  if (form.rememberPassword) {
    const initialUsername = form.username
    const credential = await getBrowserPassword()
    if (credential && form.rememberPassword && form.username === initialUsername && !form.password && !loading.value) {
      form.username = credential.id
      form.password = credential.password
    }
  }
})

async function onSubmit() {
  if (loading.value) return
  loading.value = true
  error.value = ''
  try {
    const { username, password, rememberPassword, keepLoggedIn } = form
    await auth.login(username, password, keepLoggedIn)
    saveLoginPreferences({ username, rememberPassword, keepLoggedIn })
    if (rememberPassword) await saveBrowserPassword(username, password)
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
.login-options {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 16px;
  margin: 14px 0 4px;
}
.option-checkbox {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 28px;
  margin: 0;
  color: #606266;
  line-height: 20px;
  cursor: pointer;
}
.option-checkbox input {
  appearance: none;
  display: inline-grid;
  place-content: center;
  width: 16px;
  height: 16px;
  margin: 0;
  padding: 0;
  border: 1px solid #c5cad3;
  border-radius: 4px;
  background: #fff;
  flex-shrink: 0;
  cursor: pointer;
  transition: border-color 0.15s, background-color 0.15s;
}
.option-checkbox input::before {
  content: '';
  width: 7px;
  height: 4px;
  border: solid #fff;
  border-width: 0 0 2px 2px;
  transform: translateY(-1px) rotate(-45deg);
  opacity: 0;
}
.option-checkbox:hover input {
  border-color: #409eff;
}
.option-checkbox input:checked {
  border-color: #409eff;
  background: #409eff;
}
.option-checkbox input:checked::before {
  opacity: 1;
}
.option-checkbox input:focus-visible {
  outline: 3px solid #c6e2ff;
  outline-offset: 2px;
}
.option-checkbox span {
  display: inline;
  margin: 0;
}
.switch-page {
  margin: 16px 0 0;
  color: #777;
  font-size: 13px;
  text-align: center;
}
.switch-page a {
  color: #1677c8;
  font-weight: 700;
  text-decoration: none;
}
</style>
