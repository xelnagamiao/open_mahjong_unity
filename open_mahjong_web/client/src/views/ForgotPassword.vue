<template>
  <main class="recovery-page">
    <h1>忘记密码</h1>
    <form v-if="!completed" @submit.prevent="resetPassword">
      <label for="recovery-email">邮箱</label>
      <input id="recovery-email" v-model="form.email" type="email" autocomplete="email" maxlength="255" required :disabled="busy" />
      <label for="recovery-code">邮箱验证码</label>
      <div class="code-row">
        <input id="recovery-code" v-model="form.code" inputmode="numeric" autocomplete="one-time-code" maxlength="6" pattern="[0-9]{6}" required :disabled="busy" />
        <button type="button" class="send-button" :disabled="sending || busy || cooldown > 0" @click="sendCode">
          {{ sending ? '发送中…' : cooldown > 0 ? `${cooldown} 秒后重发` : '发送验证码' }}
        </button>
      </div>
      <label for="recovery-password">新密码</label>
      <input id="recovery-password" v-model="form.password" type="password" autocomplete="new-password" minlength="6" maxlength="32" required :disabled="busy" />
      <small>6–32 位英文、数字或英文特殊字符。</small>
      <label for="recovery-confirm">确认新密码</label>
      <input id="recovery-confirm" v-model="form.confirm" type="password" autocomplete="new-password" minlength="6" maxlength="32" required :disabled="busy" />
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <p v-if="message" class="message" role="status">{{ message }}</p>
      <button type="submit" class="submit-button" :disabled="busy || sending">{{ busy ? '重置中…' : '重置密码' }}</button>
    </form>
    <div v-else class="success" role="status">
      <strong>密码已重置</strong>
      <p>请使用新密码登录。游戏客户端用户可返回游戏继续登录。</p>
    </div>
    <p class="manual-recovery">
      账户没有绑定邮箱？联系测试群群主手动找回密码，
      <a href="https://qm.qq.com/q/MGGZV58hOO" target="_blank" rel="noopener noreferrer">q906497522</a>
    </p>
    <router-link to="/login">返回登录</router-link>
  </main>
</template>

<script setup>
import { onBeforeUnmount, reactive, ref } from 'vue'
import playerApi from '@/api/playerClient'
import { usePlayerAuthStore } from '@/stores/playerAuth'

const auth = usePlayerAuthStore()
const form = reactive({ email: auth.email || '', code: '', password: '', confirm: '' })
const sending = ref(false)
const busy = ref(false)
const cooldown = ref(0)
const completed = ref(false)
const error = ref('')
const message = ref('')
let timer
onBeforeUnmount(() => clearInterval(timer))

async function sendCode() {
  error.value = ''
  message.value = ''
  const email = form.email.trim()
  if (email.length > 255 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    error.value = '请填写正确的邮箱地址'
    return
  }
  sending.value = true
  try {
    const response = await playerApi.post('/auth/password-reset/send-code', { email })
    message.value = response.data.message
    cooldown.value = 60
    clearInterval(timer)
    timer = setInterval(() => {
      cooldown.value--
      if (cooldown.value <= 0) clearInterval(timer)
    }, 1000)
  } catch (e) {
    error.value = e.response?.data?.message || '验证码发送失败，请稍后重试'
  } finally {
    sending.value = false
  }
}

async function resetPassword() {
  error.value = ''
  message.value = ''
  if (form.password.length < 6 || form.password.length > 32 || /[^\x21-\x7e]/.test(form.password)) {
    error.value = '密码须为 6–32 位英文、数字或英文特殊字符'
    return
  }
  if (form.password !== form.confirm) {
    error.value = '两次输入的密码不一致'
    return
  }
  busy.value = true
  try {
    await playerApi.post('/auth/password-reset', {
      email: form.email.trim(), code: form.code.trim(),
      new_password: form.password, confirm_password: form.confirm,
    })
    auth.logout()
    form.password = ''
    form.confirm = ''
    form.code = ''
    completed.value = true
    clearInterval(timer)
  } catch (e) {
    error.value = e.response?.data?.message || '密码重置失败，请稍后重试'
  } finally {
    busy.value = false
  }
}
</script>

<style scoped>
.recovery-page { box-sizing: border-box; width: calc(100% - 32px); max-width: 480px; margin: 32px auto; padding: 26px; background: #fff; border: 1px solid #e0e0e0; }
h1 { margin: 0 0 12px; font-size: 24px; }
.hint, small { color: #777; font-size: 13px; line-height: 1.6; }
label { display: block; margin: 16px 0 6px; font-size: 14px; }
input { box-sizing: border-box; width: 100%; min-width: 0; height: 42px; padding: 8px 10px; border: 1px solid #ddd; border-radius: 3px; font: inherit; }
.code-row { display: flex; gap: 8px; }
button { min-height: 42px; padding: 9px 14px; border-radius: 3px; font: inherit; cursor: pointer; }
button:disabled { opacity: .6; cursor: not-allowed; }
.send-button { white-space: nowrap; color: #1677c8; border: 1px solid #b8d5ed; background: #f5faff; font-size: 13px; }
.submit-button { width: 100%; margin-top: 20px; border: 0; color: #fff; background: #409eff; }
.error { color: #c33; font-size: 14px; line-height: 1.5; }
.message { color: #246a96; font-size: 14px; line-height: 1.5; }
.manual-recovery { border-top: 1px solid #eee; padding-top: 20px; margin-top: 24px; font-size: 14px; line-height: 1.8; }
a { color: #1677c8; }
.success { padding: 20px 0; color: #287246; line-height: 1.7; }
@media (max-width: 420px) { .recovery-page { padding: 20px; margin: 20px auto; } }
</style>
