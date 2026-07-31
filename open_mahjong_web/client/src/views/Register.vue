<template>
  <div class="player-register-page">
    <div class="register-card">
      <h1>玩家注册</h1>
      <p class="hint">网站账户与游戏内账户互通，注册后可直接登录网页、2D 模式和游戏客户端。</p>
      <form @submit.prevent="onSubmit">
        <label>
          <span>用户名</span>
          <input
            v-model="form.username"
            autocomplete="username"
            maxlength="16"
            autofocus
          />
          <small>支持中文、字母、数字、符号等字符；中文按 2 个长度计算，其他字符按 1 计算。</small>
        </label>
        <label>
          <span>密码</span>
          <input
            v-model="form.password"
            type="password"
            autocomplete="new-password"
            maxlength="32"
          />
          <small>6–32 位，仅限英文、数字和英文特殊字符。</small>
        </label>
        <label>
          <span>确认密码</span>
          <input
            v-model="form.confirmPassword"
            type="password"
            autocomplete="new-password"
            maxlength="32"
          />
        </label>
        <button type="submit" :disabled="loading">{{ loading ? '注册中…' : '注册并登录' }}</button>
      </form>
      <p v-if="error" class="err">{{ error }}</p>
      <p class="switch-page">
        已有账号？
        <router-link :to="loginTarget">返回登录</router-link>
      </p>
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
const form = reactive({ username: '', password: '', confirmPassword: '' })
const loginTarget = {
  path: '/login',
  query: typeof route.query.redirect === 'string' ? { redirect: route.query.redirect } : {},
}

function goAfterRegister() {
  const redirect = route.query.redirect
  if (
    typeof redirect === 'string'
    && redirect.startsWith('/')
    && redirect !== '/login'
    && redirect !== '/register'
  ) {
    router.replace(redirect)
  } else {
    router.replace('/')
  }
}

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
  if (auth.isLoggedIn) goAfterRegister()
})

async function onSubmit() {
  error.value = ''
  if (form.password !== form.confirmPassword) {
    error.value = '两次输入的密码不一致'
    return
  }

  loading.value = true
  try {
    await auth.register(form.username, form.password)
    goAfterRegister()
  } catch (e) {
    error.value = e.response?.data?.message || '注册失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.player-register-page {
  max-width: 420px;
  margin: 24px auto;
}
.register-card {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 24px 22px;
}
.register-card h1 {
  margin: 0 0 8px;
  font-size: 1.3rem;
}
.hint {
  color: #666;
  font-size: 13px;
  line-height: 1.55;
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
label small {
  display: block;
  margin-top: 4px;
  color: #888;
  line-height: 1.4;
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
