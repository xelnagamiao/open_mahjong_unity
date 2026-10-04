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
            maxlength="20"
            autofocus
          />
          <small>用户名应当在2-20个字符之间，只能包含中文、数字及英文，中文计两个字符。</small>
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
        <div class="agreement-row">
          <label class="agreement-checkbox">
            <input v-model="acceptedRegulations" type="checkbox" />
            <span>我已阅读并同意</span>
          </label>
          <button type="button" class="agreement-link" @click="regulationsVisible = true">《Salasasa 账户规约》</button>
        </div>
        <button type="submit" :disabled="loading || !acceptedRegulations">{{ loading ? '注册中…' : '注册并登录' }}</button>
      </form>
      <p v-if="error" class="err">{{ error }}</p>
      <p class="switch-page">
        已有账号？
        <router-link :to="loginTarget">返回登录</router-link>
      </p>
    </div>
    <el-dialog v-model="regulationsVisible" title="Salasasa-萨拉飒飒麻将平台账户规约"
      width="min(760px, 94vw)" append-to-body :close-on-click-modal="false">
      <div class="regulations-body" tabindex="0" aria-label="账户规约全文">{{ regulationsBody }}</div>
      <template #footer>
        <el-button @click="regulationsVisible = false">关闭</el-button>
        <el-button type="primary" @click="acceptRegulations">同意规约</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import regulations from '@/content/accountRegulations.txt?raw'

const router = useRouter()
const route = useRoute()
const auth = usePlayerAuthStore()
const loading = ref(false)
const error = ref('')
const acceptedRegulations = ref(false)
const regulationsVisible = ref(false)
const regulationsBody = regulations.slice(regulations.indexOf('\n') + 1).trim()
function acceptRegulations() {
  acceptedRegulations.value = true
  regulationsVisible.value = false
}
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
  if (loading.value) return
  error.value = ''
  if (!acceptedRegulations.value) {
    error.value = '请先阅读并同意《Salasasa 账户规约》'
    return
  }
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
.register-card form > button {
  width: 100%;
  margin-top: 8px;
  padding: 10px;
  border: 0;
  background: #409eff;
  color: #fff;
  font-weight: 700;
  cursor: pointer;
}
.register-card form > button:disabled {
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
.agreement-row { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; margin: 16px 0 10px; font-size: 13px; }
.agreement-checkbox { display: inline-flex; align-items: center; gap: 7px; margin: 0; cursor: pointer; }
.agreement-checkbox input { width: 16px; height: 16px; margin: 0; padding: 0; flex-shrink: 0; }
.agreement-checkbox span { display: inline; margin: 0; }
.agreement-link { width: auto; margin: 0; padding: 4px 0; border: 0; background: transparent; color: #1677c8; font: inherit; text-align: left; text-decoration: underline; text-underline-offset: 3px; cursor: pointer; }
.regulations-body { max-height: 58vh; overflow-y: auto; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 14px; line-height: 1.85; color: #303133; padding: 0 12px 0 2px; }
</style>
