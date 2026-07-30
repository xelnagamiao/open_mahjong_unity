<template>
  <main class="record-link-page">
    <section class="record-link-card">
      <p class="eyebrow">SALASASA · UNITY 3D</p>
      <h1>3D 牌谱分享</h1>
      <p class="game-id">牌谱 ID：{{ gameId }}</p>
      <p class="guide">
        无需登录。复制下面的链接，在 Unity 客户端登录页的用户名框粘贴，然后点击“登录”即可直接进入 3D 牌谱。
      </p>
      <div class="share-row">
        <input :value="shareUrl" readonly aria-label="3D 牌谱分享链接" @focus="$event.target.select()" />
        <button type="button" @click="copyLink">{{ copied ? '已复制' : '复制链接' }}</button>
      </div>
      <p v-if="!validGameId" class="error">牌谱 ID 格式不正确。</p>
      <router-link class="web-replay" :to="`/2d/record/${encodeURIComponent(gameId)}`">
        改用网页版牌谱
      </router-link>
    </section>
  </main>
</template>

<script setup>
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const copied = ref(false)
const gameId = computed(() => String(route.params.gameId || '').trim())
const validGameId = computed(() => /^[0-9A-Za-z]{1,16}$/.test(gameId.value))
const shareUrl = computed(() => new URL(`/3d/record/${encodeURIComponent(gameId.value)}`, window.location.origin).toString())

async function copyLink() {
  if (!validGameId.value) return
  await navigator.clipboard.writeText(shareUrl.value)
  copied.value = true
  window.setTimeout(() => { copied.value = false }, 1800)
}
</script>

<style scoped>
.record-link-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background:
    radial-gradient(circle at 20% 10%, rgba(216, 116, 31, 0.2), transparent 34%),
    #101315;
  color: #eef0f1;
}

.record-link-card {
  width: min(620px, 100%);
  padding: clamp(26px, 6vw, 54px);
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 18px;
  background: rgba(27, 31, 33, 0.96);
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.38);
}

.eyebrow { margin: 0 0 8px; color: #df7d23; font-size: 12px; font-weight: 800; letter-spacing: 0.14em; }
h1 { margin: 0; font-size: clamp(30px, 7vw, 48px); }
.game-id { margin: 14px 0 0; color: #c8cdcf; font-family: ui-monospace, monospace; }
.guide { margin: 28px 0 18px; color: #bbc1c3; line-height: 1.75; }
.share-row { display: flex; gap: 10px; }
.share-row input {
  min-width: 0;
  flex: 1;
  padding: 12px 14px;
  border: 1px solid #4a5154;
  border-radius: 8px;
  background: #111416;
  color: #fff;
}
.share-row button {
  padding: 0 18px;
  border: 0;
  border-radius: 8px;
  background: #d8741f;
  color: #fff;
  font-weight: 750;
  cursor: pointer;
}
.error { color: #ff8b7f; }
.web-replay { display: inline-block; margin-top: 24px; color: #e38a3d; }
@media (max-width: 520px) {
  .share-row { flex-direction: column; }
  .share-row button { min-height: 44px; }
}
</style>
